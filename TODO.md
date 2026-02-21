# AzgaarToCK3 — TODO

## High Priority

- [ ] Fix `PackBurgJsonConverter` — burgs array starts with `[0, {...}]`, needs dummy burg object (same fix as `PackProvinceJsonConverter`). Error: `"The JSON value could not be converted to Converter.Burg. Path: $.pack.burgs[0]"`
- [ ] Create `Converter/Lemur/Writers/` directory and implement CK3 file writers for Lemur entities (landed_titles is the most critical first step)

## Medium Priority

- [ ] Create entity adapter layer: map Lemur entities → format expected by upstream writers (ModManager, TitleManager, etc.)
- [ ] Implement major river processing (currently stubbed, minor rivers draw to PNG only)
- [ ] Rename `ConsoleUI` project to `AzgaarToCK3.CLI`

## Low Priority / Future

- [ ] Handle edge case: burgs without provinces (in a state but not assigned to any province)
- [ ] Handle edge case: non-contiguous baronies (a barony's cells may not all be connected)
- [ ] Add test data covering all edge cases (islands, burgs in wilderness, provinces without states)
- [ ] Investigate rivers implementation — current approach may need rework

## Done

- [x] Build succeeds with .NET 8.0 (0 errors)
- [x] Fix `PackProvinceJsonConverter` — dummy province for leading `0` in array
- [x] Lemur territory generation pipeline (Cells → Baronies → Counties → Duchies → Kingdoms → Empires)
- [x] Minor river drawing to PNG
