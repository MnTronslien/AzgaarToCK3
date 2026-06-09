# Unreleased

### Added
- **Generated faiths share a world, and treat other faiths as rivals.** Every faith the converter creates is gathered under a single religious family named after your map's world. Faiths of different generated traditions now regard one another, and the faiths of the outside world, as rivals rather than kin, giving the religious map friction instead of blanket tolerance.

### Changed
- **Faith tenets now reflect each religion's character.** Like culture traditions in 1.6.0, a generated faith's tenets used to be drawn at random. Now the religion shapes them: its kind — folk, organized, cult, or heresy — steers which tenets suit it; mutually exclusive tenets never land together (no pacifist warmongers); and a religion's form pulls it toward matching flavour, so a nature-worship faith leans toward nature tenets and a dark cult toward blood and the occult. Folk and cult faiths now read distinctly from organized ones instead of all drawing from the same bag. The pull strength is tunable (`FaithFormThemeBoost`).

### Fixed
- **No more inert "syncretism" tenets.** These only grant bonuses toward specific *vanilla* religions, which don't exist in a converted world — so they were wasted tenet slots. Faiths no longer receive them.
- **Religions with several faiths are now named after the right one.** When you arranged a religion's faiths into a family tree in Azgaar, the converter named the religion after one of the child faiths instead of the parent at the top of the tree, and listed that parent last. Religions now take the name of their root faith.
- **Generated faiths no longer reference a religious family CK3 removed.** They were assigned `rf_other`, a family that no longer exists in CK3 1.19, which logged a load error for each one and left faiths without a family. They now belong to the converter's own world-named family.

---

# 1.6.0 — 2026-06-05

This release is about cultural character. Until now every culture drew its traditions at random, so the desert nomads were as likely to end up seafarers as the coast-dwellers, and the cultural map read as noise. Now a culture's traditions follow from where it lives and what it values: the land it sits on decides which terrain-flavoured traditions it can take at all, and its ethos tilts the rest. Each people reads as its own thing instead of a random assortment.

### Changed
- **Culture traditions now reflect where a culture lives and what it values.** Traditions used to be handed out at random, so a desert people were as likely to get seafaring or deep-forest traditions as anything appropriate. Now the land comes first: terrain-flavoured traditions only go to cultures whose territory actually fits them, so you won't find maritime traditions on a landlocked realm or jungle traditions out in the desert. Within what's allowed, a culture's ethos tilts the odds, so a warlike people leans toward martial traditions and a scholarly or devout one leans away from them. Flavourful and regional traditions are still in the pool and can turn up anywhere, and traditions a culture inherited from its ancestors are left untouched, so a forest people descended from desert nomads can still carry an old homeland tradition. The result is that cultures read as distinct and rooted in their part of the map instead of looking like a random grab-bag. How the selection works, and how to adjust it, is written up in [CONTRIBUTING.md](CONTRIBUTING.md#culture-traditions).

### Added
- **Benchmark boot support.** The mod now ships a small defines override that points CK3's `-benchmark` mode at the first generated kingdom, so you can boot straight onto your converted map for a quick look without clicking through the bookmark. A testing convenience; no effect on normal play.

### Fixed
- **All of CK3's culture ethoses can now appear.** One ethos was never being assigned to any generated culture; cultures can now take it like any other.
- **Stopped assigning a couple of traditions that have no usable effect** (placeholder entries that were never meant to be picked).

---

# 1.5.0 — Independence — 2026-06-03

The dependency is gone. Since day one the converter's output only loaded because you subscribed to Total Conversion Sandbox and loaded it beneath the generated mod — TCS wiped vanilla's titles, characters, provinces and religions so ours wouldn't collide, and supplied the start bookmark and a handful of map render assets. That whole arrangement is now done in-house. The generated mod is self-sufficient: one mod in the playset, no subscription, no load order to get right. TCS can be removed from the machine entirely.

### Added
- **Self-sufficient clean slate.** The generated descriptor now declares the `replace_path` entries that wipe vanilla's colliding content — landed titles, religions, bookmarks, character and title history, provinces, province mappings, and map_data — so our generated world loads cleanly as the only mod. This was the single job we used to hand to TCS.
- **A start bookmark, generated from your map.** The converter picks the largest realms and writes a playable 1066 start bookmark with their rulers, so the game is startable with no external bookmark mod.
- **Map render assets generated in code.** The surround-map mask/fade and the water flow/foam/colour rasters are now produced at conversion time (flat solids matching the values TCS shipped) instead of copied from a TCS install, so nothing large is bundled in the release. `colormap.dds` is generated flat as a vanilla override. Biome-driven colormap enrichment and in-game vegetation are noted as follow-ups.

### Fixed
- **TCS-free boot crash on the main menu.** A stripped `gui/frontend_main.gui` ships with the main-menu portrait widget hidden — that 3D portrait render crashed the menu on a generated map once TCS was no longer present to override the GUI.
- **Vanilla map content bleeding through.** With `gfx/map/map_object_data` now replace_path'd and re-supplied with the map-independent layer and map-table definitions, vanilla's ~12k-instance locators and tree placements no longer load onto our ~1300-province map.

### Changed
- **First-run setup no longer asks for a TCS path.** One fewer step; the prompt and its validation are gone.
- **Build no longer reads from a TCS install.** The converter runs to completion with TCS removed from the machine — terrain masks, mask filenames, and colormap no longer depend on it.

### Removed
- **The Total Conversion Sandbox dependency, runtime and build-time.** No subscription, no load order, no TCS on disk. User docs (README, USAGE, CONFIGURATION, CONTRIBUTING, release README) updated to match.

---

# 1.4.0 — Governance — 2026-06-01

Governments and development — finally, they're here, and they're a big deal. These are two of the last things standing between "the map looks right" and "the map plays right." Until now every realm was feudal and every county started at development 0, so the whole world played the same way. Now each realm gets a government that fits its Azgaar state, and every county starts with a development level set by how big its capital is. The world opens with real political and economic texture instead of a flat feudal sheet.

### Added
- **Governments that fit your states.** Every kingdom and duchy now gets a real CK3 government instead of everyone being feudal — `feudal`, `clan`, `tribal`, `republic`, and `theocracy`, plus the DLC forms `nomad` (Khans of the Steppe), `administrative` (Roads to Power), and `japan_feudal`/Sōryō (All Under Heaven). The converter looks at each state's Azgaar form name first (an Emirate becomes a clan, a Most Serene Republic a republic, a Shogunate a Sōryō realm), falls back to the broad form if the name is unfamiliar, and only lands on feudal as a last resort. Kingdoms and duchies are decided on their own, so a republic duchy under a feudal king keeps its republic government — just like Venice and Genoa sit under their lieges in vanilla 1066. Missing the DLC for a government? CK3 quietly falls back at game start (nomad → tribal, administrative and Sōryō → feudal), so nothing breaks. The full mapping is written up in `docs/CONVERSION_RULES.md`. Resolves issue #9.
- **Counties that start with real development.** Each county now begins the game at a development level drawn from its capital's population, instead of everyone starting flat at 0. Big state capitals open around 30 and up, most counties settle around 7–9, and there's plenty of headroom left for development to grow in play (CK3 caps it at 100). Resolves issue #10 — and fixes a quiet bug along the way where a county wasn't actually holding on to its capital.

---

# 1.3.0 — 2026-05-27

The "all provinces are plains" gap — open since 0.1.0 — is closed. Every barony's CK3 terrain is now derived from its cells' Azgaar biome distribution and a cell-level roughness signal, giving the map real combat / movement / supply variety on play. The other half of this release is stability: three independent crashes that downstream users hit on 1.1.x and 1.2.0 are all resolved.

### Added
- **Province terrain (gameplay) from Azgaar biomes.** Every barony's CK3 terrain — `plains`, `hills`, `mountains`, `forest`, `desert`, `desert_mountains`, `drylands`, `jungle`, `taiga`, `wetlands`, `steppe`, etc. — is now picked from the cells' AzgaarBiome distribution plus a cell-level roughness signal. Closes the "all provinces are plains" gap that's been open since 0.1.0. Rules live in `Converter/Lemur/Provinces/TerrainRegistry.cs` as score lambdas; biome rules sum biome fractions, steepness rules (Hills, Mountains, DesertMountains) score on roughness-band cell fractions and may overshoot 1.0 to overrule biome cover at dominant relief. See `docs/CONVERSION_RULES.md` for the full mapping table and Showcase histogram.
- **Terrain debug overview image.** Under `GenerateDebugImages`, a `9_terrain_overview.png` lands next to the other debug images. Each barony filled with its assigned terrain colour (CK3-community palette), wastelands distinct, river polylines overlaid, 4-px black borders from geometric union of cell polygons. Designed for tuning `TerrainRegistry.cs` without launching CK3.
- **Converter version banner.** Every run now prints the converter version as the first log line, so bug-report logs always identify which build produced them.

### Fixed
- **CK3 1.19.x boot crash on map entry.** Custom faiths and holy sites now write to the renamed `common/religion/religion_types/` and `common/religion/holy_site_types/` folders (1.19 renamed both). Writing to the old paths on 1.19.x left every `religion = lemur_faith_N` reference dangling and crashed `on_game_start` with `EXCEPTION_GUARD_PAGE`. CK3 1.18 was unaffected; the converter output itself didn't regress.
- **Silent CK3 termination ~3 seconds after unpause.** `FaithManager` filtered religions on Azgaar's optional `cells` field, which is only present when the user has opened the Statistics pane in the editor before exporting. Maps without that field had every religion pruned except those that survived as someone's `origins[0]`, and the rest of the map fell back to a single faith via the defensive `ProvinceHistoryWriter` guard. CK3 then crashed mid-game on the resulting orphan-faith iteration storm. Now counts cells from the GeoJSON. Reported by three downstream users (Trimoyers, PlanetFambesi).
- **Flatmap missing at maximum zoom-out.** New `MapTableWriter` overrides `gfx/map/map_object_data/map_table_western.txt` with Y values lowered and X/Z recentered to our 8192×4096 map. Vanilla's table positions were calibrated for the 9216×4608 vanilla map and the unaltered tablecloth sat in front of the flatmap plane on converted maps. Symptom: 3D map-table visible instead of the parchment flatmap at the maximum zoom step.
- **`AssignCellsToBaronies` crash on smaller maps** (`Sequence contains no elements`). Two duchy-creating paths in `ConversionManager.GenerateDuchies` existed; only one filtered out empty-burg duchies. The state-inside-wastelands path could produce a 0-burg duchy whenever Azgaar point count was low enough that some states had cells in the wastelands province without burgs there. Latent since March 2026; surfaced when a downstream user dropped from 100k to 10k Azgaar points. Both paths now route through `AddDuchyOrWasteland`.

### Changed
- **All debug images now land in the same per-run folder.** `ImageUtility.GetDebugFolderName` is the single source of truth — previously duplicated across `ImageUtility`, `RiverImageGenerator`, and the new terrain image writer with three independent minute-resolution caches, so runs crossing a minute boundary scattered artefacts across two or three folders.
- **`MajorRiverThreshold` default lowered to `2000`** (was an effectively-disabled `999999`). Exercises the major-river ribbon code path on typical Azgaar maps; the known cell-swallowing edge case still affects very large widths and is tracked separately.
- **`Faith.RuralPop` / `Faith.UrbanPop` / `Faith.CellCount` removed.** Dead after the cell-count fix; no readers anywhere.

---

# 1.2.0 — 2026-05-22

The first-run experience gets a real onboarding flow and every run writes a .log file you can attach to a bug report. Builds on the foundation laid by 1.1.2 ("Saved settings.json. You won't see this screen again.") — that screen now actually walks the user all the way to a running conversion instead of dropping them at a `.json file has not been found` error.

### Added
- **Guided setup, now 5 steps end-to-end.** FirstTimeSetup gained `3/5 Where to put the converted mod` and `5/5 Point me at your Azgaar exports` — by the time the welcome screen closes, every path the converter needs is resolved and written to settings.json. No more "saved settings, now go fix it manually" cliff right after onboarding.
- **OneDrive-managed Documents one-tap fallback.** On machines where Windows redirected `Documents/` into OneDrive and the cldflt driver blocks external writes, the mods-directory step's write-test fails fast and offers the local `%USERPROFILE%\Documents\Paradox Interactive\Crusader Kings III\mod` path with a single `[Y/n]`. Users hit this in the wild on 1.1.2; now it's a single keystroke instead of a confusing FileNotFoundException mid-conversion.
- **Unified `[Y/n]` prompts.** The existing `1. Yes / 2. No` numeric prompts (`Start conversion?`, `Use them as inputs?`) now match the FirstTimeSetup style with `[Y/n]:` and accept y/yes/n/no/Enter.
- **Per-run `.log` file alongside the exe.** Every run writes `./logs/AzgaarToCK3_<timestamp>.log` with the banner, settings dump, every Logger call, and any crash stack trace. Crash-flushed via `AppDomain.UnhandledException` + `ProcessExit` hooks plus `Logger.Flush()` from `HandleFatal`. Old logs (>10) auto-cleaned at startup. On crash, the friendly error block now points the user at the exact file to attach when filing a bug. Controlled by `--no-log-file` / `--log-file <path>`.

### Fixed
- **Mid-conversion `FileNotFoundException` writing the `.mod` descriptor on fresh installs.** `ModManager.CreateMod` now `Directory.CreateDirectory` the mods folder before writing — `File.WriteAllTextAsync` doesn't create parent directories.
- **Empty section banners at default log level.** `TitleTreeDebugger` printed `DE FACTO TITLE TREE`, `CHARACTER DOMAINS`, `DE JURE TITLE TREE` headers via `Logger.Section` (visible at Info) but the bodies via `Logger.Debug` (hidden at Info). Gated each method on `LogLevel <= Debug` so headers and bodies share visibility.
- **Rivers loader crash under Native AOT.** (Already shipped in 1.1.3 hotfix; included here because it's in this release's commit range.) RiverLoader and CellDump now use source-generated JsonSerializerContexts.

### Changed
- ASCII `->` replaces Unicode `→` in user-facing setup instructions — the rightward arrow rendered as garbage in default Windows console fonts.
- Welcome banner no longer promises "Press Enter to accept the [default in brackets]" since most prompts now Enter-to-exit rather than Enter-to-accept-default. Each prompt's parenthetical speaks for itself.
- Stale `2524797018` hardcoded TCS workshop ID is gone for good — content-based detection introduced in 1.1.2 is the only path now.

---

# 1.1.3 — 2026-05-22

Hotfix for the AOT release builds. Any conversion run with rivers enabled (the default) on 1.1.1 or 1.1.2 crashed mid-pipeline with `Reflection-based serialization has been disabled for this application`. Reported by a downstream user testing a custom map.

### Fixed
- **Rivers loader crash under Native AOT.** `RiverLoader.LoadRiverControlPoints` built a fresh `JsonSerializerOptions` with no `TypeInfoResolver`, so `JsonSerializer.Deserialize<RiverGeoJson>` fell through to the reflection path which Native AOT disables. New `RiverGeoJsonContext` source-generated `JsonSerializerContext` restores deserialization under AOT. Same treatment applied to `CellDump` (`DumpFile` via `CellDumpContext`), which was on the same broken pattern but only reachable through the `--dump-cells` debug flag.

Workaround for users still on 1.1.1 / 1.1.2: pass `--no-rivers` until they update.

---

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
