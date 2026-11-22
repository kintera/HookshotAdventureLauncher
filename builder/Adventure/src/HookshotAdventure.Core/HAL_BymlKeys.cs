using System.Text;

namespace HookshotAdventure.Core;

/// <summary>Add a sorted BYML key and remap every reachable map without changing its values.</summary>
public static class HAL_BymlKeys
{
    public static byte[] Ensure(byte[] source, string name)
    {
        if (string.IsNullOrEmpty(name) || name.Contains('\0')) throw new InvalidDataException("Invalid BYML key.");
        var b = new HAL_Byml(source);
        if (b.Keys.Contains(name)) return (byte[])source.Clone();
        if (b.Keys.Count >= 0xffffff || b.Keys.Distinct(StringComparer.Ordinal).Count() != b.Keys.Count ||
            !b.Keys.SequenceEqual(b.Keys.OrderBy(x => x, StringComparer.Ordinal)))
            throw new InvalidDataException("Unsupported BYML key table ordering.");
        var keys = b.Keys.Append(name).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var indexes = keys.Select((value, index) => (value, index)).ToDictionary(x => x.value, x => x.index, StringComparer.Ordinal);
        var output = new List<byte>(source);
        void Put(int at, uint value, int size)
        {
            if (at < 0 || (long)at + size > output.Count) throw new InvalidDataException("BYML write is outside the resource.");
            for (int i = 0; i < size; i++) output[at + (b.LittleEndian ? i : size - 1 - i)] = (byte)(value >> (8 * i));
        }
        void Write(uint value, int size) { int at = output.Count; output.AddRange(new byte[size]); Put(at, value, size); }
        var seen = new HashSet<int>(); var pending = new Stack<(int Offset, byte Type)>();
        byte rootType = (byte)b.UInt(b.Root, 1);
        if (rootType is not (0xc0 or 0xc1)) throw new InvalidDataException("Unsupported BYML root node.");
        pending.Push((b.Root, rootType));
        while (pending.Count != 0)
        {
            var (offset, type) = pending.Pop();
            if (!seen.Add(offset)) continue;
            if (type == 0xc1)
            {
                foreach (var (key, field) in b.Fields(offset))
                {
                    Put(field.Slot - 4, (uint)indexes[key], 3);
                    if (field.Type is 0xc0 or 0xc1) pending.Push((checked((int)b.Child(field, field.Type)), field.Type));
                    else if (field.Type is not (0xa0 or 0xa1 or 0xd0 or 0xd1 or 0xd2 or 0xd3 or 0xd4 or 0xd5 or 0xd6 or 0xff))
                        throw new InvalidDataException("Unsupported BYML node while remapping keys.");
                }
            }
            else
            {
                // Array() takes a pointer slot; the node itself can be reached through a temporary header copy.
                int count = b.Header(offset, 0xc0), values = checked((offset + 4 + count + 3) & ~3);
                for (int i = 0; i < count; i++)
                {
                    byte childType = (byte)b.UInt(offset + 4 + i, 1);
                    if (childType is 0xc0 or 0xc1)
                        pending.Push((checked((int)b.UInt(values + 4 * i)), childType));
                    else if (childType is not (0xa0 or 0xa1 or 0xd0 or 0xd1 or 0xd2 or 0xd3 or 0xd4 or 0xd5 or 0xd6 or 0xff))
                        throw new InvalidDataException("Unsupported BYML array element while remapping keys.");
                }
            }
        }
        while ((output.Count & 3) != 0) output.Add(0);
        int table = output.Count; output.Add(0xc2); Write((uint)keys.Length, 3);
        var encoded = keys.Select(Encoding.UTF8.GetBytes).ToArray(); int atString = checked(4 + (keys.Length + 1) * 4);
        foreach (var text in encoded) { Write((uint)atString, 4); atString = checked(atString + text.Length + 1); }
        Write((uint)atString, 4); foreach (var text in encoded) { output.AddRange(text); output.Add(0); }
        Put(4, (uint)table, 4);
        return output.ToArray();
    }
}
