# LemurAlgorithm Analysis & Implementation Plan

## Test Cases to Handle

### Edge Cases (from previous work)
These are scenarios that exist in Azgaar data that need special handling:

1. **Islands** - Disconnected land masses
2. **Islands with burgs** - Settlements on islands
3. **Burgs without provinces** - Settlement exists in a state but not assigned to any province
4. **Burgs in wilderness** - Settlements in province 0 (wasteland)
5. **Provinces without states** - Province exists but not in any state (becomes wilderness)
6. **Non-contiguous baronies** - A barony's cells might not all be connected

**Note**: Not all need to be handled immediately, but we need test data covering these scenarios for long-term testing.

## Build & Run Status (2026-02-15)

### ✅ Build Status
- **SUCCESS**: Project builds with .NET 8.0
- 190 warnings (outdated packages, nullability)
- 0 errors

### ✅ Fixed Issues
1. **PackProvinceJsonConverter** - Fixed to create proper dummy province `{"i":0,"state":0,"burg":0,"name":""}` instead of empty `{}`

### ❌ Current Runtime Issues
1. **PackBurgJsonConverter** - Same issue as provinces, needs similar fix for burgs array starting with `0`
   - Error: "The JSON value could not be converted to Converter.Burg. Path: $.pack.burgs[0]"
   - Burgs array also starts with `[0, {...}]` and needs dummy burg object

2. **Test Data**: Using fresh Touria export from Azgaar (2026-02-15)

### 📝 Refactoring Notes
- **TODO**: Rename `ConsoleUI` project to something more descriptive (e.g., `AzgaarToCK3.CLI` or `Converter.CLI`)

---

# LemurAlgorithm Analysis & Implementation Plan

## Current State

### What You Built (LemurAlgorithm Branch)

**✅ Complete:**
- Cell-based map structure (Azgaar cells as atomic units)
- Burg → Barony generation (1:1 relationship)
- Outward growth algorithm (baronies claim cells from burg center)
- Graph-based partitioning system for balancing territories by population
- County generation (baronies grouped via graph partitioning)
- Duchy generation (from Azgaar provinces)
- Kingdom generation (grouped by culture-like criteria)
- Empire generation (top-level grouping)
- Visualization tools (image generation for debugging)
- Wasteland province handling
- Island support (non-land-connected territories)

**❌ Missing:**
- CK3 mod format output (no file writers for game files)
- Integration with existing ModManager/TitleManager
- All the "Write" methods from upstream

### What Upstream Has (pryvyd9/AzgaarToCK3)

**Complete pipeline with file writers for:**
1. Province map images (provinces.png, rivers.png, flatmap, heightmap)
2. Map data files (definition.csv, default.map)
3. Landed titles (common/landed_titles/*.txt)
4. Title localization (localization/english/*.yml)
5. Province history (history/provinces/*.txt)
6. Character generation & history (history/characters/*.txt)
7. Title history (history/titles/*.txt)
8. Dynasties & localization
9. Culture & religion files
10. Holy sites
11. Terrain definitions
12. Locators (map object placement)

**Upstream's territorial approach:**
- Azgaar Province → CK3 Barony (1:1)
- 4 Baronies → County
- Azgaar State → Duchy
- By Culture → Kingdom
- By Religion → Empire

**Your (Lemur) territorial approach:**
- Azgaar Cell → Cell (atomic unit)
- Azgaar Burg → CK3 Barony (1:1, more granular!)
- Graph-partitioned Baronies → County (population-balanced)
- Azgaar Province → Duchy
- Custom hierarchy → Kingdoms & Empires

## Key Differences

### Granularity
- **Upstream**: Each Azgaar province becomes ONE barony → ~100-200 baronies total
- **Lemur**: Each Azgaar settlement becomes ONE barony → ~1000+ baronies total (much more detailed!)

### Balance
- **Upstream**: Fixed 4 baronies per county (simple but rigid)
- **Lemur**: Population-based graph partitioning (smarter, more balanced)

### Philosophy
- **Upstream**: Quick conversion, Azgaar → CK3 with minimal processing
- **Lemur**: Intelligent territorial generation, respects population density

## The Gap

Your LemurAlgorithm generates **better territory structure** but has **zero CK3 file output**.

The upstream has **complete CK3 output** but uses a **simpler territory algorithm**.

## Path Forward

### Option 1: Adapt Upstream Writers to Lemur Entities
**Pros:**
- Keep your superior territorial algorithm
- Leverage tested file writing code
- Clean separation: territory generation vs. file output

**Cons:**
- Need to adapt upstream code to Lemur entity structure
- Entities are different (e.g., `Converter.Lemur.Entities.County` vs. `Converter.County`)

### Option 2: Write New File Outputs from Scratch
**Pros:**
- Full control, no legacy code
- Can optimize for Lemur's structure

**Cons:**
- More work, need to learn CK3 file formats
- Higher risk of bugs

### Option 3: Hybrid Approach (Recommended)
1. Keep Lemur territory generation (Cells → Baronies → Counties → Duchies → Kingdoms → Empires)
2. Create adapter layer to convert Lemur entities to format expected by upstream writers
3. Use upstream's file writing code with minimal modifications
4. Test and iterate

## Next Steps

1. **Understand CK3 mod structure** - Look at actual CK3 game files
2. **Create entity adapter** - Bridge Lemur entities to writer-compatible format
3. **Port key writers** - Start with landed_titles (most critical)
4. **Test with CK3** - Verify the game can load the mod
5. **Iterate** - Add remaining writers (history, characters, etc.)

## Files to Focus On

### From Upstream (adapt these):
- `ModManager.cs` - Orchestrates all file writing
- `TitleManager.cs` - Writes landed_titles and localization
- `MainConverter.cs` - Province maps, definitions, history
- `CharacterManager.cs` - Character and dynasty generation
- `CK3FileSystem.cs` - File path management

### In Lemur (your code):
- `ConversionManager.cs` - Territory generation (working!)
- `Entities/*.cs` - Your entity structure (working!)
- `Graphs/Graph.cs` - Partitioning algorithm (working!)

### Missing (need to create):
- `Lemur/Writers/` - New directory for CK3 format output
- Entity adapters or interfaces to make Lemur entities compatible with writers

## Questions to Answer

1. Do you want to keep both entity systems (old + Lemur) or fully migrate?
2. Should we test the upstream code first to understand its output?
3. Do you have CK3 installed to test generated mods?
4. What's your priority: get something working quickly vs. clean architecture?
