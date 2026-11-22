using System.Buffers.Binary;

namespace HookshotAdventure.Core;

/// <summary>Author a chest entry that checks bag ownership before selecting an ammunition reward.</summary>
public static class HAL_AmmoGuardFlow
{
    public const string Fallback = "TBox_Rupee_Blue";
    private static readonly string[] Quivers = { "Item_Quiver_Small", "Item_Quiver_Medium", "Item_Quiver_Large" };
    private static readonly string[] BombBags = { "Item_BombBag_Small", "Item_BombBag_Medium", "Item_BombBag_Large" };
    private static readonly string[] BulletBags = { "Item_BulletBag_Small", "Item_BulletBag_Medium", "Item_BulletBag_Large" };

    public static IReadOnlyList<string>? RequiredBags(string reward) => reward switch
    {
        "TBox_ArrowBundle_Medium" or "TBox_ArrowBundle_Large" or "Item_Arrow_Normal" => Quivers,
        "Item_Bomb" or "Item_BombBundle_Small" => BombBags,
        "TBox_DekuSeedBundle" or "Item_DekuSeed" => BulletBags,
        _ => null
    };

    public static string EntryName(string reward) => "Success_Prog_AmmoGuard_" + reward;

    public static byte[] Add(byte[] source, string flowName, string reward)
    {
        var owners = RequiredBags(reward) ?? throw new InvalidDataException("The reward is not supported guarded ammunition.");
        var original = new HAL_EventFlow(source, flowName);
        var entries = original.EntryPoints();
        if (!entries.TryGetValue("Success_", out var root)) throw new InvalidDataException("Chest flow is missing its reward entry.");
        if (entries.ContainsKey(EntryName(reward))) throw new InvalidDataException("Ammo guard already exists.");
        ushort pad = (ushort)original.UInt(root.Slot + 2, 2);
        var query = HAL_EventFlowExpansion.EnsureActorQuery(source, "GameDataActor", "EventQueryCheckIsGet");
        var events = HAL_EventFlowExpansion.ExpandEvents(query.Data, 2 + owners.Count);
        int first = events.FirstNewEvent;
        // Each subflow has two parameters (136 bytes); each query has one (88) plus two routes (16).
        var space = HAL_EventFlowExpansion.Reserve(events.Data, checked(2 * 136 + owners.Count * 104));
        byte[] data = space.Data;
        var flow = new HAL_EventFlow(data, flowName);
        int cursor = space.Offset;
        var slots = new List<int>();
        var strings = new List<(int Slot, string Value)>();
        void U16(int at, int value) => BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(at, 2), checked((ushort)value));
        void U32(int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at, 4), value);
        void Ptr(int slot, int target) { BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(slot, 8), (ulong)target); slots.Add(slot); }
        void Str(int slot, string value) { slots.Add(slot); if (value.Length != 0) strings.Add((slot, value)); }
        int Parameters(params (string Key, string? Value)[] values)
        {
            int container = cursor; cursor += 16 + 8 * values.Length;
            int dictionary = cursor; cursor += 8 + 16 * (values.Length + 1);
            int items = cursor; cursor += 24 * values.Length;
            data[container] = 1; U16(container + 2, values.Length); Ptr(container + 8, dictionary);
            "DIC "u8.CopyTo(data.AsSpan(dictionary)); U32(dictionary + 4, (uint)values.Length);
            var tree = HAL_EventFlowDictionary.Build(values.Select(v => v.Key).ToArray());
            for (int i = 0; i < tree.Count; i++)
            {
                int node = dictionary + 8 + i * 16;
                U32(node, tree[i].Bit); U16(node + 4, tree[i].Left); U16(node + 6, tree[i].Right);
                Str(node + 8, i == 0 ? "" : values[i - 1].Key);
            }
            for (int i = 0; i < values.Length; i++)
            {
                int item = items + i * 24; Ptr(container + 16 + i * 8, item); U16(item + 2, 1);
                if (values[i].Value is string value) { data[item] = 5; Str(item + 16, value); }
                else { data[item] = 3; U32(item + 16, 0); }
            }
            return container;
        }
        for (int i = 0; i < 2; i++)
        {
            int at = flow.EventOffset(first + i);
            Str(at, "HAL_AmmoGive_" + (first + i)); data[at + 8] = 4; U16(at + 10, 0xffff);
            int parameters = Parameters(("ContentName", i == 0 ? reward : Fallback), ("IsMiniGame", null));
            Ptr(at + 16, parameters); Str(at + 24, ""); Str(at + 32, "Success_");
        }
        int no = first + 1;
        for (int i = 0; i < owners.Count; i++)
        {
            int at = flow.EventOffset(first + 2 + i);
            Str(at, "HAL_AmmoCheck_" + (first + 2 + i)); data[at + 8] = 1;
            U16(at + 10, 2); U16(at + 12, query.Actor); U16(at + 14, query.Query);
            int routes = cursor; cursor += 16;
            U32(routes, 0); U16(routes + 4, first); U32(routes + 8, 1); U16(routes + 12, no);
            int parameters = Parameters(("ActorName", owners[i]));
            Ptr(at + 16, parameters); Ptr(at + 24, routes); no = first + 2 + i;
        }
        if (cursor != space.Offset + space.Length) throw new InvalidDataException("Ammo guard layout differs from its reservation.");
        data = HAL_EventFlowExpansion.RegisterPointers(data, slots);
        foreach (var (slot, value) in strings) data = HAL_EventFlowStrings.AppendAndPoint(data, slot, value);
        return HAL_EventFlowExpansion.AddEntry(data, EntryName(reward), no, new[] { first, first + 1 }, pad);
    }
}
