using System.Security.Cryptography;
using System.Text;

namespace HookshotAdventure.Core;

/// <summary>Place item instances using a supplied, reviewed world graph.</summary>
public static class HAL_WorldPlacement
{
    public sealed record Item(string Id, string Capability, bool Progression);
    public sealed record Result(IReadOnlyDictionary<string, string> Placements, int ExploredStates);
    private static string Key(string seed, string kind, string id) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes($"HookshotWorldPlacementV1:{seed}:{kind}:{id}")));

    public static Result Place(HAL_WorldReachability.World world, IReadOnlyList<string> slots,
        IReadOnlyList<Item> pool, IReadOnlyDictionary<string, int> starting, string seed, int maximumTokens,
        int maximumStates = 100000)
    {
        if (string.IsNullOrEmpty(seed) || seed.Length > 16 || seed.Any(c => !Uri.IsHexDigit(c)))
            throw new ArgumentException("Invalid seed.", nameof(seed));
        if (maximumStates <= 0) throw new ArgumentOutOfRangeException(nameof(maximumStates));
        seed = seed.ToUpperInvariant().PadLeft(16, '0');
        _ = HAL_WorldReachability.Evaluate(world, starting, maximumTokens);
        var locations = world.Locations.ToDictionary(l => l.Id, StringComparer.Ordinal);
        if (slots.Count != pool.Count || slots.Distinct(StringComparer.Ordinal).Count() != slots.Count ||
            slots.Any(id => !locations.TryGetValue(id, out var location) || location.Token || location.Reward is not null) ||
            pool.Any(i => string.IsNullOrWhiteSpace(i.Id) || string.IsNullOrWhiteSpace(i.Capability) || i.Capability.StartsWith("event:", StringComparison.Ordinal)) ||
            pool.Select(i => i.Id).Distinct(StringComparer.Ordinal).Count() != pool.Count)
            throw new InvalidDataException("Invalid world placement pool or locations.");
        var orderedSlots = slots.OrderBy(id => Key(seed, "slot", id), StringComparer.Ordinal).ThenBy(id => id, StringComparer.Ordinal).ToArray();
        var progression = pool.Where(i => i.Progression).OrderBy(i => Key(seed, "item", i.Id), StringComparer.Ordinal).ThenBy(i => i.Id, StringComparer.Ordinal).ToArray();
        var assigned = new Dictionary<string, Item>(StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal); int explored = 0;
        HAL_WorldReachability.Result Reach()
        {
            var placed = world with { Locations = world.Locations.Select(location => assigned.TryGetValue(location.Id, out var item)
                ? location with { Reward = item.Capability } : location).ToArray() };
            return HAL_WorldReachability.Evaluate(placed, starting, maximumTokens);
        }
        bool Search()
        {
            if (++explored > maximumStates) throw new InvalidDataException("World placement exceeded its search limit.");
            var reached = Reach();
            if (reached.GoalReached) return true;
            foreach (var item in progression.Where(item => !used.Contains(item.Id)))
                foreach (string slot in orderedSlots.Where(id => !assigned.ContainsKey(id) && reached.Collected.Contains(id)))
                {
                    assigned.Add(slot, item); used.Add(item.Id);
                    if (Search()) return true;
                    used.Remove(item.Id); assigned.Remove(slot);
                }
            return false;
        }
        if (!Search()) throw new InvalidDataException("No placement reaches the goal within the token limit and supplied world rules.");
        var remainingItems = pool.Where(i => !used.Contains(i.Id)).OrderBy(i => Key(seed, "remaining", i.Id), StringComparer.Ordinal).ThenBy(i => i.Id, StringComparer.Ordinal).ToArray();
        var remainingSlots = orderedSlots.Where(id => !assigned.ContainsKey(id)).ToArray();
        for (int i = 0; i < remainingSlots.Length; i++) assigned.Add(remainingSlots[i], remainingItems[i]);
        if (!Reach().GoalReached) throw new InvalidDataException("Completed world placement does not reach the goal.");
        return new Result(assigned.ToDictionary(x => x.Key, x => x.Value.Id, StringComparer.Ordinal), explored);
    }
}
