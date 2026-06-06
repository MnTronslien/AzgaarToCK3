# Contributing

Contributions are welcome. Please read this before opening a pull request.

---

## Getting started

**Prerequisites:**
- [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
- CK3 installed
- An Azgaar export to test with (or use the included test data)

**Build:**
```
git clone https://github.com/MnTronslien/AzgaarToCK3
cd AzgaarToCK3
dotnet build
```

**Run from source:**
```
cd ConsoleUI/bin/Debug/net8.0
./ConsoleUI -d "path/to/azgaar/exports" --no-rivers
```

Test datasets are included under `TestData/` — use `-d "TestData/Oncyia"` for a quick run.

---

## Branch model

| Branch | Purpose |
|--------|---------|
| `develop` | Integration branch — all PRs target this |
| `stable` | Advanced manually at release points |
| `feature/<name>` | Your work branch |

Always branch off `develop`:
```
git checkout -b feature/your-feature develop
```

---

## Good first contributions

A few areas where additional eyes / opinions are particularly welcome:

- **Biome textures.** Each Azgaar biome currently maps to one CK3 ground texture — see the [Biome textures table in CONVERSION_RULES.md](docs/CONVERSION_RULES.md#biome-textures). The current choices are a reasonable first pass; better picks per biome, or refinements to the hills / mountain / beach rules, are a self-contained way to contribute. All of the mapping lives in one file: `Converter/Lemur/Splats/MaterialRegistry.cs`. Each line declares one texture + one rule, so a change is usually one or two lines.
- **CK3 vanilla textures.** Run `./TerrainLab --gen-materials` to see the full list of available CK3 textures by name. A swap is just changing the string in `MaterialRegistry.cs`.
- **Province terrain (gameplay).** Each barony's CK3 terrain is picked by a small registry of scoring rules in `Converter/Lemur/Provinces/TerrainRegistry.cs`. See the [Province terrain section in CONVERSION_RULES.md](docs/CONVERSION_RULES.md#province-terrain-gameplay) for what the mapping does. Each entry is a `(Ck3Terrain, score lambda)` pair — tweaking a band, raising a max, or swapping a biome → terrain pairing is usually a few lines. Current values were chosen on a single test map; better calibration on other maps is welcome.

  To iterate: set `"GenerateDebugImages": true` in `settings.json`, run the converter, and inspect `%LOCALAPPDATA%\AzgaarToCK3\debug\<run>\9_terrain_overview.png` — each barony coloured by its assigned terrain with river polylines and province outlines drawn on top. Lets you compare before / after a tuning change without launching CK3.

### Culture traditions

Each culture's traditions are picked from a pool, filtered and weighted by the culture's terrain and ethos — see the [Culture traditions section in CONVERSION_RULES.md](docs/CONVERSION_RULES.md#culture-traditions) for what the rules do. The pieces:

- **The pool and its metadata** live in `Converter/Lemur/TraditionData.cs`: every tradition entry carries its category, its favoured-ethos set, and (for terrain-flavoured ones) a terrain gate. Adding, removing, or re-classifying a tradition is an edit here.
- **The selection logic** is in `Converter/Lemur/CultureTraditionAssigner.cs`, which combines the terrain gate (a hard yes/no) with the ethos weighting (a soft multiplier). The two tuning knobs — the terrain-coverage thresholds and the ethos penalty factor — are constants at the top of these files.
- **The per-culture land facts** (terrain mix, coastal fraction) come from `Converter/Lemur/Fields/CultureTerrainProfiler.cs`.

Good contained changes: adjusting a terrain threshold, tuning the ethos penalty, fixing a tradition's favoured-ethos set, or adding a terrain gate to a tradition that should have one. Current values were set against a single map; calibration on other maps is welcome. The profiler logs a per-culture terrain summary each run, so you can see *why* a culture was eligible for what.

If you have a screenshot of "before / after" for a texture proposal, drop it in the PR — visual diffs are the most useful thing here.

---

## Making changes

- **One logical change per commit.** Don't bundle unrelated fixes.
- **Commit message format:** imperative subject line, blank line, body explaining *why* not *what*.
  ```
  fix: county capital picks most populous barony

  Previously always picked the first barony in the partition,
  which produced capitals biased toward geography rather than
  population density.
  ```
- **Keep the converter deterministic.** If your change introduces randomness, seed it via `Helper.MixSeeds(Settings.Instance.Seed!.Value, entityId)`. Never use `new Random()` without a seed.
- **Test with at least two datasets** before opening a PR — `TestData/Oncyia` for a typical map and one of the edge-case sets.

---

## Opening a pull request

- Target `develop`, not `stable` or `main`
- Describe *what problem your PR solves*, not just what it does
- If your PR touches conversion rules (how Azgaar data maps to CK3 output), explain the reasoning — these decisions are deliberate and need justification
- Link any related issues

---

## Reporting bugs

Open an [issue](https://github.com/MnTronslien/AzgaarToCK3/issues). Include:
- Your Azgaar `.map` file if possible — this is the single most useful thing for reproducing the problem
- Your Azgaar map type (default settings? custom? approximate cell count?)
- The converter log output (run with `--log-level Debug` for more detail)
- The CK3 error log if the issue is in-game (`Documents/Paradox Interactive/Crusader Kings III/logs/error.log`)

---

## Questions

Join the [Discord](https://discord.gg/Px6dwFVdUG) for discussion before investing significant time in a large change — it's worth aligning on approach first.
