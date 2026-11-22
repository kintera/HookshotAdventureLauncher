using System.Buffers.Binary;
using System.Text;

namespace HookshotAdventure.Core;

/// <summary>Rewrites reviewed clothing texture references without changing model layout.</summary>
public static class HAL_PlayerModel
{
    public sealed record Alias(string Replacement, IReadOnlySet<string> Materials);
    public static (byte[] Data, IReadOnlyList<(int Offset, int Length)> Ranges) Patch(byte[] data, IReadOnlyDictionary<string, Alias> aliases)
    {
        ReadOnlySpan<byte> Span(int at, int length)
        {
            if (at < 0 || length < 0 || (long)at + length > data.Length) throw new InvalidDataException("Player model field outside resource.");
            return data.AsSpan(at, length);
        }
        int U16(int at) => BinaryPrimitives.ReadUInt16LittleEndian(Span(at, 2));
        uint U32(int at) => BinaryPrimitives.ReadUInt32LittleEndian(Span(at, 4));
        int Pointer(int at)
        {
            ulong value = BinaryPrimitives.ReadUInt64LittleEndian(Span(at, 8));
            return value <= int.MaxValue ? (int)value : throw new InvalidDataException("Player model pointer outside resource.");
        }
        (int At, string Text) Name(int pointer)
        {
            int at = Pointer(pointer), length = U16(at);
            if (Span(checked(at + 2 + length), 1)[0] != 0) throw new InvalidDataException("Invalid player texture reference.");
            return (at + 2, new UTF8Encoding(false, true).GetString(Span(at + 2, length)));
        }
        if (!Span(0, 14).SequenceEqual(new byte[] { 0x46, 0x52, 0x45, 0x53, 0x20, 0x20, 0x20, 0x20, 0, 4, 10, 0, 255, 254 }))
            throw new InvalidDataException("Unsupported player model version.");
        int model = Pointer(40);
        if (U32(checked(Pointer(48) + 4)) != 1 || !Span(model, 4).SequenceEqual("FMDL"u8)) throw new InvalidDataException("Expected one player model.");
        int count = U16(model + 108), table = Pointer(model + 56);
        if (count is < 1 or > 256) throw new InvalidDataException("Invalid player material count.");
        Span(table, checked(count * 176));
        var edits = new SortedDictionary<int, byte[]>(); var found = aliases.ToDictionary(x => x.Key, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
        {
            int material = checked(table + i * 176);
            if (!Span(material, 4).SequenceEqual("FMAT"u8)) throw new InvalidDataException("Invalid player material.");
            string title = Name(material + 8).Text; int names = Pointer(material + 32), textures = Span(material + 163, 1)[0];
            for (int j = 0; j < textures; j++)
            {
                var (at, original) = Name(checked(names + j * 8));
                if (!aliases.TryGetValue(original, out var alias)) continue;
                byte[] bytes = Encoding.UTF8.GetBytes(alias.Replacement);
                if (!alias.Materials.Contains(title) || bytes.Length != Encoding.UTF8.GetByteCount(original))
                    throw new InvalidDataException("Shared fabric reference outside reviewed clothing.");
                edits[at] = bytes; found[original].Add(title);
            }
        }
        if (aliases.Any(row => !found[row.Key].SetEquals(row.Value.Materials))) throw new InvalidDataException("Expected player fabric reference missing.");
        var result = (byte[])data.Clone();
        foreach (var (at, bytes) in edits) bytes.CopyTo(result, at);
        return (result, edits.Select(row => (row.Key, row.Value.Length)).ToArray());
    }
}
