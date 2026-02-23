# Notes For Later

## Graphical Region Assignment (Terrain Textures)

File written: `[mod]/map_data/geographical_regions/z_lemur_geographical_regions.txt`
Writer: `Converter/Lemur/Writers/GeographicalRegionWriter.cs`

CK3 requires every land province to be assigned to a *visual geographical region* — a region declared with `graphical = yes`. These drive which terrain texture set the engine renders for each province. Without this, CK3 errors with "Province N has no visual geographical region assigned" × all provinces, likely preventing the game from reaching the main menu.

**Current choice:** All land provinces are assigned to `graphical_western` (Western European texture style — rolling hills, oak forests, grey skies). This was chosen because:
- TCS provides `graphical_western = {}` (empty), which we override via LIOS using our `z_lemur_...txt` file
- `graphical_western` has guaranteed texture support in the base game

**To customize:** Change the region name in `GeographicalRegionWriter.cs` from `graphical_western` to any of the other built-in graphical regions:
- `graphical_mena` — Middle East / North Africa (arid, desert textures)
- `graphical_mediterranean` — Southern Europe (dry, rocky, olive trees)
- `graphical_india` — South Asian textures
- `graphical_steppe` — Flat steppe/grassland textures
- `graphical_east_asia` — East Asian textures

Future improvement: allow per-region or per-climate zone assignment based on Azgaar biome/terrain data.

## adjacencies.csv (Barony Connections)

File: `[mod]/map_data/adjacencies.csv`

Format:
```
ID From;ID To;Type;ID Through;start_x;start_y;stop_x;stop_y;Comment
1527;1526;river_large;629;948;2791;-1;-1;London-Southwark
-1;-1;;-1;-1;-1;-1;-1;
```

Key notes:
- ID From/To/Through are province IDs from definition.csv
- Type is `sea` or `river_large`
- ID Through is the sea zone or navigable river province the connection passes through
- start/stop x,y are embark/land pixel coordinates; `-1` defaults to normal army placement
- **The terminating `-1;-1;;-1;-1;-1;-1;-1;` line is REQUIRED** — omitting it causes infinite loading screen
- Baronies can also connect via adjacent pixels in provinces.png (no entry needed for those)

TODO: Implement `AdjacenciesCsvWriter` — iterate barony pairs that share a sea zone border
and emit one `sea` entry per pair (or per coastal barony pair reachable via a sea zone).
