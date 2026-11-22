using System.Text.Json;
using System.Text.Json.Nodes;

namespace HookshotAdventure.Core;

/// <summary>Produces the read-only item-pool report for a catalog and optional plan.</summary>
public static class HAL_ItemPoolReport
{
    private static JsonNode? Node<T>(T value) => JsonSerializer.SerializeToNode(value);
    private static object Counts(IEnumerable<string> ids) => ids.GroupBy(x=>x,StringComparer.Ordinal).OrderBy(x=>x.Key,StringComparer.Ordinal)
        .ToDictionary(x=>x.Key,x=>x.Count(),StringComparer.Ordinal);
    public static JsonObject Build(JsonElement catalog, JsonElement? plan = null)
    {
        var source = new JsonArray(); var sourceIds = new List<string>();
        foreach (var row in catalog.GetProperty("locations").EnumerateArray().OrderBy(x=>x.GetProperty("id").GetString(),StringComparer.Ordinal))
        {
            string? reward = row.GetProperty("reward").GetString(); if (reward is null) continue;
            string? dungeon = null;
            if (reward is "Item_Dungeon_Map" or "Item_Dungeon_Compass" or "Item_Dungeon_SmallKey" or "Item_Dungeon_BossKey")
            {
                var names = row.GetProperty("resources").EnumerateArray().Select(x =>
                {
                    string path = x.GetString()!; int at = path.IndexOf("Banc/",StringComparison.Ordinal);
                    if (at < 0) throw new InvalidDataException("Invalid item resource path."); return path[(at+5)..].Split('/')[0];
                }).Distinct(StringComparer.Ordinal).ToArray();
                if (names.Length != 1) throw new InvalidDataException("Ambiguous dungeon item."); dungeon = names[0];
            }
            string id = row.GetProperty("id").GetString()!; string? item = HAL_ItemIds.TryForActor(reward);
            if (item is null) continue;
            sourceIds.Add(item);
            source.Add(new JsonObject { ["instance_id"] = "chest:"+id, ["item_id"] = item, ["source_check"] = id,
                ["dungeon"] = dungeon, ["eligible"] = row.GetProperty("eligible").GetBoolean() });
        }
        var output = new JsonObject { ["schema_version"] = 1, ["id_namespace"] = "Hookshot randomizer; hexadecimal IDs, not native game IDs",
            ["catalog_hash"] = catalog.GetProperty("catalog_hash").GetString(), ["definitions"] = Node(HAL_ItemIds.Definitions().Select(x=>new
                { item_id=x.Id, name=x.Name, pickup_actor=x.Actor, inventory_identifier=x.Inventory, inventory_status=x.Status }).ToArray()),
            ["source_instances"] = source, ["source_counts"] = Node(Counts(sourceIds)) };
        if (plan is not JsonElement p) return output;
        if (p.GetProperty("catalog_hash").GetString() != catalog.GetProperty("catalog_hash").GetString()) throw new InvalidDataException("Plan does not belong to this catalog.");
        var shuffled = new JsonArray(); var shuffleIds = new List<string>();
        foreach (var row in p.GetProperty("assignments").EnumerateArray())
        {
            string item = HAL_ItemIds.ForActor(row.GetProperty("reward").GetString()!), donor = row.GetProperty("donor_id").GetString()!; shuffleIds.Add(item);
            shuffled.Add(new JsonObject { ["instance_id"] = "chest:"+donor, ["item_id"] = item, ["target_check"] = row.GetProperty("id").GetString(),
                ["source_check"] = donor, ["runtime_blockers"] = JsonNode.Parse(row.GetProperty("runtime_blockers").GetRawText()) });
        }
        var starting = new JsonArray();
        foreach (var row in p.GetProperty("starting_inventory").EnumerateArray())
            starting.Add(new JsonObject { ["item_id"] = HAL_ItemIds.ForActor(row.GetProperty("item").GetString()!), ["dungeon"] = row.GetProperty("dungeon").GetString(),
                ["source_check"] = row.GetProperty("source_check").GetString(), ["delivery_status"] = row.GetProperty("delivery_status").GetString() });
        output["plan_hash"] = p.GetProperty("plan_hash").GetString(); output["shuffle_instances"] = shuffled;
        output["shuffle_counts"] = Node(Counts(shuffleIds)); output["starting_items"] = starting;
        if (p.TryGetProperty("reward_ledger",out var ledger))
        {
            var translated = new JsonObject(); foreach (var category in ledger.EnumerateObject())
            {
                var counts = new JsonObject(); foreach (var entry in category.Value.EnumerateObject()) counts[HAL_ItemIds.ForActor(entry.Name)] = entry.Value.GetInt32();
                translated[category.Name] = counts;
            }
            output["reward_ledger"] = translated;
        }
        if (p.TryGetProperty("opening_audit",out var audit)) output["opening_audit"] = JsonNode.Parse(audit.GetRawText());
        output["gameplay_enabled"] = false; return output;
    }
}
