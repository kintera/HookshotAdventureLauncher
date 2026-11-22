using System.Security.Cryptography;
using System.Text.Json;

namespace HookshotAdventure.Core;

/// <summary>Read-only view of locally discovered loose chest checks and their selected-scope status.</summary>
public static class HAL_LocationPreview
{
    public sealed record Location(string Id, string Region, string Name, string Reward, string Status, string Note);
    public sealed record Result(IReadOnlyList<Location> Locations, int InPool);

    public static Result Scan(string romfs, string scope, bool heartPieces)
    {
        var dictionaries = HAL_RuntimeOverlay.Dictionaries(romfs);
        var flags = HAL_ChestCatalog.FlagCandidates(HAL_Zstd.Unpack(File.ReadAllBytes(Path.Combine(romfs, "GameData", "GameDataList.Product.110.byml.zs")), dictionaries));
        JsonElement Catalog(string selectedScope, bool hearts)
        {
            var hashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
            var variants = new List<HAL_ChestVariant>();
            foreach (var path in HAL_ChestScope.Files(romfs, selectedScope))
            {
                var name = Path.GetRelativePath(romfs, path).Replace('\\', '/');
                var raw = HAL_Zstd.Unpack(File.ReadAllBytes(path), dictionaries);
                hashes.Add(name, Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant());
                variants.AddRange(HAL_ChestScanner.Scan(raw).Select(chest => new HAL_ChestVariant(name, chest)));
            }
            return JsonSerializer.SerializeToElement(HAL_ChestCatalogDocument.Build(variants, hashes, flags, true,
                selectedScope == "childhood", hearts, selectedScope == "full_game"));
        }
        var all = Catalog("full_game", scope == "full_game" && heartPieces);
        var selected = scope == "full_game" ? all : Catalog(scope, heartPieces);
        var scoped = selected.GetProperty("locations").EnumerateArray().ToDictionary(x => x.GetProperty("id").GetString()!, StringComparer.Ordinal);
        var names = HAL_LocationDocument.FromCatalog(all);
        var descriptions = JsonSerializer.SerializeToElement(names).GetProperty("locations").EnumerateArray()
            .ToDictionary(x => x.GetProperty("check_id").GetString()!, StringComparer.Ordinal);
        var rows = new List<Location>();
        foreach (var item in all.GetProperty("locations").EnumerateArray())
        {
            string id = item.GetProperty("id").GetString()!;
            var desc = descriptions[id];
            bool inScope = scoped.TryGetValue(id, out var state);
            bool eligible = inScope && state.GetProperty("eligible").GetBoolean();
            string? actor = item.GetProperty("reward").GetString();
            string? itemId = actor is null ? null : HAL_ItemIds.TryForActor(actor);
            string reward = itemId is null ? actor ?? "Unknown reward" : HAL_ItemIds.Describe(itemId).Name;
            var note = desc.GetProperty("notes").GetString() ?? "";
            if (inScope && state.GetProperty("exclusions").EnumerateArray().Any(x => x.GetString() == "bridge_cross_map_collection_unverified"))
                note = "Bridge chest has an unverified second map placement and collection flag; both copies remain vanilla.";
            rows.Add(new Location(id, desc.GetProperty("region").GetString()!, desc.GetProperty("name").GetString()!, reward,
                !inScope ? "Outside scope" : eligible ? "In pool" : "Vanilla", note));
        }
        return new Result(rows.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray(), rows.Count(x => x.Status == "In pool"));
    }
}
