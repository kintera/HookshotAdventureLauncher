using System.Buffers.Binary;

namespace HookshotAdventure.Core;

/// <summary>Decodes BC1 and BC3 blocks into RGBA pixels for local texture edits.</summary>
public static class HAL_BcTextures
{
    /// <summary>Encodes edited RGB blocks while retaining the original BC3 alpha plane.</summary>
    public static byte[] Encode(byte[] rgba, int width, int height, byte[] original, bool bc3)
    {
        if (width <= 0 || height <= 0 || width > 8192 || height > 8192 || (long)width * height * 4 != rgba.Length)
            throw new InvalidDataException("Invalid BC image dimensions.");
        int bw = (width + 3) / 4, bh = (height + 3) / 4, stride = bc3 ? 16 : 8;
        if (original.Length != checked(bw * bh * stride)) throw new InvalidDataException("Invalid BC texture length.");
        var result = (byte[])original.Clone();
        static ushort Pack(int r, int g, int b) => (ushort)(((r * 31 + 127) / 255 << 11) | ((g * 63 + 127) / 255 << 5) | ((b * 31 + 127) / 255));
        static (int R, int G, int B) Unpack(ushort c) => (((c >> 11) & 31) * 255 / 31, ((c >> 5) & 63) * 255 / 63, (c & 31) * 255 / 31);
        for (int by = 0; by < bh; by++) for (int bx = 0; bx < bw; bx++)
        {
            int loR = 255, loG = 255, loB = 255, hiR = 0, hiG = 0, hiB = 0; bool transparent = false;
            for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++)
            {
                int px = Math.Min(width - 1, bx * 4 + x), py = Math.Min(height - 1, by * 4 + y), at = (py * width + px) * 4;
                if (!bc3 && rgba[at + 3] < 128) { transparent = true; continue; }
                loR = Math.Min(loR, rgba[at]); hiR = Math.Max(hiR, rgba[at]);
                loG = Math.Min(loG, rgba[at+1]); hiG = Math.Max(hiG, rgba[at+1]); loB = Math.Min(loB, rgba[at+2]); hiB = Math.Max(hiB, rgba[at+2]);
            }
            ushort c0 = Pack(hiR,hiG,hiB), c1 = Pack(loR,loG,loB);
            if (transparent) { if (c0 > c1) (c0,c1) = (c1,c0); }
            else if (c0 <= c1) { if (c0 < c1) (c0,c1) = (c1,c0); else if (c0 < ushort.MaxValue) c0++; else c1--; }
            var a = Unpack(c0); var b = Unpack(c1);
            var palette = new (int R, int G, int B)[4]; palette[0] = a; palette[1] = b;
            if (transparent) palette[2] = ((a.R+b.R)/2, (a.G+b.G)/2, (a.B+b.B)/2);
            else { palette[2] = ((2*a.R+b.R)/3, (2*a.G+b.G)/3, (2*a.B+b.B)/3); palette[3] = ((a.R+2*b.R)/3, (a.G+2*b.G)/3, (a.B+2*b.B)/3); }
            uint indices = 0;
            for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++)
            {
                int px = Math.Min(width-1,bx*4+x), py = Math.Min(height-1,by*4+y), at = (py*width+px)*4;
                int best = transparent && rgba[at+3] < 128 ? 3 : 0, distance = int.MaxValue;
                if (!(transparent && rgba[at+3] < 128)) for (int i = 0; i < (transparent ? 3 : 4); i++)
                {
                    int dr = rgba[at]-palette[i].R, dg = rgba[at+1]-palette[i].G, db = rgba[at+2]-palette[i].B;
                    int d = dr*dr + dg*dg + db*db; if (d < distance) { distance = d; best = i; }
                }
                indices |= (uint)best << (2*(y*4+x));
            }
            int target = (by*bw+bx)*stride + (bc3 ? 8 : 0);
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(target), c0); BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(target+2), c1);
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(target+4), indices);
        }
        return result;
    }
    public static byte[] Decode(byte[] blocks, int width, int height, bool bc3)
    {
        if (width <= 0 || height <= 0 || width > 8192 || height > 8192) throw new InvalidDataException("Invalid BC texture dimensions.");
        int bw = (width + 3) / 4, bh = (height + 3) / 4, stride = bc3 ? 16 : 8;
        if (blocks.Length != checked(bw * bh * stride)) throw new InvalidDataException("Invalid BC texture length.");
        var result = new byte[checked(width * height * 4)];
        for (int by = 0; by < bh; by++) for (int bx = 0; bx < bw; bx++)
        {
            int start = (by * bw + bx) * stride, at = start + (bc3 ? 8 : 0);
            ushort c0 = BinaryPrimitives.ReadUInt16LittleEndian(blocks.AsSpan(at, 2)), c1 = BinaryPrimitives.ReadUInt16LittleEndian(blocks.AsSpan(at + 2, 2));
            static (int R, int G, int B) Color(ushort c)
            {
                int r = (c >> 11) & 31, g = (c >> 5) & 63, b = c & 31;
                return ((r << 3) | (r >> 2), (g << 2) | (g >> 4), (b << 3) | (b >> 2));
            }
            var a = Color(c0); var b = Color(c1); var colors = new (int R, int G, int B, int A)[4];
            colors[0] = (a.R, a.G, a.B, 255); colors[1] = (b.R, b.G, b.B, 255);
            if (bc3 || c0 > c1)
            {
                colors[2] = ((2*a.R+b.R)/3, (2*a.G+b.G)/3, (2*a.B+b.B)/3, 255);
                colors[3] = ((a.R+2*b.R)/3, (a.G+2*b.G)/3, (a.B+2*b.B)/3, 255);
            }
            else { colors[2] = ((a.R+b.R)/2, (a.G+b.G)/2, (a.B+b.B)/2, 255); colors[3] = (0,0,0,0); }
            uint indices = BinaryPrimitives.ReadUInt32LittleEndian(blocks.AsSpan(at + 4, 4));
            int[] alpha = new int[8]; ulong alphaBits = 0;
            if (bc3)
            {
                alpha[0] = blocks[start]; alpha[1] = blocks[start + 1];
                if (alpha[0] > alpha[1]) for (int i = 1; i <= 6; i++) alpha[i + 1] = ((7-i)*alpha[0] + i*alpha[1])/7;
                else { for (int i = 1; i <= 4; i++) alpha[i + 1] = ((5-i)*alpha[0] + i*alpha[1])/5; alpha[6] = 0; alpha[7] = 255; }
                for (int i = 0; i < 6; i++) alphaBits |= (ulong)blocks[start + 2 + i] << (8*i);
            }
            for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++)
            {
                int px = bx*4+x, py = by*4+y, n = y*4+x; if (px >= width || py >= height) continue;
                var color = colors[(indices >> (2*n)) & 3]; int destination = (py*width+px)*4;
                result[destination] = (byte)color.R; result[destination+1] = (byte)color.G; result[destination+2] = (byte)color.B;
                result[destination+3] = (byte)(bc3 ? alpha[(alphaBits >> (3*n)) & 7] : color.A);
            }
        }
        return result;
    }
}
