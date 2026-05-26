# Roadmap
For bug reports and feature requests, see the [Issues tab](https://github.com/MnTronslien/AzgaarToCK3/issues).

---

## What works today

### Map
- Complete 5-tier title hierarchy: Barony → County → Duchy → Kingdom → Empire
- Handles large maps
- Population-balanced county grouping via graph partitioning
- De jure capitals set at all tiers; baronies and counties ordered capital-first
- Heightmap generated from Azgaar elevation data (all 4 CK3 files)
    - Fast, point cloud based
- Splatmap (terrain paint) generated from Azgaar biome data and heightmap
- Minor rivers drawn via A\* pathfinding with correct tributary connections on a valid rivers.png
- Major navigable rivers carved into the cell map as river provinces.
- Province terrain (gameplay) mapped per barony from Azgaar biome + cell roughness
- Map object locators (buildings, sieges, combat, unit stacks) placed at Azgaar burg positions, nudged inland from coastlines

### Cultures
- All 4 CK3 culture pillars: heritage, language, ethos, martial custom
- Heritage and language flow down the Azgaar lineage tree; ethos and traditions mutate slot-by-slot over generations
- Hybrid cultures blend both parents' pillars and phenotype distributions before applying mutation
- 41 verified base-game traditions (no DLC dependency)
- GFX bundles: clothing, building, unit, coat-of-arms graphics
- Deterministic per seed

### Faiths
- Custom faith per Azgaar religion, with its own religion group
- Child faiths inherit doctrines and tenets from their Azgaar parent, then mutate slot-by-slot (rate configurable via `DoctrineMutationRate`)
- Processed in topological order — parent faiths always resolved before children
- Holy sites with seeded random modifiers, attached to counties
- Full localization: faith names, religion group names, holy site names, adherent terms

### History & Characters
- Province history: dominant culture and religion per barony (cell-weighted)
- Named rulers per county, duchy, kingdom, and empire
- Rulers carry correct culture and faith; feudal hierarchy wired up in title history

---

## Known bugs

See the [Issues tab](https://github.com/MnTronslien/AzgaarToCK3/issues).

---

## Known gaps

**Sea crossing adjacencies not generated.**
`adjacencies.csv` is empty. Explicit strait and river crossing connections are absent; naval movement works through sea zone pixels only. More important now that major rivers are implemented.

**No one holds empire titles at game start.**
Empires exist as de jure structure only. Kings and dukes start independent; the empire tier is unclaimed. Look to

**Ruler demesne sizes are hardcoded.**
All rulers hold 1 county (weak kings by default). No setting to scale this up yet.

**Starting year and character ages are not data-driven.**
The game start date is a fixed hardcoded value. Ruler birth and death years are generated independently of any Azgaar timeline data.

**Culture GFX bundles are assigned randomly.**
Azgaar cultures carry a `nameBase` field implying a real-world analogue. The converter currently ignores it. When implemented, a Norse-namebase culture will get Norse graphics.

**All provinces use the Western texture set.**
CK3 has regional textures (Mediterranean, Steppe, MENA, etc.). Every province currently uses Western regardless of biome.

---

## Planned

- `adjacencies.csv` — sea and strait crossing connections
- Per-region graphical regions from biome/climate data
- GFX bundle selection from Azgaar `nameBase`
- Independent rulers for absorbed titles at game start
- Configurable ruler demesne sizes (`RulerStrength` setting)
- Data-driven start date and character ages
- Title colors from Azgaar province colors
- Holy site proximity selection (cap per faith at ~5 relevant sites)
- Government type mapping (Azgaar `state.form` → CK3 government)
- Religious head of faith assigned at game start
- Development levels from Azgaar population data
- Starting buildings from Azgaar burg flags

---

## Not planned

- **CK3 map editor steps** — the goal is to make manual map editor work unnecessary; it is not a supported workflow
