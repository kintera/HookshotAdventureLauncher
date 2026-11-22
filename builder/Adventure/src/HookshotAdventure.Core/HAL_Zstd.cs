using System.Buffers.Binary;

namespace HookshotAdventure.Core;

/// <summary>Zstandard frames and local game dictionary archives.</summary>
public static class HAL_Zstd
{
    public static byte[] Compress(byte[] source)
    {
        using var compressor = new ZstdSharp.Compressor(3);
        return compressor.Wrap(source).ToArray();
    }

    private static byte[]? TryDecompress(byte[] source, byte[]? dictionary, int limit)
    {
        try
        {
            using var input = new MemoryStream(source, writable: false);
            using var stream = new ZstdSharp.DecompressionStream(input);
            if (dictionary is not null) stream.LoadDictionary(dictionary);
            using var output = new MemoryStream(); var buffer = new byte[65536];
            while (true)
            {
                int read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, (long)limit - output.Length + 1));
                if (read == 0) return output.ToArray();
                if (output.Length + read > limit) throw new InvalidDataException("Zstandard output exceeds the permitted size.");
                output.Write(buffer, 0, read);
            }
        }
        catch (ZstdSharp.ZstdException)
        {
            return null;
        }
    }

    public static byte[] Unpack(byte[] data, IEnumerable<byte[]> dictionaries, int maxOutputSize = int.MaxValue)
    {
        if (maxOutputSize < 1) throw new ArgumentOutOfRangeException(nameof(maxOutputSize));
        if (data.Length < 4 || !data.AsSpan(0, 4).SequenceEqual(new byte[] { 0x28, 0xb5, 0x2f, 0xfd })) return (byte[])data.Clone();
        var unpacked = TryDecompress(data, null, maxOutputSize);
        if (unpacked is not null) return unpacked;
        foreach (var dictionary in dictionaries)
            if ((unpacked = TryDecompress(data, dictionary, maxOutputSize)) is not null) return unpacked;
        throw new InvalidDataException("No Zstandard dictionary fits the resource.");
    }

    public static IReadOnlyList<byte[]> ReadDictionaryArchive(byte[] compressed)
    {
        var raw = Unpack(compressed, Array.Empty<byte[]>(), 1 << 28);
        if (raw.Length < 0x20) throw new InvalidDataException("Truncated dictionary archive.");
        uint start = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(0x0c, 4));
        int count = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(0x1a, 2));
        if (0x20L + count * 16L > raw.Length) throw new InvalidDataException("Truncated dictionary entries.");
        var result = new List<byte[]>(count);
        for (int i = 0; i < count; i++)
        {
            int at = 0x20 + i * 16;
            uint from = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(at + 8, 4));
            uint end = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(at + 12, 4));
            if (end < from || (ulong)start + end > (ulong)raw.Length) throw new InvalidDataException("Invalid dictionary archive offsets.");
            result.Add(raw.AsSpan(checked((int)(start + from)), checked((int)(end - from))).ToArray());
        }
        return result;
    }
}
