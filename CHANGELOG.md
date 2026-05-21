# 1.1.2 — 2026-05-21

First-run UX overhaul. A freshly downloaded converter used to crash silently when launched from Explorer — the console window flashed and closed before the user could read anything. Both the underlying bug and the fragile auto-detection that surfaced it are fixed here. Reported by a downstream user as "I double-clicked ConsoleUI and nothing happened."

### Fixed
- **Silent first-run crash on fresh installs.** Total Conversion Sandbox's Steam Workshop ID was hardcoded to `2524797018`. TCS has since been re-uploaded under a new ID, so `SettingsManager.CreateDefault()` threw "No mod directories found" on every fresh first-run. The unhandled exception escaped Main's catch handler unread because nothing held the console window open.
- Latent bug while we were here: `Settings.Ck3Directory` is supposed to hold the CK3 install root (writers append `"game"` themselves), but the previous lookup populated it with `…/Crusader Kings III/game`. Never fired in practice because the TCS lookup crashed first.

### Added
- **Guided first-time setup.** Replaces the silent auto-detect-or-crash flow. A single contiguous welcome screen walks the user through three steps — CK3 install, Total Conversion Sandbox, ModName — confirming each auto-detected value with `[Y/n]`, prompting for a paste (drag-and-drop supported) when nothing is found, and offering a numbered picker when multiple TCS candidates exist.
- **Content-based TCS detection.** Instead of trusting a hardcoded workshop ID, the converter now walks every subfolder of `steamapps/workshop/content/1158310`, reads each `descriptor.mod`, and matches "Total Conversion Sandbox" against the `name` field. Workshop IDs can change again at any time without breaking the converter.
- **Pause-on-error.** Any unhandled exception goes through `HandleFatal` + `PauseOnExit`: framed friendly message, full stack trace, link to the issue tracker, then `Press any key to exit…`. Console window stays open until the user has read it. No-op when stdin is redirected so CI and piped runs don't hang.
- **README in the release zip.** Five-line quickstart (subscribe to TCS, generate map, run exe, point at exports, activate in launcher). The 1.1.1 zip shipped six loose files and zero documentation.

### Changed
- CI: release PR body now auto-syncs from `CHANGELOG.md` on every push to a `release/*` branch, and a one-shot nudge comment posts when a `CHANGES_REQUESTED` review goes stale. Cuts the manual `gh pr edit --body` step and the manual reviewer follow-up that used to be required on every release.
- Internal: `SettingsManager.GetGameDirectory` / `GetTotalConversionSandboxDirectory` replaced by `TryFindCk3InstallRoot` (returns `string?`) and `TryFindTotalConversionSandbox` (returns `List<TcsCandidate>`). Auto-detection no longer throws; callers decide whether to prompt, exit, or default.

---

# 1.1.1 — 2026-05-21

Compatibility patch for CK3 1.19+. Mod now boots and runs cleanly on 1.19.0.5 (and later 1.19.x patches per `supported_version = "1.19.*"`). No converter feature changes — straight compat fix on top of 1.1.0 Terra Bella.

### Changed
- `supported_version` bumped to `1.19.*`. CK3 1.19+ launcher accepts the mod.

### Fixed
- **`EXCEPTION_GUARD_PAGE` crash at `on_game_start` on CK3 1.19+.** Two root causes addressed, both via a new `VanillaScriptOverridesWriter`:
  - Vanilla `easteregg_event.0001` (Charna & Jakub duel) runs at `on_game_start` and calls `set_variable` on a vanilla-history character whose scope is invalid on converted worlds (no faith / realm anchor). On 1.19 the scope rejection recurses into a stack-overflow crash; on 1.18 it was a silent script error. Now overridden to `trigger = { always = no }` — suppresses the entire duel chain.
  - Vanilla `common/on_action/game_start.txt` context-switches to ~200 vanilla titles (`c_chandax`, `c_byzantion`, `k_magyar`, `e_byzantium`, `h_roman_empire`, ...) for `set_important_location` effects, Magyar elective law, and similar. TCS removes all vanilla titles via `replace_path`. On 1.19 the null `Landed_title - 4294967295` scope crashes the engine; on 1.18 it was a silent warning. New `common/landed_titles/01_vanilla_compat_landless.txt` declares 194 landless stubs so the lookups succeed and effects run as no-ops.

Vanilla `on_game_start` now runs unmodified — vanilla initialization logic is preserved.

### Known limitations
- `error.log` retains ~52k lines of non-fatal vanilla-script noise (missing characters in script links, dynasty CoA fallbacks, locator transforms, faction triggers, etc.). None affect gameplay. These will be addressed incrementally as we extend the stub list and surface converter-side improvements.
- `lemur_faith_N is not a valid faith` warnings (~1,600 entries in province + character history) — pre-existing converter-side issue, **not** 1.19-specific. Tracked for a follow-up release.
- See `bugs/BUG_ck3-1.19-compat.md` for the full diagnosis, iteration log, and what this fix does NOT cover.

---

# 1.1.0 — Terra Bella — 2026-05-20

Three flagship features: biome-textured maps, a rewritten heightmap pipeline, and major rivers drawn as proper provinces. Plus a regression fix to character names, the usual performance pass, and known gaps documented for the next release.

### Splatmap — biome textures + steepness overlays + coastal blend
The "all terrain renders as plains" gap from 1.0.0 is closed.
- Per-pixel material registry driving the CK3 detail TGAs. 12 biome → texture mappings, exposed for tuning in `MaterialRegistry.cs` (called out in `CONTRIBUTING.md` as a single-file contributor entry point).
- Delaunay-barycentric biome blending with sea cells included in the triangulation, so the biome boundary fades smoothly into the coast.
- Steepness-driven hills + mountain layers cross-fading on top of the biome base.
- Asymmetric beach + mud-seafloor blend at the waterline.
- Mapping documented in `docs/CONVERSION_RULES.md`.

### Heightmap — new build pipeline
Older flat-polygon heightmap replaced with a Delaunay + poly-node algorithm.
- Conforming-Delaunay-Triangulation algorithm: terrain nodes per cell plus relaxed poly-nodes for sub-cell detail, with coast-walked boundary nodes pinned as CDT constraints. Bay-shortcut Steiner edges patched via triangle-fan replacement.
- Coastal lift: pronounced coastline pinned at the first land byte (`LowestLandByte`); waterline semantics tightened (`MaxWaterByte`) end-to-end.
- Packed heightmap rewrite. Detail-level metric replaced — the old signed-2nd-derivative collapsed to zero on both ocean and rough interiors, starving the middle detail levels. Now mean-magnitude of the first derivative, with percentile-based cutoffs that self-calibrate per map regardless of land/ocean balance.

### Major rivers — provinces, not just lines
Previously only minor rivers were drawn (to `rivers.png`). Major rivers are now first-class provinces.
- Mouth-width / discharge above `MajorRiverThreshold` → drawn as river-tier provinces with their own IDs in `definition.csv` and entries in landed_titles.
- Discharge-based width scaling, self-calibrating per cell diameter (replaces hard-coded thresholds).
- Tributary-first ordering with perpendicular junction cuts at confluences — no overlapping cells where rivers join.
- Tiny split pieces auto-merged into the best geometric neighbour. Cells fully engulfed by an oversized ribbon fall back to land (workaround until the width clamp lands — see Known gaps).

### Also added
- **TerrainLab** — new sibling project. Standalone harness for iterating on splatmap and heightmap tuning without full converter runs: cell painting, pixel sampling, packing diagnostics (`pack_metric.png`, `pack_detail_levels.png`), `--coast-map` / `--river-map` / `--steepness-map` debug images. `./TerrainLab --gen-materials` regenerates `Ck3MaterialBytes.cs` from `materials.settings` after CK3 patches.
- Pipeline warning when the `TerrainMasks` writer is enabled without `Heightmap` (silent fallback used to leave splatmap biome-only).

### Performance
- `SplatmapBuilder` outer pixel loop parallelized.
- `AssignCellsToBaronies` parallelized across duchies.
- Terrain mask write steps parallelized; colormap + blank-mask file-copy caches.
- O(C log C) cell assignment (was LINQ scan).

### Fixed
- **Characters generated without names** (regression). Two stacked bugs: `name_list_arabic` did not exist in vanilla CK3 (now `name_list_bedouin` for the mena ThemeBundle); CK3 does not auto-fill names for history-defined characters, so we now emit explicit `name = "..."` with pools parsed from vanilla `name_lists/*.txt` via a new `NameListLoader`. Side effect: kingdom and duchy titles no longer render as `[blank]` in the UI.
- `LocatorWriter.ComputeCentroid` sorts cells by ID for run-to-run determinism.
- `HeightmapMasks` now emits `hills_01_mask.png` / `mountain_02_mask.png` / `mountain_02_c_snow_mask.png` as black placeholders. These files are CK3 Map Editor input, not runtime-consumed — generating real per-pixel data was wasted CPU and disk.
- Dead `Settings` tunables (`RoughnessNormalisation`, `HillsThreshold`, `MountainsThreshold`) and unused `Helper.ComputeRoughness` removed — abandoned earlier design, superseded by the per-pixel steepness pipeline.
- Major rivers wider than a cell no longer corrupt the province grid: engulfed cells fall back to being kept as land. Workaround, not a root-cause fix — full width clamp tracked for next release.

### CI
- Linux and macOS release builds disabled — no hardware available to verify these binaries before publishing.

### Known gaps
- `province_terrain` still emits `plains` for every land province. Visual terrain is in; gameplay terrain assignment (combat/movement/supply modifiers) is queued for the next release.
- CK3 1.19 compatibility deferred — dev environment is intentionally pinned to 1.18.4 due to a vanilla 1.19 stack-overflow bug.
- Activity locators (tournaments, hunts, etc.) default to map position (0, 0). The 8 standard locator types from 1.0.0 are correct; activity-specific locators are next release.

---

# 1.0.0 — 2026-03-26

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
