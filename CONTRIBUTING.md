# Contributing

Contributions are welcome. Please read this before opening a pull request.

---

## Getting started

**Prerequisites:**
- [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
- CK3 installed with [Total Conversion Sandbox](https://steamcommunity.com/sharedfiles/filedetails/?id=2524797018)
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
