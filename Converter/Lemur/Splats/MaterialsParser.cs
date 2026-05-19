using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Converter.Lemur.Splats;

// Parses CK3's gfx/map/terrain/materials.settings.
//
// The file is a Paradox-flavoured config (Clausewitz syntax-ish): a top-level `{ ... }` block
// containing many `{ name = "..."; diffuse = "..."; ... }` entries. Each entry's ordinal position
// in the file IS the material index used by detail_index.tga. The file's comment "Dynamic
// materials - reliant on material index, so don't change the order of these" makes this explicit.
//
// Lines (or whole blocks) prefixed with `#` are commented out and DO NOT count toward the index.
// The mountain_02 chunk around lines 325-403 of vanilla 1.18.4 is the canonical example —
// the `#` shifts those entries out of the active list, so `mud_wet_01` (line 406) takes
// whatever index follows the last active entry above the commented chunk.
//
// We don't need a full Clausewitz parser. The structure we care about is just:
//   - lines starting with `#` (after whitespace) are skipped
//   - any remaining line matching `name = "<x>"` is the next active entry; its index is the
//     count of active entries we've seen so far.
public static class MaterialsParser
{
    private static readonly Regex NameLine = new(
        @"^\s*name\s*=\s*""([^""]+)""",
        RegexOptions.Compiled);

    public sealed record ParseResult(
        IReadOnlyList<(byte Index, string Name)> Materials,
        string SourcePath,
        DateTime SourceMtimeUtc,
        string SourceSha256);

    public static ParseResult Parse(string materialsSettingsPath)
    {
        var bytes = File.ReadAllBytes(materialsSettingsPath);
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var mtime = File.GetLastWriteTimeUtc(materialsSettingsPath);
        var lines = File.ReadAllLines(materialsSettingsPath);

        var materials = new List<(byte Index, string Name)>();
        int nextIndex = 0;
        foreach (var raw in lines)
        {
            var trimmed = raw.TrimStart();
            // Commented-out lines and whole commented blocks: `#` is the only comment marker used
            // in vanilla materials.settings. `//` not observed but cheap to skip too.
            if (trimmed.StartsWith('#') || trimmed.StartsWith("//")) continue;
            var m = NameLine.Match(raw);
            if (!m.Success) continue;

            if (nextIndex > byte.MaxValue)
                throw new InvalidDataException(
                    $"materials.settings has more than {byte.MaxValue + 1} active entries — " +
                    "CK3 byte indices wrap. Check the file or our parser logic.");

            materials.Add(((byte)nextIndex, m.Groups[1].Value));
            nextIndex++;
        }
        return new ParseResult(materials, materialsSettingsPath, mtime, sha);
    }
}
