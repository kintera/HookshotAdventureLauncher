using System.Globalization;

namespace HookshotAdventure.Core;

public sealed record HAL_ChestVariant(string Resource, HAL_ChestPlacement Chest);
public sealed record HAL_CatalogLocation(string Id, ulong Placement, string Actor, string? Reward, string? Event,
    IReadOnlyList<string> Resources, IReadOnlyList<HAL_ChestVariant> Variants, IReadOnlyList<string> CollectionFlagCandidates,
    bool Eligible, IReadOnlyList<string> Exclusions);

/// <summary>Groups local chest placements and applies the reviewed eligibility rules.</summary>
public static class HAL_ChestCatalog
{
    private const ulong Sword = 16757992849254467386, Slingshot = 821372075822700622;
    private const string ChestEvent = "KokiriForest_TreasureBox_NaviChatOff";
    private static readonly HashSet<string> Common = new(StringComparer.Ordinal)
    {
        "TBox_Rupee_Green", "TBox_Rupee_Blue", "TBox_Rupee_Red", "TBox_Rupee_Purple", "TBox_Rupee_Gold",
        "TBox_ArrowBundle_Medium", "TBox_ArrowBundle_Large", "Item_BombBundle_Small", "Item_BombchuBundle_Small",
        "TBox_DekuNutBundle", "Item_DekuStickBundle_Small", "TBox_DekuSeedBundle", "Tbox_MagicJar_Large"
    };
    private static readonly HashSet<string> Actors = new(StringComparer.Ordinal)
        { "Obj_TBox_A_01", "Obj_TBox_A_01_NoBake", "Obj_TBox_A_02", "Obj_TBox_A_02_NoBake" };
    private static readonly HashSet<string> ExperimentalEquipment = new(StringComparer.Ordinal)
        { "TBox_Shield_Deku", "Item_GlassTruth", "Item_BombBag_Small", "Item_Boomerang" };

    public static bool ReviewedEquipmentEvent(ulong placement, string? reward, string? name) =>
        (placement, reward, name) is
            (0xA8C7127DD81A8BB9UL, "Item_BombBag_Small", "Get_StoneOfFire_GetBombBag") or
            (0x4CB5467D30E9D84AUL, "Item_Boomerang", "InsideJabuJabuBelly_TreasureBoxWithRuto09") or
            (0xE721A50E0511D8BEUL, "Item_GlassTruth", "BottomOfTheWell_GetGlassTruth");

    public static IReadOnlyDictionary<uint, IReadOnlyList<uint>> FlagCandidates(byte[] gameData)
    {
        var b = new HAL_Byml(gameData);
        var tables = b.Fields(checked((int)b.Child(b.Fields(b.Root)["Data"], 0xc1)));
        var bools = new HashSet<uint>();
        foreach (var field in b.Array(tables["Bool"]))
            bools.Add(b.Child(b.Fields(checked((int)b.Child(field, 0xc1)))["Hash"], 0xd3));
        var result = new Dictionary<uint, HashSet<uint>>();
        foreach (var field in b.Array(tables["Struct"]))
        {
            var table = b.Fields(checked((int)b.Child(field, 0xc1)));
            if (!table.TryGetValue("DefaultValue", out var members) || members.Type != 0xc0) continue;
            foreach (var member in b.Array(members))
            {
                var entry = b.Fields(checked((int)b.Child(member, 0xc1)));
                if (!entry.TryGetValue("Hash", out var hash) || !entry.TryGetValue("Value", out var value) || value.Type != 0xd3) continue;
                uint reference = b.Child(value, 0xd3);
                if (!bools.Contains(reference)) continue;
                uint key = b.Child(hash, 0xd3);
                if (!result.TryGetValue(key, out var values)) result[key] = values = new();
                values.Add(reference);
            }
        }
        return result.ToDictionary(row => row.Key, row => (IReadOnlyList<uint>)row.Value.Order().ToArray());
    }

    public static IReadOnlyList<HAL_CatalogLocation> Build(IEnumerable<HAL_ChestVariant> rows,
        IReadOnlyDictionary<uint, IReadOnlyList<uint>>? flags = null, bool expanded = false,
        bool childhood = false, bool heartPieces = false, bool experimentalEquipment = false, bool fullGame = false)
    {
        var result = new List<HAL_CatalogLocation>();
        foreach (var group in rows.GroupBy(row => row.Chest.Placement).OrderBy(group => group.Key))
        {
            var variants = group.ToArray(); var first = variants[0].Chest; var reasons = new HashSet<string>(StringComparer.Ordinal);
            if (variants.Select(row => (row.Chest.Actor, row.Chest.Reward, row.Chest.Event)).Distinct().Count() != 1)
                reasons.Add("variant_metadata_conflict");
            if (first.Reward is null) reasons.Add("reward_requires_actor_default_inspection");
            else if (!Common.Contains(first.Reward) && !(group.Key == Sword && first.Reward == "Item_Sword_Kokiri") &&
                !(expanded && group.Key == Slingshot && first.Reward == "Item_Range_SlingShot"))
                reasons.Add("progression_or_unreviewed_reward");
            if (first.Event is not null && first.Event != ChestEvent &&
                !(group.Key == Sword && first.Event == "Get_StoneOfForest_GetSword") &&
                !(expanded && group.Key == Slingshot && first.Event == "InsideTheDekuTree_GetSlingShot"))
                reasons.Add("unreviewed_special_event");
            if (!Actors.Contains(first.Actor)) reasons.Add("unreviewed_chest_actor");
            // The same bridge chest is present in KokiriForest and LostWood
            // under different placement IDs and candidate collection flags.
            // Do not assign two rewards until cross-map collection is verified.
            if (group.Key is 0x3C5BB811B4CF54DDUL or 0xF2948997EDE7B25FUL)
                reasons.Add("bridge_cross_map_collection_unverified");
            var resources = variants.Select(row => row.Resource).OrderBy(path => path, StringComparer.Ordinal).ToArray();
            if (resources.Distinct(StringComparer.Ordinal).Count() != resources.Length) reasons.Add("duplicate_placement_in_resource");
            if (childhood && resources.Any(path => path.Contains("Banc/SpiritTemple/", StringComparison.Ordinal)))
                reasons.Add("shared_age_locations_require_review");
            if (childhood && heartPieces && first.Reward == "Obj_Heart_Piece" &&
                (!fullGame || group.Key is 0x2B702A67D25F1421UL or 0x8588AAEEAC14EED7UL or 0xDC0CBD0AC9F0B18AUL))
                reasons.Remove("progression_or_unreviewed_reward");
            if (experimentalEquipment && first.Reward is not null && ExperimentalEquipment.Contains(first.Reward))
            {
                reasons.Remove("progression_or_unreviewed_reward");
                if (ReviewedEquipmentEvent(group.Key, first.Reward, first.Event)) reasons.Remove("unreviewed_special_event");
            }
            uint key = HAL_GameDataHash.Name(group.Key.ToString(CultureInfo.InvariantCulture));
            var candidates = flags is not null && flags.TryGetValue(key, out var values)
                ? values.Order().Select(value => value.ToString("X8", CultureInfo.InvariantCulture)).ToArray() : Array.Empty<string>();
            result.Add(new(group.Key.ToString("X16", CultureInfo.InvariantCulture), group.Key, first.Actor, first.Reward,
                first.Event, resources, variants.OrderBy(row => row.Resource, StringComparer.Ordinal).ToArray(), candidates,
                reasons.Count == 0, reasons.OrderBy(reason => reason, StringComparer.Ordinal).ToArray()));
        }
        return result;
    }
}
