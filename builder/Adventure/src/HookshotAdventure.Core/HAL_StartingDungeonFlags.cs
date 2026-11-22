namespace HookshotAdventure.Core;

/// <summary>Verified map and compass ownership flags for the supported dungeons.</summary>
public static class HAL_StartingDungeonFlags
{
    private static readonly IReadOnlyDictionary<(string Dungeon, string Item), uint> Flags =
        new Dictionary<(string, string), uint>
        {
            [("InsideTheDekuTree", "Item_Dungeon_Map")] = 0x89787395,
            [("InsideTheDekuTree", "Item_Dungeon_Compass")] = 0x06c51ff2,
            [("DodongoCavern", "Item_Dungeon_Map")] = 0xa52c3a19,
            [("DodongoCavern", "Item_Dungeon_Compass")] = 0x8b93edf9,
            [("InsideJabuJabuBelly", "Item_Dungeon_Map")] = 0xfcadd210,
            [("InsideJabuJabuBelly", "Item_Dungeon_Compass")] = 0x9a2218db,
            [("BottomOfTheWell", "Item_Dungeon_Map")] = 0x77c47ec9,
            [("BottomOfTheWell", "Item_Dungeon_Compass")] = 0x9d6efaac,
            [("SpiritTemple", "Item_Dungeon_Map")] = 0xb85002ba,
            [("SpiritTemple", "Item_Dungeon_Compass")] = 0x9bd1181c,
        };

    public static bool IsSupported(string dungeon, string item) => Flags.ContainsKey((dungeon, item));
    public static IReadOnlyList<uint> Resolve(IEnumerable<(string Dungeon, string Item)> items, bool enabled)
    {
        var result = new HashSet<uint>();
        foreach (var item in items)
        {
            if (!Flags.TryGetValue(item, out uint flag)) throw new InvalidDataException("Unverified starting dungeon item.");
            if (!result.Add(flag)) throw new InvalidDataException("Duplicate starting dungeon ownership flag.");
        }
        if (enabled && result.Count == 0) throw new InvalidDataException("Start With maps and compasses has no supported grants.");
        return result.Order().ToArray();
    }
}
