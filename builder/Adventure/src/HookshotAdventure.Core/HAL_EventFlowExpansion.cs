using System.Buffers.Binary;

namespace HookshotAdventure.Core;

/// <summary>Reserve space for new event structures without changing existing event identities.</summary>
public static class HAL_EventFlowExpansion
{
    public readonly record struct Reservation(byte[] Data, int Offset, int Length);

    // BFEVFL's string pool is the last data block. Place new structures immediately before it,
    // moving the pool and its registered pointers together. New pointer fields must be registered
    // by the eventual structure writer before the resulting resource is used by the game.
    public static Reservation Reserve(byte[] source, int length)
    {
        if (length <= 0 || (length & 7) != 0) throw new ArgumentOutOfRangeException(nameof(length));
        if (source.Length < 64 || !source.AsSpan(0, 8).SequenceEqual("BFEVFL\0\0"u8))
            throw new InvalidDataException("Invalid event resource.");
        uint U32(int at) => at >= 0 && (long)at + 4 <= source.Length
            ? BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(at, 4)) : throw new InvalidDataException("Truncated event resource.");
        ulong U64(int at) => at >= 0 && (long)at + 8 <= source.Length
            ? BinaryPrimitives.ReadUInt64LittleEndian(source.AsSpan(at, 8)) : throw new InvalidDataException("Truncated event resource.");
        int At(ulong v) => v <= int.MaxValue ? (int)v : throw new InvalidDataException("Event pointer is out of range.");
        int relocation = At(U32(24));
        if (U32(28) != source.Length || relocation < 64 || (long)relocation + 40 > source.Length ||
            !source.AsSpan(relocation, 4).SequenceEqual("RELT"u8) || U32(relocation + 4) != relocation ||
            U32(relocation + 8) != 1 || U32(relocation + 24) != 0 || U32(relocation + 32) != 0)
            throw new InvalidDataException("Unsupported event relocation table.");
        int end = At(U32(relocation + 28)), entries = At(U32(relocation + 36));
        if (end < 64 || end > relocation || (long)relocation + 40L + entries * 8L != source.Length)
            throw new InvalidDataException("Invalid event relocation section.");
        if (BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(32, 2)) != 1 ||
            BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(34, 2)) != 0)
            throw new InvalidDataException("Only single-flow event resources can be expanded.");
        int flowList = At(U64(40)), flow = At(U64(flowList));
        if (flow < 64 || (long)flow + 8 > end || !source.AsSpan(flow, 4).SequenceEqual("EVFL"u8))
            throw new InvalidDataException("Invalid event flowchart.");
        int pool = checked(flow + At(U32(flow + 4)));
        if (pool < flow + 8 || (long)pool + 20 > end || !source.AsSpan(pool, 4).SequenceEqual("STR "u8))
            throw new InvalidDataException("Invalid event string pool.");
        int count = At(U32(pool + 16)), position = pool + 20;
        for (int i = 0; i <= count; i++)
        {
            if ((long)position + 2 > end) throw new InvalidDataException("Truncated event string pool.");
            int n = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(position, 2));
            if ((long)position + n + 3 > end || source[position + n + 2] != 0)
                throw new InvalidDataException("Invalid event string pool entry.");
            position = checked((position + n + 4) & ~1);
        }
        if (position != end) throw new InvalidDataException("Unexpected event string pool boundary.");
        var pointers = new HashSet<int>();
        for (int i = 0; i < entries; i++)
        {
            int entry = relocation + 40 + i * 8, first = At(U32(entry)); uint mask = U32(entry + 4);
            for (int bit = 0; bit < 32; bit++)
            {
                if ((mask & (1u << bit)) == 0) continue;
                int slot = checked(first + 8 * bit);
                if (slot < 0 || (long)slot + 8 > pool || !pointers.Add(slot))
                    throw new InvalidDataException("Invalid or duplicate event relocation pointer.");
                ulong target = U64(slot);
                if (target >= (ulong)end && target != 0) throw new InvalidDataException("Event pointer is outside the data section.");
            }
        }
        int name = At(U32(16));
        if (name != 0 && name >= end) throw new InvalidDataException("Event name is outside the data section.");
        byte[] result = new byte[checked(source.Length + length)];
        source.AsSpan(0, pool).CopyTo(result);
        source.AsSpan(pool).CopyTo(result.AsSpan(pool + length));
        foreach (int slot in pointers)
        {
            ulong target = U64(slot);
            if (target >= (ulong)pool)
                BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(slot), checked(target + (ulong)length));
        }
        if (name >= pool) BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(16), checked((uint)(name + length)));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(flow + 4), checked((uint)(pool + length - flow)));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(24), checked((uint)(relocation + length)));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(28), checked((uint)result.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(relocation + length + 4), checked((uint)(relocation + length)));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(relocation + length + 28), checked((uint)(end + length)));
        return new Reservation(result, pool, length);
    }

    /// <summary>Register pointers in newly written structures for the game's relocation loader.</summary>
    public static byte[] RegisterPointers(byte[] source, IReadOnlyList<int> newSlots)
    {
        if (source.Length < 64 || !source.AsSpan(0, 8).SequenceEqual("BFEVFL\0\0"u8))
            throw new InvalidDataException("Invalid event resource.");
        uint U32(int at) => at >= 0 && (long)at + 4 <= source.Length
            ? BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(at, 4)) : throw new InvalidDataException("Truncated event resource.");
        ulong U64(int at) => at >= 0 && (long)at + 8 <= source.Length
            ? BinaryPrimitives.ReadUInt64LittleEndian(source.AsSpan(at, 8)) : throw new InvalidDataException("Truncated event resource.");
        int At(ulong v) => v <= int.MaxValue ? (int)v : throw new InvalidDataException("Event pointer is out of range.");
        int relocation = At(U32(24));
        if (U32(28) != source.Length || relocation < 64 || (long)relocation + 40 > source.Length ||
            !source.AsSpan(relocation, 4).SequenceEqual("RELT"u8) || U32(relocation + 4) != relocation ||
            U32(relocation + 8) != 1 || U32(relocation + 24) != 0 || U32(relocation + 32) != 0)
            throw new InvalidDataException("Unsupported event relocation table.");
        int end = At(U32(relocation + 28)), entries = At(U32(relocation + 36));
        if (end < 64 || end > relocation || (long)relocation + 40L + entries * 8L != source.Length)
            throw new InvalidDataException("Invalid event relocation section.");
        int flow = At(U64(At(U64(40))));
        if (flow < 64 || (long)flow + 8 > end || !source.AsSpan(flow, 4).SequenceEqual("EVFL"u8))
            throw new InvalidDataException("Invalid event flowchart.");
        int pool = checked(flow + At(U32(flow + 4)));
        if (pool < flow + 8 || (long)pool + 20 > end || !source.AsSpan(pool, 4).SequenceEqual("STR "u8))
            throw new InvalidDataException("Invalid event string pool.");
        var slots = new SortedSet<int>();
        void Validate(int slot)
        {
            if (slot < 0 || (slot & 7) != 0 || (long)slot + 8 > pool)
                throw new InvalidDataException("Invalid event relocation pointer.");
            ulong target = U64(slot);
            if (target >= (ulong)end && target != 0) throw new InvalidDataException("Event pointer is outside the data section.");
        }
        for (int i = 0; i < entries; i++)
        {
            int entry = relocation + 40 + i * 8, first = At(U32(entry)); uint mask = U32(entry + 4);
            for (int bit = 0; bit < 32; bit++)
                if ((mask & (1u << bit)) != 0)
                {
                    int slot = checked(first + 8 * bit); Validate(slot);
                    if (!slots.Add(slot)) throw new InvalidDataException("Duplicate event relocation pointer.");
                }
        }
        foreach (int slot in newSlots) { Validate(slot); slots.Add(slot); }
        var groups = new List<(int Offset, uint Mask)>();
        foreach (int slot in slots)
        {
            if (groups.Count == 0 || slot - groups[^1].Offset >= 256) groups.Add((slot, 1));
            else
            {
                var group = groups[^1];
                groups[^1] = (group.Offset, group.Mask | (1u << ((slot - group.Offset) / 8)));
            }
        }
        byte[] result = new byte[checked(relocation + 40 + groups.Count * 8)];
        source.AsSpan(0, relocation + 40).CopyTo(result);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(28), checked((uint)result.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(relocation + 36), checked((uint)groups.Count));
        for (int i = 0; i < groups.Count; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(relocation + 40 + i * 8), checked((uint)groups[i].Offset));
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(relocation + 44 + i * 8), groups[i].Mask);
        }
        return result;
    }

    /// <summary>Clone the event array into private space and reserve new event records.</summary>
    public static (byte[] Data, int FirstNewEvent) ExpandEvents(byte[] source, int additionalEvents)
    {
        if (additionalEvents <= 0) throw new ArgumentOutOfRangeException(nameof(additionalEvents));
        // Validate the original flow and the relocation table before copying any structures.
        var checkedFlow = new HAL_EventFlow(source, FlowName(source));
        int oldCount = checkedFlow.Count;
        if ((long)oldCount + additionalEvents > ushort.MaxValue) throw new InvalidDataException("Too many event records.");
        int size = checked((oldCount + additionalEvents) * 48);
        var reservation = Reserve(source, size);
        var data = reservation.Data;
        int flowList = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(40, 8)));
        int flow = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(flowList, 8)));
        int oldEvents = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(flow + 48, 8)));
        if (oldEvents < 0 || (long)oldEvents + oldCount * 48L > reservation.Offset)
            throw new InvalidDataException("Event array is outside the original data section.");
        data.AsSpan(oldEvents, oldCount * 48).CopyTo(data.AsSpan(reservation.Offset));
        int relocation = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(24, 4)));
        int entries = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(relocation + 36, 4)));
        var copiedSlots = new List<int> { flow + 48 };
        for (int i = 0; i < entries; i++)
        {
            int e = relocation + 40 + i * 8;
            int first = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(e, 4)));
            uint mask = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(e + 4, 4));
            for (int bit = 0; bit < 32; bit++)
            {
                if ((mask & (1u << bit)) == 0) continue;
                int slot = checked(first + bit * 8);
                if (slot >= oldEvents && (long)slot + 8 <= (long)oldEvents + oldCount * 48L)
                    copiedSlots.Add(checked(reservation.Offset + slot - oldEvents));
            }
        }
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(flow + 48, 8), (ulong)reservation.Offset);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(flow + 22, 2), (ushort)(oldCount + additionalEvents));
        return (RegisterPointers(data, copiedSlots), oldCount);
    }

    /// <summary>Add a query to an existing actor without changing its current action or query indexes.</summary>
    public static (byte[] Data, int Actor, int Query) EnsureActorQuery(byte[] source, string actorName, string queryName)
    {
        if (string.IsNullOrEmpty(actorName) || string.IsNullOrEmpty(queryName))
            throw new ArgumentException("Actor and query names are required.");
        _ = new HAL_EventFlow(source, FlowName(source));
        ulong U64(byte[] b, int at) => at >= 0 && (long)at + 8 <= b.Length
            ? BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan(at, 8)) : throw new InvalidDataException("Truncated event resource.");
        int At(ulong n) => n <= int.MaxValue ? (int)n : throw new InvalidDataException("Invalid event pointer.");
        string ReadString(byte[] b, int slot)
        {
            int at = At(U64(b, slot));
            if (at == 0) return "";
            if ((long)at + 2 > b.Length) throw new InvalidDataException("Invalid event string.");
            int n = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(at, 2));
            if ((long)at + 2 + n > b.Length) throw new InvalidDataException("Truncated event string.");
            return new System.Text.UTF8Encoding(false, true).GetString(b.AsSpan(at + 2, n));
        }
        int flow = At(U64(source, At(U64(source, 40))));
        int actorCount = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(flow + 16, 2));
        int actorArray = At(U64(source, flow + 40)), actorIndex = -1;
        for (int i = 0; i < actorCount; i++)
        {
            int at = checked(actorArray + 56 * i);
            if (ReadString(source, at) != actorName || ReadString(source, at + 8) != "") continue;
            if (actorIndex >= 0) throw new InvalidDataException("Event actor is ambiguous.");
            actorIndex = i;
        }
        if (actorIndex < 0) throw new InvalidDataException("Required event actor is absent.");
        int actor = checked(actorArray + actorIndex * 56);
        int queryCount = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(actor + 50, 2));
        int oldQueries = At(U64(source, actor + 32));
        for (int i = 0; i < queryCount; i++)
            if (ReadString(source, checked(oldQueries + i * 8)) == queryName)
                return ((byte[])source.Clone(), actorIndex, i);
        if (queryCount == ushort.MaxValue) throw new InvalidDataException("Too many event queries.");
        int totalQueries = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(flow + 20, 2));
        if (totalQueries == ushort.MaxValue) throw new InvalidDataException("Too many flowchart queries.");
        var reservation = Reserve(source, checked((queryCount + 1) * 8));
        byte[] data = reservation.Data;
        if (oldQueries < 0 || (long)oldQueries + queryCount * 8L > reservation.Offset)
            throw new InvalidDataException("Event query array is outside the original data section.");
        data.AsSpan(oldQueries, queryCount * 8).CopyTo(data.AsSpan(reservation.Offset));
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(actor + 32, 8), (ulong)reservation.Offset);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(actor + 50, 2), (ushort)(queryCount + 1));
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(flow + 20, 2), (ushort)(totalQueries + 1));
        var slots = Enumerable.Range(0, queryCount + 1).Select(i => reservation.Offset + i * 8).Append(actor + 32).ToArray();
        data = RegisterPointers(data, slots);
        data = HAL_EventFlowStrings.AppendAndPoint(data, reservation.Offset + queryCount * 8, queryName);
        return (data, actorIndex, queryCount);
    }

    /// <summary>Add a named entry while retaining the existing entry order and subflow references.</summary>
    public static byte[] AddEntry(byte[] source, string name, int firstEvent, IReadOnlyList<int> subflows, ushort pad)
    {
        var flowView = new HAL_EventFlow(source, FlowName(source));
        var oldNames = flowView.EntryPoints().Keys.ToArray();
        if (oldNames.Contains(name, StringComparer.Ordinal)) throw new InvalidDataException("Event entry already exists.");
        if (oldNames.Length >= ushort.MaxValue || firstEvent < 0 || firstEvent >= flowView.Count ||
            subflows.Count > ushort.MaxValue || subflows.Any(x => x < 0 || x >= flowView.Count))
            throw new InvalidDataException("Invalid event entry or subflow index.");
        var names = oldNames.Append(name).ToArray();
        var tree = HAL_EventFlowDictionary.Build(names);
        int entriesBytes = checked(names.Length * 32), dictionaryBytes = checked(8 + tree.Count * 16);
        int subBytes = checked((subflows.Count * 2 + 7) & ~7);
        var reservation = Reserve(source, checked(entriesBytes + dictionaryBytes + subBytes));
        byte[] data = reservation.Data;
        int At(ulong n) => n <= int.MaxValue ? (int)n : throw new InvalidDataException("Invalid event pointer.");
        ulong U64(int at) => at >= 0 && (long)at + 8 <= data.Length
            ? BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(at, 8)) : throw new InvalidDataException("Truncated event resource.");
        int flow = At(U64(At(U64(40)))), oldDictionary = At(U64(flow + 56)), oldEntries = At(U64(flow + 64));
        if ((reservation.Offset & 7) != 0 || oldDictionary < 0 || (long)oldDictionary + 24L + oldNames.Length * 16L > reservation.Offset ||
            oldEntries < 0 || (long)oldEntries + oldNames.Length * 32L > reservation.Offset)
            throw new InvalidDataException("Event entry table is outside the original data section.");
        int newEntries = reservation.Offset, dictionary = newEntries + entriesBytes, subs = dictionary + dictionaryBytes;
        data.AsSpan(oldEntries, oldNames.Length * 32).CopyTo(data.AsSpan(newEntries));
        int entry = newEntries + oldNames.Length * 32;
        if (subflows.Count != 0) BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(entry, 8), (ulong)subs);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(entry + 24, 2), (ushort)subflows.Count);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(entry + 28, 2), (ushort)firstEvent);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(entry + 30, 2), pad);
        for (int i = 0; i < subflows.Count; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(subs + i * 2, 2), (ushort)subflows[i]);
        "DIC "u8.CopyTo(data.AsSpan(dictionary));
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(dictionary + 4, 4), (uint)names.Length);
        var slots = new List<int> { flow + 56, flow + 64, entry, entry + 16 };
        for (int i = 0; i < tree.Count; i++)
        {
            int node = dictionary + 8 + i * 16, stringSlot = node + 8;
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(node, 4), tree[i].Bit);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(node + 4, 2), tree[i].Left);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(node + 6, 2), tree[i].Right);
            if (i <= oldNames.Length) BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(stringSlot, 8), U64(oldDictionary + 16 + i * 16));
            slots.Add(stringSlot);
        }
        int relocation = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(24, 4)));
        int count = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(relocation + 36, 4)));
        for (int i = 0; i < count; i++)
        {
            int at = relocation + 40 + i * 8;
            int first = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at, 4)));
            uint mask = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at + 4, 4));
            for (int bit = 0; bit < 32; bit++)
            {
                if ((mask & (1u << bit)) == 0) continue;
                int slot = checked(first + bit * 8);
                if (slot >= oldEntries && (long)slot + 8 <= (long)oldEntries + oldNames.Length * 32L)
                    slots.Add(checked(newEntries + slot - oldEntries));
            }
        }
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(flow + 24, 2), (ushort)names.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(flow + 56, 8), (ulong)dictionary);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(flow + 64, 8), (ulong)newEntries);
        data = RegisterPointers(data, slots);
        return HAL_EventFlowStrings.AppendAndPoint(data, dictionary + 16 + names.Length * 16, name);
    }

    private static string FlowName(byte[] data)
    {
        if (data.Length < 64) throw new InvalidDataException("Invalid event resource.");
        int At(ulong n) => n <= int.MaxValue ? (int)n : throw new InvalidDataException("Invalid event pointer.");
        ulong U64(int at) => at >= 0 && (long)at + 8 <= data.Length
            ? BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(at, 8)) : throw new InvalidDataException("Truncated event resource.");
        int flow = At(U64(At(U64(40)))), text = At(U64(checked(flow + 32)));
        if (text < 0 || (long)text + 2 > data.Length) throw new InvalidDataException("Invalid event flow name.");
        int length = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(text, 2));
        if ((long)text + 2 + length > data.Length) throw new InvalidDataException("Truncated event flow name.");
        return new System.Text.UTF8Encoding(false, true).GetString(data.AsSpan(text + 2, length));
    }
}
