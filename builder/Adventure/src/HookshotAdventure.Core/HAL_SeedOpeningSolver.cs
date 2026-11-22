using System.Text.Json;

namespace HookshotAdventure.Core;

/// <summary>Checks the reviewed child opening in the final seed placement, not full-game beatability.</summary>
public static class HAL_SeedOpeningSolver
{
    private const ulong AuthoredSlingshotChest = 821372075822700622;
    private const string Sword = "Item_Sword_Kokiri", Slingshot = "Item_Range_SlingShot";

    public static void Validate(JsonElement plan, bool startingSword, bool startingSlingshot, JsonElement? catalog = null,
        IReadOnlyList<HAL_WorldReachability.Location>? tokens = null)
    {
        var opening = plan.GetProperty("opening_audit");
        if (opening.GetProperty("status").GetString() == "not_applicable") return;
        if (opening.GetProperty("status").GetString() != "opening_progression_verified")
            throw new InvalidDataException("Unsupported opening progression audit.");
        var locations = new List<HAL_WorldReachability.Location>();
        if (tokens is not null) locations.AddRange(tokens);
        foreach (var row in plan.GetProperty("assignments").EnumerateArray())
        {
            string id = row.GetProperty("id").GetString() ?? throw new InvalidDataException("Missing chest identity.");
            string actor = row.GetProperty("reward").GetString() ?? throw new InvalidDataException("Missing chest reward.");
            var resources = row.GetProperty("resources").EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
            ulong placement = row.GetProperty("placement").GetUInt64();
            bool forest = HAL_OpeningAccess.ForestBeforeDeku(placement, resources);
            bool deku = placement == AuthoredSlingshotChest && resources.Length > 0 &&
                resources.All(path => path.Contains("/InsideTheDekuTree/", StringComparison.Ordinal));
            if (HAL_OpeningAccess.DekuLobbyMap(placement, resources) && actor != Sword)
                locations.Add(new(id, HAL_ChildForestLogic.DekuLobby, new([]), actor));
            else if (forest || deku)
                locations.Add(new(id, forest ? "KokiriForest" : "DekuSlingshotRoom", new([]), actor));
            else if (resources.Length > 0 && resources.All(path => path.Contains("/KokiriForest/", StringComparison.Ordinal)))
            {
                if (placement == HAL_OpeningAccess.StormsGrottoChest) locations.Add(new(id, HAL_ChildForestLogic.StormsGrotto, new([]), actor));
                else if (placement == HAL_OpeningAccess.BridgeChest) locations.Add(new(id, HAL_ChildForestLogic.Bridge,
                    new([[new("ability:access_bridge_chest")]]), actor));
            }
        }
        if (catalog is { } selected)
        {
            var assigned = plan.GetProperty("assignments").EnumerateArray().Select(row => row.GetProperty("id").GetString()).ToHashSet(StringComparer.Ordinal);
            foreach (var row in selected.GetProperty("locations").EnumerateArray())
            {
                string id = row.GetProperty("id").GetString()!;
                string? reward = row.GetProperty("reward").GetString();
                if (assigned.Contains(id) || reward is not (Sword or Slingshot)) continue;
                var resources = row.GetProperty("resources").EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
                ulong placement = row.GetProperty("placement").GetUInt64();
                bool forest = HAL_OpeningAccess.ForestBeforeDeku(placement, resources);
                bool deku = placement == AuthoredSlingshotChest && resources.Length > 0 &&
                    resources.All(path => path.Contains("/InsideTheDekuTree/", StringComparison.Ordinal));
                if (HAL_OpeningAccess.DekuLobbyMap(placement, resources) && reward != Sword)
                    locations.Add(new(id, HAL_ChildForestLogic.DekuLobby, new([]), reward));
                else if (forest || deku) locations.Add(new(id, forest ? "KokiriForest" : "DekuSlingshotRoom", new([]), reward));
            }
        }
        if (plan.TryGetProperty("event_assignments", out var events))
            foreach (var row in events.EnumerateArray())
            {
                string id = row.GetProperty("id").GetString() ?? "";
                bool skull = id.StartsWith("event:skulltula_house_", StringComparison.Ordinal);
                if (!skull && id is not (HAL_EventRewardPlan.CheckId or HAL_EventRewardPlan.SariaCheckId)) continue;
                string reward = row.GetProperty("reward").GetString() ?? throw new InvalidDataException("Missing event reward.");
                if (skull)
                {
                    int threshold = row.GetProperty("tokens").GetInt32();
                    if (id != $"event:skulltula_house_{threshold}") throw new InvalidDataException("Skulltula reward identity mismatch.");
                    locations.Add(new(id, HAL_ChildForestLogic.SkullHouse, HAL_ChildForestLogic.SkulltulaAccess(threshold), reward));
                }
                else locations.Add(new(id, id == HAL_EventRewardPlan.CheckId ? HAL_ChildForestLogic.LostWoods : HAL_ChildForestLogic.Bridge,
                    HAL_ChildForestLogic.EventAccess(id), reward));
            }
        // The normal opening audit assumes a shield can be purchased in Kokiri.
        var starting = new Dictionary<string, int>(StringComparer.Ordinal) { [HAL_ChildForestLogic.Shield] = 1 };
        if (startingSword) starting.Add(Sword, 1);
        if (startingSlingshot) starting.Add(Slingshot, 1);
        var world = HAL_ChildForestLogic.Build(locations);
        if (!HAL_WorldReachability.Evaluate(world, starting, 0).GoalReached)
            throw new InvalidDataException("Generated seed fails the reviewed opening progression solver.");
    }
}
