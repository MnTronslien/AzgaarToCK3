# Conversion Rules

How your Azgaar data becomes a CK3 world — and where manual prep pays off.

---

## Mapping table

| Azgaar | CK3 | Notes |
|--------|-----|-------|
| Burg | Barony (province) | Exact 1:1. Every burg becomes one barony. |
| Province | Duchy | Exact 1:1. |
| State | Kingdom | Exact 1:1. |
| Culture | Culture | Name, pillars, and traditions derived from Azgaar culture data. |
| Religion | Faith + Religion group | Each Azgaar religion becomes its own faith and religion group. |
| Cell | — | Atomic map unit. Cells vote to determine each barony's dominant culture and religion. |
| **County** | County | **Inferred** — see below. |
| **Empire** | Empire | **Inferred** — see below. |

---

## The inferred tiers

Azgaar has no direct equivalent for CK3 counties or empires. The converter creates them algorithmically.

**Counties** are formed by grouping baronies within a duchy using population-balanced graph partitioning. The algorithm targets equal population per county and respects duchy boundaries. Results are deterministic given the same input data and seed.

**Empires** are formed by grouping kingdoms. The grouping uses geographic proximity and shared duchy/province relationships. A kingdom that doesn't fit cleanly into any group becomes its own empire.

---

## Fine-tuning your Azgaar map

The converter works from any default Azgaar map. These are areas where deliberate choices in Azgaar produce better CK3 output — none are required.

**Provinces without burgs become wasteland.**
A province with no burgs generates no county and no playable territory. If you want a region to be inhabited, it needs at least one burg. Ocean and sea provinces are expected to have no burgs and are handled correctly.

**Province size affects county balance.**
Very large provinces (many cells, spread out) can produce awkwardly shaped counties because the partitioning must stay within duchy boundaries. Smaller, more compact provinces partition more cleanly.

**More burgs = richer duchy.**
A province with 10 burgs becomes a duchy with 10 baronies. A province with 1 burg becomes a duchy with 1 barony. Barony count directly affects how much territory and income a duchy has in-game.

**Culture and religion distribution is cell-weighted.**
Each barony's starting culture and religion are determined by a vote across the cells that make up that barony. A barony on a cultural border will reflect whichever culture holds more cells. The border is taken from your Azgaar map directly — adjusting it in Azgaar adjusts the CK3 output.
