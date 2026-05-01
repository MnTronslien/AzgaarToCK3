# HeightmapLab

Standalone experiment harness for tuning the heightmap generation algorithm
before committing changes to the main converter.

Loads Azgaar cell data and writes a PNG directly — no CK3, no province mapping,
no terrain masks. Change a parameter, rebuild, view the output.

---

## Usage

```
HeightmapLab --json <path> --geojson <path> --output <path.png> [options]
```

Key options (all have defaults):

| Flag | Default | Meaning |
|------|---------|---------|
| `--strength F` | 0.25 | Poly-node displacement magnitude |
| `--nodes N` | 4 | Poly-nodes spawned per land cell |
| `--roughness-norm F` | 2.0 | Multiplier on top of auto p95 normalization; higher = flatter plains |
| `--roughness-power F` | 2.0 | Power curve on roughness before perturbation; higher = smoother plains |
| `--blur-radius N` | 3 | Separable Gaussian blur radius in pixels |
| `--relax N` | 5 | Repulsion relaxation iterations |
| `--relax-step F` | 0.05 | Per-step damping (1.0 = full force, causes over-travel) |
| `--terrain-to-poly-sep F` | 0.25 | Min poly-to-terrain-centroid distance as fraction of avg cell spacing |
| `--debug` | — | Overlay: green=terrain nodes, blue/red=poly-nodes (alpha=roughness) |
| `--base-only` | — | Terrain-only Delaunay layer, no poly-nodes |
| `--mesh` | — | Delaunay triangulation wireframe |
| `--spawn-lines` | — | Lines from each poly-node to its parent terrain centroid |
| `--drift-lines` | — | Lines from spawn position to final position after relaxation |

---

## Algorithm overview

1. **Terrain nodes** — one per Azgaar cell. Height scaled from GeoHeight to [20, 255].
   Roughness = auto-normalized avg absolute height-diff to land neighbours (p95 → 1.0).

2. **Poly-node spawning** — `NodesPerCell` nodes per land cell, placed in stratified
   angular sectors within a radius derived from cell area.

3. **Repulsion relaxation** — poly-nodes push each other apart and away from terrain
   centroids. Controlled by `RelaxIterations`, `RelaxStep`, `TerrainToPolySep`.

4. **Height at final position** — IDW (1/d²) from nearest N terrain nodes gives
   `baseHeight` and `idwRoughness`. Perturbation = `rand × roughness^power × strength × 235`.
   Land nodes floored at CK3WaterLevel+1 to prevent carving ocean pixels.

5. **Delaunay triangulation** — all terrain + poly-nodes triangulated; each triangle
   scanline-rasterized with barycentric height interpolation.

6. **Gaussian blur** — separable H+V passes in-place on the float[] heightmap.
   One extra float[] buffer (same size). O(n × radius).

---

## Porting changes back to Converter

The algorithm lives in three files. When changes are made here and validated,
copy them to `Converter/Lemur/Writers/` with these adjustments:

| HeightmapLab file | Converter file | Changes needed |
|-------------------|---------------|----------------|
| `HeightmapGenerator.cs` | `HeightmapAlgorithm.cs` | Namespace `HeightmapLab` → `Converter.Lemur.Writers`; class rename `HeightmapGenerator` → `HeightmapAlgorithm`; `Console.WriteLine` → `Logger.Info`; add `Params.FromMap(Map map)` factory; remove `BaseOnly` flag and debug-only `bool` params |
| `SpatialGrid.cs` | `HeightmapSpatialGrid.cs` | Namespace change; class rename `SpatialGrid<T>` → `HeightmapSpatialGrid<T>` (avoids collision with any future generic SpatialGrid elsewhere in Converter) |
| `Nodes.cs` | `HeightmapNodes.cs` | Namespace change; interface rename `INode` → `IHeightmapNode` |

Then update `HeightmapWriter.cs`:
- Replace `DrawHeightmap(map, path)` with a call to `HeightmapAlgorithm.Generate(map.Cells!, params)`
- Write the returned `result.Pixels` (byte[]) to `heightmap.png` via MagickImage
- Pass `result.Pixels` directly to `CreatePackedHeightmap` instead of reading the PNG back from disk

The three algorithm files have no Converter-specific dependencies beyond `Cell`
(Azgaar cell data) and NetTopologySuite (already a Converter dependency), making
them straightforward to upstream back to pryvyd9/AzgaarToCK3 as well.
