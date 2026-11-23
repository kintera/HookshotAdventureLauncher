using System.Buffers.Binary;
using System.Text;

namespace HookshotAdventure.Core;

/// <summary>Construct a deterministic SARC from local actor resources.</summary>
public static class HAL_ActorArchive
{
    public static byte[] Write(IReadOnlyDictionary<string, byte[]> files)
    {
        if (files.Count > ushort.MaxValue) throw new InvalidDataException("Too many actor archive members.");
        static uint Hash(string name) { uint h = 0; foreach (byte b in Encoding.UTF8.GetBytes(name)) h = unchecked(h * 0x65 + b); return h; }
        var rows = files.Select(x => (Name: x.Key, Data: x.Value, Hash: Hash(x.Key))).OrderBy(x => x.Hash).ThenBy(x => x.Name, StringComparer.Ordinal).ToArray();
        using var namesStream = new MemoryStream(); var names = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (string.IsNullOrEmpty(row.Name) || row.Name.Contains('\0')) throw new InvalidDataException("Invalid actor archive member name.");
            while ((namesStream.Length & 3) != 0) namesStream.WriteByte(0);
            int offset = checked((int)namesStream.Length / 4);
            if (offset > 0xffffff) throw new InvalidDataException("Actor archive names are too large.");
            names.Add(row.Name, offset); namesStream.Write(Encoding.UTF8.GetBytes(row.Name)); namesStream.WriteByte(0);
        }
        int dataStart = checked((int)((0x14 + 12 + rows.Length * 16L + 8 + namesStream.Length + 0xff) & ~0xffL));
        using var output = new MemoryStream(); using var writer = new BinaryWriter(output, Encoding.UTF8, true);
        writer.Write("SARC"u8); writer.Write((ushort)0x14); writer.Write((ushort)0xfeff); writer.Write(0u); writer.Write((uint)dataStart); writer.Write((ushort)0x100); writer.Write((ushort)0);
        writer.Write("SFAT"u8); writer.Write((ushort)12); writer.Write((ushort)rows.Length); writer.Write(0x65u);
        int dataOffset = 0;
        foreach (var row in rows)
        {
            dataOffset = checked((dataOffset + 0xff) & ~0xff);
            writer.Write(row.Hash); writer.Write(0x01000000u | (uint)names[row.Name]); writer.Write((uint)dataOffset);
            dataOffset = checked(dataOffset + row.Data.Length); writer.Write((uint)dataOffset);
        }
        writer.Write("SFNT"u8); writer.Write((ushort)8); writer.Write((ushort)0); writer.Write(namesStream.ToArray());
        while (output.Length < dataStart) writer.Write((byte)0);
        foreach (var row in rows) { while (((output.Length - dataStart) & 0xff) != 0) writer.Write((byte)0); writer.Write(row.Data); }
        var result = output.ToArray(); BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), checked((uint)result.Length));
        var check = HAL_ChestEvidence.SarcFiles(result);
        if (check.Count != files.Count || files.Any(x => !check.TryGetValue(x.Key, out var member) || !member.SequenceEqual(x.Value)))
            throw new InvalidDataException("Actor archive did not round-trip.");
        return result;
    }
}
