using System.Text;
using System.Text.Json;

namespace HookshotAdventure.Core;

/// <summary>Use native conditional chest logic for randomized ammunition rewards.</summary>
public static class HAL_NativeAmmoChest
{
    public static IReadOnlyDictionary<string, IReadOnlyList<HAL_AmmoChestRoute>> Routes(JsonElement plan)
    {
        var byResource = new SortedDictionary<string, List<HAL_AmmoChestRoute>>(StringComparer.Ordinal);
        if (!plan.TryGetProperty("assignments", out var assignments)) return new Dictionary<string, IReadOnlyList<HAL_AmmoChestRoute>>();
        foreach (var assignment in assignments.EnumerateArray())
        {
            string reward = assignment.GetProperty("reward").GetString() ?? throw new InvalidDataException("Missing chest reward.");
            string actor = assignment.GetProperty("actor").GetString() ?? throw new InvalidDataException("Missing chest actor.");
            if (HAL_AmmoGuardFlow.RequiredBags(reward) is null || actor is not ("Obj_TBox_A_01" or "Obj_TBox_A_01_NoBake" or "Obj_TBox_A_02" or "Obj_TBox_A_02_NoBake")) continue;
            var route = new HAL_AmmoChestRoute(assignment.GetProperty("placement").GetUInt64(), actor, reward);
            var resources = assignment.GetProperty("resources").EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
            if (resources.Length == 0 || resources.Any(string.IsNullOrEmpty) || resources.Distinct(StringComparer.Ordinal).Count() != resources.Length)
                throw new InvalidDataException("Invalid ammunition chest resources.");
            foreach (string resource in resources)
            {
                if (!byResource.TryGetValue(resource, out var routes)) byResource.Add(resource, routes = new());
                if (routes.Any(x => x.Placement == route.Placement)) throw new InvalidDataException("Duplicate ammunition chest placement.");
                routes.Add(route);
            }
        }
        return byResource.ToDictionary(x => x.Key, x => (IReadOnlyList<HAL_AmmoChestRoute>)x.Value, StringComparer.Ordinal);
    }

    public static byte[] Patch(byte[] source, HAL_AmmoChestRoute route)
    {
        if (route.Actor is not ("Obj_TBox_A_01" or "Obj_TBox_A_01_NoBake" or "Obj_TBox_A_02" or "Obj_TBox_A_02_NoBake"))
            throw new InvalidDataException("Unsupported native conditional chest actor.");
        string check = route.Reward switch
        {
            "TBox_ArrowBundle_Medium" or "TBox_ArrowBundle_Large" or "Item_Arrow_Normal" => "Item_Quiver_Small",
            "Item_Bomb" or "Item_BombBundle_Small" => "Item_BombBag_Small",
            "TBox_DekuSeedBundle" or "Item_DekuSeed" => "Item_BulletBag_Small",
            _ => throw new InvalidDataException("Unsupported ammunition reward.")
        };
        string replacementActor = route.Actor.StartsWith("Obj_TBox_A_02", StringComparison.Ordinal) ? HAL_ConditionalLargeChest.Alias(route.Actor) :
            route.Actor.EndsWith("_NoBake", StringComparison.Ordinal) ? "Obj_TBox_A_06_NoBake" : "Obj_TBox_A_06";
        byte[] data = HAL_BymlKeys.Ensure(HAL_BymlKeys.Ensure(source, "Drop__CheckActor"), "Drop__DropActor_Substitute");
        var b = new HAL_Byml(data); var root = b.Fields(b.Root);
        if (!root.TryGetValue("Actors", out var actors)) throw new InvalidDataException("Chest map has no actors.");
        var matches = b.Array(actors).Select(x => b.Fields((int)b.Child(x, 0xc1)))
            .Where(x => x.TryGetValue("Hash", out var hash) && b.UInt((int)b.Child(hash, 0xd5), 8) == route.Placement).ToArray();
        if (matches.Length != 1 || b.Text(matches[0]["Gyaml"]) != route.Actor || !matches[0].TryGetValue("Dynamic", out var dynamic))
            throw new InvalidDataException("Conditional chest does not match its source placement.");
        int node = (int)b.Child(dynamic, 0xc1); var fields = b.Fields(node);
        if (!fields.TryGetValue("Drop__DropActor", out var drop) || b.Text(drop) != route.Reward ||
            fields.ContainsKey("Drop__CheckActor") || fields.ContainsKey("Drop__DropActor_Substitute"))
            throw new InvalidDataException("Conditional chest has a changed reward or existing substitution.");
        var output = new List<byte>(data);
        void Align() { while ((output.Count & 3) != 0) output.Add(0); }
        void Put(int at, uint value, int size)
        {
            for (int i = 0; i < size; i++) output[at + (b.LittleEndian ? i : size - 1 - i)] = (byte)(value >> (8 * i));
        }
        void Write(uint value, int size) { int at = output.Count; output.AddRange(new byte[size]); Put(at, value, size); }
        var missing = new[] { replacementActor, check, HAL_AmmoGuardFlow.Fallback, route.Reward }
            .Where(x => !b.Values.Contains(x)).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        if (missing.Length != 0)
        {
            var values = b.Values.Concat(missing).ToArray(); Align(); int table = output.Count;
            output.Add(0xc2); Write((uint)values.Length, 3); var encoded = values.Select(Encoding.UTF8.GetBytes).ToArray();
            int at = checked(4 + (values.Length + 1) * 4);
            foreach (var text in encoded) { Write((uint)at, 4); at = checked(at + text.Length + 1); }
            Write((uint)at, 4); foreach (var text in encoded) { output.AddRange(text); output.Add(0); }
            Put(8, (uint)table, 4); b = new HAL_Byml(output.ToArray());
        }
        int Value(string text) { int i = b.Values.ToList().IndexOf(text); return i >= 0 ? i : throw new InvalidDataException("Missing chest value."); }
        int Key(string text) { int i = b.Keys.ToList().IndexOf(text); return i >= 0 ? i : throw new InvalidDataException("Missing chest key."); }
        Put(matches[0]["Gyaml"].Slot, (uint)Value(replacementActor), 4);
        var entries = new List<(int Key, byte[] Bytes)>();
        for (int i = 0; i < b.Header(node, 0xc1); i++)
        {
            int at = node + 4 + i * 8, key = (int)b.UInt(at, 3); byte[] item = b.Bytes(at, 8);
            if (at + 4 == drop.Slot)
                for (int j = 0; j < 4; j++) item[4 + (b.LittleEndian ? j : 3 - j)] = (byte)((uint)Value(HAL_AmmoGuardFlow.Fallback) >> (8 * j));
            entries.Add((key, item));
        }
        foreach (var (keyName, valueName) in new[] { ("Drop__CheckActor", check), ("Drop__DropActor_Substitute", route.Reward) })
        {
            int key = Key(keyName), value = Value(valueName); byte[] item = new byte[8];
            for (int i = 0; i < 3; i++) item[b.LittleEndian ? i : 2 - i] = (byte)((uint)key >> (8 * i));
            item[3] = 0xa0;
            for (int i = 0; i < 4; i++) item[4 + (b.LittleEndian ? i : 3 - i)] = (byte)((uint)value >> (8 * i));
            entries.Add((key, item));
        }
        Align(); int privateNode = output.Count; output.Add(0xc1); Write((uint)entries.Count, 3);
        foreach (var item in entries.OrderBy(x => x.Key)) output.AddRange(item.Bytes);
        Put(dynamic.Slot, (uint)privateNode, 4);
        return output.ToArray();
    }
}
