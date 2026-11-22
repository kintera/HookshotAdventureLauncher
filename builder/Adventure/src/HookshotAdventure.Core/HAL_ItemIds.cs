namespace HookshotAdventure.Core;

/// <summary>Stable item identifiers used by the randomizer seed format.</summary>
public static class HAL_ItemIds
{
    private static readonly IReadOnlyDictionary<string, string> ByActor = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["TBox_Rupee_Green"] = "0001",
        ["TBox_Rupee_Blue"] = "0002",
        ["TBox_Rupee_Purple"] = "0003",
        ["Item_Sword_Kokiri"] = "0004",
        ["Item_Range_SlingShot"] = "0005",
        ["TBox_DekuNutBundle"] = "0006",
        ["Item_DekuStickBundle_Small"] = "0007",
        ["Item_Dungeon_Map"] = "0008",
        ["Item_Dungeon_Compass"] = "0009",
        ["TBox_Rupee_Red"] = "000A",
        ["Obj_Heart_Piece"] = "000B",
        ["Item_ScrubBusinessSeed"] = "000C",
        ["TBox_Shield_Hylian"] = "000D",
        ["TBox_Shield_Deku"] = "000E",
        ["Item_Dungeon_SmallKey"] = "000F",
        ["Item_GlassTruth"] = "0010",
        ["Item_BombBag_Small"] = "0011",
        ["Item_Boomerang"] = "0012",
        ["Item_Gauntlet_Silver"] = "0013",
        ["Item_Shield_Mirror"] = "0014",
        ["Item_BombBundle_Small"] = "0015",
        ["Item_BombchuBundle_Small"] = "0016",
        ["Item_Dungeon_BossKey"] = "0017",
        ["TBox_Rupee_Gold"] = "0018",
        ["TBox_ArrowBundle_Medium"] = "0019",
        ["TBox_ArrowBundle_Large"] = "001A",
        ["TBox_DekuSeedBundle"] = "001B",
        ["Tbox_MagicJar_Large"] = "001C",
        ["Item_BulletBag_Medium"] = "001D",
        ["Item_Ocarina_Fairy"] = "001E",
        ["Item_Wallet_Medium"] = "001F",
        ["Item_AgonyStone"] = "0020",
        ["Item_Wallet_Large"] = "0021",
        ["Item_BombchuBundle_Large"] = "0022",
    };
    public static string ForActor(string actor) => ByActor.TryGetValue(actor, out var id) ? id :
        throw new InvalidDataException("Unregistered pickup actor: " + actor);
    public static string? TryForActor(string actor) => ByActor.TryGetValue(actor, out var id) ? id : null;
    private static readonly string[] Names = ["Green Rupee", "Blue Rupee", "Purple Rupee", "Kokiri Sword", "Fairy Slingshot",
        "Deku Nuts bundle", "Deku Sticks bundle", "Dungeon Map", "Dungeon Compass", "Red Rupee", "Heart Piece",
        "Scrub business seed (unreviewed)", "Hylian Shield", "Deku Shield", "Small Key", "Lens of Truth", "Bomb Bag",
        "Boomerang", "Silver Gauntlets", "Mirror Shield", "Bomb bundle", "Bombchu bundle", "Boss Key", "Gold Rupee",
        "Arrows (medium bundle)", "Arrows (large bundle)", "Deku Seeds bundle", "Large Magic Jar", "Bigger Deku Seed Bag", "Fairy Ocarina",
        "Adult's Wallet", "Stone of Agony", "Giant's Wallet", "Bombchus (large bundle)"];
    public static (string Name, string Actor) Describe(string id)
    {
        var match = ByActor.SingleOrDefault(row => row.Value == id);
        if (match.Key is null || !int.TryParse(id, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out int index) || index < 1 || index > Names.Length)
            throw new InvalidDataException("Unregistered item ID.");
        return (Names[index - 1], match.Key);
    }
    public static IReadOnlyList<(string Id, string Name, string Actor, string? Inventory, string Status)> Definitions()
    {
        string?[] inventory = [null,null,null,"Weapon_Sword_Kokiri","Weapon_Range_SlingShot","Item_DekuNut","Item_DekuStick"];
        string[] status = ["currency_not_pouch_item","currency_not_pouch_item","currency_not_pouch_item","runtime_verified",
            "existing_inventory_editor_unverified_here","existing_inventory_editor_unverified_bundle_quantity",
            "existing_inventory_editor_unverified_bundle_quantity","dungeon_qualified_gamedata_flag","dungeon_qualified_gamedata_flag"];
        // These pouch identifiers are also used by the native inventory editor.
        // Their presence there is evidence for experiments, not proof that a
        // one-time starting grant or its original reward has been handled.
        var editorBindings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["TBox_Shield_Deku"] = "Weapon_Shield_Deku",
            ["Item_GlassTruth"] = "Item_GlassTruth",
            ["Item_BombBag_Small"] = "Item_BombBag_Small",
            ["Item_Boomerang"] = "Item_Boomerang"
        };
        return ByActor.OrderBy(x => x.Value, StringComparer.Ordinal).Select(row =>
        {
            int index = int.Parse(row.Value,System.Globalization.NumberStyles.HexNumber,System.Globalization.CultureInfo.InvariantCulture)-1;
            bool knownEditor = editorBindings.TryGetValue(row.Key, out var editorItem);
            return (row.Value, Names[index], row.Key, index < inventory.Length ? inventory[index] : editorItem,
                index < status.Length ? status[index] : knownEditor ? "existing_inventory_editor_unverified_here" : "pickup_catalog_only_inventory_binding_unverified");
        }).ToArray();
    }
}
