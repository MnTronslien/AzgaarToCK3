using Converter.Lemur.Entities;

namespace Converter.Lemur.Governments;

/// <summary>
/// Resolves CK3 governments onto Kingdom and Duchy titles. Two independent passes:
///
///   * Kingdom pass: each kingdom looks up its Azgaar state via <see cref="Kingdom.Id"/>,
///     which equals the state's <c>i</c> by construction (see <c>GenerateKingdoms</c>).
///   * Duchy pass: each duchy looks up its Azgaar state via <see cref="Duchy.AzgaarStateId"/>
///     (derived from its cells; immutable across <c>MergeTinyKingdoms</c>).
///
/// No propagation between tiers and no character traversal — each title resolves from data
/// it owns directly. This sidesteps the <see cref="Duchy.IsAbsorbed"/> edge case: an absorbed
/// duchy in a foreign kingdom still picks up *its own* original state's government, which
/// gives the in-game ruler the right government even when his liege has a different one
/// (vanilla Venice/Genoa pattern under feudal lieges).
///
/// Must run after <c>MergeTinyKingdoms</c> + the kingdom/empire cull so <c>map.Kingdoms</c>
/// is final — otherwise we'd resolve governments for kingdoms that get dissolved.
/// </summary>
public static class GovernmentResolver
{
    public static void Resolve(Map map)
    {
        var statesById = map.JsonMap.pack.states.ToDictionary(s => s.i);
        var kingdomCounts = new Dictionary<string, int>();
        var duchyCounts = new Dictionary<string, int>();
        int stateMisses = 0;

        foreach (var kingdom in map.Kingdoms)
        {
            if (!statesById.TryGetValue(kingdom.Id, out var state))
            {
                Logger.Warning($"[GovernmentResolver] Kingdom {kingdom.Ck3_Id()} has no matching Azgaar state (kingdom.Id={kingdom.Id}). Falling back to Feudal.");
                stateMisses++;
            }
            kingdom.Government = GovernmentMap.For(state?.form, state?.formName);
            kingdomCounts[kingdom.Government.Key] = kingdomCounts.GetValueOrDefault(kingdom.Government.Key) + 1;
        }

        foreach (var duchy in map.Duchies!)
        {
            if (!statesById.TryGetValue(duchy.AzgaarStateId, out var state))
            {
                // Wasteland-derived duchies legitimately point at state 0 (which doesn't exist in
                // the states array). Don't spam a warning for the wasteland case.
                if (duchy.AzgaarStateId != 0)
                {
                    Logger.Warning($"[GovernmentResolver] Duchy {duchy.Ck3_Id()} has no matching Azgaar state (AzgaarStateId={duchy.AzgaarStateId}). Falling back to Feudal.");
                    stateMisses++;
                }
            }
            duchy.Government = GovernmentMap.For(state?.form, state?.formName);
            duchyCounts[duchy.Government.Key] = duchyCounts.GetValueOrDefault(duchy.Government.Key) + 1;
        }

        var kingdomSummary = string.Join(", ",
            kingdomCounts.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Value} {kv.Key}"));
        var duchySummary = string.Join(", ",
            duchyCounts.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Value} {kv.Key}"));
        Logger.Info($"Resolved governments for {map.Kingdoms.Count} kingdoms: {kingdomSummary}");
        Logger.Info($"Resolved governments for {map.Duchies.Count} duchies: {duchySummary}");
    }
}
