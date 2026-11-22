using System.Text;

namespace HookshotAdventure.Core;

/// <summary>Bounded BYML reader for locally owned game resources.</summary>
public sealed class HAL_Byml
{
    public readonly record struct Field(byte Type, int Slot);
    private readonly byte[] data;
    private readonly bool little;
    public IReadOnlyList<string> Keys { get; }
    public IReadOnlyList<string> Values { get; }
    public int Root { get; }
    public bool LittleEndian => little;
    public byte[] Bytes(int offset, int size) => Span(offset, size).ToArray();

    public HAL_Byml(byte[] source)
    {
        data = (byte[])source.Clone();
        if (data.Length < 16 || !((data[0] == 'Y' && data[1] == 'B') || (data[0] == 'B' && data[1] == 'Y')))
            throw new InvalidDataException("Invalid BYML header.");
        little = data[0] == 'Y';
        Keys = Strings(Offset(UInt(4))); Values = Strings(Offset(UInt(8))); Root = Offset(UInt(12));
    }

    private ReadOnlySpan<byte> Span(int offset, int size)
    {
        if (offset < 0 || size < 0 || (long)offset + size > data.Length)
            throw new InvalidDataException("BYML offset outside resource.");
        return data.AsSpan(offset, size);
    }
    private static int Offset(ulong value) => value <= int.MaxValue ? (int)value : throw new InvalidDataException("BYML offset outside resource.");
    public ulong UInt(int offset, int size = 4)
    {
        if (size is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(size));
        var bytes = Span(offset, size); ulong result = 0;
        for (int i = 0; i < size; i++) result = (result << 8) | bytes[little ? size - i - 1 : i];
        return result;
    }
    public int Header(int offset, byte kind)
    {
        if (UInt(offset, 1) != kind) throw new InvalidDataException("Unexpected BYML node type.");
        return Offset(UInt(checked(offset + 1), 3));
    }
    private IReadOnlyList<string> Strings(int offset)
    {
        int count = Header(offset, 0xc2);
        Span(checked(offset + 4), checked((count + 1) * 4));
        var result = new List<string>(count); var utf8 = new UTF8Encoding(false, true);
        for (int i = 0; i < count; i++)
        {
            int start = checked(offset + Offset(UInt(checked(offset + 4 + i * 4))));
            Span(start, 1);
            int end = System.Array.IndexOf(data, (byte)0, start);
            if (end < 0) throw new InvalidDataException("Unterminated BYML string.");
            result.Add(utf8.GetString(data, start, end - start));
        }
        return result.AsReadOnly();
    }
    public IReadOnlyDictionary<string, Field> Fields(int offset)
    {
        int count = Header(offset, 0xc1);
        Span(checked(offset + 4), checked(count * 8));
        var result = new Dictionary<string, Field>(StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
        {
            int at = checked(offset + 4 + i * 8), key = Offset(UInt(at, 3));
            if (key >= Keys.Count || !result.TryAdd(Keys[key], new Field((byte)UInt(at + 3, 1), at + 4)))
                throw new InvalidDataException("Invalid or duplicate BYML key.");
        }
        return result;
    }
    public uint Child(Field field, byte type)
    {
        if (field.Type != type) throw new InvalidDataException("Unexpected BYML field type.");
        return (uint)UInt(field.Slot);
    }
    public string Text(Field field)
    {
        int index = Offset(Child(field, 0xa0));
        if (index >= Values.Count) throw new InvalidDataException("Invalid BYML string index.");
        return Values[index];
    }
    public IReadOnlyList<Field> Array(Field field)
    {
        int at = Offset(Child(field, 0xc0)), count = Header(at, 0xc0);
        int slots = checked((at + 4 + count + 3) & ~3);
        Span(slots, checked(count * 4));
        var result = new Field[count];
        for (int i = 0; i < count; i++) result[i] = new Field((byte)UInt(at + 4 + i, 1), slots + i * 4);
        return result;
    }
}
