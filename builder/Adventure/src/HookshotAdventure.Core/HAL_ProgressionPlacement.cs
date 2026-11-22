using System.Security.Cryptography;
using System.Text;

namespace HookshotAdventure.Core;

/// <summary>
/// Conservative progression placement over an explicitly supplied world graph.
/// A caller must supply complete reviewed access rules before using this as a
/// full-game beatability guarantee.
/// </summary>
public static class HAL_ProgressionPlacement
{
    public sealed record Requirement(string Item, int Count = 1);
    // Each alternative is a conjunction; alternatives are joined by OR. An
    // empty outer list means no item requirements. Tokens are always required.
    public sealed record Access(IReadOnlyList<IReadOnlyList<Requirement>> Alternatives, int Tokens = 0);
    public sealed record Check(string Id, Access Access, bool Shuffle, bool GrantsToken = false,
        string? FixedItem = null, int SkulltulaRewardThreshold = 0);
    public sealed record Item(string Id, string Actor, bool Progression);
    public sealed record Result(IReadOnlyDictionary<string,string> Placements, int ExploredStates);
    private sealed record State(HashSet<string> Reached, Dictionary<string,int> Inventory, int Tokens);

    private static bool Accessible(Access access, Dictionary<string,int> inventory, int tokens) =>
        tokens >= access.Tokens && (access.Alternatives.Count == 0 || access.Alternatives.Any(path =>
            path.All(need => inventory.GetValueOrDefault(need.Item) >= need.Count)));

    private static State Reach(IReadOnlyList<Check> checks, IReadOnlyDictionary<string,Item> assigned,
        IReadOnlyDictionary<string,int> starting, int tokenLimit)
    {
        var reached = new HashSet<string>(StringComparer.Ordinal);
        var inventory = new Dictionary<string,int>(starting, StringComparer.Ordinal);
        int tokens = 0; bool changed;
        do
        {
            changed = false;
            foreach (var check in checks)
            {
                if (reached.Contains(check.Id) || !Accessible(check.Access,inventory,tokens)) continue;
                reached.Add(check.Id); changed = true;
                if (check.GrantsToken && tokens < tokenLimit) tokens++;
                string? item = check.FixedItem;
                if (check.Shuffle && assigned.TryGetValue(check.Id, out var placed)) item = placed.Actor;
                if (item is not null) inventory[item] = inventory.GetValueOrDefault(item) + 1;
            }
        } while (changed);
        return new State(reached,inventory,tokens);
    }

    private static string Key(string seed, string kind, string id) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes($"HookshotProgressionV1:{seed}:{kind}:{id}")));

    private static void ValidateWorld(IReadOnlyList<Check> checks, IReadOnlyList<Item> pool,
        IReadOnlyDictionary<string,int> starting, Access goal, int maximumRequiredSkulltulas)
    {
        if (maximumRequiredSkulltulas is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(maximumRequiredSkulltulas));
        if (checks.Any(c => string.IsNullOrEmpty(c.Id) || c.Access.Tokens < 0 || c.SkulltulaRewardThreshold is < 0 or > 100 ||
                c.SkulltulaRewardThreshold > 0 && (c.SkulltulaRewardThreshold != c.Access.Tokens || !c.Shuffle) ||
                c.Shuffle && (c.FixedItem is not null || c.GrantsToken)) ||
            checks.Select(c=>c.Id).Distinct(StringComparer.Ordinal).Count()!=checks.Count ||
            pool.Any(i=>string.IsNullOrEmpty(i.Id) || string.IsNullOrEmpty(i.Actor)) ||
            pool.Select(i=>i.Id).Distinct(StringComparer.Ordinal).Count()!=pool.Count ||
            checks.Count(c=>c.Shuffle)!=pool.Count || starting.Any(x=>string.IsNullOrEmpty(x.Key) || x.Value < 0))
            throw new InvalidDataException("Invalid progression world or item pool.");
        foreach (var access in checks.Select(c=>c.Access).Append(goal))
            if (access.Tokens < 0 || access.Alternatives.Any(path=>path.Count==0 || path.Any(r=>string.IsNullOrEmpty(r.Item) || r.Count <= 0)))
                throw new InvalidDataException("Invalid progression access rule.");
        if (goal.Tokens > maximumRequiredSkulltulas)
            throw new InvalidDataException("Goal exceeds the maximum required Skulltula count.");
    }

    /// <summary>Check a complete placement against the supplied world and the token limit for the goal.</summary>
    public static bool IsGoalReachable(IReadOnlyList<Check> checks, IReadOnlyList<Item> pool,
        IReadOnlyDictionary<string,int> starting, Access goal, int maximumRequiredSkulltulas,
        IReadOnlyDictionary<string,string> placements)
    {
        ValidateWorld(checks,pool,starting,goal,maximumRequiredSkulltulas);
        var slots=checks.Where(c=>c.Shuffle).Select(c=>c.Id).ToHashSet(StringComparer.Ordinal);
        var items=pool.ToDictionary(i=>i.Id,StringComparer.Ordinal);
        if (!slots.SetEquals(placements.Keys) || placements.Values.Distinct(StringComparer.Ordinal).Count()!=pool.Count ||
            placements.Values.Any(id=>!items.ContainsKey(id))) throw new InvalidDataException("Invalid progression placement.");
        var assigned=placements.ToDictionary(row=>row.Key,row=>items[row.Value],StringComparer.Ordinal);
        var state=Reach(checks,assigned,starting,maximumRequiredSkulltulas);
        return Accessible(goal,state.Inventory,state.Tokens);
    }

    public static Result Place(IReadOnlyList<Check> checks, IReadOnlyList<Item> pool,
        IReadOnlyDictionary<string,int> starting, Access goal, string seed, int maximumRequiredSkulltulas,
        int maximumStates = 100000)
    {
        if (string.IsNullOrEmpty(seed) || seed.Length > 16 || seed.Any(c => !Uri.IsHexDigit(c)))
            throw new ArgumentException("Invalid seed.", nameof(seed));
        if (maximumStates <= 0) throw new ArgumentOutOfRangeException(nameof(maximumStates));
        ValidateWorld(checks,pool,starting,goal,maximumRequiredSkulltulas);
        seed=seed.ToUpperInvariant().PadLeft(16,'0');

        var slots=checks.Where(c=>c.Shuffle).OrderBy(c=>Key(seed,"check",c.Id),StringComparer.Ordinal).ThenBy(c=>c.Id,StringComparer.Ordinal).ToArray();
        var progression=pool.Where(i=>i.Progression).OrderBy(i=>Key(seed,"item",i.Id),StringComparer.Ordinal).ThenBy(i=>i.Id,StringComparer.Ordinal).ToArray();
        var assigned=new Dictionary<string,Item>(StringComparer.Ordinal); var used=new HashSet<string>(StringComparer.Ordinal);
        int explored=0;
        bool Search()
        {
            if (++explored > maximumStates) throw new InvalidDataException("Progression placement exceeded its search limit.");
            var state=Reach(checks,assigned,starting,maximumRequiredSkulltulas);
            // The cap applies to the route to the goal, not to every item that
            // might be useful elsewhere. Once the goal is reachable, unused
            // progression items can be distributed with the remaining pool.
            if (Accessible(goal,state.Inventory,state.Tokens)) return true;
            foreach (var item in progression.Where(i=>!used.Contains(i.Id)))
                foreach (var slot in slots.Where(c=>!assigned.ContainsKey(c.Id) && state.Reached.Contains(c.Id) &&
                    (c.SkulltulaRewardThreshold==0 || c.SkulltulaRewardThreshold<=maximumRequiredSkulltulas)))
                {
                    assigned.Add(slot.Id,item); used.Add(item.Id);
                    if (Search()) return true;
                    used.Remove(item.Id); assigned.Remove(slot.Id);
                }
            return false;
        }
        if (!Search()) throw new InvalidDataException("No progression placement satisfies the supplied access rules and Skulltula cap.");
        var filler=pool.Where(i=>!used.Contains(i.Id)).OrderBy(i=>Key(seed,"remaining",i.Id),StringComparer.Ordinal).ThenBy(i=>i.Id,StringComparer.Ordinal).ToArray();
        var remaining=slots.Where(c=>!assigned.ContainsKey(c.Id)).ToArray();
        for (int i=0;i<remaining.Length;i++) assigned.Add(remaining[i].Id,filler[i]);
        var placements=assigned.ToDictionary(x=>x.Key,x=>x.Value.Id,StringComparer.Ordinal);
        if (!IsGoalReachable(checks,pool,starting,goal,maximumRequiredSkulltulas,placements))
            throw new InvalidDataException("Completed placement does not satisfy the goal within the Skulltula cap.");
        return new Result(placements,explored);
    }
}
