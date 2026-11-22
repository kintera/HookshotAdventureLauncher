using System.Buffers.Binary;
using System.Text;

namespace HookshotAdventure.Core;

/// <summary>Append a private event string while preserving existing pointer relocations.</summary>
public static class HAL_EventFlowStrings
{
    public static byte[] AppendAndPoint(byte[] data, int pointerSlot, string value)
    {
        if (data.Length < 64 || !data.AsSpan(0, 8).SequenceEqual("BFEVFL\0\0"u8) ||
            string.IsNullOrEmpty(value) || value.Contains('\0')) throw new InvalidDataException("Invalid event string edit.");
        byte[] text = new UTF8Encoding(false, true).GetBytes(value);
        if (text.Length > ushort.MaxValue) throw new InvalidDataException("Event string is too long.");
        uint U32(int at) => at >= 0 && (long)at + 4 <= data.Length ? BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at, 4)) : throw new InvalidDataException("Truncated event resource.");
        ushort U16(int at) => at >= 0 && (long)at + 2 <= data.Length ? BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at, 2)) : throw new InvalidDataException("Truncated event string pool.");
        int relocation = checked((int)U32(24));
        if (U32(28) != data.Length || relocation < 64 || (long)relocation + 40 > data.Length ||
            !data.AsSpan(relocation, 4).SequenceEqual("RELT"u8) || U32(relocation + 4) != relocation || U32(relocation + 8) != 1 ||
            U32(relocation + 24) != 0 || U32(relocation + 32) != 0)
            throw new InvalidDataException("Unsupported event relocation table.");
        int end = checked((int)U32(relocation + 28)), entries = checked((int)U32(relocation + 36));
        if (end < 64 || end > relocation || (long)relocation + 40L + entries * 8L != data.Length ||
            pointerSlot < 0 || (long)pointerSlot + 8 > end) throw new InvalidDataException("Invalid event relocation section.");
        bool registered = false;
        for (int i = 0; i < entries; i++)
        {
            int at = relocation + 40 + i * 8; uint first = U32(at), mask = U32(at + 4);
            long distance = (long)pointerSlot - first;
            if (distance >= 0 && distance % 8 == 0 && distance / 8 < 32 && (mask & (1u << (int)(distance / 8))) != 0) registered = true;
        }
        if (!registered) throw new InvalidDataException("Event string pointer is not relocated.");
        int pool = data.AsSpan(0, end).LastIndexOf("STR "u8);
        if (pool < 0 || (long)pool + 20 > end || U32(pool + 4) != 0 || U32(pool + 8) != 0 || U32(pool + 12) != 0)
            throw new InvalidDataException("Event string pool is missing.");
        uint count = U32(pool + 16); int position = pool + 20;
        for (ulong i = 0; i <= count; i++)
        {
            int length = U16(position);
            if ((long)position + 3 + length > end || data[position + 2 + length] != 0) throw new InvalidDataException("Invalid event string pool entry.");
            position = checked((position + length + 4) & ~1);
        }
        if (position != end) throw new InvalidDataException("Unexpected event string pool boundary.");
        int newEnd = checked((end + 2 + text.Length + 1 + 1) & ~1), newRelocation = checked((newEnd + 7) & ~7);
        byte[] result = new byte[checked(newRelocation + data.Length - relocation)];
        data.AsSpan(0, end).CopyTo(result); data.AsSpan(relocation).CopyTo(result.AsSpan(newRelocation));
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(end), (ushort)text.Length);
        text.CopyTo(result.AsSpan(end + 2));
        BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(pointerSlot), (ulong)end);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(pool + 16), checked(count + 1));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(24), (uint)newRelocation);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(28), (uint)result.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(newRelocation + 4), (uint)newRelocation);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(newRelocation + 28), (uint)newEnd);
        return result;
    }
}
