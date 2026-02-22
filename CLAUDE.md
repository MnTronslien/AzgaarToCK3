# AzgaarToCK3 — CLAUDE.md

## Instructions for Claude

- **At the start of every session**, read `TODO.md` (project root) and suggest the top tasks to the user before doing anything else.
- **Keep `TODO.md` up to date** throughout the session: mark items done as they are completed, add new items when issues or work are discovered, move completed items to the Done section.

---

## Project Overview

A .NET 8 (C#) CLI tool that converts map data exported from **Azgaar's Fantasy Map Generator** into a playable **Crusader Kings 3 mod**. The tool reads two Azgaar export files (GeoJSON cells + JSON full data) and produces a complete CK3 mod directory.

Target CK3 version: `1.12.5`
Required CK3 mod dependency: **Total Conversion Sandbox** (Steam Workshop)

---

## Solution Structure

```
AzgaarToCK3.sln
├── ConsoleUI/           # CLI entry point (Program.cs) — TODO: rename to AzgaarToCK3.CLI
├── Converter/           # Main conversion library
│   ├── Lemur/           # ← ACTIVE branch: new algorithm (see below)
│   │   ├── ConversionManager.cs   # Orchestrates the full pipeline
│   │   ├── Deserialization/       # AzgaarLoader.cs, AzgaarData.cs
│   │   ├── Entities/              # Barony, Burg, Cell, County, Duchy, Empire, Kingdom, Map, River, Wasteland
│   │   ├── Algorithms/            # AStarPathfinder.cs
│   │   ├── Graphs/                # Graph.cs (partitioning)
│   │   ├── Rivers/                # RiverLoader, RiverImageGenerator
│   │   ├── IdManager.cs
│   │   ├── ImageUtility.cs
│   │   └── Helper.cs
│   ├── ModManager.cs    # ← Upstream: orchestrates all CK3 file writing
│   ├── TitleManager.cs  # ← Upstream: landed_titles + localization
│   ├── CharacterManager.cs
│   ├── MapManager.cs
│   ├── HeightMapManager.cs
│   ├── Entities.cs      # Upstream entity types (different from Lemur entities)
│   └── ...
├── Converter.Tests/     # Test project
└── TestData/            # Sample Azgaar exports for testing
```

---

## Two Approaches in the Codebase

### Upstream (original pryvyd9/AzgaarToCK3)
- Lives in `Converter/` root files (ModManager, TitleManager, etc.)
- **Territory mapping**: Azgaar Province → CK3 Barony (1:1, ~100-200 baronies)
- 4 baronies grouped per county (fixed, simple)
- **Has complete CK3 file output** (landed_titles, history, characters, localization, maps, etc.)

### LemurAlgorithm (active development branch)
- Lives in `Converter/Lemur/`
- **Territory mapping**: Azgaar Burg → CK3 Barony (1:1, ~1000+ baronies — much more granular)
- Population-based graph partitioning for county grouping (smarter/balanced)
- Azgaar Province → Duchy, custom hierarchy → Kingdoms & Empires
- **Has NO CK3 file output yet** — this is the main gap to fill

---

## Current Pipeline (LemurAlgorithm)

`ConversionManager.Run()` sequence:
1. Load Azgaar data (`AzgaarLoader`) → `Map` object
2. Draw raw cells (visualization checkpoint)
3. Link cells to burgs
4. (Optional) Load & draw rivers, then early-exit
5. Generate Duchies (from Azgaar provinces)
6. Generate Baronies (from Azgaar burgs)
7. Assign cells to baronies (outward growth algorithm)
8. Generate Wasteland provinces
9. (Graph partitioning for counties, etc.)

---

## Key Entities (Lemur)

| Entity   | Source                        | Notes                          |
|----------|-------------------------------|--------------------------------|
| Cell     | Azgaar GeoJSON cell           | Atomic map unit, has neighbors |
| Burg     | Azgaar burg                   | Settlement, becomes a Barony   |
| Barony   | 1 per Burg                    | CK3 barony (province)          |
| County   | Graph-partitioned Baronies    | Population-balanced grouping   |
| Duchy    | Azgaar Province               | 1:1 mapping                    |
| Kingdom  | Grouped by culture-like criteria |                             |
| Empire   | Top-level grouping             |                                |
| River    | Azgaar rivers                 | Minor drawn to PNG, major TBD  |

---

## Known Issues & Status (as of 2026-02-15)

### Build
- **Builds successfully** with .NET 8.0 (190 warnings — nullability/outdated packages, 0 errors)

### Fixed
- `PackProvinceJsonConverter` — fixed dummy province `{"i":0,"state":0,"burg":0,"name":""}` for the leading `0` in the array

### Open Issues
- `PackBurgJsonConverter` — same problem as provinces; burgs array starts with `[0, {...}]`, needs dummy burg object. Error: `"The JSON value could not be converted to Converter.Burg. Path: $.pack.burgs[0]"`
- Rivers implementation: minor rivers draw to PNG; major river processing is stubbed out and needs rework
- **No CK3 file writers exist for Lemur entities** — biggest gap

---

## Path Forward

The recommended approach is **hybrid**:
1. Keep Lemur territory generation (it's better)
2. Create an adapter layer mapping Lemur entities → format expected by upstream writers
3. Reuse upstream's file writing code (ModManager, TitleManager, etc.) with minimal modifications
4. Test with actual CK3

### Files to focus on
- **Adapt from upstream**: `ModManager.cs`, `TitleManager.cs`, `MapManager.cs`, `CharacterManager.cs`, `CK3FileSystem.cs`
- **Lemur (working)**: `ConversionManager.cs`, `Entities/*.cs`, `Graphs/Graph.cs`
- **To create**: `Converter/Lemur/Writers/` — new directory for CK3 format output

---

## Edge Cases to Handle

1. Islands — disconnected land masses
2. Islands with burgs
3. Burgs without provinces (in a state but not assigned to any province)
4. Burgs in wilderness (province 0 / wasteland)
5. Provinces without states (become wilderness)
6. Non-contiguous baronies (a barony's cells may not all be connected)

---

## Running the Converter

### Quickest way (uses settings.json)
```
cd ConsoleUI/bin/Debug/net8.0
./ConsoleUI
```
`settings.json` in that folder stores all input paths and options. Edit it directly to change test data or settings. Delete it to reset all settings.

### CLI flags (override settings.json)
**Always use CLI flags when running a one-off conversion with different input files — never edit settings.json for this.** Run `--help` first to see all available flags.

```
cd ConsoleUI/bin/Debug/net8.0
./ConsoleUI --help
./ConsoleUI \
  --json <path-to-full.json> \
  --geojson <path-to-cells.geojson> \
  --rivers-geojson <path-to-rivers.geojson>
```

Example with TestData:
```
./ConsoleUI \
  --json "C:/Users/mattro/Documents/Private/CK3 claude/AzgaarToCK3/TestData/Oncyia Full 2026-02-22-21-38.json" \
  --geojson "C:/Users/mattro/Documents/Private/CK3 claude/AzgaarToCK3/TestData/Oncyia Cells 2026-02-22-21-38.geojson" \
  --rivers-geojson "C:/Users/mattro/Documents/Private/CK3 claude/AzgaarToCK3/TestData/Oncyia Rivers 2026-02-22-21-38.geojson"
```

### Test data files (TestData/)
| Name | Files |
|------|-------|
| Minimum Rivers Test | `Minimum Rivers Test Full 2026-02-19-16-01.json` + `Cells` + `Rivers` geojson |
| Oncyia | `Oncyia Full 2026-02-22-21-38.json` + `Cells` + `Rivers` geojson |
| Handcrafted Edge Cases | `Handcrafted Edge Cases Full 2026-02-17-10-09.json` + `Cells` + `Rivers` geojson |
| Handcrafted The Second | `Handcrafted The Seccond Full 2026-02-17-13-01.json` + `Cells` + `Rivers` geojson |
| 10k Touria (large) | `10k-touria.json` + `10k-touria.geojson` (no rivers geojson) |

`settings.json` currently points to **Oncyia** (last used). Use CLI flags to run with a different dataset without touching settings.json.

### Debug output
With `"Debug": true` in settings.json, images are saved to:
`%LOCALAPPDATA%\AzgaarToCK3\debug\<mapname>_<timestamp>\`
- `1_cells.png` — raw cell map
- `7_rivers.png` — final rivers image
- `rivers_local/` — cropped views of rivers with validation violations

### Known issue: IsTributary misclassification
Rivers with `parent == self` (e.g. Bay A rivers) are incorrectly classified as tributaries because `IsTributary => ParentId != 0`. These show `RedPixel:1` validation violations. Pre-existing bug, not river-pathfinding related.

---

## Usage (original)

1. Export from Azgaar: **GeoJSON cells** + **JSON full** + **rivers GeoJSON**
2. Configure `settings.json` with file paths
3. Run `ConsoleUI`
4. Enable the generated mod in CK3

Set `"OnlyCounts": true` to make all characters start as counts.
