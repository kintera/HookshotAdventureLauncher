using System.Buffers.Binary;
using System.Globalization;

namespace HookshotAdventure.Core;

/// <summary>Changes the trail shader in a private copy of a supported sword effect.</summary>
public static class HAL_SwordTrailPatch
{
    public static byte[] Apply(byte[] source, string color)
    {
        if (color.Length != 7 || color[0] != '#' || !int.TryParse(color.AsSpan(1), NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out int rgb))
            throw new InvalidDataException("Invalid sword trail color.");
        var result = (byte[])source.Clone();
        int found = -1;
        // Match the three adjacent vertex operations that form the trail's RGB
        // varying. Check registers, operands and instruction modes, and require
        // a single match inside a structurally valid shader archive.
        for (int at = 0; at <= result.Length - 48; at += 4)
        {
            if (!Matches(result, at)) continue;
            bool inShader = false;
            for (int start = 0; start <= at - 100; start += 4)
            {
                if (!result.AsSpan(start, 4).SequenceEqual("BNSH"u8) ||
                    !result.AsSpan(start + 96, 4).SequenceEqual("grsc"u8)) continue;
                uint length = BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan(start + 28, 4));
                if ((long)start + length <= result.Length && at + 48L <= (long)start + length) { inShader = true; break; }
            }
            if (!inShader) continue;
            if (found >= 0) throw new InvalidDataException("Ambiguous sword trail shader.");
            found = at;
        }
        if (found < 0) throw new InvalidDataException("Sword trail shader differs from the supported game version.");
        for (int i = 0; i < 3; i++)
        {
            var instruction = result.AsSpan(found + i * 16, 16);
            uint opcode = BinaryPrimitives.ReadUInt32LittleEndian(instruction);
            // Use an immediate float in place of the original uniform operand.
            BinaryPrimitives.WriteUInt32LittleEndian(instruction, (opcode & ~0xfffu) | 0x820u);
            float component = ((rgb >> (16 - 8 * i)) & 255) / 255f;
            BinaryPrimitives.WriteSingleLittleEndian(instruction[4..], component);
        }
        return result;
    }

    private static bool Matches(byte[] data, int at)
    {
        for (int i = 0; i < 3; i++)
        {
            var ins = data.AsSpan(at + i * 16, 16);
            uint first = BinaryPrimitives.ReadUInt32LittleEndian(ins);
            if (first != 0x12107a20u + ((uint)i << 16) ||
                BinaryPrimitives.ReadUInt32LittleEndian(ins[4..]) != 0x03805c00u + ((uint)i << 8) ||
                BinaryPrimitives.ReadUInt32LittleEndian(ins[8..]) != 0x00410000u ||
                (BinaryPrimitives.ReadUInt32LittleEndian(ins[12..]) & ~0x04000000u) != 0x000fe200u)
                return false;
        }
        return true;
    }
}
