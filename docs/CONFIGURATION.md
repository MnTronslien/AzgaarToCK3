# Configuration Reference

Settings are stored in `settings.json` next to the `ConsoleUI` executable. CLI flags override `settings.json` for a single run. Delete `settings.json` to reset everything and be prompted again.

---

## Input paths

| Key | CLI | Type | Description |
|-----|-----|------|-------------|
| `InputDirectory` | `-d` | string | Directory to scan for input files (auto-detects latest) |
| `InputJsonPath` | `-j` | string | Explicit path to Azgaar full data `.json` |
| `InputGeojsonPath` | `-g` | string | Explicit path to Azgaar cells `.geojson` |
| `InputRiversGeojsonPath` | `-r` | string | Explicit path to Azgaar rivers `.geojson` |
Explicit paths (`-j`, `-g`, `-r`) always override directory auto-detection.

---

## Output

| Key | CLI | Type | Default | Description |
|-----|-----|------|---------|-------------|
| `ModsDirectory` | — | string | *(required)* | CK3 mods folder |
| `Ck3Directory` | — | string | *(required)* | CK3 install root |
| `TotalConversionSandboxPath` | — | string | *(required)* | Path to TCS mod folder |
| `ModName` | — | string | *(required)* | Name of the generated mod |
| `AutoWipeOutput` | `--no-wipe` | bool | `true` | Wipe mod folder before converting |

---

## Randomization

| Key | CLI | Type | Default | Description |
|-----|-----|------|---------|-------------|
| `Seed` | `--seed` | int? | `null` | Global seed; `null` = new seed each run |
| `TenetCount` | `--tenet-count` | int | `3` | Tenets per faith (1–5) |
| `DoctrineMutationRate` | `--doctrine-mutation-rate` | float | `0.3` | Child faith doctrine divergence (0 = identical, 1 = fully random) |

---

## Territory & hierarchy

| Key | CLI | Type | Default | Description |
|-----|-----|------|---------|-------------|
| `EmpireFromCulture` | `--empire-from-culture` | bool | `true` | Group kingdoms into empires by culture; `false` = by religion |
| `MinimumDuchiesPerKingdom` | `--min-duchies-per-kingdom` | int | `4` | Kingdoms with fewer duchies merge into a neighbour |
| `MinimumKingdomsPerEmpire` | `--min-kingdoms-per-empire` | int | `3` | Empires with fewer kingdoms merge into a neighbour |
| `HighPopulationThreshold` | — | int | `13000` | Duchy population above this → fewer, larger counties |
| `LowPopulationThreshold` | — | int | `2000` | Duchy population below this → more, smaller counties |
| `MinCounties` | — | int | `2` | Floor on counties per duchy |
| `MaxCounties` | — | int | `5` | Ceiling on counties per duchy |

---

## Rivers

| Key | CLI | Type | Default | Description |
|-----|-----|------|---------|-------------|
| `EnableRivers` | `--no-rivers` | bool | `true` | Draw rivers to the province map |
| `MajorRiverThreshold` | — | float | `999999` | Discharge threshold for major rivers (effectively disabled — major rivers are not yet implemented) |

---

## Sea zones

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `SeaZoneTargetArea` | int | `25000` | Target area per sea zone (Azgaar cell-area units) |
| `SeaZoneMinimumArea` | int | `2500` | Minimum sea zone area |
| `FarSeaZoneCount` | int | `8` | Far-sea strips drawn behind the map edge |

---

## Logging & debug

| Key | CLI | Type | Default | Description |
|-----|-----|------|---------|-------------|
| `LogLevel` | `--log-level` | enum | `Info` | `Verbose` / `Debug` / `Info` / `Warning` / `Error` |
| `GenerateDebugImages` | `--no-images` | bool | `true` | Save intermediate map images to `%LOCALAPPDATA%\AzgaarToCK3\debug\` |

---

## Writer toggles

Individual writers can be disabled for faster bisection runs. All default to `true`.

```json
"Writers": {
  "DefinitionCsv": true,
  "DefaultMap": true,
  "LandedTitles": true,
  "Religion": true,
  "Faiths": true,
  "Cultures": true,
  "Characters": true,
  "TitleHistory": true,
  "ProvinceHistory": true,
  "Heightmap": true,
  "GeographicalRegions": true,
  "ProvinceTerrain": true,
  "Locators": true,
  "TerrainMasks": true,
  "Flatmap": true,
  "Adjacencies": true,
  "MapStaticFiles": true,
  "MapDefines": true
}
```
