using System.Buffers.Binary;

namespace HookshotAdventure.Core;

/// <summary>Experimental new-save defaults for the two independent Kokiri blockers.</summary>
public static class HAL_OpenForestDefaults
{
    public static byte[] Patch(byte[] data, bool openForest, bool openDeku)
    {
        if (!openForest && !openDeku) throw new ArgumentException("At least one forest gate must be selected.");
        var wanted = new HashSet<uint>();
        if (openDeku) wanted.Add(HAL_GameDataHash.Name("KokiriForest_Npc_Kokiri_Mido_Child_Blocker"));
        if (openForest) wanted.Add(HAL_GameDataHash.Name("KokiriForest_Npc_Kokiri_007_Child_Blocker_00"));
        var b = new HAL_Byml(data);
        int Node(HAL_Byml.Field field) => checked((int)b.Child(field, 0xc1));
        var tables = b.Fields(Node(b.Fields(b.Root)["Data"]));
        var slots = new Dictionary<uint, int>();
        foreach (var entry in b.Array(tables["Bool"]))
        {
            var f = b.Fields(Node(entry)); uint hash = b.Child(f["Hash"], 0xd3);
            if (!wanted.Contains(hash)) continue;
            if (!slots.TryAdd(hash, f["DefaultValue"].Slot) || b.Child(f["DefaultValue"], 0xd0) != 1 ||
                b.Child(f["SaveFileIndex"], 0xd1) != 0 || b.Child(f["ResetTypeValue"], 0xd1) != 8)
                throw new InvalidDataException("Unexpected forest blocker default or persistence policy.");
        }
        if (slots.Count != wanted.Count) throw new InvalidDataException("Missing forest blocker defaults.");
        var result = (byte[])data.Clone();
        foreach (int slot in slots.Values)
            if (b.LittleEndian) BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(slot), 0);
            else BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(slot), 0);
        return result;
    }
}
