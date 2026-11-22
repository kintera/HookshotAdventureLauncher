using System.Buffers.Binary;
using System.Text;

namespace HookshotAdventure.Core;

/// <summary>Bounded reader for the single-flowchart BFEVFL resources used by overlays.</summary>
public sealed class HAL_EventFlow
{
    private readonly byte[] data;
    private readonly int flow, actors, events, actorCount;
    public int Count { get; }
    public readonly record struct Entry(int Slot, int EventIndex);

    public HAL_EventFlow(byte[] source, string expectedName)
    {
        data = (byte[])source.Clone();
        if (!Span(0, 8).SequenceEqual("BFEVFL\0\0"u8) || UInt(8, 2) != 0x400 || UInt(12, 2) != 0xfeff ||
            UInt(32, 2) != 1 || UInt(34, 2) != 0)
            throw new InvalidDataException("Unsupported event flow format.");
        flow = Offset(UInt(Offset(UInt(40, 8)), 8));
        if (!Span(flow, 4).SequenceEqual("EVFL"u8) || String(flow + 32) != expectedName)
            throw new InvalidDataException("Unexpected event flowchart.");
        actorCount = (int)UInt(flow + 16, 2); Count = (int)UInt(flow + 22, 2);
        actors = Offset(UInt(flow + 40, 8)); events = Offset(UInt(flow + 48, 8));
        Span(actors, checked(actorCount * 56)); Span(events, checked(Count * 48));
    }
    private ReadOnlySpan<byte> Span(int offset, int size)
    {
        if (offset < 0 || size < 0 || (long)offset + size > data.Length) throw new InvalidDataException("Event flow offset outside resource.");
        return data.AsSpan(offset, size);
    }
    private static int Offset(ulong value) => value <= int.MaxValue ? (int)value : throw new InvalidDataException("Event flow offset outside resource.");
    public ulong UInt(int offset, int size)
    {
        var bytes = Span(offset, size);
        return size switch { 1 => bytes[0], 2 => BinaryPrimitives.ReadUInt16LittleEndian(bytes),
            4 => BinaryPrimitives.ReadUInt32LittleEndian(bytes), 8 => BinaryPrimitives.ReadUInt64LittleEndian(bytes),
            _ => throw new ArgumentOutOfRangeException(nameof(size)) };
    }
    private string String(int slot)
    {
        int pointer = Offset(UInt(slot, 8));
        if (pointer == 0) return "";
        int size = (int)UInt(pointer, 2);
        return new UTF8Encoding(false, true).GetString(Span(checked(pointer + 2), size));
    }
    public int EventOffset(int index)
    {
        if (index < 0 || index >= Count) throw new InvalidDataException("Invalid event index.");
        return events + index * 48;
    }
    public IReadOnlyDictionary<string, Entry> EntryPoints()
    {
        int count = (int)UInt(flow + 24, 2), dictionary = Offset(UInt(flow + 56, 8)), entries = Offset(UInt(flow + 64, 8));
        if (!Span(dictionary, 4).SequenceEqual("DIC "u8) || UInt(dictionary + 4, 4) != (ulong)count)
            throw new InvalidDataException("Invalid event entry dictionary.");
        Span(entries, checked(count * 32)); var result = new Dictionary<string, Entry>(StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
        {
            string name = String(checked(dictionary + 32 + i * 16)); int slot = entries + i * 32 + 28;
            int index = (int)UInt(slot, 2);
            if (index >= Count || !result.TryAdd(name, new Entry(slot, index))) throw new InvalidDataException("Invalid event entry.");
        }
        return result;
    }
    public (string Actor, string Action) Action(int index)
    {
        int at = EventOffset(index), actorIndex = (int)UInt(at + 12, 2), actionIndex = (int)UInt(at + 14, 2);
        if (actorIndex >= actorCount) throw new InvalidDataException("Invalid event actor index.");
        int actor = actors + actorIndex * 56;
        if (actionIndex >= (int)UInt(actor + 48, 2)) throw new InvalidDataException("Invalid event action index.");
        return (String(actor), String(checked(Offset(UInt(actor + 24, 8)) + actionIndex * 8)));
    }
    public (string Actor, string Query) Query(int index)
    {
        int at = EventOffset(index);
        if (UInt(at + 8, 1) != 1) throw new InvalidDataException("Event is not a switch.");
        int actorIndex = (int)UInt(at + 12, 2), queryIndex = (int)UInt(at + 14, 2);
        if (actorIndex >= actorCount) throw new InvalidDataException("Invalid event actor index.");
        int actor = actors + actorIndex * 56;
        if (queryIndex >= (int)UInt(actor + 50, 2)) throw new InvalidDataException("Invalid event query index.");
        return (String(actor), String(checked(Offset(UInt(actor + 32, 8)) + queryIndex * 8)));
    }
    public (string Resource, string Entry) Subflow(int index)
    {
        int at = EventOffset(index);
        if (UInt(at + 8, 1) != 4) throw new InvalidDataException("Event is not a subflow.");
        return (String(at + 24), String(at + 32));
    }
    public IReadOnlyDictionary<uint, int> SwitchRoutes(int index)
    {
        int at = EventOffset(index);
        if (UInt(at + 8, 1) != 1) throw new InvalidDataException("Event is not a switch.");
        int count = (int)UInt(at + 10, 2), routes = Offset(UInt(at + 24, 8));
        Span(routes, checked(count * 8));
        var result = new Dictionary<uint, int>();
        for (int i = 0; i < count; i++)
        {
            int route = routes + i * 8, target = (int)UInt(route + 4, 2);
            if (target >= Count || !result.TryAdd((uint)UInt(route, 4), target))
                throw new InvalidDataException("Invalid event switch route.");
        }
        return result;
    }
    /// <summary>Returns the branches and join of a fork without following any out-of-range indices.</summary>
    public (IReadOnlyList<int> Branches, int Join) ForkRoutes(int index)
    {
        int at = EventOffset(index);
        if (UInt(at + 8, 1) != 2) throw new InvalidDataException("Event is not a fork.");
        int count = (int)UInt(at + 10, 2), join = (int)UInt(at + 12, 2), offset = Offset(UInt(at + 16, 8));
        if (count == 0 || offset == 0 || join >= Count) throw new InvalidDataException("Invalid event fork.");
        Span(offset, checked(count * 2));
        var branches = new int[count];
        for (int i = 0; i < count; i++)
        {
            branches[i] = (int)UInt(offset + i * 2, 2);
            if (branches[i] >= Count) throw new InvalidDataException("Invalid event fork branch.");
        }
        return (branches, join);
    }
    public IReadOnlyDictionary<string, string> StringParameters(int index)
    {
        int at = Offset(UInt(EventOffset(index) + 16, 8));
        if (at == 0) return new Dictionary<string, string>(StringComparer.Ordinal);
        if (UInt(at, 1) != 1) throw new InvalidDataException("Invalid event parameter container.");
        int count = (int)UInt(at + 2, 2), dictionary = Offset(UInt(at + 8, 8));
        if (!Span(dictionary, 4).SequenceEqual("DIC "u8) || UInt(dictionary + 4, 4) != (ulong)count)
            throw new InvalidDataException("Invalid event parameter dictionary.");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
        {
            string name = String(checked(dictionary + 32 + i * 16));
            int item = Offset(UInt(checked(at + 16 + i * 8), 8));
            if (UInt(item, 1) == 5) result[name] = String(item + 16);
        }
        return result;
    }
    /// <summary>Offset of a string pointer in one event parameter, for bounded in-place pointer edits.</summary>
    public int StringParameterPointerSlot(int index, string name)
    {
        int at = Offset(UInt(EventOffset(index) + 16, 8));
        if (at == 0 || UInt(at, 1) != 1) throw new InvalidDataException("Invalid event parameter container.");
        int count = (int)UInt(at + 2, 2), dictionary = Offset(UInt(at + 8, 8));
        if (!Span(dictionary, 4).SequenceEqual("DIC "u8) || UInt(dictionary + 4, 4) != (ulong)count)
            throw new InvalidDataException("Invalid event parameter dictionary.");
        int result = -1;
        for (int i = 0; i < count; i++)
        {
            if (String(checked(dictionary + 32 + i * 16)) != name) continue;
            int item = Offset(UInt(checked(at + 16 + i * 8), 8));
            if (result >= 0 || UInt(item, 1) != 5) throw new InvalidDataException("Invalid or duplicate string parameter.");
            result = checked(item + 16);
            _ = String(result);
        }
        return result >= 0 ? result : throw new InvalidDataException("Missing event string parameter.");
    }
    public (byte Type, uint Value)? ScalarParameter(int index, string name)
    {
        int at = Offset(UInt(EventOffset(index) + 16, 8));
        if (UInt(at, 1) != 1) throw new InvalidDataException("Invalid event parameter container.");
        int count = (int)UInt(at + 2, 2), dictionary = Offset(UInt(at + 8, 8));
        if (!Span(dictionary, 4).SequenceEqual("DIC "u8) || UInt(dictionary + 4, 4) != (ulong)count)
            throw new InvalidDataException("Invalid event parameter dictionary.");
        (byte, uint)? result = null;
        for (int i = 0; i < count; i++)
        {
            if (String(checked(dictionary + 32 + i * 16)) != name) continue;
            if (result is not null) throw new InvalidDataException("Duplicate event parameter.");
            int item = Offset(UInt(checked(at + 16 + i * 8), 8));
            result = ((byte)UInt(item, 1), (uint)UInt(item + 16, 4));
        }
        return result;
    }
    public int ScalarParameterValueSlot(int index, string name)
    {
        int at = Offset(UInt(EventOffset(index) + 16, 8));
        if (at == 0 || UInt(at, 1) != 1) throw new InvalidDataException("Invalid event parameter container.");
        int count = (int)UInt(at + 2, 2), dictionary = Offset(UInt(at + 8, 8));
        if (!Span(dictionary, 4).SequenceEqual("DIC "u8) || UInt(dictionary + 4, 4) != (ulong)count)
            throw new InvalidDataException("Invalid event parameter dictionary.");
        int slot = -1;
        for (int i = 0; i < count; i++)
        {
            if (String(checked(dictionary + 32 + i * 16)) != name) continue;
            int item = Offset(UInt(checked(at + 16 + i * 8), 8));
            if (slot >= 0 || UInt(item, 1) is not (2 or 3 or 4)) throw new InvalidDataException("Invalid or duplicate scalar parameter.");
            slot = checked(item + 16);
            _ = UInt(slot, 4);
        }
        return slot >= 0 ? slot : throw new InvalidDataException("Missing event scalar parameter.");
    }
    public IReadOnlyList<string> ParameterNames(int index)
    {
        int at = Offset(UInt(EventOffset(index) + 16, 8));
        if (at == 0) return Array.Empty<string>();
        if (UInt(at, 1) != 1) throw new InvalidDataException("Invalid event parameter container.");
        int count = (int)UInt(at + 2, 2), dictionary = Offset(UInt(at + 8, 8));
        if (!Span(dictionary, 4).SequenceEqual("DIC "u8) || UInt(dictionary + 4, 4) != (ulong)count)
            throw new InvalidDataException("Invalid event parameter dictionary.");
        return Enumerable.Range(0, count).Select(i => String(checked(dictionary + 32 + i * 16))).ToArray();
    }
}
