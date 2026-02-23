# AzgaarToCK3 — TODO

## High Priority

- [ ] **Province history writer** — implement `Converter/Lemur/Writers/ProvinceHistoryWriter.cs`. Every barony needs `culture`, `religion`, and `holding = auto` in `history/provinces/`. Without this, no game can be started. Modelled on upstream `MapManager.WriteHistoryProvinces`. See `CK3_MAP_MODDING_FACTS.md` § Province History.
- [ ] **Province 409 TOO LARGE BOX** — identify which barony/wasteland is index 409 in `AllProvinces`, inspect its cells for geographic spread across the map, split if needed.
- [ ] Fix `PackBurgJsonConverter` — burgs array starts with `[0, {...}]`, needs dummy burg object (same fix as `PackProvinceJsonConverter`). Error: `"The JSON value could not be converted to Converter.Burg. Path: $.pack.burgs[0]"`

## Medium Priority

- [ ] **Province terrain** — currently all provinces are `plains`. Map Azgaar biome data to CK3 terrain types per province.
- [ ] **Black pixels in provinces.png** — multiplayer desync warning. Audit wasteland rendering; ensure all cells are painted with a province colour.
- [ ] Implement major/navigable river processing (stubbed; minor rivers are done)
- [ ] Rename `ConsoleUI` project to `AzgaarToCK3.CLI`
- [ ] Delete TCS leftover history files (`k_maghreb.txt`) — log noise only

## Low Priority / Future

- [ ] `adjacencies.csv` writer — sea/river crossing connections between baronies
- [ ] Per-biome graphical region assignment (currently all provinces use `graphical_western`)
- [ ] Handle edge case: burgs without provinces (in a state but not assigned to any province)
- [ ] Handle edge case: non-contiguous baronies (a barony's cells may not all be connected)
- [ ] Add test data covering all edge cases (islands, burgs in wilderness, provinces without states)
- [ ] Investigate IsTributary misclassification (rivers with `parent == self` incorrectly flagged; pre-existing bug)

## Done

- [x] Build succeeds with .NET 8.0 (0 errors)
- [x] Fix `PackProvinceJsonConverter` — dummy province for leading `0` in array
- [x] Lemur territory generation pipeline (Cells → Baronies → Counties → Duchies → Kingdoms → Empires)
- [x] Minor river drawing to PNG (CK3-compatible indexed PNG, correct palette, orthogonal A* pathfinding, tributary support, validator)
- [x] `definition.csv` — fixed UTF-8 BOM issue (was writing with BOM, CK3 requires no BOM)
- [x] `province_terrain` — fixed wrong key (`default=` → `default_land=`, `default_sea=`, `default_coastal_sea=`)
- [x] `ProvinceImageValidator` — improved to report per-province colour mismatches with ID/name/colour
- [x] `GeographicalRegionWriter` — assigns all land provinces to `graphical_western` via LIOS override; verified 0 geographical_region.cpp errors in CK3 log
- [x] `CK3_MAP_MODDING_FACTS.md` — comprehensive verified facts reference document
