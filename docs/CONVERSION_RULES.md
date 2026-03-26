# Conversion Rules

How your Azgaar data becomes a CK3 world — and where manual prep pays off.

---

## Mapping table

| Azgaar | CK3 | Notes |
|--------|-----|-------|
| Burg | Barony (province) | Exact 1:1. Every burg becomes one barony. |
| Province | Duchy | Exact 1:1. |
| State | Kingdom | Approximate. Small states are absorbed into larger kingdoms — see De Jure Consolidation below. |
| Culture | Culture | Name and heritage from Azgaar. Pillars and traditions seeded-randomly assigned, mutating down the lineage. |
| Religion | Faith + Religion group | Each root Azgaar religion becomes a religion group. Offshoot religions become faiths within their root religion's group. |
| Cell | — | Data basis for culture/religion distribution and province geometry. |
| **County** | County | **Inferred** — see below. |
| **Empire** | Empire | **Inferred** — grouped by culture or faith, depending on the `EmpireFromCulture` setting. |

The same Azgaar map, before and after conversion:

| Azgaar | CK3 |
|--------|-----|
| ![Azgaar political map](images/azgaar_political.png) | ![CK3 kingdoms](images/ck3_kingdoms.png) |
| ![Azgaar culture map](images/azgaar_cultures.png) | ![CK3 culture map](images/ck3_cultures.png) |
| ![Azgaar religion map](images/azgaar_religion.png) | ![CK3 religion map](images/ck3_religion.png) |

---

## The inferred tiers

Azgaar has no direct equivalent for CK3 counties or empires. The converter creates them algorithmically.

**Counties** are formed by grouping baronies within a duchy using population-balanced graph partitioning. The algorithm targets equal population per county and respects duchy boundaries. Results are deterministic given the same input and seed. The county capital is the most populous barony, unless a barony is marked as the province or state capital in Azgaar — that takes precedence.

**Empires** group kingdoms by shared culture or faith (controlled by the `EmpireFromCulture` setting), then apply the same consolidation process described below.

The images below show the same map at each tier — each colour is a distinct title:

| Counties | Duchies |
|----------|---------|
| ![Counties](images/pipeline_counties.png) | ![Duchies](images/pipeline_duchies.png) |

| Kingdoms | Empires |
|----------|---------|
| ![Kingdoms](images/pipeline_kingdoms.png) | ![Empires](images/pipeline_empires.png) |

Zooming in: barony positions faithfully reflect Azgaar burg placement. Terrain variety is a known gap — every province currently renders as plains.

![Close-up of barony placement in CK3](images/ck3_barony_closeup.png)

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

## Fine-tuning your Azgaar map

The converter works from any default Azgaar map. These areas benefit from deliberate choices in Azgaar — none are required.

**Provinces without burgs become wasteland.**
A province with no burgs generates no county and no playable territory. Add at least one burg if you want a region inhabited.

**Province size and burg count affect county quality.**
The converter works best with 4–12 burgs per province, which produces roughly 2–3 counties of about 4 baronies each. Provinces with only 1–2 burgs result in very thin duchies. Clean up province borders where Azgaar has produced oddly shaped or near-empty provinces.

**More burgs = richer duchy.**
A province with 10 burgs becomes a duchy with 10 baronies. A province with 1 burg becomes a duchy with 1 barony.

**Culture and religion distribution is cell-weighted.**
Each barony's starting culture and religion are determined by a vote across the cells that make up that barony. A barony on a cultural border reflects whichever culture holds more cells. Adjusting cultural borders in Azgaar directly adjusts the CK3 output.
