using System.Buffers.Binary;
using System.Text;

namespace HookshotAdventure.Core;

/// <summary>Bounded RESTBL reader and allocation-budget editor for local overlays.</summary>
public sealed class HAL_ResourceSizeTable
{
    private readonly byte[] data;
    private readonly Dictionary<uint, int> hashed = new();
    private readonly Dictionary<string, int> named = new(StringComparer.Ordinal);
    private readonly int width, hashCount, nameCount;

    public HAL_ResourceSizeTable(byte[] source)
    {
        data = (byte[])source.Clone();
        if (data.Length < 22 || !data.AsSpan(0, 6).SequenceEqual("RESTBL"u8))
            throw new InvalidDataException("Invalid resource size table.");
        uint version = Read(6), w = Read(10), h = Read(14), n = Read(18);
        if (version != 1 || w is < 1 or > 4096 || 22L + h * 8L + n * (w + 4L) != data.Length)
            throw new InvalidDataException("Unsupported or truncated resource size table.");
        width = (int)w; hashCount = checked((int)h); nameCount = checked((int)n);
        for (int i = 0; i < hashCount; i++)
        {
            int at = 22 + i * 8;
            if (!hashed.TryAdd(Read(at), at + 4)) throw new InvalidDataException("Duplicate resource hash.");
        }
        var utf8 = new UTF8Encoding(false, true);
        for (int i = 0; i < nameCount; i++)
        {
            int at = 22 + hashCount * 8 + i * (width + 4);
            var bytes = data.AsSpan(at, width);
            int end = bytes.IndexOf((byte)0);
            var name = utf8.GetString(end < 0 ? bytes : bytes[..end]);
            if (!named.TryAdd(name, at + width)) throw new InvalidDataException("Duplicate named resource.");
        }
    }

    private uint Read(int at) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at, 4));
    private static void Write(byte[] target, int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(target.AsSpan(at, 4), value);
    private static string Uncompressed(string path) => path.EndsWith(".zs", StringComparison.Ordinal) ? path[..^3] : path;

    private static uint Crc32(string text)
    {
        uint crc = 0xffffffff;
        foreach (byte b in Encoding.UTF8.GetBytes(text))
        {
            crc ^= b;
            for (int i = 0; i < 8; i++) crc = (crc >> 1) ^ (0xedb88320u & (uint)-(int)(crc & 1));
        }
        return ~crc;
    }

    private int Slot(string path)
    {
        var name = Uncompressed(path);
        if (named.TryGetValue(name, out int slot) || hashed.TryGetValue(Crc32(name), out slot)) return slot;
        throw new InvalidDataException("Map missing from resource size table: " + name);
    }

    public byte[] Patch(IReadOnlyDictionary<string, (long Original, long Patched)> sizes, byte[]? additionalTable = null)
    {
        var result = (byte[])data.Clone();
        if (additionalTable is not null)
        {
            var extra = new HAL_ResourceSizeTable(additionalTable);
            if (!hashed.Keys.ToHashSet().SetEquals(extra.hashed.Keys) || !named.Keys.ToHashSet().SetEquals(extra.named.Keys))
                throw new InvalidDataException("Prompt resource size table does not match the game.");
            foreach (var (key, at) in hashed) Write(result, at, Math.Max(Read(at), extra.Read(extra.hashed[key])));
            foreach (var (key, at) in named) Write(result, at, Math.Max(Read(at), extra.Read(extra.named[key])));
        }
        var touched = new HashSet<int>();
        foreach (var (path, lengths) in sizes.OrderBy(row => row.Key, StringComparer.Ordinal))
        {
            if (lengths.Original < 0 || lengths.Patched < 0) throw new InvalidDataException("Invalid resource lengths.");
            int slot = Slot(path);
            if (!touched.Add(slot)) throw new InvalidDataException("Ambiguous map resource size entry.");
            long allowance = Math.Max(0, (long)Read(slot) - lengths.Original);
            if (lengths.Patched > uint.MaxValue - allowance) throw new InvalidDataException("Resource allocation size overflow.");
            uint current = BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan(slot, 4));
            Write(result, slot, Math.Max(current, (uint)(lengths.Patched + allowance)));
        }
        return result;
    }

    public byte[] CloneEntries(IReadOnlyDictionary<string, string> aliases)
    {
        var additions = new Dictionary<uint, uint>();
        foreach (var (alias, original) in aliases.OrderBy(row => row.Key, StringComparer.Ordinal))
        {
            foreach (var name in new[] { alias, original })
                if (string.IsNullOrEmpty(name) || name.StartsWith('/') || name.Contains('\\') || name.Split('/').Contains(".."))
                    throw new InvalidDataException("Invalid resource alias.");
            string normalized = Uncompressed(alias);
            uint hash = Crc32(normalized);
            if (named.ContainsKey(normalized) || hashed.ContainsKey(hash) || !additions.TryAdd(hash, Read(Slot(original))))
                throw new InvalidDataException("Private resource alias collides with an existing budget.");
        }
        if (additions.Count == 0) return (byte[])data.Clone();
        var entries = hashed.ToDictionary(row => row.Key, row => Read(row.Value));
        foreach (var (key, value) in additions) entries.Add(key, value);
        var result = new byte[checked(data.Length + additions.Count * 8)];
        data.AsSpan(0, 22).CopyTo(result);
        Write(result, 14, checked((uint)entries.Count));
        int at = 22;
        foreach (var (key, value) in entries.OrderBy(row => row.Key))
        {
            Write(result, at, key); Write(result, at + 4, value); at += 8;
        }
        data.AsSpan(22 + hashCount * 8).CopyTo(result.AsSpan(at));
        return result;
    }
}
