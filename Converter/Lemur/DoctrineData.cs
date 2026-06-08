namespace Converter.Lemur;

/// <summary>
/// Static data for all valid CK3 doctrine groups and tenet keys.
/// Source: game/common/religion/doctrines/ (CK3 v1.12.5)
/// Long-term TODO: parse from Ck3Directory at runtime.
/// </summary>
public static class DoctrineData
{
    /// <summary>
    /// All 21 required doctrine groups in output order.
    /// A faith must declare exactly one key from each group.
    /// </summary>
    /// <param name="Label">Human-readable label for the doctrine group (not used in output)</param>
    /// <param name="Options">Valid doctrine keys for this group; a faith must pick exactly one per group</param>
    public static readonly (string Label, string[] Options)[] Groups =
    [
        ("head of faith",       ["doctrine_no_head", "doctrine_spiritual_head", "doctrine_temporal_head"]),
        ("gender",              ["doctrine_gender_male_dominated", "doctrine_gender_equal", "doctrine_gender_female_dominated"]),
        ("clerical marriage",   ["doctrine_clerical_marriage_allowed", "doctrine_clerical_marriage_disallowed"]),
        ("clerical succession", ["doctrine_clerical_succession_temporal_appointment", "doctrine_clerical_succession_spiritual_appointment", "doctrine_clerical_succession_temporal_fixed_appointment", "doctrine_clerical_succession_spiritual_fixed_appointment"]),
        ("clerical gender",     ["doctrine_clerical_gender_male_only", "doctrine_clerical_gender_female_only", "doctrine_clerical_gender_either"]),
        ("clerical function",   ["doctrine_clerical_function_taxation", "doctrine_clerical_function_alms_and_pacification", "doctrine_clerical_function_recruitment"]),
        ("consanguinity",       ["doctrine_consanguinity_dynastic", "doctrine_consanguinity_restricted", "doctrine_consanguinity_cousins", "doctrine_consanguinity_aunt_nephew_and_uncle_niece", "doctrine_consanguinity_unrestricted"]),
        ("bastardry",           ["doctrine_bastardry_none", "doctrine_bastardry_legitimization", "doctrine_bastardry_all"]),
        ("divorce",             ["doctrine_divorce_disallowed", "doctrine_divorce_approval", "doctrine_divorce_allowed"]),
        ("adultery men",        ["doctrine_adultery_men_crime", "doctrine_adultery_men_shunned", "doctrine_adultery_men_accepted"]),
        ("adultery women",      ["doctrine_adultery_women_crime", "doctrine_adultery_women_shunned", "doctrine_adultery_women_accepted"]),
        ("pluralism",           ["doctrine_pluralism_fundamentalist", "doctrine_pluralism_righteous", "doctrine_pluralism_pluralistic"]),
        ("theism",              ["doctrine_monotheist", "doctrine_polytheist"]),
        ("theocracy",           ["doctrine_theocracy_temporal", "doctrine_theocracy_lay_clergy"]),
        ("kinslaying",          ["doctrine_kinslaying_any_dynasty_member_crime", "doctrine_kinslaying_extended_family_crime", "doctrine_kinslaying_close_kin_crime", "doctrine_kinslaying_shunned", "doctrine_kinslaying_accepted"]),
        ("deviancy",            ["doctrine_deviancy_crime", "doctrine_deviancy_shunned", "doctrine_deviancy_accepted", "doctrine_deviancy_virtuous"]),
        ("homosexuality",       ["doctrine_homosexuality_crime", "doctrine_homosexuality_shunned", "doctrine_homosexuality_accepted"]),
        ("witchcraft",          ["doctrine_witchcraft_crime", "doctrine_witchcraft_shunned", "doctrine_witchcraft_accepted", "doctrine_witchcraft_virtuous"]),
        ("pilgrimage",          ["doctrine_pilgrimage_forbidden", "doctrine_pilgrimage_encouraged", "doctrine_pilgrimage_local_rites", "doctrine_pilgrimage_mandatory"]),
        ("coronation",          ["doctrine_no_anointment", "doctrine_anointment_permitted", "doctrine_imperial_anointment"]),
        ("funeral",             ["doctrine_funeral_stoic", "doctrine_funeral_bewailment", "doctrine_funeral_cremation", "doctrine_funeral_sky_burial", "doctrine_funeral_mummification", "doctrine_family_rites"]),
    ];

    // The tenet pool moved to TenetData.cs (inclusive registry with affinity/exclusivity/dud flags,
    // consumed by FaithTenetAssigner). DoctrineData now owns only the structural doctrine Groups.
}
