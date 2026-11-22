using System.Buffers.Binary;
using System.Text;

namespace HookshotAdventure.Core;

/// <summary>Reads icon texture metadata and untile blocks from local BNTX archives.</summary>
public static class HAL_IconTexture
{
    public sealed record Texture(string Name, int Format, int Width, int Height, int Gobs, byte[] Blocks);
    public static Texture Read(byte[] data, string name)
    {
        ReadOnlySpan<byte> Span(int at, int length)
        {
            if (at < 0 || length < 0 || (long)at + length > data.Length) throw new InvalidDataException("Icon texture field outside resource.");
            return data.AsSpan(at, length);
        }
        uint U32(int at) => BinaryPrimitives.ReadUInt32LittleEndian(Span(at, 4));
        int Pointer(int at)
        {
            ulong value = BinaryPrimitives.ReadUInt64LittleEndian(Span(at, 8));
            return value <= int.MaxValue ? (int)value : throw new InvalidDataException("Icon texture pointer outside resource.");
        }
        if (!Span(0, 4).SequenceEqual("BNTX"u8) || !Span(0x20, 4).SequenceEqual("Ounc"u8)) throw new InvalidDataException("Invalid icon archive.");
        uint count = U32(0x24); if (count > 100000) throw new InvalidDataException("Invalid icon texture count.");
        int table = Pointer(0x28); Span(table, checked((int)count * 8)); Texture? result = null;
        for (int i = 0; i < count; i++)
        {
            int p = Pointer(checked(table + i * 8));
            if (!Span(p, 4).SequenceEqual("BRTI"u8)) throw new InvalidDataException("Invalid icon texture record.");
            int text = Pointer(p + 0x60), length = BinaryPrimitives.ReadUInt16LittleEndian(Span(text, 2));
            string textureName = new UTF8Encoding(false, true).GetString(Span(text + 2, length));
            if (textureName != name) continue;
            if (result is not null) throw new InvalidDataException("Duplicate icon texture name.");
            int format = checked((int)U32(p + 0x1c)), width = checked((int)U32(p + 0x24)), height = checked((int)U32(p + 0x28));
            uint exponent = U32(p + 0x34);
            if (width is < 1 or > 8192 || height is < 1 or > 8192 || exponent > 5 || (format >> 8) is not (0x2d or 0x1d))
                throw new InvalidDataException("Unsupported icon texture format or dimensions.");
            int gobs = 1 << (int)exponent, blockBytes = (format >> 8) == 0x2d ? 16 : 8;
            int bw = (width + 3) / 4, bh = (height + 3) / 4, pitch = (bw * blockBytes + 63) / 64;
            int mip = Pointer(Pointer(p + 0x70)); var blocks = new byte[checked(bw * bh * blockBytes)];
            for (int y = 0; y < bh; y++) for (int x = 0; x < bw; x++)
                Span(checked(mip + HAL_TunicTexture.GobOffset(x * blockBytes, y, gobs, pitch)), blockBytes)
                    .CopyTo(blocks.AsSpan((y * bw + x) * blockBytes));
            result = new Texture(name, format, width, height, gobs, blocks);
        }
        return result ?? throw new InvalidDataException("Icon texture not found.");
    }
    public static byte[] DecodeBc4(Texture texture)
    {
        if ((texture.Format >> 8) != 0x1d) throw new InvalidDataException("Icon texture is not BC4.");
        int bw = (texture.Width + 3) / 4, bh = (texture.Height + 3) / 4;
        if (texture.Blocks.Length != checked(bw * bh * 8)) throw new InvalidDataException("Invalid BC4 blocks.");
        var pixels = new byte[checked(texture.Width * texture.Height)];
        for (int by = 0; by < bh; by++) for (int bx = 0; bx < bw; bx++)
        {
            int at = (by * bw + bx) * 8; int a = texture.Blocks[at], b = texture.Blocks[at + 1];
            var values = new int[8]; values[0] = a; values[1] = b;
            if (a > b) for (int i = 1; i <= 6; i++) values[i + 1] = ((7 - i) * a + i * b) / 7;
            else { for (int i = 1; i <= 4; i++) values[i + 1] = ((5 - i) * a + i * b) / 5; values[6] = 0; values[7] = 255; }
            ulong bits = 0; for (int i = 0; i < 6; i++) bits |= (ulong)texture.Blocks[at + 2 + i] << (8 * i);
            for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++)
            {
                int px = bx * 4 + x, py = by * 4 + y;
                if (px < texture.Width && py < texture.Height) pixels[py * texture.Width + px] = (byte)values[(bits >> (3 * (y * 4 + x))) & 7];
            }
        }
        return pixels;
    }
}
