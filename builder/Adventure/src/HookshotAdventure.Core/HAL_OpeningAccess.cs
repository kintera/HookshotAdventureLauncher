namespace HookshotAdventure.Core;

/// <summary>Known exceptions to immediate child access within the Kokiri map.</summary>
public static class HAL_OpeningAccess
{
    // Local placement identities cross-checked against NX2-ZOOTR's remake map:
    // https://github.com/abslswrld/NX2-ZOOTR/blob/main/data/world/KokiriForest.json
    public const ulong StormsGrottoChest = 0xCDA7447B91AB4DBAUL;
    public const ulong BridgeChest = 0x3C5BB811B4CF54DDUL;
    // The Deku map chest is in the lobby, before the slingshot-room scrub.
    public const ulong DekuMapChest = 0x771092AE2F3B7100UL;

    public static bool ForestBeforeDeku(ulong placement, IReadOnlyList<string> resources) =>
        placement is not (StormsGrottoChest or BridgeChest) && resources.Count > 0 &&
        resources.All(path => path.Contains("/KokiriForest/", StringComparison.Ordinal));

    public static bool DekuLobbyMap(ulong placement, IReadOnlyList<string> resources) =>
        placement == DekuMapChest && resources.Count > 0 &&
        resources.All(path => path == "Banc/InsideTheDekuTree/InsideTheDekuTree_Static.bcett.byml.zs");
}
