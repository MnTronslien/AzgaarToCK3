using L = Converter.Lemur.Entities;

namespace Converter.Lemur.Writers;

public static class GeographicalRegionWriter
{
    public static async Task Write(L.Map map, string outputDirectory)
    {
        var baronies = map.Baronies!;

        // Build barony ID → province ID mapping (1-based index in AllProvinces)
        var baronIdToProvinceId = new Dictionary<int, int>(baronies.Count);
        for (int i = 0; i < baronies.Count; i++)
            baronIdToProvinceId[baronies[i].Id] = i + 1;

        // 1. Parse all vanilla region files
        var ck3GeoDir = Helper.GetPath(
            Settings.Instance.Ck3Directory, "game", "map_data", "geographical_regions");

        List<GeographicalRegion> vanillaRegions;
        if (Directory.Exists(ck3GeoDir))
        {
            vanillaRegions = GeographicalRegionParser.ParseFiles(
                Directory.EnumerateFiles(ck3GeoDir, "*.txt"));
            Logger.Info($"Parsed {vanillaRegions.Count} vanilla geographical regions.");
        }
        else
        {
            Logger.Warning($"Warning: vanilla geographical_regions dir not found at {ck3GeoDir} — no stubs written.");
            vanillaRegions = [];
        }

        // 2. Build lemur_land_region and PREPEND to list
        var lemurLandRegion = new GeographicalRegion { Name = "lemur_land_region" };
        lemurLandRegion.DirectMembers = map.Kingdoms.Cast<L.ITitle>().ToList();

        // Kingdoms cover all baronies; wastelands are not part of any kingdom so add them via province IDs
        var allProvinces = map.AllProvinces!;
        for (int i = 0; i < allProvinces.Count; i++)
            if (allProvinces[i] is L.Wasteland)
                lemurLandRegion.ExtraProvinceIds.Add(i + 1);

        vanillaRegions.Insert(0, lemurLandRegion);

        // 3a. Find and mutate graphical_western
        var graphicalWestern = vanillaRegions.FirstOrDefault(r => r.Name == "graphical_western");
        if (graphicalWestern != null)
        {
            graphicalWestern.SubRegions = [lemurLandRegion];
            graphicalWestern.Graphical = true;
            graphicalWestern.Color = (255, 255, 0);
        }
        else
        {
            Logger.Warning("Warning: graphical_western not found in vanilla regions — appending it.");
            var gw = new GeographicalRegion { Name = "graphical_western" };
            gw.SubRegions = [lemurLandRegion];
            gw.Graphical = true;
            gw.Color = (255, 255, 0);
            vanillaRegions.Add(gw);
        }

        // 3b. Find and mutate material_wood_elm
        var materialWoodElm = vanillaRegions.FirstOrDefault(r => r.Name == "material_wood_elm");
        if (materialWoodElm != null)
        {
            materialWoodElm.SubRegions = [lemurLandRegion];
        }
        else
        {
            Logger.Warning("Warning: material_wood_elm not found in vanilla regions — appending it.");
            var mwe = new GeographicalRegion { Name = "material_wood_elm" };
            mwe.SubRegions = [lemurLandRegion];
            vanillaRegions.Add(mwe);
        }

        // 4. Serialize all regions
        var lines = new List<string>
        {
            "# Lemur conversion: geographical regions file.",
            "# Stubs are sourced from the base game; lemur_land_region is appended.",
            "",
        };

        foreach (var region in vanillaRegions)
            lines.AddRange(Serialize(region, baronIdToProvinceId));

        var path = Helper.GetPath(outputDirectory, "map_data", "geographical_regions", "geographical_region.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllLinesAsync(path, lines, Helper.Utf8Bom);

        var stubCount = vanillaRegions.Count - 1; // minus lemur_land_region itself
        Logger.Info($"Wrote geographical_region.txt ({baronies.Count} baronies via {map.Kingdoms.Count} kingdoms, ~{stubCount} vanilla stubs)");
    }

    private static IEnumerable<string> Serialize(GeographicalRegion region, Dictionary<int, int> baronIdToProvinceId)
    {
        bool isEmpty = !region.Graphical
            && region.Color == null
            && region.DirectMembers.Count == 0
            && region.ExtraProvinceIds.Count == 0
            && region.SubRegions.Count == 0;

        if (isEmpty)
        {
            yield return $"{region.Name} = {{ }}";
            yield break;
        }

        yield return $"{region.Name} = {{";

        if (region.Graphical)
            yield return "\tgraphical = yes";

        if (region.Color.HasValue)
        {
            var (r, g, b) = region.Color.Value;
            yield return $"\tcolor = {{ {r} {g} {b} }}";
        }

        // provinces = { ... } for Barony DirectMembers + ExtraProvinceIds (e.g. wastelands)
        var baroniesList = region.DirectMembers.OfType<L.Barony>().ToList();
        var provIds = baroniesList
            .Select(b => baronIdToProvinceId.TryGetValue(b.Id, out int id) ? id : -1)
            .Where(id => id > 0)
            .Concat(region.ExtraProvinceIds)
            .OrderBy(id => id)
            .ToList();
        if (provIds.Count > 0)
            yield return $"\tprovinces = {{ {string.Join(" ", provIds)} }}";

        // kingdoms = { ... }
        var kingdoms = region.DirectMembers.OfType<L.Kingdom>().ToList();
        if (kingdoms.Count > 0)
        {
            var kIds = kingdoms.Select(k => LandedTitlesWriter.ToCk3Id("k", k.Name, k.Id));
            yield return $"\tkingdoms = {{ {string.Join(" ", kIds)} }}";
        }

        // duchies = { ... }
        var duchies = region.DirectMembers.OfType<L.Duchy>().ToList();
        if (duchies.Count > 0)
        {
            var dIds = duchies.Select(d => LandedTitlesWriter.ToCk3Id("d", d.Name, d.Id));
            yield return $"\tduchies = {{ {string.Join(" ", dIds)} }}";
        }

        // counties = { ... }
        var counties = region.DirectMembers.OfType<L.County>().ToList();
        if (counties.Count > 0)
        {
            var cIds = counties.Select(c => LandedTitlesWriter.ToCk3Id("c", c.Name, c.Id));
            yield return $"\tcounties = {{ {string.Join(" ", cIds)} }}";
        }

        // Empires — not yet supported
        var empires = region.DirectMembers.OfType<L.Empire>().ToList();
        if (empires.Count > 0)
            Logger.Warning($"Warning: GeographicalRegion '{region.Name}' has Empire DirectMembers — not yet supported, skipping.");

        // regions = { ... } for sub-regions
        if (region.SubRegions.Count > 0)
            yield return $"\tregions = {{ {string.Join(" ", region.SubRegions.Select(r => r.Name))} }}";

        yield return "}";
        yield return ""; // blank line after each region block
    }
}
