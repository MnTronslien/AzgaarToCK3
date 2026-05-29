using Converter.Lemur.Entities;

namespace Converter.Lemur.Governments;

/// <summary>
/// Single-pass resolver that stamps a <see cref="Ck3Government"/> onto every duchy by reading
/// the Azgaar state's <c>form</c> / <c>formName</c> and running it through <see cref="GovernmentMap"/>.
///
/// Runs after duchy generation, before <c>CharacterFactory</c>. No character traversal — the state
/// data lives on the duchy (via cells), so resolution lives at the duchy level. <c>TitleHistoryWriter</c>
/// later reads <see cref="Duchy.Government"/> when emitting the 1066.1.1 history block.
/// </summary>
public static class GovernmentResolver
{
    public static void Resolve(Map map)
    {
        if (map.Duchies is null) return;

        var statesById = map.JsonMap.pack.states.ToDictionary(s => s.i);

        var counts = new Dictionary<string, int>();
        var dlcGated = 0;

        foreach (var duchy in map.Duchies)
        {
            statesById.TryGetValue(duchy.AzgaarStateId, out var state);
            var gov = GovernmentMap.For(state?.form, state?.formName);
            duchy.Government = gov;

            counts[gov.Key] = counts.GetValueOrDefault(gov.Key) + 1;
            if (gov.DlcFeature is not null) dlcGated++;
        }

        var summary = string.Join(", ",
            counts.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Value} {kv.Key}"));
        Logger.Info($"Resolved governments for {map.Duchies.Count} duchies: {summary}"
                    + (dlcGated > 0 ? $" ({dlcGated} DLC-gated)" : ""));
    }
}
