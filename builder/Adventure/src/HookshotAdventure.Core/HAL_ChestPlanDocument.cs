using System.Text.Json;
using System.Text.Json.Nodes;

namespace HookshotAdventure.Core;

/// <summary>Builds the version-four plan document used by existing seed records.</summary>
public static class HAL_ChestPlanDocument
{
    private static JsonNode? Node<T>(T value) => JsonSerializer.SerializeToNode(value);
    private static Dictionary<string, int> Counts(IEnumerable<string> values) => values.GroupBy(v => v, StringComparer.Ordinal)
        .OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

    /// <summary>Check the final plan after all event exchanges have modified it.</summary>
    public static void ValidateProgressionExclusions(JsonElement plan)
    {
        foreach (var row in plan.GetProperty("assignments").EnumerateArray())
            if (row.TryGetProperty("progression_excluded", out var excluded) && excluded.GetBoolean() &&
                !HAL_ChestPermutation.IsSafeFiller(row.GetProperty("reward").GetString() ?? ""))
                throw new InvalidDataException("A filler-only check contains a progression or unreviewed reward.");
    }

    public static JsonObject Build(JsonElement source, string seed, bool startingMaps, bool expanded,
        IReadOnlySet<string>? startingEquipment = null)
    {
        if (source.GetProperty("schema_version").GetInt32() != 4 ||
            source.GetProperty("catalog_hash").GetString() != HAL_CanonicalJson.DigestWithoutProperty(source, "catalog_hash"))
            throw new InvalidDataException("Catalog version or hash mismatch.");
        var locations = source.GetProperty("locations").EnumerateArray().Select(row => new HAL_PlannerLocation(
            row.GetProperty("id").GetString()!, row.GetProperty("placement").GetUInt64(), row.GetProperty("actor").GetString()!,
            row.GetProperty("reward").GetString() ?? "", row.GetProperty("event").GetString(),
            row.GetProperty("resources").EnumerateArray().Select(p => p.GetString()!).ToArray(), row.GetProperty("eligible").GetBoolean(),
            row.GetProperty("exclusions").EnumerateArray().Select(p => p.GetString()!).ToArray(),
            row.TryGetProperty("progression_excluded", out var excluded) && excluded.GetBoolean())).ToArray();
        var plan = HAL_ChestPlanner.Plan(locations, seed, startingMaps, expanded, startingEquipment);
        string canonicalSeed = seed.ToUpperInvariant().PadLeft(16, '0');
        var freed = plan.StartingInventory.Select(item => item.SourceCheck).ToHashSet(StringComparer.Ordinal);
        var audit = plan.OpeningAudit;
        JsonObject opening = audit.Status == "not_applicable" ? new JsonObject
        {
            ["status"] = "not_applicable", ["full_beatability"] = false,
            ["reason"] = "The reviewed sword and slingshot checks are not both in this catalog"
        } : new JsonObject
        {
            ["status"] = audit.Status, ["full_beatability"] = false, ["sword_check"] = audit.SwordCheck,
            ["slingshot_check"] = audit.SlingshotCheck, ["slingshot_route"] = audit.SlingshotRoute,
            ["scrub_rule"] = "shield_or_slingshot",
            ["assumptions"] = Node(new[] { "Selected Kokiri Forest sword check is reachable from the child start",
                "Deku Shield can be bought in Kokiri before entering the closed Deku Tree",
                "The selected Kokiri Forest sword check is reachable before leaving the forest",
                "The slingshot check is reachable in Kokiri Forest or at its authored Deku Tree chest" }),
            ["unverified"] = Node(new[] { "Full-game completion", "Access to other dungeon rooms and non-chest checks" })
        };
        bool childhood = source.TryGetProperty("scope", out var scope) && scope.GetString() == "childhood";
        bool fullGame = source.TryGetProperty("scope", out scope) && scope.GetString() == "full_game";
        var result = new JsonObject
        {
            ["schema_version"] = 4,
            ["algorithm"] = fullGame ? "sha256-full-game-v1" : childhood ? "sha256-childhood-v2" : expanded ? "sha256-combined-v2" : "sha256-donor-order-v1",
            ["seed"] = canonicalSeed, ["catalog_hash"] = source.GetProperty("catalog_hash").GetString(), ["gameplay_enabled"] = false,
            ["settings"] = new JsonObject { ["start_with_dungeon_maps_compasses"] = startingMaps },
            ["starting_inventory"] = Node(plan.StartingInventory.Select(row => new
                { dungeon = row.Dungeon, item = row.Item, source_check = row.SourceCheck,
                    delivery_status = row.Item is "Item_Dungeon_Map" or "Item_Dungeon_Compass" ? "verified_gamedata_flag" : "development_pouch_grant" }).ToArray()),
            ["starting_inventory_scope"] = startingMaps ? "scanned dungeons only; verified map/compass GameData flags" :
                "scanned dungeons only; full-game startup delivery is unimplemented",
            ["freed_checks"] = Node(freed.OrderBy(x => x, StringComparer.Ordinal).ToArray()),
            ["filler_items"] = Node(plan.Filler.Select(row => new { source_check = row.SourceCheck, removed = row.Removed, added = row.Added }).ToArray()),
            ["reward_ledger"] = new JsonObject
            {
                ["source"] = Node(Counts(plan.Assignments.Select(row => row.ExpectedReward))),
                ["added_filler"] = Node(Counts(plan.Filler.Select(row => row.Added))),
                ["shuffled_chests"] = Node(Counts(plan.Assignments.Select(row => row.Reward))),
                ["starting_inventory"] = Node(Counts(plan.StartingInventory.Select(row => row.Item)))
            },
            ["opening_audit"] = opening,
            ["logic"] = fullGame ? "full-game loose chest subset; early sword/slingshot constraints only; full-game reachability is not verified" : childhood ? "childhood chest subset; early sword/slingshot constraints only; not a complete reachability solver" :
                expanded ? "early sword/slingshot constraints only; not a full reachability solver" :
                "none; experimental map-local pools, not a beatability guarantee",
            ["assignments"] = Node(plan.Assignments.Select(row => new { id = row.Id, placement = row.Placement, actor = row.Actor,
                resources = row.Resources, expected_reward = row.ExpectedReward, expected_event = row.ExpectedEvent,
                donor_id = row.DonorId, reward = row.Reward, @event = row.Event, runtime_blockers = Array.Empty<string>() }).ToArray()),
            ["excluded"] = Node(locations.Where(row => !row.Eligible && !freed.Contains(row.Id))
                .Select(row => new { id = row.Id, reasons = row.Exclusions }).ToArray())
        };
        if (startingEquipment is { Count: > 0 })
        {
            result["settings"]!["starting_equipment"] = Node(startingEquipment.OrderBy(x => x, StringComparer.Ordinal).ToArray());
            result["starting_inventory_scope"] = "reviewed equipment and map/compass grants in development builds";
        }
        var marked = plan.Assignments.Where(row => row.ProgressionExcluded).Select(row => row.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var assignment in result["assignments"]!.AsArray())
            if (marked.Contains(assignment!["id"]!.GetValue<string>())) assignment["progression_excluded"] = true;
        using var document = JsonDocument.Parse(result.ToJsonString());
        result["plan_hash"] = HAL_CanonicalJson.Digest(document.RootElement);
        return result;
    }
}
