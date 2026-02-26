namespace Converter.Lemur.Writers;

public static class StaticFilesWriter
{
    public static async Task Write(string tcsSandboxPath, string outputDirectory)
    {
        var mapDataDir = Helper.GetPath(outputDirectory, "map_data");
        Directory.CreateDirectory(mapDataDir);

        await WriteTextFiles(mapDataDir);
        CopyHeightmapBinaries(tcsSandboxPath, mapDataDir);
    }

    private static async Task WriteTextFiles(string mapDataDir)
    {
        var enc = Helper.Utf8Bom;

        // seasons.txt — seasonal date ranges (same as vanilla/TCS)
        await File.WriteAllTextAsync(Helper.GetPath(mapDataDir, "seasons.txt"),
            "spring = {\n" +
            "\tstart = { month=2 day=15 }\n" +
            "\tend = { month=5 day=15 }\n" +
            "}\n" +
            "summer = {\n" +
            "\tstart = { month=5 day=15 }\n" +
            "\tend = { month=8 day=15 }\n" +
            "}\n" +
            "fall = {\n" +
            "\tstart = { month=8 day=15 }\n" +
            "\tend = { month=11 day=15 }\n" +
            "}\n" +
            "winter = {\n" +
            "\tstart = { month=11 day=15 }\n" +
            "\tend = { month=2 day=15 }\n" +
            "}\n", enc);

        // climate.txt — empty winter/summer climate blocks
        await File.WriteAllTextAsync(Helper.GetPath(mapDataDir, "climate.txt"),
            "mild_winter = {\n}\n" +
            "normal_winter = {\n}\n" +
            "severe_winter = {\n}\n", enc);

        // island_region.txt — no islands to declare
        await File.WriteAllTextAsync(Helper.GetPath(mapDataDir, "island_region.txt"), "", enc);

        // positions.txt — empty (CK3 can derive positions from provinces)
        await File.WriteAllTextAsync(Helper.GetPath(mapDataDir, "positions.txt"), "", enc);

        // heightmap.heightmap — 9-line text config referencing the binary PNGs
        await File.WriteAllTextAsync(Helper.GetPath(mapDataDir, "heightmap.heightmap"),
            "heightmap_file=\"map_data/packed_heightmap.png\"\n" +
            "indirection_file=\"map_data/indirection_heightmap.png\"\n" +
            "original_heightmap_size={ 8192 4096 }\n" +
            "packed_heightmap_size={ 2048 1024 }\n" +
            "indirection_heightmap_size={ 512 256 }\n" +
            "heightmap_max_height=25.5\n" +
            "sea_level=3.8\n" +
            "min_height=-4.0\n" +
            "max_height=50.0\n", enc);

        Console.WriteLine("Wrote static map_data text files (seasons, climate, island_region, positions, heightmap.heightmap)");
    }

    private static void CopyHeightmapBinaries(string tcsSandboxPath, string mapDataDir)
    {
        // Try TCS mod (3595862458 = newer, 2524797018 = older)
        string[] tcsIds = ["3595862458", "2524797018"];
        string? tcsBase = null;

        // tcsSandboxPath already points to one of these — check if it exists, else try siblings
        if (Directory.Exists(tcsSandboxPath))
        {
            tcsBase = tcsSandboxPath;
        }
        else
        {
            // Fall back: try the other workshop ID in the same parent directory
            var workshopContent = Path.GetDirectoryName(tcsSandboxPath);
            if (workshopContent != null)
            {
                foreach (var id in tcsIds)
                {
                    var candidate = Helper.GetPath(workshopContent, id);
                    if (Directory.Exists(candidate)) { tcsBase = candidate; break; }
                }
            }
        }

        if (tcsBase == null)
        {
            Console.WriteLine("WARNING: TCS mod not found — packed_heightmap.png, indirection_heightmap.png not copied.");
            Console.WriteLine("         CK3 will crash on map load. Copy these manually from any TCS mod installation.");
            return;
        }

        // Binary heightmap files
        string[] binaries = ["packed_heightmap.png", "indirection_heightmap.png"];
        foreach (var file in binaries)
        {
            var src = Helper.GetPath(tcsBase, "map_data", file);
            var dst = Helper.GetPath(mapDataDir, file);
            if (File.Exists(src))
            {
                File.Copy(src, dst, overwrite: true);
                Console.WriteLine($"Copied {file} from TCS mod");
            }
            else
            {
                Console.WriteLine($"WARNING: {file} not found in TCS mod at {src}");
            }
        }
    }
}
