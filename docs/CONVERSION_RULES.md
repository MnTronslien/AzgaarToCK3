# Conversion Rules

How your Azgaar data becomes a CK3 world — and where manual prep pays off.

The converter is designed to work well with the default settings in Azgaar — the philosophy being that you probably started tweaking and falling in love with a default map before you started to understand what the best settings are.

But for the very best results some handcrafting is recommended.

---

## Mapping table

| Azgaar | CK3 | Notes |
|--------|-----|-------|
| Burg | Barony (province) | Exact 1:1. Every burg becomes one barony. Cells within a province are distributed among the burgs using a BFS algorithm along the neighbour graph. If there are cells that it cannot reach through land-only neighbour traversal (such as if blocked by a major river or ocean) it will make a temporary connection and continue using the same BFS algorithm.|
| Province | Duchy | Exact 1:1. For very small states they can be generated without any Provinces in Azgaar. In those cases we backform a province from the state and use that as basis for the Duchy in CK3. Provinces without any burgs are skipped and rendered as wasteland.|
| State | Kingdom | Approximate. Small states are absorbed into larger kingdoms — see De Jure Consolidation below. But they remain de facto independents as a duchy on game start. |
| **County** | County | **Inferred** counties are constructed by grouping baronies together. The algorithm targets equal population per county and respects duchy boundaries. The balance tries to mimic base CK3 where in populous urbanized parts of the world there are fewer baronies per county (ref Byzantium), but in sparsely populated areas of the world (Russia, Nordics) there are more baronies per county. All this while maintaining a decent ratio between duchies and counties. The county capital is the most populous barony, unless a barony is marked as the province or state capital in Azgaar — that takes precedence. |
| **Empire** | Empire | **Inferred** — grouped by culture or faith, depending on the `EmpireFromCulture` setting. |
| Culture | Culture | Name and lineage from Azgaar. Pillars and traditions are selected randomly for root cultures and then mutated randomly for diverging cultures and merge-mutated for hybrid cultures. Culture also randomly selects a "theme bundle" — a converter concept for namelist, clothing, genes and architecture. |
| Religion | Faith + Religion group | Each root Azgaar religion becomes a religion group. Offshoot religions become faiths within their root religion's group. Tenets and doctrines are randomly selected for the base faith, and mutated slightly for new religions in the same group. |
| Cell | — | Data basis for culture/religion distribution and province geometry. |


The same Azgaar map, before and after conversion:

| Azgaar | CK3 |
|--------|-----|
|**Political** ![Azgaar political map](images/azgaar_political.png) | ![CK3 kingdoms](images/ck3_kingdoms.png) |
|**Cultural** ![Azgaar culture map](images/azgaar_cultures.png) | ![CK3 culture map](images/ck3_cultures.png) |
|**Religious** ![Azgaar religion map](images/azgaar_religion.png) | ![CK3 religion map](images/ck3_religion.png) |

---

The images below show the same map at each tier — each colour is a distinct title:

| Counties | Duchies |
|----------|---------|
| ![Counties](images/pipeline_counties.png) | ![Duchies](images/pipeline_duchies.png) |

| Kingdoms | Empires |
|----------|---------|
| ![Kingdoms](images/pipeline_kingdoms.png) | ![Empires](images/pipeline_empires.png) |

Zooming in: barony positions faithfully reflect Azgaar burg placement.

![Close-up of barony placement in CK3](images/ck3_barony_closeup.png)

---

## Biome textures

Each Azgaar biome is rendered with a chosen CK3 ground texture, plus steepness-driven hills/mountain overlays and a coastal beach/seafloor blend. All of this lives in one file — `Converter/Lemur/Splats/MaterialRegistry.cs` — and a single line maps one biome to one texture.

| Azgaar biome | CK3 texture |
|---|---|
| Wetland (swamp) | `wetlands_02` |
| Grassland | `plains_01` |
| Savanna | `plains_01_dry` |
| Hot Desert | `desert_01` |
| Cold Desert | `desert_02` |
| Tropical Seasonal Forest | `drylands_01_grassy` |
| Temperate Deciduous Forest | `forest_leaf_01` |
| Tropical Rainforest | `forest_jungle_01` |
| Temperate Rainforest | `forest_pine_01` |
| Taiga | `forestfloor` |
| Tundra | `northern_plains_01` |
| Glacier | `snow` |

Three extra materials run on top of the biome layer:

| Material | When it fires | CK3 texture |
|---|---|---|
| Hills | Mid-steepness slopes (steepness tent 0.20 → 0.40 → 0.75) | `hills_01` |
| Mountain | High-steepness slopes (steepness ramp 0.65 → 1.0) | `central_mountain` |
| Beach | Narrow band at the waterline, asymmetric (3 bytes inland, 30 bytes underwater) | `beach_02` |
| Seafloor | Underwater coastal blend matching the beach band | `mud_wet_01` |

These texture choices are a starting point — see [CONTRIBUTING.md](../CONTRIBUTING.md) for how to propose better ones.

---

## Province terrain (gameplay)

Each barony's CK3 terrain type is picked by scoring it against every candidate terrain and taking the highest score. Two rule shapes contribute:

- **Biome rules** sum the relevant Azgaar biome fractions across the barony's cells. A 60 % Forest / 40 % Jungle barony scores `0.6` for Forest and `0.4` for Jungle. A pure-biome barony scores `1.0`.
- **Steepness rules** (`Hills`, `Mountains`, `DesertMountains`) score on the fraction of cells in a roughness band, and may exceed 1.0 so dominant relief overrules biome cover. Crossovers vs a pure biome: Hills at 67 % hill-grade cells, Mountains/DesertMountains at 50 % mountain-grade cells.

### Azgaar biome → CK3 terrain (default)

| Azgaar biome | CK3 terrain |
|---|---|
| Wetland | `wetlands` |
| Grassland | `plains` |
| Savanna | `drylands` |
| Hot Desert | `desert` (or `desert_mountains` on dominant relief) |
| Cold Desert | `steppe` |
| Tropical Seasonal Forest | `drylands` |
| Temperate Deciduous Forest | `forest` |
| Tropical Rainforest | `jungle` |
| Temperate Rainforest | `forest` |
| Taiga | `taiga` |
| Tundra | `taiga` |
| Glacier | `taiga` (flips to `mountains` on dominant relief) |

### Typical output (Showcase test map, 1094 baronies)

![Terrain overview — Showcase](images/terrain_overview_showcase.png)

```
jungle               490  ( 44.8%)
drylands             272  ( 24.9%)
forest               234  ( 21.4%)
hills                 50  (  4.6%)
wetlands              25  (  2.3%)
mountains             19  (  1.7%)
desert                 4  (  0.4%)
```

Showcase is a tropical archipelago with cold arid uplands in the north. A temperate continental map would show different proportions (more `forest`, more `plains`, a long `taiga` belt).

---

## De Jure Consolidation

Small kingdoms and empires are absorbed into larger neighbours to prevent the map fragmenting into dozens of tiny de jure realms.

**Eligibility:** A kingdom with fewer than `MinimumDuchiesPerKingdom` duchies (default: 4) is a merge candidate. An empire with fewer than `MinimumKingdomsPerEmpire` kingdoms (default: 3) is a merge candidate.

**Merge target:** The candidate is absorbed into the neighbour with the most shared cell borders. For kingdoms, a neighbour that shares cultural or religious ancestry is preferred.

**What happens to the title:** The original kingdom or empire title is removed. Its duchies are re-parented into the merge target, becoming de jure part of that larger realm. No ruler is assigned to the merged kingdom — so the dukes of those duchies start the game as independent rulers who happen to be de jure members of a kingdom they don't recognise.

This keeps the map politically fragmented in a way that reflects the Azgaar data, while giving CK3 a coherent de jure structure to build from.

---

## Theme bundles

A **theme bundle** is the converter's internal concept for a coherent cultural aesthetic package. Each culture is assigned one theme bundle, which determines the visual and naming style CK3 uses for that culture's rulers, buildings, armies, and coats of arms.

A bundle groups five CK3 GFX keys and a name list:

| Component | Controls |
|-----------|---------|
| `coa_gfx` | Coat of arms style |
| `building_gfx` | Building appearance on the map |
| `clothing_gfx` | Ruler portrait clothing |
| `unit_gfx` | Army unit appearance |
| Name list | Pool CK3 draws character names from |

Bundles are currently assigned randomly per culture (seeded). The following bundles are available — this list is a snapshot and will expand over time:

| Bundle | Clothing | Units | Names |
|--------|----------|-------|-------|
| `western` | Western European | Western | English |
| `byzantine` | Byzantine | Eastern | Greek |
| `mena` | Middle Eastern / North African | Eastern | Arabic |
| `northern` | Norse / Northern European | Western | Norse |

Future work: bundle selection driven by Azgaar's `nameBase` field so cultures with a Norse namebase automatically receive the `northern` bundle, Arabic → `mena`, etc.

---

## Culture inheritance in practice

Each culture inherits its heritage and language from its Azgaar lineage. Ethos and traditions mutate slot-by-slot as you move down the tree — sibling cultures share the same ancestors but diverge independently.

The Azgaar culture tree for the Showcase map:

![Azgaar culture lineage tree](images/azgaar_culture_tree.png)

Two sibling cultures in CK3 — same heritage and language (both Elfish), different ethos and traditions:

| Eldar (Elfish) | Quenian (Elfish) |
|----------------|-----------------|
| ![Eldar Elfish culture tab](images/ck3_culture_tab.png) | ![Quenian Elfish culture tab](images/ck3_culture_tab_sibling.png) |

---

## Faiths in practice

Each Azgaar religion becomes a faith with its own doctrine set and holy sites. Child faiths inherit from their parent and mutate slot-by-slot.

| Faith doctrines and tenets | Holy sites with modifiers |
|---------------------------|--------------------------|
| ![Ormlarism faith tab](images/ck3_religion_tab.png) | ![Ormlarism holy sites](images/ck3_holy_sites.png) |

---

## Major Rivers

Rivers with a discharge value at or above `MajorRiverThreshold` are treated as navigable rivers in CK3. They might need a little help to look their best.
Tips:
- Move the river control point closer to one or the other side of the cells they run through.
- Move burgs that are in the middle of rivers to the side of the river.
- Shape province borders along rivers.

**How it works:**

1. Each major river is traced as a ribbon of cells following the Azgaar river path using A\* pathfinding. The ribbon width (in cells) is set by `RiverProvinceCellCount`.
2. The ribbon cells are carved out of any land cells they overlap, splitting land cells along the river edge.
3. The carved ribbon becomes a river province.
4. Very small fragments produced by carving (less than 30% the size of the main piece) are absorbed into their geometrically closest land neighbor to prevent orphaned slivers.

---

## Fine-tuning your Azgaar map

The converter works from any default Azgaar map. These areas benefit from deliberate choices in Azgaar. None are required.

**Provinces without burgs become wasteland.**
A province with no burgs generates no county and no playable territory. Add at least one burg if you want a region inhabited.

**Province size and burg count affect county quality.**
The converter works best with 4–12 burgs per province, which produces roughly 2–3 counties of about 4 baronies each. Provinces with only 1–2 burgs result in very thin duchies. Clean up province borders where Azgaar has produced oddly shaped or near-empty provinces.

**More burgs = richer duchy.**
A province with 10 burgs becomes a duchy with 10 baronies. A province with 1 burg becomes a duchy with 1 barony.

**Culture and religion distribution is cell-weighted.**
Each barony's starting culture and religion are determined by a vote across the cells that make up that barony. A barony on a cultural border reflects whichever culture holds more cells. Adjusting cultural borders in Azgaar directly adjusts the CK3 output.

**Major rivers work best along duchy borders.**
A major river that runs between two Azgaar provinces produces clean barony borders on both sides. A river that cuts through the middle of a single province will divide that province's baronies but cannot prevent them from being assigned across the river — the converter does its best to reassign cross-river cells, but the result is cleaner when rivers follow your political boundaries.

**Check discharge values before setting the threshold.**
In Azgaar, hover over a river to see its discharge. Major rivers typically read in the hundreds to thousands; minor rivers are in the single or low double digits. Set `MajorRiverThreshold` just below the discharge of the rivers you want to be navigable. A threshold of 1000 is a reasonable starting point for most maps.

**Fewer major rivers = faster conversion and cleaner maps.**
Each major river requires cell carving and geometry operations. 2–5 major rivers per map is a good sweet spot. If conversion is slow or province geometry looks fragmented near rivers, try raising the threshold to reduce the number of rivers processed.
