# TerrainLab

Visualization and diagnostic harness for the heightmap generation algorithm.

Loads Azgaar cell data and writes a PNG directly — no CK3, no province mapping,
no terrain masks. Change a parameter, rebuild, view the output. Diagnostics run
automatically on every generation and print to stdout.

---

## Architecture

The algorithm lives in **`Converter/Lemur/Writers/HeightmapAlgorithm.cs`**,
not in this project. TerrainLab is a thin shim:

```
TerrainLab/
  Program.cs             CLI + visualisation modes
  HeightmapGenerator.cs  Thin shim: calls HeightmapAlgorithm.Generate(),
                         then runs Lab-only diagnostics on the result

Converter/Lemur/Writers/
  HeightmapAlgorithm.cs  ← The actual algorithm (edit this)
  HeightmapNodes.cs      TerrainNode, PolyNode, CoastNode, IHeightmapNode
  HeightmapSpatialGrid.cs  Generic spatial index used inside the algorithm
```

**Iteration workflow:** edit `HeightmapAlgorithm.cs` → build → run TerrainLab.
No porting step needed; the Converter and the Lab always run the same code.

The Lab adds diagnostics that do not run in production:
- `AssertNoSteinerSteinerEdges` — classifies Steiner-to-Steiner CDT edges as
  same-seg / same-cell / cape-clip (benign) or bay-shortcut (artifact)
- `CheckCoastConnectivity` — verifies every coast node has ≥ 2 coast-coast edges
- `RasterizeRoughness` — Delaunay barycentric raster of per-cell roughness values

---

## Usage

```
TerrainLab --json <path> --geojson <path> --output <path.png> [options]
```

### Generation options

| Flag | Default | Meaning |
|------|---------|---------|
| `--seed N` | 42 | RNG seed for poly-node placement and perturbation |
| `--strength F` | 0.25 | Poly-node displacement magnitude (fraction of height range) |
| `--nodes N` | 4 | Poly-nodes spawned per land cell |
| `--sample-count N` | 4 | IDW nearest-neighbour count for poly-node base height |
| `--roughness-norm F` | 1.0 | Multiplier on top of auto p95 normalization |
| `--roughness-power F` | 2.0 | Power curve on roughness before perturbation |
| `--blur-radius N` | 3 | Separable Gaussian blur radius in pixels |
| `--relax N` | 5 | Repulsion relaxation iterations |
| `--relax-step F` | 0.05 | Per-step damping (1.0 = full force, causes over-travel) |
| `--terrain-to-poly-sep F` | 0.25 | Min poly-to-terrain-centroid distance as fraction of avg cell spacing |
| `--terrain-out <dir>` | — | Write hills/mountains/snow masks to this directory alongside the main PNG |

### Visualisation modes (mutually exclusive, replace the plain heightmap output)

| Flag | Output |
|------|--------|
| `--debug` | Overlay: green = terrain nodes, blue/red = poly-nodes (alpha = roughness) |
| `--mesh` | Delaunay triangulation wireframe (white on black) |
| `--spawn-lines` | Lines from each poly-node to its parent terrain centroid |
| `--drift-lines` | Lines from spawn position to final relaxed position |
| `--coast-map` | Greyscale land + blue-tinted ocean; coast nodes colour-coded by status |
| `--coast-map --mesh` | Coast-map with CDT triangulation wireframe overlaid |
| `--coast-map --debug` | Coast-map with terrain + poly-node dots overlaid |
| `--steepness-map` | Black = flat, white = vertical (p95-normalized gradient magnitude) |
| `--roughness-map` | Delaunay barycentric raster of per-cell roughness (terrain nodes only) |
| `--detail-intensity` | Writes a `detail_intensity.tga` RGB checkerboard to `--terrain-out` dir |

### Utility modes (no generation)

```
TerrainLab --compare <path-a> <path-b>
```

Pixel-by-pixel comparison of two grayscale PNGs. Exits 0 if identical, 1 if any
pixel differs. Used to verify zero drift after algorithm changes.

---

## Coast-map node colours

When running `--coast-map`, coast nodes are drawn as 2 px dots:

| Colour | Meaning |
|--------|---------|
| Yellow | Original walk-derived node, ≥ 2 coast-coast CDT edges (healthy) |
| Magenta | CDT Steiner insertion point (inserted by ConformingDelaunay to enforce constraint) |
| Orange | Hull node with < 2 coast-coast edges (boundary — typically benign) |
| Red | Non-hull node with < 2 coast-coast edges (real connectivity problem) |

Red rings (radius 50 px) highlight real connectivity failures;
orange rings highlight Steiner nodes involved in bay-shortcut artifacts.

---

## Algorithm overview

The algorithm runs in `HeightmapAlgorithm.Generate(cells, params)`.
`cells` is an `IReadOnlyDictionary<int, Cell>` keyed by Azgaar cell ID.

### Phase 1 — Terrain nodes
One node per Azgaar cell. Height scaled from Azgaar `geoHeight` field into
`[CK3WaterLevel+1, 255]` for land, `[0, CK3WaterLevel-1]` for sea.
Roughness = auto-normalized average absolute height-diff to land neighbours
(p95 → 1.0, then multiplied by `RoughnessNorm`).

### Phase 2 — Sea flood fill
Connected sea regions are labelled by flood fill over cell adjacency. This
separates the ocean from inland lakes so coast-walking can handle each body
independently.

### Phase 3 — Coast walk (per sea body)
For each distinct sea body, walk the land/sea boundary polygon-by-polygon to
produce an ordered ring of coast vertices. These become the CDT constraint
segments in Phase 4. Handles open-path coast regions (edge of map) as well as
closed islands and inland lakes.

### Phase 4 — Conforming Delaunay triangulation (CDT)
All terrain nodes + coast nodes are triangulated with `ConformingDelaunayTriangulationBuilder`.
The coast walk rings are fed as constraint polylines; the CDT inserts Steiner
points where needed to enforce them. Coast nodes are pinned to `CK3WaterLevel`
(byte 20) so the waterline crosses exactly at the cell boundary rather than
drifting inland.

### Phase 5 — Steiner absorption
After CDT, Steiner insertions that land within `CoastExclusionRadius` pixels of
an existing coast node are absorbed into the nearest original — keeping the coast
ring clean for downstream rasterisation.

### Phase 6 — Bay-shortcut patching
CDT can connect two Steiner nodes across an open bay, creating a spurious
underwater ridge. When detected, the algorithm replaces that triangle pair with
a fan of four triangles meeting at the midpoint (height 0), eliminating the
artifact without re-triangulating.

### Phase 7 — Poly-node spawning and relaxation
`NodesPerCell` poly-nodes per land cell, placed in stratified angular sectors.
Repulsion pushes nodes apart and away from terrain centroids. IDW (1/d²) from
nearest `PolyNodeSampleCount` terrain nodes gives `baseHeight` and
`idwRoughness`. Perturbation = `rand × roughness^power × strength × 235`.
All land poly-nodes floored at `CK3WaterLevel+1`.

### Phase 8 — Rasterisation
All terrain + poly + coast nodes triangulated together; each triangle
scanline-rasterized with barycentric height interpolation into a `float[]`
heightmap, then quantized to `byte[]`.

### Phase 9 — Gaussian blur
Separable H+V passes in-place on the `float[]` heightmap. One extra `float[]`
buffer (same size). O(n × radius).

---

## GenerateResult fields

`HeightmapAlgorithm.Generate()` returns a `GenerateResult` record:

| Field | Type | Contents |
|-------|------|----------|
| `Pixels` | `byte[]` | 8-bit greyscale, row-major, Width×Height |
| `HeightmapF` | `float[]` | Pre-quantization float heightmap (same layout) |
| `TerrainNodes` | `IReadOnlyList<TerrainNode>` | One per cell; holds Px, Py, Height, Roughness, IsLand, Area |
| `PolyNodes` | `IReadOnlyList<PolyNode>` | Spawned detail nodes; holds Px, Py, SpawnPx/Py, ParentId, C0–C2 terrain indices, W0–W2 weights, IdwRoughness, RawRand, Height |
| `CoastNodes` | `IReadOnlyList<CoastNode>` | All coast ring nodes including CDT Steiner insertions |
| `OriginalCoastNodeCount` | `int` | Index boundary: `[0, OriginalCoastNodeCount)` = walk-derived, `[OriginalCoastNodeCount, Count)` = Steiner |
| `ConstraintSegs` | `IReadOnlyList<LineString>` | Coast constraint segments fed to CDT |
| `ConstraintSegToCellId` | `IReadOnlyList<int>` | Maps each constraint segment to its source Azgaar cell ID |
| `Triangulation` | `GeometryCollection` | The raw CDT triangulation (used by Lab diagnostics) |

---

## Key types (all in `Converter.Lemur.Writers`)

```csharp
public record struct TerrainNode(int Id, float Px, float Py, float Height,
    float Roughness, bool IsLand, int Area) : IHeightmapNode;

public record struct PolyNode(float Px, float Py, float SpawnPx, float SpawnPy,
    int ParentId, int C0, int C1, int C2, float W0, float W1, float W2,
    float IdwRoughness, float RawRand, float Height) : IHeightmapNode;

public record struct CoastNode(float Px, float Py) : IHeightmapNode
{
    public float Height => HeightmapAlgorithm.CK3WaterLevel;  // pinned to 20
}

public record Params(
    float LonW, float LonT, float LatS, float LatT,   // map bounds
    int Width, int Height,                             // output resolution
    int Seed = 42,
    float DisplacementStrength = 0.25f,
    int NodesPerCell = 4,
    int PolyNodeSampleCount = 4,
    float RoughnessNorm = 1f,
    int RelaxIterations = 5,
    float TerrainToPolySep = 0.25f,
    float RelaxStep = 0.05f,
    int BlurRadius = 3,
    float RoughnessPower = 2.0f,
    float CoastExclusionRadius = 30f,    // px; Steiner absorption radius
    float MaxCoastEdgePixels = 60f);     // constraint segments longer than this are split
```

---

## Porting guide for sister repos

TerrainLab and `HeightmapAlgorithm.cs` can be adapted to any project that
exports Azgaar cell data. The algorithm has two external dependencies:

- **NetTopologySuite** — `ConformingDelaunayTriangulationBuilder`, `GeometryFactory`,
  `LineString`, `GeometryCollection`. Available on NuGet.
- **`Cell`** — the Azgaar cell entity. See below for the required surface.

### What `Cell` must expose

The algorithm reads only these fields:

| Member | Type | Source |
|--------|------|--------|
| `Id` | `int` | Azgaar cell ID |
| `Neighbors` | `IEnumerable<int>` | Adjacent cell IDs |
| `GeoHeight` | `int` | Azgaar elevation (0–100) |
| `IsLand` | `bool` | Land vs. sea |
| `Area` | `float` | Cell area in map units |
| `Vertices` | `IEnumerable<(float x, float y)>` | Polygon vertex coordinates |
| `X`, `Y` | `float` | Cell centroid in pixel space |

If your `Cell` type has all of these (possibly under different names), the
algorithm can be adapted by adjusting the field accesses in `BuildTerrainNodes`
and the coast-walk phases.

### What to copy

To use the algorithm in a project that does not share the Converter codebase:

1. Copy `HeightmapAlgorithm.cs` — the core algorithm. Change the namespace.
   Replace `HeightmapSpatialGrid<T>` with your own spatial index (or copy
   `HeightmapSpatialGrid.cs` as well).

2. Copy `HeightmapNodes.cs` — `TerrainNode`, `PolyNode`, `CoastNode`,
   `IHeightmapNode`. Change the namespace.

3. Copy `HeightmapSpatialGrid.cs` — simple 2D bucket grid with `NearestN`.
   Change the namespace.

4. Remove or replace the two `Logger.Info(...)` calls in `HeightmapAlgorithm.cs`
   (roughness stats and bay-shortcut patch count). Replace with `Console.WriteLine`
   or your own logging.

5. Replace `Params.FromMap(Map map)` with a factory that reads your own map
   coordinate metadata (lonW/lonT/latS/latT equivalents), or construct `Params`
   directly.

6. The output is `result.Pixels` — a flat `byte[]`, 8-bit greyscale, row-major,
   `Width × Height`. Feed directly to your PNG writer.

### What to leave behind

The Lab-only diagnostics in `HeightmapGenerator.cs` (`AssertNoSteinerSteinerEdges`,
`CheckCoastConnectivity`, `RasterizeRoughness`) are visualization tools only.
They have no effect on the output pixels and do not need to be ported.

### Verifying a clean port

After porting, use `--compare` to verify zero drift:

```
# Baseline from original
TerrainLab --json … --geojson … --output baseline.png

# Post-port from your build
YourTool --json … --output result.png

# Compare (exits 0 = identical)
TerrainLab --compare baseline.png result.png
```

The comparison reads both images as 8-bit greyscale and reports differing pixel
count and max absolute delta. Any algorithmic difference — even a single
floating-point reordering — will show up here.
