# Roadmap

An honest account of what the converter produces today, what it doesn't, and what's planned.
For bug reports and feature requests, see the [Issues tab](https://github.com/MnTronslien/AzgaarToCK3/issues).

---

## What works today

### Map
- Complete 5-tier title hierarchy: Barony → County → Duchy → Kingdom → Empire
- 1,000+ baronies from burg-based mapping (exact 1:1)
- Population-balanced county grouping via graph partitioning
- De jure capitals set at all tiers; baronies and counties ordered capital-first
- Heightmap generated from Azgaar elevation data (all 4 CK3 files)
- Minor rivers drawn via A\* pathfinding with correct tributary connections

### Cultures
- All 4 CK3 culture pillars: heritage, language, ethos, martial custom
- 41 verified base-game traditions (no DLC dependency)
- GFX bundles: clothing, building, unit, coat-of-arms graphics
- Phenotype distributions; hybrid cultures blend parent distributions
- Deterministic per seed

### Faiths
- Custom faith per Azgaar religion, with its own religion group
- Doctrines and tenets from a validated base-game pool
- Holy sites with seeded random modifiers, attached to counties
- Full localization: faith names, religion group names, holy site names, adherent terms

### History & Characters
- Province history: dominant culture and religion per barony (cell-weighted)
- Named rulers per county, duchy, kingdom, and empire
- Rulers carry correct culture and faith; feudal hierarchy wired up in title history

---

## Known bugs

| Bug | Impact |
|-----|--------|
| Faith `_adj` keys broken in-game | Adjective form of faith names doesn't resolve — root cause under investigation |
| Holy site assignment at scale | Every faith receives every holy site. Fine for typical maps (10–30 faiths); may cause UI/performance issues for large maps with 100+ faiths |

---

## Known gaps

**Province terrain — all provinces are plains.**
Azgaar has rich biome data. None of it maps to CK3 terrain types yet. Every province renders as plains regardless of geography.

**Major rivers not processed.**
Minor rivers draw correctly. Major/navigable rivers — the kind that create crossing penalties — are not yet implemented.

**Sea crossing adjacencies not generated.**
`adjacencies.csv` is empty. Explicit strait and river crossing connections are absent; naval movement works through sea zone pixels only.

**All rulers start as subjects, not independent.**
When kingdoms are grouped into a shared empire, their rulers should start independent. This isn't wired up yet — you'll need to manually release them in-game if you want a fragmented start.

**Ruler demesne sizes are hardcoded.**
Kings hold 3 counties, dukes 2, counts 1. No setting to adjust this yet.

**Culture GFX bundles are assigned randomly.**
Azgaar cultures carry a `nameBase` field implying a real-world analogue. The converter currently ignores it. When implemented, a Norse-namebase culture will get Norse graphics.

**All provinces use the Western texture set.**
CK3 has regional textures (Mediterranean, Steppe, MENA, etc.). Every province currently uses Western regardless of biome.

**Building and combat locators use TCS fallback positions.**
The icons for buildings, sieges, and combat events are placed using Total Conversion Sandbox positions — correct layout, wrong map. They won't crash the game.

---

## Planned

- Terrain types from Azgaar biome data
- Major river processing
- `adjacencies.csv` — sea and strait crossing connections
- Per-region graphical regions from biome/climate data
- Correct locator files from province centroids
- GFX bundle selection from Azgaar `nameBase`
- Independent rulers for absorbed titles at game start
- Configurable ruler demesne sizes (`RulerStrength` setting)
- Title colors from Azgaar province colors
- Holy site proximity selection (cap per faith at ~5 relevant sites)
- Government type mapping (Azgaar `state.form` → CK3 government)
- Religious head of faith assigned at game start
- Development levels from Azgaar population data
- Starting buildings from Azgaar burg flags

---

## Not planned

- **Multiplayer checksum bypass** — use [UMMS](https://steamcommunity.com/sharedfiles/filedetails/?id=3227254722) for multiplayer
- **CK3 map editor steps** — the goal is to make manual map editor work unnecessary; it is not a supported workflow
