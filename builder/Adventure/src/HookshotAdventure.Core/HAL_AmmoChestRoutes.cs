using System.Text;

namespace HookshotAdventure.Core;

public sealed record HAL_AmmoChestRoute(ulong Placement, string Actor, string Reward);

/// <summary>Give ordinary ammunition chests an event route without changing their planned reward.</summary>
public static class HAL_AmmoChestRoutes
{
    public static string EventName(string actor, string reward)
    {
        if (!actor.StartsWith("Obj_TBox_", StringComparison.Ordinal) || HAL_AmmoGuardFlow.RequiredBags(reward) is null)
            throw new InvalidDataException("Unsupported ammunition chest.");
        bool large = actor.StartsWith("Obj_TBox_A_02", StringComparison.Ordinal) || actor.StartsWith("Obj_TBox_A_03", StringComparison.Ordinal);
        return "HAL_AmmoGuard_" + reward + (large ? "_Big" : "_Small");
    }

    public static byte[] Patch(byte[] source, IReadOnlyList<HAL_AmmoChestRoute> routes)
    {
        if (routes.Count == 0) return (byte[])source.Clone();
        var data = HAL_BymlKeys.Ensure(source, "GetEventName");
        var byml = new HAL_Byml(data);
        var root = byml.Fields(byml.Root);
        if (!root.TryGetValue("Actors", out var actors)) throw new InvalidDataException("Chest map has no actors.");
        var indexed = new Dictionary<ulong, List<IReadOnlyDictionary<string, HAL_Byml.Field>>>();
        foreach (var item in byml.Array(actors))
        {
            var actor = byml.Fields(checked((int)byml.Child(item, 0xc1)));
            if (!actor.TryGetValue("Hash", out var hash)) continue;
            ulong id = byml.UInt(checked((int)byml.Child(hash, 0xd5)), 8);
            if (!indexed.TryGetValue(id, out var list)) indexed[id] = list = new();
            list.Add(actor);
        }
        var edits = new List<(int Pointer, int Node, string Event)>(); var seen = new HashSet<ulong>();
        foreach (var route in routes)
        {
            string gameEvent = EventName(route.Actor, route.Reward);
            if (!seen.Add(route.Placement) || !indexed.TryGetValue(route.Placement, out var matches) || matches.Count != 1)
                throw new InvalidDataException("Missing or duplicate ammo chest placement.");
            var actor = matches[0];
            if (byml.Text(actor["Gyaml"]) != route.Actor || !actor.TryGetValue("Dynamic", out var dynamicField))
                throw new InvalidDataException("Ammo chest actor differs from the plan.");
            int node = checked((int)byml.Child(dynamicField, 0xc1)); var fields = byml.Fields(node);
            if (!fields.TryGetValue("Drop__DropActor", out var reward) || byml.Text(reward) != route.Reward || fields.ContainsKey("GetEventName"))
                throw new InvalidDataException("Ammo chest reward changed or already uses a special event.");
            edits.Add((dynamicField.Slot, node, gameEvent));
        }
        var output = new List<byte>(data);
        void Align() { while ((output.Count & 3) != 0) output.Add(0); }
        void Put(int at, uint value, int size)
        {
            for (int i = 0; i < size; i++) output[at + (byml.LittleEndian ? i : size - 1 - i)] = (byte)(value >> (8 * i));
        }
        void Write(uint value, int size) { int at = output.Count; output.AddRange(new byte[size]); Put(at, value, size); }
        var missing = edits.Select(x => x.Event).Where(x => !byml.Values.Contains(x)).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        if (missing.Length != 0)
        {
            var values = byml.Values.Concat(missing).ToArray(); Align(); int table = output.Count;
            output.Add(0xc2); Write((uint)values.Length, 3); var encoded = values.Select(Encoding.UTF8.GetBytes).ToArray();
            int offset = checked(4 + (values.Length + 1) * 4);
            foreach (var text in encoded) { Write((uint)offset, 4); offset = checked(offset + text.Length + 1); }
            Write((uint)offset, 4); foreach (var text in encoded) { output.AddRange(text); output.Add(0); }
            Put(8, (uint)table, 4); byml = new HAL_Byml(output.ToArray());
        }
        int eventKey = byml.Keys.ToList().IndexOf("GetEventName");
        foreach (var edit in edits)
        {
            var entries = new List<(int Key, byte[] Bytes)>();
            for (int i = 0; i < byml.Header(edit.Node, 0xc1); i++)
            {
                int at = edit.Node + 4 + i * 8; entries.Add(((int)byml.UInt(at, 3), byml.Bytes(at, 8)));
            }
            int value = byml.Values.ToList().IndexOf(edit.Event);
            if (eventKey < 0 || value < 0) throw new InvalidDataException("Missing chest event key or value.");
            byte[] newEntry = new byte[8];
            for (int i = 0; i < 3; i++) newEntry[byml.LittleEndian ? i : 2 - i] = (byte)((uint)eventKey >> (8 * i));
            newEntry[3] = 0xa0;
            for (int i = 0; i < 4; i++) newEntry[4 + (byml.LittleEndian ? i : 3 - i)] = (byte)((uint)value >> (8 * i));
            entries.Add((eventKey, newEntry));
            Align(); int newNode = output.Count; output.Add(0xc1); Write((uint)entries.Count, 3);
            foreach (var entry in entries.OrderBy(x => x.Key)) output.AddRange(entry.Bytes);
            Put(edit.Pointer, (uint)newNode, 4);
        }
        return output.ToArray();
    }
}
