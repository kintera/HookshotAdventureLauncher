using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace HookshotAdventure.Core;

/// <summary>Builds and validates editable location metadata against a native catalog.</summary>
public static class HAL_LocationDocument
{
    private const ulong Mido = 9725242744547055868, Sword = 16757992849254467386, Slingshot = 821372075822700622;
    private static readonly string[] NativeFields = { "region", "type", "vanilla_item_id", "resources", "collection_flag_candidates", "event_policy" };
    public static IReadOnlyList<string> ValidateExclusions(IEnumerable<string>? ids)
    {
        var result = (ids ?? Array.Empty<string>()).ToArray();
        if (result.Length > 10000 || result.Any(id => id is null || !Regex.IsMatch(id, "^[0-9A-F]{16}$", RegexOptions.CultureInvariant)) ||
            result.Distinct(StringComparer.Ordinal).Count() != result.Length)
            throw new InvalidDataException("Invalid excluded check IDs.");
        return result.OrderBy(id => id, StringComparer.Ordinal).ToArray();
    }

    public static void ApplyExclusions(JsonObject locations, IEnumerable<string>? ids)
    {
        var excluded = ValidateExclusions(ids).ToHashSet(StringComparer.Ordinal);
        foreach (var node in locations["locations"]!.AsArray())
            if (excluded.Contains(node!["check_id"]!.GetValue<string>())) node["enabled"] = false;
    }

    /// <summary>Mark reviewed checks as filler-only without removing them or their rewards from the pool.</summary>
    public static void ApplyProgressionExclusions(JsonObject catalog, JsonObject locations, IEnumerable<string>? ids)
    {
        var excluded = ValidateExclusions(ids).ToHashSet(StringComparer.Ordinal);
        foreach (var node in catalog["locations"]!.AsArray())
            if (node!["eligible"]!.GetValue<bool>() && excluded.Contains(node["id"]!.GetValue<string>()))
                node["progression_excluded"] = true;
        foreach (var node in locations["locations"]!.AsArray())
            if (node!["enabled"]!.GetValue<bool>() && excluded.Contains(node["check_id"]!.GetValue<string>()))
                node["progression_excluded"] = true;
        catalog.Remove("catalog_hash");
        using var doc = JsonDocument.Parse(catalog.ToJsonString());
        catalog["catalog_hash"] = HAL_CanonicalJson.Digest(doc.RootElement);
    }
    private static string Region(string path)
    {
        int index = path.IndexOf("Banc/", StringComparison.Ordinal);
        if (index < 0) throw new InvalidDataException("Invalid chest resource path.");
        return path[(index + 5)..].Split('/')[0];
    }
    public static JsonObject FromCatalog(JsonElement catalog)
    {
        var output = new JsonArray();
        foreach (var row in catalog.GetProperty("locations").EnumerateArray())
        {
            string id = row.GetProperty("id").GetString()!;
            ulong placement = row.GetProperty("placement").GetUInt64();
            var resources = row.GetProperty("resources");
            string region = Region(resources[0].GetString()!);
            string key = placement == Mido ? "KF_MIDOS_RIGHTMOST_CHEST" : placement == Sword ? "KF_KOKIRI_SWORD_CHEST" :
                (region == "KokiriForest" ? "KF" : region == "InsideTheDekuTree" ? "DT" : region.ToUpperInvariant()) + "_CHEST_" + id;
            string? reward = row.GetProperty("reward").GetString();
            string label = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(key.Replace('_', ' ').ToLowerInvariant());
            bool enabled = row.GetProperty("eligible").GetBoolean();
            output.Add(new JsonObject { ["key"] = key, ["name"] = label, ["region"] = region, ["type"] = "chest",
                ["enabled"] = enabled, ["check_id"] = id, ["vanilla_item_id"] = reward is null ? null : HAL_ItemIds.TryForActor(reward),
                ["resources"] = JsonNode.Parse(resources.GetRawText()),
                ["collection_flag_candidates"] = JsonNode.Parse(row.GetProperty("collection_flag_candidates").GetRawText()),
                ["event_policy"] = placement == Sword || (enabled && placement == Slingshot) ? "actor_default" : "preserve",
                ["notes"] = key.Contains("_CHEST_", StringComparison.Ordinal) && key.EndsWith(id, StringComparison.Ordinal) ? "Room label unverified" : "" });
        }
        return new JsonObject { ["schema_version"] = 1, ["catalog_hash"] = catalog.GetProperty("catalog_hash").GetString(),
            ["scope"] = catalog.TryGetProperty("scope_regions", out var scope) ? JsonNode.Parse(scope.GetRawText()) :
                JsonSerializer.SerializeToNode(new[] { "KokiriForest", "InsideTheDekuTree" }), ["locations"] = output };
    }

    public static JsonObject SelectCatalog(JsonElement catalog, JsonElement document)
    {
        JsonObject baseline = FromCatalog(catalog);
        string? hash = document.GetProperty("catalog_hash").GetString();
        var documentScope = JsonNode.Parse(document.GetProperty("scope").GetRawText());
        if (document.GetProperty("schema_version").GetInt32() != 1 || hash is null ||
            !Regex.IsMatch(hash, "^[0-9a-f]{64}$", RegexOptions.CultureInvariant) || !JsonNode.DeepEquals(documentScope, baseline["scope"]))
            throw new InvalidDataException("Location JSON does not match this scoped game scan.");
        var expected = baseline["locations"]!.AsArray().Select(node => node!.AsObject())
            .ToDictionary(row => row["check_id"]!.GetValue<string>(), StringComparer.Ordinal);
        var selected = JsonNode.Parse(catalog.GetRawText())!.AsObject();
        var byId = selected["locations"]!.AsArray().Select(node => node!.AsObject())
            .ToDictionary(row => row["id"]!.GetValue<string>(), StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal); var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in document.GetProperty("locations").EnumerateArray())
        {
            string id = row.GetProperty("check_id").GetString()!;
            if (!expected.TryGetValue(id, out var native) || !seen.Add(id)) throw new InvalidDataException("Unknown or duplicate location check ID.");
            string key = row.GetProperty("key").GetString()!;
            if (key is null || !Regex.IsMatch(key, "^[A-Z][A-Z0-9_]*$", RegexOptions.CultureInvariant) || !keys.Add(key))
                throw new InvalidDataException("Location keys must be unique uppercase identifiers.");
            foreach (string field in NativeFields)
                if (!JsonNode.DeepEquals(JsonNode.Parse(row.GetProperty(field).GetRawText()), native[field]))
                    throw new InvalidDataException("Unsupported edit to native location mapping: " + field);
            bool enabled = row.GetProperty("enabled").GetBoolean();
            if (enabled && !byId[id]["eligible"]!.GetValue<bool>()) throw new InvalidDataException("Unreviewed location cannot be enabled.");
            string name = row.GetProperty("name").GetString() ?? throw new InvalidDataException("Location name must be a string.");
            string notes = row.GetProperty("notes").GetString() ?? throw new InvalidDataException("Location notes must be a string.");
            if (!enabled && native["enabled"]!.GetValue<bool>())
            {
                byId[id]["eligible"] = false;
                var reasons = byId[id]["exclusions"]!.AsArray().Select(x => x!.GetValue<string>()).Append("disabled_in_location_json")
                    .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
                byId[id]["exclusions"] = JsonSerializer.SerializeToNode(reasons);
            }
            byId[id]["location_key"] = key; byId[id]["name"] = name;
        }
        if (!seen.SetEquals(expected.Keys)) throw new InvalidDataException("Location JSON must contain every scoped check.");
        selected.Remove("catalog_hash");
        using var json = JsonDocument.Parse(selected.ToJsonString());
        selected["catalog_hash"] = HAL_CanonicalJson.Digest(json.RootElement);
        return selected;
    }
}
