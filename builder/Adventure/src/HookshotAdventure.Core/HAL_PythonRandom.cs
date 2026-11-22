using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace HookshotAdventure.Core;

/// <summary>Python's integer-seeded MT19937 sequence for existing cosmetic seeds.</summary>
public sealed class HAL_PythonRandom
{
    private readonly uint[] state = new uint[624]; private int index = 624;
    public HAL_PythonRandom(string seed)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(seed)); var words = new uint[8];
        for (int i = 0; i < words.Length; i++) words[i] = BinaryPrimitives.ReadUInt32BigEndian(digest.AsSpan(28 - i * 4, 4));
        int length = words.Length; while (length > 1 && words[length - 1] == 0) length--;
        var key = words[..length];
        unchecked
        {
            state[0] = 19650218;
            for (int i = 1; i < 624; i++) state[i] = 1812433253 * (state[i-1] ^ (state[i-1] >> 30)) + (uint)i;
            int at = 1, j = 0;
            for (int k = Math.Max(624, key.Length); k > 0; k--)
            {
                state[at] = (state[at] ^ ((state[at-1] ^ (state[at-1] >> 30)) * 1664525)) + key[j] + (uint)j;
                if (++at >= 624) { state[0] = state[623]; at = 1; } if (++j >= key.Length) j = 0;
            }
            for (int k = 623; k > 0; k--)
            {
                state[at] = (state[at] ^ ((state[at-1] ^ (state[at-1] >> 30)) * 1566083941)) - (uint)at;
                if (++at >= 624) { state[0] = state[623]; at = 1; }
            }
            state[0] = 0x80000000;
        }
    }
    private uint Next()
    {
        if (index >= 624)
        {
            for (int i = 0; i < 624; i++)
            {
                uint y = (state[i] & 0x80000000) | (state[(i+1)%624] & 0x7fffffff);
                state[i] = state[(i+397)%624] ^ (y >> 1) ^ ((y & 1) != 0 ? 0x9908b0dfu : 0);
            }
            index = 0;
        }
        uint value = state[index++]; value ^= value >> 11; value ^= (value << 7) & 0x9d2c5680;
        value ^= (value << 15) & 0xefc60000; value ^= value >> 18; return value;
    }
    public int ColorComponent()
    {
        uint value; do { value = Next() >> 24; } while (value >= 208); return 24 + (int)value;
    }
}
