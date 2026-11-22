using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HookshotAdventure.Core;

/// <summary>Scans local loose chest maps and creates the randomizer research documents.</summary>
public static class HAL_ChestResearch
{
    public sealed record Result(JsonObject Scan, JsonObject Catalog, JsonObject Locations, JsonObject Items, JsonObject? Plan, JsonObject Audit);
    public static Result Scan(string romfs, string scope, bool heartPieces, string? seed, bool startingMaps, bool legacyPools = false, JsonElement? existingLocations = null)
    {
        if (scope is not ("kokiri_deku" or "childhood" or "full_game")) throw new ArgumentException("Unsupported chest scope.");
        if (legacyPools && scope != "kokiri_deku") throw new ArgumentException("Childhood and full-game scopes use combined pools.");
        if (startingMaps && seed is null) throw new ArgumentException("Starting maps require a seed.");
        romfs = Path.GetFullPath(romfs); var dictionaries = HAL_RuntimeOverlay.Dictionaries(romfs);
        var hashes = new SortedDictionary<string,string>(StringComparer.Ordinal); var variants = new List<HAL_ChestVariant>(); var maps = new Dictionary<string,byte[]>(StringComparer.Ordinal);
        foreach (string path in HAL_ChestScope.Files(romfs, scope))
        {
            string name = Path.GetRelativePath(romfs,path).Replace('\\','/'); byte[] raw = HAL_Zstd.Unpack(File.ReadAllBytes(path),dictionaries);
            hashes.Add(name,Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant()); maps.Add(name,raw);
            variants.AddRange(HAL_ChestScanner.Scan(raw).Select(x=>new HAL_ChestVariant(name,x)));
        }
        byte[] gameData = HAL_Zstd.Unpack(File.ReadAllBytes(Path.Combine(romfs,"GameData","GameDataList.Product.110.byml.zs")),dictionaries);
        var catalog = HAL_ChestCatalogDocument.Build(variants,hashes,HAL_ChestCatalog.FlagCandidates(gameData),!legacyPools,scope=="childhood",heartPieces,scope=="full_game");
        var catalogElement = JsonSerializer.SerializeToElement(catalog);
        var locations = existingLocations is JsonElement saved ? JsonNode.Parse(saved.GetRawText())!.AsObject() : HAL_LocationDocument.FromCatalog(catalogElement);
        var selected = HAL_LocationDocument.SelectCatalog(catalogElement,JsonSerializer.SerializeToElement(locations));
        var selectedElement = JsonSerializer.SerializeToElement(selected);
        JsonObject? plan = seed is null ? null : HAL_ChestPlanDocument.Build(selectedElement,HAL_RandomizerSeedService.NormalizeSeed(seed),startingMaps,!legacyPools);
        if (plan is not null)
        {
            var assignments = JsonSerializer.SerializeToElement(plan).GetProperty("assignments").EnumerateArray().Where(x=>x.GetProperty("runtime_blockers").GetArrayLength()==0)
                .Select(x=>new HAL_ChestAssignment(x.GetProperty("placement").GetUInt64(),x.GetProperty("actor").GetString()!,x.GetProperty("expected_reward").GetString()!,
                    x.GetProperty("expected_event").GetString(),x.GetProperty("reward").GetString()!,x.GetProperty("event").GetString(),
                    x.GetProperty("resources").EnumerateArray().Select(y=>y.GetString()!).ToArray())).ToArray();
            foreach (var (name,raw) in maps) _ = HAL_ChestPatcher.Patch(raw,assignments.Where(x=>x.Resources.Contains(name,StringComparer.Ordinal)).ToArray());
        }
        var items = HAL_ItemPoolReport.Build(plan is null ? catalogElement : selectedElement, plan is null ? null : JsonSerializer.SerializeToElement(plan));
        var audit = HAL_ChestEvidence.Audit(romfs,variants.Select(x=>new HAL_ChestEvidence.Placement(x.Resource,x.Chest.Placement,x.Chest.Actor,x.Chest.Reward,x.Chest.Event)).ToArray(),dictionaries);
        var rows = new JsonArray();
        foreach (var variant in variants.OrderBy(x=>x.Resource,StringComparer.Ordinal))
        {
            var chest = variant.Chest;
            rows.Add(new JsonObject { ["resource"] = variant.Resource, ["position"] = JsonSerializer.SerializeToNode(chest.Position),
                ["dynamic_offset"] = chest.DynamicOffset, ["placement"] = chest.Placement, ["actor"] = chest.Actor, ["reward"] = chest.Reward,
                ["event"] = chest.Event, ["shared_parameters"] = chest.SharedParameters, ["location_id"] = chest.LocationId });
        }
        var scan = new JsonObject { ["scope"] = scope == "full_game" ? "Loose child and adult maps; unreviewed checks and packed resources excluded" : scope == "childhood" ? "Childhood loose child/shared maps; age-unreviewed and non-chest checks excluded" :
            "KokiriForest and InsideTheDekuTree only; base and AoC1 variants are separate rows", ["resources"] = maps.Count, ["chests"] = rows, ["errors"] = new JsonArray() };
        return new Result(scan,catalog,locations,items,plan,audit);
    }
}
