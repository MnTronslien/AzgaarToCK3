using Converter.Lemur;

namespace Converter.Lemur.Writers;

/// <summary>
/// Writes <c>common/scripted_character_templates/zz_lemur_pool_repopulation.txt</c>:
/// a redefinition of vanilla's <c>pool_repopulate_local_flavor</c> template that
/// stops court-pool councillors from spawning <b>cultureless</b> on converted worlds.
///
/// <para>Vanilla's template (<c>00_pool_repopulation_character_templates.txt</c>) builds
/// the new character's culture from a <c>random_culture</c> list of hard-coded real-world
/// cultures (ashkenazi, han, rajput, …), each gated on a vanilla
/// <c>geographical_region</c> (<c>world_europe</c>, <c>world_india</c>, …). The host's own
/// <c>root.culture</c> is reachable only for <c>rf_pagan</c>-family characters. A converted
/// mod <c>replace_path</c>s <c>map_data</c> (so those regions don't exist) and
/// <c>common/religion/religions</c> (so the referenced faiths don't exist), so every
/// non-pagan pool-repopulated councillor matched <i>no</i> entry and was created with no
/// culture at all.</para>
///
/// <para>This redefinition rebuilds <c>random_culture</c> to always fall back to the host's
/// culture, and drops the vanilla culture/faith/region branches from <c>after_creation</c>
/// so they no longer reference symbols that don't exist on a converted world. It relies on
/// last-define-wins by load order (the <c>zz_</c> prefix sorts after vanilla's <c>00_</c>),
/// the same mechanism <see cref="VanillaEventOverridesWriter"/> uses — no <c>replace_path</c>
/// needed. Fix verified by the community (Discord, 2026-06).
/// See <c>bugs/BUG_cultureless-wanderers-pool.md</c>.</para>
/// </summary>
public static class PoolRepopulationOverrideWriter
{
    public static async Task Write(string outputDirectory)
    {
        using var _ = OperationTimer.Start("Writing pool-repopulation culture override");

        var dir = Helper.GetPath(outputDirectory, "common", "scripted_character_templates");
        Directory.CreateDirectory(dir);

        await File.WriteAllTextAsync(
            Helper.GetPath(dir, "zz_lemur_pool_repopulation.txt"),
            Content,
            Helper.Utf8Bom);

        Logger.Info("Wrote pool-repopulation culture override to " +
                    "common/scripted_character_templates/zz_lemur_pool_repopulation.txt");
    }

    // Redefines pool_repopulate_local_flavor. The structural fields (age, skills,
    // random_traits_list, generic after_creation) mirror vanilla; only the
    // random_culture block and the vanilla-specific after_creation branches changed.
    private const string Content =
@"# Lemur converter — override of vanilla pool_repopulate_local_flavor.
#
# Vanilla's random_culture (game/common/scripted_character_templates/
# 00_pool_repopulation_character_templates.txt) offers only hard-coded real-world
# cultures, each gated on a vanilla geographical_region (world_europe, world_india,
# ...). A converted mod replace_paths map_data (those regions are gone) and
# common/religion/religions (the referenced faiths are gone), leaving the host's
# own culture reachable ONLY for rf_pagan-family characters. Every other
# pool-repopulated councillor therefore matched NO entry and spawned CULTURELESS.
#
# This redefinition (last-define-wins by load order; the zz_ prefix sorts after
# vanilla's 00_) rebuilds random_culture to always fall back to the host's culture,
# and strips the vanilla culture/faith/region branches from after_creation so they
# no longer reference symbols that don't exist on a converted world. Community-
# verified fix (Discord, 2026-06). See bugs/BUG_cultureless-wanderers-pool.md.

pool_repopulate_local_flavor = {
	age = { 25 45 }
	gender_female_chance = root_faith_dominant_gender_adjusted_female_chance #because council gender is doctrine dependent
	random_traits = yes
	faith = root.faith

	random_culture = {
		root.culture = {
			trigger = {
				trigger_if = {
					limit = { exists = scope:activity }
					scope:activity.activity_host = { restricted_culture = no }
				}
				trigger_else = { restricted_culture = no }
			}
		}
		root.culture = { trigger = { always = yes } }
	}

	learning = {
		min_guest_template_skill max_guest_template_skill
	}

	stewardship = {
		min_guest_template_skill max_guest_template_skill
	}

	diplomacy = {
		min_guest_template_skill max_guest_template_skill
	}

	random_traits_list = {
		education_learning_3 = { weight = { base = 10 } }
		education_learning_4 = { weight = { base = 20 } }
		education_stewardship_3 = { weight = { base = 5 } }
		education_stewardship_4 = { weight = { base = 10 } }
		education_diplomacy_3 = { weight = { base = 10 } }
		education_diplomacy_4 = { weight = { base = 20 } }
	}
	random_traits_list = {
		scholar = {}
		theologian = {}
		lifestyle_physician = {}
		lifestyle_mystic = {}
		lifestyle_herbalist = {}
		administrator = {}
		architect = {}
		diplomat = {}
		lifestyle_hunter = {}
	}
	dynasty = none

	after_creation = {
		random_list = {
			200 = {
				# Character is of average weight, nothing happens
			}
			25 = {
				change_current_weight = -25
			}
			25 = {
				change_current_weight = -75
			}
			25 = {
				change_current_weight = 25
			}
			25 = {
				change_current_weight = 75
			}
			5 = {
				change_current_weight = 100
			}
			5 = {
				change_current_weight = 200
			}
		}
		set_interesting_traits_and_modifiers_effect = yes
		add_random_tiered_trait_xp_effect = {
			TRAIT = lifestyle_physician
			LEVEL_1 = yes
			LEVEL_3 = yes
		}
		add_random_tiered_trait_xp_effect = {
			TRAIT = lifestyle_mystic
			LEVEL_1 = yes
			LEVEL_3 = no
		}
		add_random_tiered_trait_track_xp_effect = {
			TRAIT = lifestyle_hunter
			TRACK = hunter
			LEVEL_1 = yes
			LEVEL_3 = no
		}
	}
}
";
}
