# Usage Guide

Assumes you have CK3, the [Total Conversion Sandbox](https://steamcommunity.com/sharedfiles/filedetails/?id=2524797018) mod, and [.NET 8](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) installed.

---

## 1. Export from Azgaar

You need two files from Azgaar's **Download** menu:

| Export | Format | Notes |
|--------|--------|-------|
| Full data | `.json` | Contains all map data |
| Cells | `.geojson` | Contains cell geometry |

Rivers are optional — export **Rivers** as `.geojson` if you want them drawn on the map.

Put all exported files in the same folder.

---

## 2. Run the converter

Point the converter at that folder with `-d`:

```
./ConsoleUI -d "C:/path/to/your/azgaar/exports"
```

The converter auto-detects the most recent `.json`, cells `.geojson`, and rivers `.geojson` in that directory.

On first run you'll be prompted for your CK3 install path, mods directory, and a mod name. These are saved to `settings.json` — subsequent runs need no prompts.

**Skip rivers** (faster, useful during iteration):
```
./ConsoleUI -d "..." --no-rivers
```

**Reproducible output** — pass a seed to get the same cultures and faiths every run. The seed controls culture pillars, traditions, theme bundles, phenotype distributions, and faith doctrines. Title generation and character names are fully deterministic regardless of seed (CK3 assigns names from culture name lists at game start).
```
./ConsoleUI -d "..." --seed 12345
```

---

## 3. Enable the mod in CK3

1. Launch CK3
2. Open the **Mods** menu → **Playsets**
3. Create a playset containing **Total Conversion Sandbox** and your generated mod
4. Launch

---

## Common options

| Flag | What it does |
|------|-------------|
| `-d <dir>` | Auto-detect input files in directory (recommended) |
| `-j`, `-g`, `-r` | Explicit paths to `.json`, cells `.geojson`, rivers `.geojson` |
| `--seed <N>` | Fix the random seed for reproducible output |
| `--no-rivers` | Skip river drawing (faster runs) |
| `--log-level <level>` | `Verbose` / `Debug` / `Info` / `Warning` / `Error` |
| `--no-wipe` | Don't wipe the mod output folder before converting |
| `--help` | Full flag reference |

See [CONFIGURATION.md](CONFIGURATION.md) for the complete settings reference.

---

## Test data

The repo includes several test datasets under `TestData/`. Each lives in its own subdirectory — use `-d` to point at one:

```
./ConsoleUI -d "TestData/Oncyia" --no-rivers
```

| Dataset | Notes |
|---------|-------|
| `Oncyia/` | Primary test map, includes rivers |
| `Minimum Rivers Test/` | Small map for river testing |
| `Handcrafted Edge Cases/` | Stress-tests edge cases |
| `10k Touria/` | Large map (~10k cells), no rivers |

---

## Debug images

With `GenerateDebugImages = true` in `settings.json` (default), the converter saves intermediate map images to:

```
%LOCALAPPDATA%\AzgaarToCK3\debug\<mapname>_<timestamp>\
```

Useful for diagnosing province layout, river paths, and cell assignment.
