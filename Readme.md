# AzgaarToCK3

**The Lemur Converter** turns your [Azgaar's Fantasy Map Generator](https://azgaar.github.io/Fantasy-Map-Generator/) world into a fully playable Crusader Kings III mod — no manual fine-tuning required to start playing.

![In-game screenshot of a converted map]()

---

## Why this converter

Most converters treat your Azgaar map as a rough sketch to approximate. This one treats it as source truth.

**The title hierarchy maps directly from your Azgaar data:**
- Every burg becomes a barony — exact, 1:1. A typical map produces 1,000+ baronies.
- Every Azgaar province becomes a duchy.
- Every Azgaar state becomes a kingdom.

Where CK3 needs structure Azgaar doesn't have — counties and empires — the converter infers them algorithmically from population data. Predictable, not arbitrary.

**Same input, same output.** Determinism is a core design goal. Pass `--seed <N>` and every run produces the same world. The title hierarchy is entirely rule-based; the only randomness is in culture and faith fields where Azgaar simply doesn't carry enough data to fill every CK3-specific slot — and even that is seeded.

**What gets generated:**

| | |
|---|---|
| **Cultures** | Full CK3 culture system following your Azgaar family tree — heritage and language flow down lineages, traditions drift over generations, hybrid cultures blend both parents first then mutate. 41 verified base-game traditions, GFX bundles, phenotype distributions. Azgaar cultures with more than two parents use the first two. |
| **Faiths** | Doctrines and tenets follow the Azgaar religion tree — child faiths inherit from their parent then mutate slot-by-slot. Holy sites with modifiers, full localization. |
| **Characters** | Rulers per title with correct culture and faith; feudal hierarchy wired up |
| **Rivers** | The first Azgaar converter to draw rivers — A\* pathfinding, correct tributary connections |

![Culture screen showing generated cultures]()
![Faith screen showing generated faiths]()

---

## Requirements

- [.NET 8 Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) — required to run the current build; future release packages may ship self-contained
- Crusader Kings III `1.12.5`
- [Total Conversion Sandbox](https://steamcommunity.com/sharedfiles/filedetails/?id=2524797018) mod (Steam Workshop)
- [Azgaar's Fantasy Map Generator](https://azgaar.github.io/Fantasy-Map-Generator/) — default settings work out of the box. An [alternate build](https://pryvyd9.github.io/Fantasy-Map-Generator/) produces better sea zones; if you use it, **do not use its built-in "Export for CK3" button** — that targets a different converter.

---

## Quick start

1. In Azgaar, export: **GeoJSON cells**, **JSON full data**, and optionally **Rivers** (GeoJSON)
2. Run the converter pointing at the folder containing those files:
   ```
   ./ConsoleUI -d "path/to/your/export/folder"
   ```
3. On first run you'll be prompted for your CK3 path, mods directory, and mod name — these are saved and not asked again
4. Enable the generated mod in your CK3 playset and launch

See the [Usage Guide](docs/USAGE.md) for CLI options, seed control, and debug output.

---

## What's missing

The converter is under active development. Province terrain, major rivers, and a handful of other features are incomplete or missing. See **[ROADMAP.md](ROADMAP.md)** for the full honest account.

---

## Links

| | |
|---|---|
| [Roadmap](ROADMAP.md) | Current state, known bugs, planned features |
| [Conversion Rules](docs/CONVERSION_RULES.md) | How Azgaar data maps to CK3; fine-tuning tips |
| [Usage Guide](docs/USAGE.md) | Detailed run instructions and options |
| [Configuration](docs/CONFIGURATION.md) | All settings and CLI flags |
| [Issues](https://github.com/MnTronslien/AzgaarToCK3/issues) | Bug reports and feature requests |
| [Discord](https://discord.gg/Px6dwFVdUG) | Community and support |
