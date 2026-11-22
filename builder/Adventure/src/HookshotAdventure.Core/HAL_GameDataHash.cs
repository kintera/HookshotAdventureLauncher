using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace HookshotAdventure.Core;

/// <summary>GameData member-name hashes, Murmur3 x86-32 with seed zero.</summary>
public static class HAL_GameDataHash
{
    public static uint Name(string text)
    {
        var data = new UTF8Encoding(false, true).GetBytes(text);
        unchecked
        {
            static uint Mix(uint value) => BitOperations.RotateLeft(value * 0xcc9e2d51u, 15) * 0x1b873593u;
            uint hash = 0;
            int full = data.Length & ~3;
            for (int i = 0; i < full; i += 4)
            {
                hash ^= Mix(BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(i, 4)));
                hash = BitOperations.RotateLeft(hash, 13) * 5 + 0xe6546b64u;
            }
            uint tail = 0;
            for (int i = full; i < data.Length; i++) tail |= (uint)data[i] << (8 * (i - full));
            if (full != data.Length) hash ^= Mix(tail);
            hash ^= (uint)data.Length;
            hash ^= hash >> 16; hash *= 0x85ebca6bu;
            hash ^= hash >> 13; hash *= 0xc2b2ae35u;
            return hash ^ (hash >> 16);
        }
    }
}
