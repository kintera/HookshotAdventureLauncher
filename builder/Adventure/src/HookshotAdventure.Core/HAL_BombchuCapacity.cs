using System.Buffers.Binary;

namespace HookshotAdventure.Core;

/// <summary>Give bombchus a separate fixed pouch limit without granting a bomb bag.</summary>
public static class HAL_BombchuCapacity
{
    public const string Resource = "RSDB/PouchActorInfo.Product.111.rstbl.byml.zs";
    public const int Capacity = 20;

    public static byte[] Patch(byte[] source)
    {
        var byml = new HAL_Byml(source);
        var key = byml.Keys.ToList().IndexOf("FixedMaxNum");
        if (key < 0 || key > 0xffffff) throw new InvalidDataException("Pouch table has no fixed-capacity field.");
        var required = new HashSet<string>(StringComparer.Ordinal) { "Item_Bombchu" };
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var edits = new List<(int Node, int Slot)>();
        foreach (var row in byml.Array(new HAL_Byml.Field(0xc0, 12)))
        {
            int node = checked((int)byml.Child(row, 0xc1));
            var fields = byml.Fields(node);
            if (!fields.TryGetValue("__RowId", out var id) || id.Type != 0xa0) continue;
            string name = byml.Text(id);
            if (!required.Contains(name)) continue;
            if (!seen.Add(name) || fields.ContainsKey("FixedMaxNum") ||
                !fields.TryGetValue("MaxNumGameData", out var limit) || limit.Type != 0xa0 || byml.Text(limit) != "Bomb" ||
                !fields.TryGetValue("SmallBagActor", out var bag) || bag.Type != 0xa0 ||
                byml.Text(bag) != "Work/Actor/Item_BombBag_Small.engine__actor__ActorParam.gyml")
                throw new InvalidDataException("Bombchu pouch rule differs from the reviewed game version.");
            edits.Add((node, limit.Slot));
        }
        if (seen.Count != required.Count || edits.Count != 1)
            throw new InvalidDataException("Bombchu pouch rules are missing or duplicated.");

        byte[] result = (byte[])source.Clone();
        foreach (var (node, slot) in edits)
        {
            int count = byml.Header(node, 0xc1);
            var entries = new List<(int Key, byte[] Bytes)>();
            for (int i = 0; i < count; i++)
            {
                int at = node + 4 + i * 8;
                byte[] entry = byml.Bytes(at, 8);
                int entryKey = (int)byml.UInt(at, 3);
                if (at + 4 == slot)
                {
                    entryKey = key;
                    for (int j = 0; j < 3; j++) entry[byml.LittleEndian ? j : 2 - j] = (byte)(key >> (8 * j));
                    entry[3] = 0xd1;
                    if (byml.LittleEndian) BinaryPrimitives.WriteInt32LittleEndian(entry.AsSpan(4), Capacity);
                    else BinaryPrimitives.WriteInt32BigEndian(entry.AsSpan(4), Capacity);
                }
                entries.Add((entryKey, entry));
            }
            int offset = node + 4;
            foreach (var entry in entries.OrderBy(x => x.Key))
            {
                entry.Bytes.CopyTo(result, offset);
                offset += 8;
            }
        }
        return result;
    }
}
