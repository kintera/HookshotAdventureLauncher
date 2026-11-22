using System.Text.Json;
using System.Text.Json.Nodes;

namespace HookshotAdventure.Core;

/// <summary>Writes version-four catalogs compatible with the Python seed generator.</summary>
public static class HAL_ChestCatalogDocument
{
    private static readonly string[] Regions = { "KokiriForest", "LostWood", "SacredForestMeadow", "HyruleField",
        "Market", "HyruleCastle", "LonLonRanch", "LakeHylia", "KakarikoVillage", "Graveyard", "BottomOfTheWell",
        "DeathMountainTrail", "GoronCity", "DodongoCavern", "DeathMountainCrater", "ZoraRiver", "ZoraDomain",
        "ZoraFountain", "InsideJabuJabuBelly", "GerudoValley", "HountedWasteland", "DesertColossus", "InsideTheDekuTree", "SpiritTemple" };
    private static JsonNode? Node<T>(T value) => JsonSerializer.SerializeToNode(value);
    private static string Region(string path)
    {
        int at = path.IndexOf("Banc/", StringComparison.Ordinal);
        if (at < 0) throw new InvalidDataException("Invalid chest resource path.");
        return path[(at + 5)..].Split('/')[0];
    }
    private static JsonObject Variant(HAL_ChestVariant variant)
    {
        var chest = variant.Chest;
        JsonArray? position = null;
        if (chest.Position is not null)
        {
            position = new JsonArray();
            foreach (float value in chest.Position) position.Add(JsonNode.Parse(HAL_CanonicalJson.Float(value)));
        }
        return new JsonObject { ["resource"] = variant.Resource, ["position"] = position, ["dynamic_offset"] = chest.DynamicOffset,
            ["placement"] = chest.Placement, ["actor"] = chest.Actor, ["reward"] = chest.Reward, ["event"] = chest.Event,
            ["shared_parameters"] = chest.SharedParameters, ["location_id"] = chest.LocationId };
    }
    public static JsonObject Build(IEnumerable<HAL_ChestVariant> variants, IReadOnlyDictionary<string, string> resourceHashes,
        IReadOnlyDictionary<uint, IReadOnlyList<uint>>? flags, bool expanded, bool childhood = false, bool heartPieces = false, bool fullGame = false)
    {
        var locations = HAL_ChestCatalog.Build(variants, flags, expanded || childhood || fullGame, childhood || fullGame, heartPieces, childhood || fullGame, fullGame);
        var locationArray = new JsonArray();
        foreach (var row in locations)
        {
            var variantArray = new JsonArray();
            foreach (var variant in row.Variants) variantArray.Add(Variant(variant));
            locationArray.Add(new JsonObject { ["id"] = row.Id, ["placement"] = row.Placement, ["actor"] = row.Actor,
                ["reward"] = row.Reward, ["event"] = row.Event, ["resources"] = Node(row.Resources), ["variants"] = variantArray,
                ["collection_flag_candidates"] = Node(row.CollectionFlagCandidates), ["flag_status"] = "candidate_not_runtime_verified",
                ["eligible"] = row.Eligible, ["exclusions"] = Node(row.Exclusions) });
        }
        var result = new JsonObject { ["schema_version"] = 4, ["resources"] = Node(resourceHashes), ["locations"] = locationArray };
        if (childhood || fullGame)
        {
            var coverage = new JsonObject();
            var regions = fullGame ? resourceHashes.Keys.Select(Region).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray() : Regions;
            foreach (string region in regions)
                coverage[region] = new JsonObject { ["map_files"] = resourceHashes.Keys.Count(path => Region(path) == region),
                    ["checks"] = locations.Count(row => row.Resources.Any(path => Region(path) == region)) };
            result["scope"] = fullGame ? "full_game" : "childhood"; result["scope_regions"] = Node(regions); result["coverage"] = coverage;
            result["coverage_note"] = fullGame ? "Loose child and adult maps; experimental equipment chests can shuffle with their original events. Packed resources and unsupported checks remain vanilla. Full-game reachability is not verified." :
                "Loose child/shared maps only; packed checks, age-specific Spirit Temple checks and non-chest rewards are not implemented.";
        }
        using var document = JsonDocument.Parse(result.ToJsonString());
        result["catalog_hash"] = HAL_CanonicalJson.Digest(document.RootElement);
        return result;
    }
}
