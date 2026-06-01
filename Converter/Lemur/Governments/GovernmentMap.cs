namespace Converter.Lemur.Governments;

/// <summary>
/// Maps Azgaar state government to a CK3 government catalog entry.
///
/// Lookup priority (per the design in PLAN_governments / GitHub issue #9 comment):
///   1. Exact <c>formName</c> match (granular) — e.g. "Khanate" → Nomad, "Diocese" → Theocracy
///   2. Broad <c>form</c> match (coarse) — e.g. Monarchy → Feudal, Theocracy → Theocracy
///   3. Final default — Feudal (matches the most common Monarchy default)
///
/// Disambiguation rules from the issue comment (Empire→Admin if bureaucratic, Caliphate→Theocracy
/// if priest-ruled, Heptarchy→Feudal if settled, Imamah→Clan if dynastic) are deliberately not
/// implemented — Azgaar doesn't expose the disambiguating signal.
/// </summary>
public static class GovernmentMap
{
    private static readonly Dictionary<string, Ck3Government> FormNameMap = new()
    {
        // Monarchy formNames
        ["Duchy"]               = Ck3Governments.Feudal,
        ["Grand Duchy"]         = Ck3Governments.Feudal,
        ["Principality"]        = Ck3Governments.Feudal,
        ["Kingdom"]             = Ck3Governments.Feudal,
        ["Empire"]              = Ck3Governments.Feudal,
        ["Marches"]             = Ck3Governments.Feudal,
        ["Dominion"]            = Ck3Governments.Feudal,
        ["Protectorate"]        = Ck3Governments.Feudal,
        ["Tsardom"]             = Ck3Governments.Feudal,
        ["Khanate"]             = Ck3Governments.Nomad,
        ["Khaganate"]           = Ck3Governments.Nomad,
        ["Ulus"]                = Ck3Governments.Nomad,
        ["Horde"]               = Ck3Governments.Nomad,
        ["Beylik"]              = Ck3Governments.Clan,
        ["Emirate"]             = Ck3Governments.Clan,
        ["Caliphate"]           = Ck3Governments.Clan,
        ["Despotate"]           = Ck3Governments.Administrative,
        ["Satrapy"]             = Ck3Governments.Administrative,
        ["Shogunate"]           = Ck3Governments.Soryo,

        // Republic formNames
        ["Republic"]            = Ck3Governments.Republic,
        ["Federation"]          = Ck3Governments.Republic,
        ["Trade Company"]       = Ck3Governments.Republic,
        ["Most Serene Republic"] = Ck3Governments.Republic,
        ["Oligarchy"]           = Ck3Governments.Republic,
        ["Tetrarchy"]           = Ck3Governments.Republic,
        ["Triumvirate"]         = Ck3Governments.Republic,
        ["Diarchy"]             = Ck3Governments.Republic,
        ["Junta"]               = Ck3Governments.Republic,
        ["Free City"]           = Ck3Governments.Republic,
        ["City-state"]          = Ck3Governments.Republic,

        // Union formNames
        ["Union"]               = Ck3Governments.Republic,
        ["League"]              = Ck3Governments.Republic,
        ["Confederation"]       = Ck3Governments.Republic,
        ["United Republic"]     = Ck3Governments.Republic,
        ["United Provinces"]    = Ck3Governments.Republic,
        ["Commonwealth"]        = Ck3Governments.Republic,
        ["United Kingdom"]      = Ck3Governments.Feudal,
        ["Heptarchy"]           = Ck3Governments.Tribal,

        // Theocracy formNames
        ["Theocracy"]               = Ck3Governments.Theocracy,
        ["Brotherhood"]             = Ck3Governments.Theocracy,
        ["Thearchy"]                = Ck3Governments.Theocracy,
        ["See"]                     = Ck3Governments.Theocracy,
        ["Holy State"]              = Ck3Governments.Theocracy,
        ["Divine Duchy"]            = Ck3Governments.Theocracy,
        ["Divine Grand Duchy"]      = Ck3Governments.Theocracy,
        ["Divine Principality"]     = Ck3Governments.Theocracy,
        ["Divine Kingdom"]          = Ck3Governments.Theocracy,
        ["Divine Empire"]           = Ck3Governments.Theocracy,
        ["Diocese"]                 = Ck3Governments.Theocracy,
        ["Bishopric"]               = Ck3Governments.Theocracy,
        ["Eparchy"]                 = Ck3Governments.Theocracy,
        ["Exarchate"]               = Ck3Governments.Theocracy,
        ["Patriarchate"]            = Ck3Governments.Theocracy,
        ["Imamah"]                  = Ck3Governments.Theocracy,

        // Anarchy formNames
        ["Free Territory"]      = Ck3Governments.Tribal,
        ["Council"]             = Ck3Governments.Tribal,
        ["Community"]           = Ck3Governments.Tribal,
        ["Commune"]             = Ck3Governments.Republic,
    };

    private static readonly Dictionary<string, Ck3Government> FormMap = new()
    {
        ["Monarchy"]  = Ck3Governments.Feudal,
        ["Republic"]  = Ck3Governments.Republic,
        ["Union"]     = Ck3Governments.Republic,   // revised from issue body's "Tribal" per issue comment
        ["Theocracy"] = Ck3Governments.Theocracy,
        ["Anarchy"]   = Ck3Governments.Tribal,
    };

    public static Ck3Government For(string? form, string? formName)
    {
        if (formName != null && FormNameMap.TryGetValue(formName, out var byName))
            return byName;

        if (form != null && FormMap.TryGetValue(form, out var byForm))
            return byForm;

        // Both null or unknown — most Azgaar maps are monarchies; this matches that default.
        return Ck3Governments.Feudal;
    }
}
