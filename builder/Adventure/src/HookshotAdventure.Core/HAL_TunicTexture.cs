using System.Buffers.Binary;
using System.Text;

namespace HookshotAdventure.Core;

/// <summary>Bounded view of a single block-linear player texture.</summary>
public sealed class HAL_TunicTexture
{
    private static readonly (int X, int Y)[] AstcBlocks = [(4,4),(5,4),(5,5),(6,5),(6,6),(8,5),(8,6),
        (8,8),(10,5),(10,6),(10,8),(10,10),(12,10),(12,12)];
    private readonly byte[] data;
    public int Width { get; }
    public int Height { get; }
    public int Format { get; }
    public int BlockWidth { get; }
    public int BlockHeight { get; }
    public int BlockBytes { get; }
    public int NameOffset { get; }
    public int NameLength { get; }
    public string Name { get; }
    public IReadOnlyList<int[]> MipOffsets { get; }
    private ReadOnlySpan<byte> Span(int at, int length)
    {
        if (at < 0 || length < 0 || (long)at + length > data.Length) throw new InvalidDataException("Texture field outside resource.");
        return data.AsSpan(at, length);
    }
    private int U16(int at) => BinaryPrimitives.ReadUInt16LittleEndian(Span(at, 2));
    private uint U32(int at) => BinaryPrimitives.ReadUInt32LittleEndian(Span(at, 4));
    private int Pointer(int at)
    {
        ulong value = BinaryPrimitives.ReadUInt64LittleEndian(Span(at, 8));
        return value <= int.MaxValue ? (int)value : throw new InvalidDataException("Texture pointer outside resource.");
    }
    public HAL_TunicTexture(byte[] source)
    {
        data = (byte[])source.Clone();
        if (!Span(0, 4).SequenceEqual("BNTX"u8) || !Span(0x20, 4).SequenceEqual("Ounc"u8) || U32(0x24) != 1)
            throw new InvalidDataException("Expected a single Switch 2 texture.");
        int p = Pointer(Pointer(0x28));
        if (!Span(p, 4).SequenceEqual("BRTI"u8) || U16(p + 0x14) != 0) throw new InvalidDataException("Expected a block-linear texture.");
        int count = U16(p + 0x16); Format = checked((int)U32(p + 0x1c)); Width = checked((int)U32(p + 0x24)); Height = checked((int)U32(p + 0x28));
        if (U32(p + 0x2c) != 1 || U32(p + 0x30) != 1 || Width is < 1 or > 4096 || Height is < 1 or > 4096 ||
            count < 1 || count > System.Numerics.BitOperations.Log2((uint)Math.Max(Width, Height)) + 1)
            throw new InvalidDataException("Unsupported texture dimensions or mip chain.");
        (BlockWidth, BlockHeight) = Format is 0x1a06 or 0x1c06 ? (4,4) :
            Format >= 0x2d06 && Format <= 0x3a06 && (Format & 255) == 6 ? AstcBlocks[(Format >> 8) - 0x2d] :
            throw new InvalidDataException("Unsupported texture format.");
        BlockBytes = Format == 0x1a06 ? 8 : 16;
        uint exponent = U32(p + 0x34); if (exponent > 5) throw new InvalidDataException("Invalid texture block height.");
        int gobsInitial = 1 << (int)exponent; NameOffset = Pointer(p + 0x60); NameLength = U16(NameOffset);
        Name = new UTF8Encoding(false, true).GetString(Span(NameOffset + 2, NameLength));
        if (Span(NameOffset + 2 + NameLength, 1)[0] != 0) throw new InvalidDataException("Invalid texture name.");
        int pointers = Pointer(p + 0x70); var starts = Enumerable.Range(0, count).Select(i => Pointer(checked(pointers + i * 8))).ToArray();
        int end = checked(starts[0] + (int)U32(p + 0x50)); Span(starts[0], end - starts[0]);
        var offsets = new List<int[]>(); var used = new HashSet<int>();
        for (int level = 0; level < count; level++)
        {
            int stop = level + 1 < count ? starts[level + 1] : end;
            if (starts[level] >= stop) throw new InvalidDataException("Overlapping texture mip levels.");
            int w = Math.Max(1, Width >> level), h = Math.Max(1, Height >> level);
            int bw = (w + BlockWidth - 1) / BlockWidth, bh = (h + BlockHeight - 1) / BlockHeight, gobs = gobsInitial;
            while (gobs > 1 && bh <= 8 * (gobs / 2)) gobs /= 2;
            int pitch = (bw * BlockBytes + 63) / 64; var levelOffsets = new int[checked(bw * bh)];
            for (int y = 0; y < bh; y++) for (int x = 0; x < bw; x++)
            {
                int at = checked(starts[level] + GobOffset(x * BlockBytes, y, gobs, pitch));
                if (at < starts[level] || (long)at + BlockBytes > stop || !used.Add(at)) throw new InvalidDataException("Texture block outside allocation.");
                levelOffsets[y * bw + x] = at;
            }
            offsets.Add(levelOffsets);
        }
        MipOffsets = offsets.AsReadOnly();
    }
    public static int GobOffset(int x, int y, int gobs, int pitch)
    {
        int baseOffset = (y / (8 * gobs)) * 512 * gobs * pitch + (x / 64) * 512 * gobs + (y % (8 * gobs) / 8) * 512;
        return baseOffset + (((x & 63) >> 5) << 8 | ((y & 7) >> 2) << 7 | ((x & 31) >> 4) << 6 | ((y >> 1) & 1) << 5 | (y & 1) << 4 | (x & 15));
    }
    public byte[] Linear(int level)
    {
        var offsets = MipOffsets[level]; var result = new byte[checked(offsets.Length * BlockBytes)];
        for (int i = 0; i < offsets.Length; i++) Span(offsets[i], BlockBytes).CopyTo(result.AsSpan(i * BlockBytes));
        return result;
    }
}
