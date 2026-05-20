# 1.1.0 — 2026-05-20

### Added
- add major river province entity and supporting foundation
- implement MajorRiverInserter with 4-phase pipeline
- wire major river provinces into pipeline output
- restore round-cap ribbon slices with overlap + coverage/overlap assertions
- eliminate intra-river cell overlap via perpendicular junction cuts
- process rivers per-river in tributary-first order; carve river cells
- merge tiny split pieces into best geometric neighbor
- add HeightmapLab standalone heightmap experiment harness
- replace Perlin with separable Gaussian blur
- auto-normalize roughness to p95, power curve, land floor
- replace flat polygon heightmap with Delaunay + poly-node algorithm
- generate hills/mountains/snow masks from heightmap gradient
- roughness map via Delaunay barycentric rasterization of terrain nodes
- add --coast-map debug image
- --terrain-out flag for hot-reload iteration in CK3
- insert coast constraint nodes at land/sea polygon boundary vertices
- show terrain+poly nodes on --coast-map when --debug is also set
- assert coast node Delaunay connectivity >= 2 coast-coast edges
- three-colour coast connectivity visualisation
- colour Steiner points magenta, write nodes+mesh as separate files
- assert no Steiner-Steiner Delaunay edges
- walk-based coastline for CDT constraints
- add --dump-cells / --cells pipeline for river-aware heightmap iteration
- add --manifest <dir> mode for output verification
- discharge-based width scaling with self-calibrating cell diameter
- drop river cell TerrainNodes, seed centerline from control points
- densify river centerline + add --river-map diagnostic
- add --check-neighbors and --neighbor-arrows diagnostics
- add --vertex-debug for inspecting river-cell vs neighbour geometry
- skip rivers in coast walk's land-iteration
- add --alpha N override flag to TerrainLab paint flags
- add --sample-tga pixel-histogram tool + document splat-map model
- M1 — Delaunay-barycentric biome blending
- M2 — Material framework + hills/mountain layers
- coastal lift — pronounced coastline, tighter beach band
- asymmetric beach curve + mud_wet_01 seafloor + tropical→drylands
- include sea cells in Delaunay → natural cell-space biome fade
- --pack-heightmap harness for iterating on packed-heightmap algorithm
- emit pack_metric.png + per-tile metric distribution percentiles
- replace per-tile metric with mean(|first-derivative|)
- percentile-based detail-level bucketing
- warn when TerrainMasks runs without Heightmap

### Fixed
- eliminate river cell geometry overlap via sequential subtraction
- use true angular bisector for junction cuts; add river CP debug image
- exclude river cells as merge targets for tiny land splinters
- exclude river cells from sea zone generation
- river provinces display plain river name in-game
- correct NearestN early-exit causing bucket-boundary artifacts
- widen gradient kernel to suppress quantization banding
- use float heightmap for normal computation, add roughness-map to lab
- hills tent function — no overlap with mountains at high steepness
- restore detail_index + detail_intensity per-cell biome painting
- copy+resize detail_intensity.tga from TCS instead of generating it
- correct detail_intensity.tga checkerboard to row×column independent cycling
- --coast-map --mesh now combines correctly; mesh skips early exit when coast-map active
- classify poly nodes by cell polygon containment, not IDW baseHeight
- use ConformingDelaunayTriangulation with coast-coast CDT constraints
- drop sea poly nodes; add violation rings on coast map
- eliminate all coast connectivity violations via exclusion zone
- exclude sea cells from CDT constraint collection
- only constrain coast pairs present in both land AND sea cell rings
- per-sea-body coast walk to handle inland lakes
- patch bay-shortcut Steiner-Steiner edges via triangle fan replacement
- add OutputDirectoryOverride to Settings
- sort cells by ID in ComputeCentroid for FP determinism
- default MajorRiverThreshold when Settings.Instance is null
- re-verify split-piece neighbours against geometry
- snap shared boundaries between adjacent cell polygons
- write detail_intensity.tga with low alpha to eliminate cell-square artefact
- pin coast nodes to first land byte instead of waterline byte
- biome coast-fade to crossfade with beach material
- widen beach+biome coast fade to span post-lift coast band
- use name_list_bedouin in mena ThemeBundle (was non-existent name_list_arabic)
- emit explicit names; load name pools from vanilla name_lists
- empty-pool fallback + deterministic random name pick

### Changed
- defer tiny-piece merging to post-carve pass
- extract TerrainMaskPreparer — move biome/cell decisions out of writer
- replace Delaunay rasterization with unified IDW over terrain+poly-nodes
- replace per-pixel IDW with Delaunay triangle rasterization
- struct nodes, IDW roughness, relaxation step + tuned defaults
- port heightmap algorithm to Converter, invert HeightmapLab dependency
- rename HeightmapLab → TerrainLab to reflect expanding scope
- lift detail_index/intensity painters to public static for TerrainLab use
- decouple detail painters from Map
- rename CK3WaterLevel → MaxWaterByte, fix off-by-one semantics
- add LowestLandByte = MaxWaterByte + 1 for clarity
- make percentile bucketing ocean-ratio-independent
- drop dead roughness-threshold settings + ComputeRoughness
- write hills/mountain/snow masks as black (Map Editor input only)
- lift detail-level percentile cutoffs to named constants

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
