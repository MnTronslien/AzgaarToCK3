# 0.1.1 — 2026-03-26

### Added
- locator file overhaul — correct positions, full coverage, 8 files
- culture ethnicities + deterministic RNG seeding
- blend parent ethnicities for hybrid cultures
- HolySite as a first-class entity
- de jure capitals — all tiers + fix Bukex Skip(1) bug

### Fixed
- faith localization, holy site names/effects, zero-cell faith filter

### Changed
- remove dead upstream pipeline code

---

# 0.1.0 — Lemur Converter — 2026-03-26

First public release of the Lemur Converter — a full rewrite of the AzgaarToCK3 pipeline.

### Added
- **Lemur pipeline** — burg→barony (1:1), province→duchy, population-balanced county partitioning, kingdoms and empires from Azgaar state data
- **Culture generation** — full CK3 culture system following Azgaar lineage tree: heritage, language, ethos, traditions, GFX bundles, name lists, phenotype distributions; ethnicity blending for hybrid cultures
- **Faith generation** — doctrines and tenets mutating down the Azgaar religion tree; holy sites with modifiers and full localization
- **De jure capitals** — assigned at all tiers (barony through empire)
- **Character generation** — rulers per title with correct culture and faith; feudal hierarchy; liege history
- **River generation** — A\* pathfinding, correct tributary connections, minor rivers drawn to provinces.png
- **Heightmap generation** from Azgaar elevation data
- **Locator files** — correct positions, full coverage across all 8 locator types
- **CLI** — `--seed`, `--no-rivers`, `--no-wipe`, `--log-level`, writer toggles, input directory auto-detection

### Fixed
- CK3 OOM crash — rivers palette, locators, terrain masks
- Faith localization missing keys; holy site names and effects

### Known gaps
- All terrain renders as plains
- Major rivers are not processed (minor rivers only)
- Start date and character ages are not driven by Azgaar data

---

# Upstream history (pryvyd9/AzgaarToCK3)

# 1.4.6
- removed parallelism from mask writing.

# 1.4.5
- fixed cultures (199 original cultures).

# 1.4.4
- raised culture limit to 239 (original culture count).

# 1.4.3
- skip states with all provinces removed due to some filtering. Do not create titles for them.

# 1.4.2
- all detail tiles available.

# 1.4.1
- fixed tile borders.

# 1.4.0
- added writing packed_heightmap.

# 1.3.0
- added onlyCounts option.
- improved input search.

# 1.2.13
- fixed localization
- added character names

# 1.2.12
- path

# 1.2.11
- same

# 1.2.10
- used Path even more everywhere.

# 1.2.9
- used Path everywhere.

# 1.2.8
- used Path methods for paths.

# 1.2.7
- switched to ProcessPath.
- added input search.

# 1.2.6
- changed to userProfile.

# 1.2.5
- Added `~` to mac paths.

# 1.2.4
- Added MacOS folder detection.

# 1.2.3
- Switched to AOT compilation for publishing.
- Added MacOS (not AOT as cross-OS compilationis not possible).

# 1.2.2
- Added publishing for multiple platforms.
