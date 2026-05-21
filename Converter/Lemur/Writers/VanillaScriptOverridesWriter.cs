using System.Text;
using Converter.Lemur;

namespace Converter.Lemur.Writers;

/// <summary>
/// Overrides / stubs specific vanilla CK3 scripts and titles that crash or
/// emit errors on converted worlds. Each entry is targeted by namespace.id
/// (events) or title key (landed_titles) so we replace only the offending
/// entry, not the whole vanilla file. See bugs/BUG_ck3-1.19-compat.md for
/// the per-override rationale.
/// </summary>
public static class VanillaScriptOverridesWriter
{
    public static async Task Write(string outputDirectory)
    {
        using var _ = OperationTimer.Start("Writing vanilla script overrides");

        var eventsDir = Helper.GetPath(outputDirectory, "events");
        var ltDir = Helper.GetPath(outputDirectory, "common", "landed_titles");
        Directory.CreateDirectory(eventsDir);
        Directory.CreateDirectory(ltDir);

        // easteregg_event.0001 — Charna & Jakub duel.
        // On CK3 1.19+ the vanilla immediate block runs set_variable on
        // easteregg_charna_frostwhisper, but on converted worlds her scope is
        // not valid for variables (no faith / realm anchoring). The script
        // error recurses into EXCEPTION_GUARD_PAGE at on_game_start.
        // Override with always-false trigger to skip the chain entirely.
        await File.WriteAllTextAsync(
            Helper.GetPath(eventsDir, "easteregg_events.txt"),
            "namespace = easteregg_event\n" +
            "\n" +
            "# Override: vanilla immediate crashes on converted worlds because\n" +
            "# easteregg_charna_frostwhisper has no faith/realm anchor. See\n" +
            "# bugs/BUG_ck3-1.19-compat.md.\n" +
            "easteregg_event.0001 = {\n" +
            "\thidden = yes\n" +
            "\tscope = none\n" +
            "\ttrigger = { always = no }\n" +
            "\timmediate = { }\n" +
            "}\n",
            Helper.Utf8Bom);

        // Landless stubs for vanilla titles referenced by vanilla on_game_start.
        // TCS replaces common/landed_titles, removing all vanilla titles. Vanilla
        // on_game_start then context-switches to titles like title:c_chandax for
        // set_important_location effects; on 1.19+ a missing title resolves to a
        // null Landed_title (4294967295) scope and the effect crashes the engine.
        // Declaring each referenced title as a top-level landless stub makes the
        // lookups succeed; the effects then run on real-but-empty titles (no-op).
        // TCS already declares e_hre / e_byzantium / e_roman_empire so we skip
        // those three. Baronies (110 vanilla refs) are skipped because they need
        // parent counties + provinces; if a barony-level crash surfaces, revisit.
        // List is built from vanilla game/common/on_action/game_start.txt as of
        // CK3 1.19.0.5. Extend as needed when other vanilla scripts crash on
        // missing titles.
        var sb = new StringBuilder();
        sb.Append("# Landless stubs for vanilla titles referenced by vanilla on_game_start\n");
        sb.Append("# (and friends). Without these, CK3 1.19+ crashes when scripts\n");
        sb.Append("# context-switch to missing titles. See bugs/BUG_ck3-1.19-compat.md.\n");
        sb.Append("\n");
        foreach (var id in VanillaTitlesToStub)
            sb.Append(id).Append(" = { landless = yes }\n");

        await File.WriteAllTextAsync(
            Helper.GetPath(ltDir, "01_vanilla_compat_landless.txt"),
            sb.ToString(),
            Helper.Utf8Bom);

        Logger.Info($"Wrote vanilla script overrides (1 event + {VanillaTitlesToStub.Length} landless title stubs)");
    }

    /// <summary>
    /// Vanilla title IDs referenced by vanilla common/on_action/game_start.txt
    /// (1.19.0.5). Each is declared as landless so the lookups succeed.
    /// TCS-declared titles (e_hre, e_byzantium, e_roman_empire) excluded.
    /// Baronies (110 refs) excluded — need parents; revisit if needed.
    /// </summary>
    private static readonly string[] VanillaTitlesToStub = new[]
    {
        // Empires (7 — minus e_hre, e_byzantium handled by TCS)
        "e_andong", "e_arabia", "e_caspian", "e_goryeo",
        "e_japan", "e_minister_of_rites", "e_scandinavia",

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
