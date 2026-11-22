using HookshotAdventure.Core;
using static HookshotAdventure.Core.HAL_WorldReachability;

static class WorldReachabilityFixture
{
    private static Rule Free(Ages age = Ages.Both) => new([], Age: age);
    private static Rule Need(string item, Ages age = Ages.Both, int tokens = 0) => new([[new(item)]], tokens, age);

    public static void Check()
    {
        World Build(string swordRegion, int finalTokens) => new(
            ["forest", "field", "temple", "house"], [new("forest", Ages.Child)],
            [new("forest", "field", Need("sword", Ages.Child)), new("field", "house", Free()),
             new("field", "temple", Need("event:letter", Ages.Child)),
             new("temple", "temple", Need("ocarina", Ages.Child), Ages.Adult)],
            [new("letter", "field", Need("sword", Ages.Child))],
            [new("sword-check", swordRegion, Free(), "sword"), new("ocarina-check", "field", Free(Ages.Child), "ocarina"),
             new("token-1", "forest", Free(Ages.Child), Token: true), new("token-2", "field", Free(Ages.Child), Token: true),
             new("optional", "house", new([], 2), "optional-upgrade")],
            new("temple", new([], finalTokens, Ages.Adult)));
        var start = new Dictionary<string, int>();
        var reached = Evaluate(Build("forest", 1), start, 1);
        AssertTest.True(reached.GoalReached && reached.Tokens == 1 && reached.Events.Contains("letter") &&
            reached.Reachable.Contains(("temple", Ages.Adult, Times.Day)) && !reached.Collected.Contains("optional"),
            "world graph follows child access, persistent events, age change and the token cap to the goal");
        AssertTest.True(!Evaluate(Build("house", 1), start, 1).GoalReached,
            "world graph rejects a self-locked sword behind its own entrance");
        AssertTest.True(!Evaluate(Build("forest", 2), start, 1).GoalReached && Evaluate(Build("forest", 2), start, 2).GoalReached,
            "world graph enforces the token limit on the goal without removing higher rewards");
        AssertTest.True(!reached.Reachable.Contains(("field", Ages.Adult, Times.Day)),
            "adult reachability does not leak backwards through a child-only transition");
        var nightOnly = Build("forest", 1) with { Goal = new("temple", new([], 1, Ages.Adult, Times.Night)) };
        AssertTest.True(!Evaluate(nightOnly, start, 1).GoalReached,
            "night-only checks are not reached from daytime without a reviewed time transition");
        nightOnly = nightOnly with { Connections = nightOnly.Connections.Append(new Connection("temple", "temple", Free(), ArriveAt: Times.Night)).ToArray() };
        AssertTest.True(Evaluate(nightOnly, start, 1).GoalReached,
            "a reviewed time transition enables night-only access");
        var waiting = Build("forest", 1) with {
            Goal = new("temple", new([], 1, Ages.Adult, Times.Night)),
            TimePassingRegions = new HashSet<string> { "field" } };
        var waited = Evaluate(waiting, start, 1);
        AssertTest.True(waited.GoalReached && waited.Reachable.Contains(("field", Ages.Child, Times.Night)) &&
            !waited.Reachable.Contains(("field", Ages.Adult, Times.Night)),
            "waiting in a time-passing region reaches night without leaking access between ages");
        bool invalidClock = false;
        try { Evaluate(waiting with { TimePassingRegions = new HashSet<string> { "unknown" } }, start, 1); }
        catch (InvalidDataException) { invalidClock = true; }
        AssertTest.True(invalidClock, "time progression cannot be enabled for an unknown region");
        var malformed = Build("forest", 1) with { Connections = [new("forest", "missing", Free())] };
        bool rejected = false; try { Evaluate(malformed, start, 1); } catch (InvalidDataException) { rejected = true; }
        AssertTest.True(rejected, "world graph rejects connections to unknown regions");
        var placementWorld = new World(["forest", "field"], [new("forest", Ages.Child)],
            [new("forest", "field", Need("sword", Ages.Child))], [],
            [new("start", "forest", Free()), new("late", "forest", new([], 2)),
             new("token-a", "forest", Free(), Token: true), new("token-b", "forest", Free(), Token: true)],
            new("field", Free()));
        var items = new[] { new HAL_WorldPlacement.Item("sword-id", "sword", true),
            new HAL_WorldPlacement.Item("optional-id", "optional-upgrade", true) };
        var placed = HAL_WorldPlacement.Place(placementWorld, ["start", "late"], items, start, "5", 1);
        var placedAgain = HAL_WorldPlacement.Place(placementWorld, ["start", "late"], items, start, "5", 1);
        AssertTest.True(placed.Placements["start"] == "sword-id" && placed.Placements["late"] == "optional-id" &&
            placed.Placements.OrderBy(x => x.Key).SequenceEqual(placedAgain.Placements.OrderBy(x => x.Key)),
            "world placement finds a goal route under the cap while allowing optional progression above it");
        var impossible = placementWorld with { Goal = new("field", new([], 2)) };
        bool failed = false; try { HAL_WorldPlacement.Place(impossible, ["start", "late"], items, start, "5", 1); }
        catch (InvalidDataException) { failed = true; }
        AssertTest.True(failed, "world placement refuses a seed whose goal exceeds the token cap");
    }
}
