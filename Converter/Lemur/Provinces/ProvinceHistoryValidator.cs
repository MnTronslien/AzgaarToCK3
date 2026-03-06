using System.Text;

namespace Converter.Lemur.Provinces;

public static class ProvinceHistoryValidator
{
    public static bool Validate(string directory, out List<string> errors)
    {
        errors = new List<string>();

        if (!Directory.Exists(directory))
        {
            errors.Add($"Directory not found: {directory}");
            return false;
        }

        var files = Directory.GetFiles(directory, "*.txt", SearchOption.TopDirectoryOnly);
        Logger.Info($"Validating history/provinces/ ({files.Length} files)...");

        // Check 1 — Encoding
        int encodingOk = 0;
        foreach (var file in files)
        {
            if (CheckEncoding(file, out string? encError))
                encodingOk++;
            else
                errors.Add(encError!);
        }

        // Check 2 — Format + collect IDs; Check 3 — Duplicates (warning only)
        var seenIds = new Dictionary<int, (string file, int line)>(capacity: 4096);
        int formatErrorCount = 0;
        var duplicateWarnings = new List<string>();

        foreach (var file in files)
        {
            var fileErrors = new List<string>();
            var fileIds = ParseProvinceIds(file, fileErrors);

            foreach (var e in fileErrors)
            {
                errors.Add(e);
                formatErrorCount++;
            }

            var fileName = Path.GetFileName(file);
            foreach (var (id, lineNum) in fileIds)
            {
                if (seenIds.TryGetValue(id, out var prev))
                {
                    // Warning, not error — base game intentionally shares province IDs
                    // across e_china/e_dali/e_viet for DLC history overrides (last-wins, no CTD)
                    duplicateWarnings.Add($"Province {id} defined in multiple files: {prev.file} (line {prev.line}), {fileName} (line {lineNum})");
                }
                else
                {
                    seenIds[id] = (fileName, lineNum);
                }
            }
        }

        // Summary
        int encodingErrors = files.Length - encodingOk;
        Logger.Info($"  Encoding:   {(encodingErrors == 0 ? $"{files.Length}/{files.Length} OK \u2713" : $"{encodingOk}/{files.Length} OK \u2717 ({encodingErrors} bad)")}");
        Logger.Info($"  Format:     {(formatErrorCount == 0 ? $"{files.Length}/{files.Length} OK \u2713" : $"\u2717 ({formatErrorCount} error(s))")}");
        Logger.Info($"  Duplicates: {(duplicateWarnings.Count == 0 ? $"none \u2713" : $"WARN ({duplicateWarnings.Count} duplicate ID(s))")}");
        foreach (var w in duplicateWarnings)
            Logger.Warning(w);

        if (errors.Count == 0)
            Logger.Info($"  \u2713 Valid ({seenIds.Count} province IDs across {files.Length} files)");
        else
            Logger.Info($"  \u2717 Invalid ({errors.Count} total error(s))");

        return errors.Count == 0;
    }

    // -------------------------------------------------------------------------

    /// <summary>
    /// Checks that the file is readable. Accepts UTF-8 (with or without BOM) and Latin-1/Windows-1252.
    /// Base game ships some locale-specific files in Windows-1252, so both are valid.
    /// Returns false only if the file cannot be opened at all.
    /// </summary>
    private static bool CheckEncoding(string filePath, out string? error)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(filePath);
        }
        catch (IOException ex)
        {
            error = $"{Path.GetFileName(filePath)}: cannot read file: {ex.Message}";
            return false;
        }

        try
        {
            // Strict UTF-8: throws on invalid byte sequences
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // Accept as Latin-1 fallback — covers Windows-1252 (base game locale files).
            // Latin-1 maps every byte value, so this never throws.
            _ = Encoding.Latin1.GetString(bytes);
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Parses one province history file, collecting (provinceId, lineNumber) pairs.
    /// Validates brace balance and that top-level keys are integers.
    /// Tolerates nested blocks (date overrides), inline comments, and empty blocks.
    /// </summary>
    private static List<(int id, int line)> ParseProvinceIds(string filePath, List<string> errors)
    {
        var fileName = Path.GetFileName(filePath);
        var ids = new List<(int, int)>();
        int depth = 0;
        int lineNum = 0;
        int blockStartLine = 0;

        foreach (var rawLine in File.ReadLines(filePath))
        {
            lineNum++;
            var line = rawLine.Trim();

            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            // Strip inline comment — everything from # onward
            var commentIdx = line.IndexOf('#');
            var content = (commentIdx >= 0 ? line[..commentIdx] : line).Trim();
            if (content.Length == 0)
                continue;

            int opens = content.Count(c => c == '{');
            int closes = content.Count(c => c == '}');

            if (depth == 0)
            {
                if (opens > 0)
                {
                    // Top-level block — key must be an integer province ID
                    var keyPart = content[..content.IndexOf('{')].Split('=')[0].Trim();
                    if (int.TryParse(keyPart, out int id))
                    {
                        ids.Add((id, lineNum));
                        blockStartLine = lineNum;
                    }
                    else
                    {
                        errors.Add($"{fileName}:{lineNum}: non-integer top-level key \"{keyPart}\"");
                        blockStartLine = lineNum;
                    }
                    depth += opens - closes;
                }
                else if (closes > 0)
                {
                    errors.Add($"{fileName}:{lineNum}: closing brace with no open block");
                }
                else if (content.StartsWith('@'))
                {
                    // CK3 script variable declaration (@var = value) — valid at top level
                }
                else
                {
                    errors.Add($"{fileName}:{lineNum}: unexpected content outside block: \"{content}\"");
                }
            }
            else
            {
                // Inside a block — nested structures (date overrides etc.) are fine
                depth += opens - closes;
                if (depth < 0)
                {
                    errors.Add($"{fileName}:{lineNum}: unmatched closing brace (depth went negative)");
                    depth = 0;
                }
            }
        }

        if (depth > 0)
            errors.Add($"{fileName}:{blockStartLine}: unclosed block at EOF (depth {depth})");

        return ids;
    }
}
