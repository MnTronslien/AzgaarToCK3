using System.Text;
using Converter.Lemur;

namespace Converter.Lemur.Writers;

/// <summary>
/// Writes <c>&lt;mod&gt;/common/landed_titles/01_vanilla_compat_landless.txt</c>.
///
/// The file declares a curated list of vanilla CK3 title IDs as top-level
/// landless stubs (<c>id = { landless = yes }</c>) — empires, kingdoms,
/// duchies, counties — with no land, no provinces, no holders. Pure
/// identifiers that exist so script lookups succeed.
///
/// Why we need this: vanilla CK3 scripts (notably <c>common/on_action/game_start.txt</c>
/// and various event chains, decisions, factions) context-switch to specific
/// title IDs for effects like <c>set_important_location</c>, Magyar elective law,
/// Roman restoration, etc. We replace vanilla landed_titles wholesale with our
/// converted hierarchy, so all those vanilla IDs are otherwise missing.
///
/// On CK3 1.18 a missing-title lookup emitted a silent script error and the
/// game continued. On CK3 1.19+ the resulting null <c>Landed_title - 4294967295</c>
/// scope crashes the engine with <c>EXCEPTION_GUARD_PAGE</c> when downstream effects
/// run on it. The landless stubs make the lookups succeed, so the effects then
/// run on real (but empty) titles as no-ops, no crash.
///
/// Extend <see cref="VanillaTitlesToStub"/> as new missing-title crashes surface
/// in other vanilla scripts. See <c>bugs/BUG_ck3-1.19-compat.md</c> for the full
/// rationale.
/// </summary>
public static class LandlessTitleStubsWriter
{
    public static async Task Write(string outputDirectory)
    {
        using var _ = OperationTimer.Start("Writing landless title stubs");

        var ltDir = Helper.GetPath(outputDirectory, "common", "landed_titles");
        Directory.CreateDirectory(ltDir);

        var sb = new StringBuilder();
        sb.Append("# Landless stubs for vanilla titles referenced by vanilla on_game_start\n");
        sb.Append("# and other vanilla scripts. Without these, CK3 1.19+ crashes when scripts\n");
        sb.Append("# context-switch to missing titles. See bugs/BUG_ck3-1.19-compat.md.\n\n");
        foreach (var id in VanillaTitlesToStub)
            sb.Append(id).Append(" = { landless = yes }\n");

        await File.WriteAllTextAsync(
            Helper.GetPath(ltDir, "01_vanilla_compat_landless.txt"),
            sb.ToString(),
            Helper.Utf8Bom);

        Logger.Info($"Wrote {VanillaTitlesToStub.Length} landless title stubs to common/landed_titles/01_vanilla_compat_landless.txt");
    }

    /// <summary>
    /// Vanilla title IDs referenced by vanilla <c>common/on_action/game_start.txt</c>
    /// (as of CK3 1.19.0.5). Baronies (110 vanilla refs) excluded — they need parent
    /// counties + province IDs. If a barony-level crash surfaces, wrap them in a
    /// dummy parent county here.
    ///
    /// Includes <c>e_hre</c> / <c>e_byzantium</c> / <c>e_roman_empire</c> even though TCS also
    /// declares these — we intentionally don't rely on TCS for compat stubs, so we
    /// stay independent when (if) TCS is dropped or replaced.
    /// </summary>
    private static readonly string[] VanillaTitlesToStub = new[]
    {
        // Empires (10)
        "e_andong", "e_arabia", "e_byzantium", "e_caspian", "e_goryeo",
        "e_hre", "e_japan", "e_minister_of_rites", "e_roman_empire", "e_scandinavia",

        // Historical empires (3)
        "h_china", "h_eastern_roman_empire", "h_roman_empire",

        // Kingdoms (21)
        "k_chrysanthemum_throne", "k_daibei", "k_dali", "k_denmark",
        "k_england", "k_guannei", "k_hebei", "k_hedong", "k_huainan",
        "k_lingxi", "k_magyar", "k_norway", "k_shannan", "k_silla",
        "k_sweden", "k_viet", "k_xia", "k_xichuan", "k_xingyuan",
        "k_yongson_throne", "k_youji",

        // Duchies (33)
        "d_agder", "d_angria", "d_bergslagen", "d_bohemia", "d_bukgye",
        "d_dalir", "d_donggye", "d_east_franconia", "d_ghur", "d_gotland",
        "d_halogaland", "d_hitakami", "d_iceland", "d_jamtland", "d_jylland",
        "d_norrland", "d_northern_isles", "d_nushiro", "d_ostergotland",
        "d_ostmark", "d_sjaelland", "d_skane", "d_slesvig", "d_smaland",
        "d_sunni", "d_svealand", "d_trandalog", "d_vastergotland",
        "d_vestlandi", "d_viken", "d_western_isles", "d_york", "d_yukju",

        // Counties (130)
        "c_aalborg", "c_aarhus", "c_abbadan", "c_achaia", "c_aeolis",
        "c_aetolia", "c_agdeside", "c_aland", "c_alexandria", "c_angermanland",
        "c_antiocheia", "c_antipatreia", "c_argyll", "c_attica", "c_austisland",
        "c_avlonas", "c_bari", "c_blekinge", "c_boeotia", "c_bornholm",
        "c_bothin", "c_buthrotum", "c_byzantion", "c_cephalonia", "c_chalkidike",
        "c_chandax", "c_chikuzen", "c_chios", "c_cologne", "c_cumberland",
        "c_dal", "c_dalabergslagen", "c_dalarna", "c_dathina", "c_demetrias",
        "c_dublin", "c_dyrrachion", "c_east_riding", "c_edessa", "c_epeiros",
        "c_euboea", "c_eystridalir", "c_faereyar", "c_finnveden", "c_firdafylki",
        "c_fyn", "c_gastrikland", "c_gauldala", "c_gudbrandsdalir", "c_gutland",
        "c_halland", "c_halsingland", "c_harjadalen", "c_hedmork", "c_helgum",
        "c_hordalandi", "c_inner_hebrides", "c_ionia", "c_jamtfir", "c_jerusalem",
        "c_kinda", "c_korinthos", "c_laconia", "c_lesbos", "c_lolland_falster",
        "c_lombardia", "c_mainz", "c_mandab", "c_medelpad", "c_messenia",
        "c_metzovo", "c_morarna", "c_more", "c_mosynopolis", "c_murcia",
        "c_namdalfylki", "c_narke", "c_naxos", "c_nedenes", "c_neopatras",
        "c_njudung", "c_nordmark", "c_nordrland", "c_northumberland",
        "c_norwegian_more", "c_ohrid", "c_oland", "c_orkney", "c_ostergotland",
        "c_raniriki", "c_ravenna", "c_ribe", "c_ringkobing", "c_rogalandi",
        "c_roma", "c_sanaa", "c_serres", "c_sevede", "c_shetland",
        "c_siracusa", "c_sjaelland", "c_skane", "c_skara", "c_slesvig",
        "c_sodermannaland", "c_sogn", "c_sudurland", "c_taizz", "c_tangiers",
        "c_telemark", "c_thessalia", "c_thessaliotis", "c_thessalonika",
        "c_tourraine", "c_trandheim", "c_trier", "c_tunis", "c_upland",
        "c_varend", "c_varmland", "c_vastergotland", "c_vastmanland",
        "c_vastvag", "c_veria", "c_vestfold", "c_vestisland", "c_viborg",
        "c_vingulmork", "c_vorbasse", "c_zabid",
    };
}
