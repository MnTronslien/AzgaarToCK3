using System.Text;
using System.Text.RegularExpressions;

namespace Converter.Lemur;

/// <summary>
/// Loads male/female name pools from vanilla CK3 <c>game/common/culture/name_lists/*.txt</c>.
///
/// PDX format reminder: each file contains one or more top-level
/// <c>name_list_&lt;key&gt; = { ... }</c> blocks; each holds
/// <c>male_names = { Name1 Name2 ... }</c> and <c>female_names = { ... }</c>.
/// The bare identifiers inside (e.g. <c>O_lafr</c>, <c>T_orsteinn</c>) double as the
/// localization keys CK3 resolves at runtime — we pass them through unchanged into
/// <c>name = "..."</c> in our character history file.
///
/// Cached at process scope: vanilla name_lists don't change between converter runs.
/// </summary>
public static class NameListLoader
{
    private static readonly object _lock = new();
    private static Dictionary<string, (string[] Male, string[] Female)>? _namesByList;

    /// <summary>
    /// Returns <c>(male, female)</c> name pools for <paramref name="nameListKey"/>
    /// (e.g. <c>"name_list_bedouin"</c>). Throws if the key isn't found in any vanilla file
    /// under <c>&lt;ck3Dir&gt;/game/common/culture/name_lists/</c>. A pool may be empty if
    /// the vanilla block declares no names of that gender (rare).
    /// </summary>
    public static (string[] Male, string[] Female) GetNames(string ck3Dir, string nameListKey)
    {
        EnsureLoaded(ck3Dir);
        if (!_namesByList!.TryGetValue(nameListKey, out var pools))
            throw new InvalidOperationException(
                $"NameListLoader: '{nameListKey}' not found in vanilla name_lists under " +
                $"'{ck3Dir}'. Available keys: {string.Join(", ", _namesByList.Keys.Order().Take(20))}…");
        return pools;
    }

    /// <summary>Convenience: male names only. Equivalent to <c>GetNames(...).Male</c>.</summary>
    public static string[] GetMaleNames(string ck3Dir, string nameListKey) =>
        GetNames(ck3Dir, nameListKey).Male;

    private static void EnsureLoaded(string ck3Dir)
    {
        if (_namesByList != null) return;
        lock (_lock)
        {
            if (_namesByList != null) return;

            var nameListsDir = Path.Combine(ck3Dir, "game", "common", "culture", "name_lists");
            if (!Directory.Exists(nameListsDir))
                throw new DirectoryNotFoundException(
                    $"NameListLoader: vanilla name_lists directory not found at '{nameListsDir}'.");

            var loaded = new Dictionary<string, (string[] Male, string[] Female)>(StringComparer.Ordinal);
            foreach (var path in Directory.EnumerateFiles(nameListsDir, "*.txt"))
            {
                var content = StripComments(File.ReadAllText(path));
                foreach (var (key, pools) in ExtractFromFile(content))
                {
                    // Last-write wins if duplicate keys appear across files; vanilla has no overlap.
                    loaded[key] = pools;
                }
            }
            _namesByList = loaded;
        }
    }

    /// <summary>
    /// Yields <c>(name_list_key, (male, female))</c> for every <c>name_list_*</c> block in
    /// <paramref name="content"/>. A name pool may be empty if its sub-block is missing.
    /// </summary>
    private static IEnumerable<(string Key, (string[] Male, string[] Female))> ExtractFromFile(string content)
    {
        // Match any top-level identifier that starts with "name_list_"
        var listOpener = new Regex(@"^\s*(name_list_\w+)\s*=\s*\{", RegexOptions.Multiline);
        foreach (Match m in listOpener.Matches(content))
        {
            var key = m.Groups[1].Value;
            var blockStart = m.Index + m.Length;
            var blockEnd = FindMatchingBrace(content, blockStart);
            if (blockEnd < 0) continue;

            var block = content[blockStart..blockEnd];
            _ = TryExtractBareIdentifierList(block, "male_names", out var male);
            _ = TryExtractBareIdentifierList(block, "female_names", out var female);
            yield return (key, (male, female));
        }
    }

    /// <summary>
    /// Finds the index of the <c>}</c> matching the <c>{</c> that ended at <paramref name="openAfter"/>.
    /// Returns the index of the matching <c>}</c>, or -1 if unmatched.
    /// </summary>
    private static int FindMatchingBrace(string content, int openAfter)
    {
        int depth = 1;
        for (int i = openAfter; i < content.Length; i++)
        {
            if (content[i] == '{') depth++;
            else if (content[i] == '}')
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// Locates <c>&lt;blockKey&gt; = { ... }</c> within <paramref name="parent"/> and tokenises the contents
    /// as whitespace-separated bare identifiers. Returns false if the block isn't found.
    /// </summary>
    private static bool TryExtractBareIdentifierList(string parent, string blockKey, out string[] names)
    {
        names = [];
        var opener = new Regex(@"\b" + Regex.Escape(blockKey) + @"\s*=\s*\{");
        var m = opener.Match(parent);
        if (!m.Success) return false;

        var blockStart = m.Index + m.Length;
        var blockEnd = FindMatchingBrace(parent, blockStart);
        if (blockEnd < 0) return false;

        var inner = parent[blockStart..blockEnd];
        // Depth-track: vanilla blocks contain nested `{ }` (e.g. weighted `Name = { 10 }`).
        // Only collect bare tokens at depth 0; drop structural `{`, `}`, `=` and nested contents.
        var collected = new List<string>();
        var token = new StringBuilder();
        int depth = 0;
        void Flush()
        {
            if (token.Length > 0)
            {
                var t = token.ToString();
                if (t != "=") collected.Add(t);
                token.Clear();
            }
        }
        foreach (var c in inner)
        {
            if (c == '{') { Flush(); depth++; }
            else if (c == '}') { Flush(); if (depth > 0) depth--; }
            else if (depth > 0) { /* inside nested block: skip */ }
            else if (c is ' ' or '\t' or '\r' or '\n') Flush();
            else token.Append(c);
        }
        Flush();
        names = [.. collected];
        return true;
    }

    private static string StripComments(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var rawLine in text.Split('\n'))
        {
            var idx = rawLine.IndexOf('#');
            sb.Append(idx >= 0 ? rawLine[..idx] : rawLine).Append('\n');
        }
        return sb.ToString();
    }
}
