# CK3 Map Modding — Verified Facts

Facts in this file have been verified by inspecting actual CK3/TCS game files on disk,
running the converter and observing results, or confirmed against CK3 wiki documentation.
Each entry notes the verification method.

---

## definition.csv

**Path:** `[mod]/map_data/definition.csv`

**Encoding:** UTF-8 **without** BOM.
- Verified by hex-inspecting `C:\Program Files (x86)\Steam\steamapps\common\Crusader Kings III\game\map_data\definition.csv` — starts with `30 3b 30 3b` (`0;0;0;0`), no BOM bytes (`ef bb bf`).
- Writing with BOM causes a game assertion error at startup.

**Format:**
```
0;0;0;0;Black - Impassable;x;
1;34;160;0;Barony Name;x;
2;68;64;0;Another Barony;x;
```
- Column order: `province_id ; R ; G ; B ; name ; x ;`
- Row 0 (`0;0;0;0;Black - Impassable;x;`) is **required** — it declares the impassable/background black colour.
- Province IDs must be sequential starting at 1. Gaps cause a CTD.
- R/G/B values must be in range 0–255.
- Names with empty strings are valid (base game uses them for virtual sea provinces).

**Validator behaviour (observed):**
- Base game definition.csv has 13,269 entries; 519 have no pixels in provinces.png (sea/virtual zones) — this is expected and not an error.
- It is valid to have definition.csv entries with no corresponding pixels (virtual/sea provinces).
- It is **not** valid to have pixels in provinces.png with colours absent from definition.csv.

**Writer:** `Converter/Lemur/Writers/DefinitionCsvWriter.cs`

---

## provinces.png

**Path:** `[mod]/map_data/provinces.png`

**Format:** 8-bit palette PNG **or** 24-bit TrueColor RGB. Alpha channel causes a CTD.
- Verified by running `ProvinceImageValidator` against base game file — reported `ColorType.TrueColor`.
- Our generated output reports `ColorType.Palette` (ImageMagick auto-quantizes when colour count is small enough). Both are accepted by CK3.

**Colours:** Every non-black pixel must correspond to a colour declared in definition.csv.
- Black (0,0,0) is reserved for impassable/background. Black pixels outside province 0 generate a multiplayer desync warning but are not a single-player CTD.

**Size:** Must match rivers.png exactly.

**Validator:** `Converter/Lemur/Provinces/ProvinceImageValidator.cs` — run via `--validate-provinces <path> --definition-csv <path>`.
- Format check: rejects alpha channel, accepts TrueColor or Palette.
- Cross-check: reports colours in image not in definition.csv; reports definition.csv entries with no pixels in image (per-province, with name and colour).

**Colour generation** (`Helper.GetColor(i, maxI)`):
- Divides 24-bit colour space evenly: `color = (256³ / maxI) * i`
- Byte extraction: `r = color & 0xFF`, `g = (color >> 8) & 0xFF`, `b = (color >> 16) & 0xFF`
- Result confirmed consistent between definition.csv and provinces.png by validator.

**Writer:** `Converter/Lemur/ImageUtility.cs` → `DrawProvincesImage()`

---

## rivers.png

**Path:** `[mod]/map_data/rivers.png`

**Format:** 8-bit **indexed palette** PNG. Must use the exact CK3 colour palette. Any colour not in the palette (including antialiasing or transparency) causes a CTD.
- Source: wiki, confirmed by CK3 documentation.

**Palette colours (verified from game files / `RiverImageValidator.cs`):**
| Index | Hex | Meaning |
|-------|-----|---------|
| 0 | `#00ff00` | River source marker |
| 1 | `#ff0000` | Junction / tributary end |
| 2 | `#fffc00` | Unknown special marker |
| 3 | `#00e1ff` | River body — thinnest |
| 4 | `#00c8ff` | River body |
| 5 | `#0096ff` | River body |
| 6 | `#0064ff` | River body |
| 7 | `#0000ff` | River body |
| 8 | `#0000e1` | River body |
| 9 | `#0000c8` | River body |
| 10 | `#000096` | River body |
| 11 | `#000064` | River body — widest |
| 12 | `#005500` | Land edge (dark green) |
| 13–15 | green shades | Land edge variants |
| 16 | `#ff0080` | Ocean / sea background |
| 17 | `#ffffff` | Land (white background) |

**Connectivity rules:**
- River body pixels: must have exactly 1–2 orthogonal blue neighbours.
- Source (`#00ff00`): must have exactly 1 blue neighbour.
- Junction (`#ff0000`): must have exactly 2 blue neighbours.
- Paths must be orthogonal (N/S/E/W only — no diagonals).

**Validator:** `Converter/Lemur/Rivers/RiverImageValidator.cs` — run via `--validate-rivers <path>`.

**Writer:** `Converter/Lemur/Rivers/RiverImageGenerator.cs`

---

## province_terrain

**Path:** `[mod]/common/province_terrain/00_province_terrain.txt`

**Encoding:** UTF-8 **with** BOM.
- Verified by hex-inspecting `C:\Program Files (x86)\Steam\steamapps\common\Crusader Kings III\game\common\province_terrain\00_province_terrain.txt` — starts with `ef bb bf` (BOM) then `default_land`.

**Required default keys:**
```
default_land=plains
default_sea=sea
default_coastal_sea=coastal_sea
```
- Using `default=plains` (old/wrong key) causes: `terrain_type.cpp:459: Cannot read default at 00_province_terrain.txt line: 1`.
- All three keys are required.

**Per-province format:**
```
1=plains
2=farmlands
3=hills
```

**Valid terrain values:** `plains`, `farmlands`, `hills`, `mountains`, `desert`, `jungle`, `forest`, `taiga`, `wetlands`, `steppe`, `floodplains`, `oasis`, `coastal_sea`, `sea`

**Writer:** `Converter/Lemur/Writers/ProvinceTerrainWriter.cs`

---

## geographical_regions/geographical_region.txt

**Path:** `[mod]/map_data/geographical_regions/geographical_region.txt`

**Purpose:** Declares named regions used for game mechanics (trade routes, cultural regions, special mechanics) and visual rendering. Every land province **must** be assigned to at least one region with `graphical = yes` or CK3 errors at map initialisation: `geographical_region.cpp:310: Province N has no visual geographical region assigned`.

**TCS behaviour:** TCS's `geographical_region.txt` declares all standard region names but leaves every block empty `{}`. This satisfies the scripting validator (region names exist) but assigns no provinces to any visual region.

**Graphical regions (built-in, verified from base game file):**
| Region name | Terrain style |
|-------------|--------------|
| `graphical_western` | Western Europe — rolling hills, oak forests, grey skies |
| `graphical_mena` | Middle East / North Africa — arid, desert |
| `graphical_mediterranean` | Southern Europe — dry, rocky, olive trees |
| `graphical_india` | South Asian |
| `graphical_steppe` | Flat steppe / grassland |
| `graphical_siberia` | Siberian |
| `graphical_east_asia` | East Asian |

**Declaration syntax:**
```
my_sub_region = {
    provinces = { 1 2 3 4 5 }
}

graphical_western = {
    graphical = yes
    color = { 255 0 0 }
    regions = {
        my_sub_region
    }
}
```

**LIOS override pattern:** To override TCS's empty `graphical_western = {}`, name your file so it sorts alphabetically **after** `geographical_region.txt`. We use `z_lemur_geographical_regions.txt`. Last file alphabetically wins (LIOS — Last In Only Served).

**Our solution:** `GeographicalRegionWriter.cs` writes `z_lemur_geographical_regions.txt` declaring `lemur_land_region` with all land province IDs, then overrides `graphical_western` to include it.

**Upstream (pryvyd9/AzgaarToCK3):** Does not handle geographical regions at all. Our approach is strictly better.

---

## LIOS / FIOS — File Loading Order

**LIOS (Last In Only Served):** For most CK3 script objects (scripted triggers, defines, geographical regions), when two files define the same top-level key, the **last-loaded** file's definition wins.
- Files within the same directory are loaded in **ASCII alphabetical order**.
- To override TCS or vanilla definitions, name your file so it sorts later (`z_...` > `geographical_region.txt`).

**FIOS (First In Only Served):** GUI types and templates use FIOS — first definition wins. Name your file `00_...` to take priority.

---

## Province History

**Path:** `[mod]/history/provinces/[any_name].txt`

**Required for:** holdings, culture, religion on baronies and counties.

**Format (upstream verified):**
```
<province_id> = {
    culture = english
    religion = catholic
    holding = auto
}
```
- `province_id` is the sequential ID from definition.csv (1-based).
- `holding = auto` lets CK3 choose the holding type automatically.
- The **first barony in each county** (lowest province ID) must have a holding assigned; without it the county's government type is undefined.
- CK3 uses the capital barony's culture and religion to determine the county's culture and religion.

**Upstream approach (`MapManager.WriteHistoryProvinces`):**
- Assigns a randomly selected original CK3 culture name per barony (from Azgaar's culture mapping).
- Assigns a randomly selected original CK3 religion name per barony.
- Uses `holding = auto` for all.
- Also blanks out all vanilla history/provinces files to prevent vanilla province data overriding the new assignments.

**Our Lemur implementation:** Not yet written. This is the next major missing piece.

---

## default.map

**Path:** `[mod]/map_data/default.map`

**Purpose:** Declares which province IDs are sea zones, major rivers, lakes, wastelands, and impassable terrain. Also references all map data files.

**Sea zones declaration:**
```
sea_zones = LIST { 409 410 411 412 413 }
```

**Writer:** `Converter/Lemur/Writers/DefaultMapWriter.cs`

---

## Mod File Encodings

| File type | Encoding | Verified |
|-----------|----------|---------|
| `definition.csv` | UTF-8 **no BOM** | Hex inspection of game file |
| `province_terrain/*.txt` | UTF-8 **with BOM** | Hex inspection of game file |
| `landed_titles.txt` and most script files | UTF-8 **with BOM** | Wiki + convention |
| `localization/*_l_english.yml` | UTF-8 **with BOM** | Wiki (required for CK3 to read) |
| Localization files in `replace/` subfolder | UTF-8 **with BOM** | Wiki |

**In code:**
```csharp
Helper.Utf8Bom   = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
Helper.Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
```

---

## TCS (Total Conversion Sandbox) Dependency

**Steam Workshop ID:** `3595862458` (newer), `2524797018` (older)
**Path:** `C:\Program Files (x86)\Steam\steamapps\workshop\content\1158310\<id>\`

**What TCS provides:**
- `map_data/packed_heightmap.png` — binary heightmap (required, cannot generate)
- `map_data/indirection_heightmap.png` — binary heightmap index (required, cannot generate)
- `map_data/geographical_regions/geographical_region.txt` — all region names as empty blocks (needed so the scripting validator doesn't error on missing region names)

**What TCS does NOT provide:**
- Any province assignments (all blocks empty)
- Cultures, religions, characters, history

**Upstream AzgaarToCK3:** Does not use TCS as a dependency — it is a standalone map replacement for vanilla CK3.

---

## Known Validator Tools (CLI)

```
# Validate provinces.png colour consistency with definition.csv
./ConsoleUI --validate-provinces <path/to/provinces.png> --definition-csv <path/to/definition.csv>

# Validate rivers.png palette and connectivity
./ConsoleUI --validate-rivers <path/to/rivers.png>
```

Both tools exit immediately without running a full conversion.

---

## Upstream vs Lemur Feature Comparison

| Feature | Upstream (pryvyd9) | Lemur (ours) |
|---------|-------------------|--------------|
| Province granularity | Azgaar Province → Barony (~100–200) | Azgaar Burg → Barony (~1000+) |
| County grouping | Fixed (4 baronies per county) | Population-balanced graph partitioning |
| Geographical regions | Not handled | ✅ `GeographicalRegionWriter` |
| Province history | ✅ culture + religion + `holding = auto` | ❌ Not yet implemented |
| Province terrain | ✅ Per-biome mapping | Flat `plains` for all |
| Rivers | Polyline draw, no validation | A* pathfinding, palette validation, `--no-rivers` fallback |
| Province colour validator | None | ✅ `ProvinceImageValidator` |
| TCS dependency | No | Yes |

