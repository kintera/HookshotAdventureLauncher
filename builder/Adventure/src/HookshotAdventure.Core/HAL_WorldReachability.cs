namespace HookshotAdventure.Core;

/// <summary>Monotone reachability over reviewed regions, ages, time, events and item checks.</summary>
public static class HAL_WorldReachability
{
    [Flags] public enum Ages { Child = 1, Adult = 2, Both = Child | Adult }
    [Flags] public enum Times { Day = 1, Night = 2, Both = Day | Night }
    public sealed record Requirement(string Capability, int Count = 1);
    // Alternatives are ORed; each alternative is an AND of capabilities.
    public sealed record Rule(IReadOnlyList<IReadOnlyList<Requirement>> Alternatives, int Tokens = 0, Ages Age = Ages.Both, Times Time = Times.Both);
    public sealed record Start(string Region, Ages Age, Times Time = Times.Day);
    public sealed record Connection(string From, string To, Rule Access, Ages? ArriveAs = null, Times? ArriveAt = null);
    public sealed record Event(string Id, string Region, Rule Access);
    public sealed record Location(string Id, string Region, Rule Access, string? Reward = null, bool Token = false);
    public sealed record Goal(string Region, Rule Access);
    public sealed record World(IReadOnlyList<string> Regions, IReadOnlyList<Start> Starts,
        IReadOnlyList<Connection> Connections, IReadOnlyList<Event> Events, IReadOnlyList<Location> Locations, Goal Goal,
        IReadOnlySet<string>? TimePassingRegions = null);
    public sealed record Result(bool GoalReached, int Tokens, IReadOnlySet<string> Collected,
        IReadOnlySet<string> Events, IReadOnlySet<(string Region, Ages Age, Times Time)> Reachable,
        IReadOnlyDictionary<string, int> Inventory);

    private static bool ValidAges(Ages age) => age is Ages.Child or Ages.Adult or Ages.Both;
    private static void Validate(World world, IReadOnlyDictionary<string, int> starting, int maximumTokens)
    {
        if (maximumTokens is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(maximumTokens));
        var regions = world.Regions.ToHashSet(StringComparer.Ordinal);
        if (regions.Count != world.Regions.Count || regions.Any(string.IsNullOrWhiteSpace) || !regions.Contains(world.Goal.Region) ||
            world.Starts.Count == 0 || world.Starts.Any(s => !regions.Contains(s.Region) || s.Age is not (Ages.Child or Ages.Adult) || s.Time is not (Times.Day or Times.Night)) ||
            world.TimePassingRegions?.Any(region => !regions.Contains(region)) == true ||
            world.Connections.Any(c => !regions.Contains(c.From) || !regions.Contains(c.To) || c.ArriveAs is not (null or Ages.Child or Ages.Adult) || c.ArriveAt is not (null or Times.Day or Times.Night)) ||
            world.Events.Any(e => string.IsNullOrWhiteSpace(e.Id) || !regions.Contains(e.Region)) ||
            world.Events.Select(e => e.Id).Distinct(StringComparer.Ordinal).Count() != world.Events.Count ||
            world.Locations.Any(l => string.IsNullOrWhiteSpace(l.Id) || !regions.Contains(l.Region) || l.Token && l.Reward is not null || l.Reward == "" || l.Reward?.StartsWith("event:", StringComparison.Ordinal) == true) ||
            world.Locations.Select(l => l.Id).Distinct(StringComparer.Ordinal).Count() != world.Locations.Count ||
            starting.Any(x => string.IsNullOrWhiteSpace(x.Key) || x.Key.StartsWith("event:", StringComparison.Ordinal) || x.Value < 0))
            throw new InvalidDataException("Invalid world graph.");
        foreach (var rule in world.Connections.Select(c => c.Access).Concat(world.Events.Select(e => e.Access))
                     .Concat(world.Locations.Select(l => l.Access)).Append(world.Goal.Access))
            if (!ValidAges(rule.Age) || rule.Time is not (Times.Day or Times.Night or Times.Both) || rule.Tokens is < 0 or > 100 || rule.Alternatives.Any(path => path.Count == 0 ||
                    path.Any(r => string.IsNullOrWhiteSpace(r.Capability) || r.Count <= 0)))
                throw new InvalidDataException("Invalid world access rule.");
    }

    public static Result Evaluate(World world, IReadOnlyDictionary<string, int> starting, int maximumTokens)
    {
        Validate(world, starting, maximumTokens);
        var reachable = world.Starts.Select(s => (s.Region, s.Age, s.Time)).ToHashSet();
        var inventory = new Dictionary<string, int>(starting, StringComparer.Ordinal);
        var events = new HashSet<string>(StringComparer.Ordinal);
        var collected = new HashSet<string>(StringComparer.Ordinal);
        int tokens = 0;
        bool Allows(Rule rule, Ages age, Times time) => (rule.Age & age) != 0 && (rule.Time & time) != 0 && tokens >= rule.Tokens &&
            (rule.Alternatives.Count == 0 || rule.Alternatives.Any(path => path.All(r => inventory.GetValueOrDefault(r.Capability) >= r.Count)));
        bool CanAccess(string region, Rule rule) => reachable.Any(state => state.Region == region && Allows(rule, state.Age, state.Time));
        bool changed;
        do
        {
            changed = false;
            if (world.TimePassingRegions is { Count: > 0 } passing)
                foreach (var state in reachable.ToArray())
                    if (passing.Contains(state.Region) && reachable.Add((state.Region, state.Age,
                            state.Time == Times.Day ? Times.Night : Times.Day))) changed = true;
            foreach (var c in world.Connections)
                foreach (var age in new[] { Ages.Child, Ages.Adult })
                    foreach (var time in new[] { Times.Day, Times.Night })
                        if (reachable.Contains((c.From, age, time)) && Allows(c.Access, age, time) &&
                            reachable.Add((c.To, c.ArriveAs ?? age, c.ArriveAt ?? time))) changed = true;
            foreach (var e in world.Events)
                if (!events.Contains(e.Id) && CanAccess(e.Region, e.Access))
                {
                    events.Add(e.Id); inventory.Add("event:" + e.Id, 1); changed = true;
                }
            foreach (var location in world.Locations)
                if (!collected.Contains(location.Id) && CanAccess(location.Region, location.Access))
                {
                    collected.Add(location.Id); changed = true;
                    if (location.Token && tokens < maximumTokens) tokens++;
                    if (location.Reward is { } item) inventory[item] = checked(inventory.GetValueOrDefault(item) + 1);
                }
        } while (changed);
        return new Result(CanAccess(world.Goal.Region, world.Goal.Access), tokens, collected, events, reachable, inventory);
    }
}
