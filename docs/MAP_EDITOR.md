# Map Editor support

Converted maps open in the CK3 map editor with the terrain pre-painted from your map's biomes — smoothly blended to match what the game renders, not a blank pink canvas. This is **on by default**: a fresh conversion is editor-ready with nothing extra to do.

You only need the steps below if you've **already converted and customised** a mod (faiths, titles, characters, …) and want to add the editor terrain *without* losing that work.

## Adding Map Editor support to a map you've already customised

This refreshes only the map terrain. Your faiths, titles, characters, history and cultures stay untouched.

1. **Back up your whole mod folder** somewhere safe.

2. Open `settings.json` (next to the converter, `AzgaarToCK3.exe`) in a text editor and set:

   ```json
   "AutoWipeOutput": false
   ```

3. Replace the `"Writers"` block with this:

   ```json
   "Writers": {
     "DefinitionCsv": false,
     "DefaultMap": false,
     "Adjacencies": false,
     "MapStaticFiles": false,
     "GeographicalRegions": false,
     "LandedTitles": false,
     "ProvinceTerrain": false,
     "MapDefines": false,
     "Religion": false,
     "Faiths": false,
     "Cultures": false,
     "TerrainMasks": true,
     "Flatmap": false,
     "Locators": false,
     "Characters": false,
     "TitleHistory": false,
     "ProvinceHistory": false,
     "Heightmap": true,
     "Bookmark": false,
     "Flavorization": false,
     "MapEditor": true
   }
   ```

4. **Run the converter** the same way you ran it the first time. No special flags — the `"AutoWipeOutput": false` above is what stops it erasing your mod.

5. **Put your heightmap back.** The run rebuilds the heightmap from your map export, so copy these four files from your backup's `map_data` folder back into your mod's `map_data` folder, overwriting:

   - `heightmap.png`
   - `heightmap.heightmap`
   - `packed_heightmap.png`
   - `indirection_heightmap.png`

6. **Open the Map Editor** — done.

> Your `settings.json` must still point at your original map export. Heads-up: the mountain/hill texture masks are built from the rebuilt heightmap, so on a hand-edited heightmap they may not line up perfectly — just repaint those in the editor if it bugs you.

## Turning it off

Editor masks are on by default. To skip them (slightly smaller output), set `"MapEditor": false` in the `Writers` block. Converted maps still open in the editor — the masks will just be blank for you to paint from scratch.
