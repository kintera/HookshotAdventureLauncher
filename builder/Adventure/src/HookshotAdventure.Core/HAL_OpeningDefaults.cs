using System.Buffers.Binary;

namespace HookshotAdventure.Core;

/// <summary>Set verified new-save opening defaults without changing existing saves.</summary>
public static class HAL_OpeningDefaults
{
    public static byte[] Patch(byte[] data)
    {
        var byml = new HAL_Byml(data);
        int Node(HAL_Byml.Field f) => checked((int)byml.Child(f, 0xc1));
        var tables = byml.Fields(Node(byml.Fields(byml.Root)["Data"]));
        string[] events = ["Dm_Kokiri_0045", "Dm_Kokiri_0050", "Dm_Kokiri_0060"];
        var members = events.ToDictionary(HAL_GameDataHash.Name, x => x);
        var references = new Dictionary<string, uint>(); var location = new List<uint>();
        foreach (var field in byml.Array(tables["Struct"]))
        {
            var fields = byml.Fields(Node(field)); uint hash = byml.Child(fields["Hash"], 0xd3);
            if (hash != HAL_GameDataHash.Name("IsVisitLocation") && hash != HAL_GameDataHash.Name("HavePlayedEvent")) continue;
            foreach (var member in byml.Array(fields["DefaultValue"]))
            {
                var entry = byml.Fields(Node(member)); uint key = byml.Child(entry["Hash"], 0xd3);
                if (hash == HAL_GameDataHash.Name("IsVisitLocation") && key == HAL_GameDataHash.Name("KokiriForest"))
                    location.Add(byml.Child(entry["Value"], 0xd3));
                if (hash == HAL_GameDataHash.Name("HavePlayedEvent") && members.TryGetValue(key, out var name) &&
                    !references.TryAdd(name, byml.Child(entry["Value"], 0xd3)))
                    throw new InvalidDataException("Duplicate opening event member.");
            }
        }
        if (references.Count != events.Length || events.Any(e => !references.TryGetValue(e, out var value) || value != HAL_GameDataHash.Name("HavePlayedEvent." + e)) ||
            location.Count != 1 || location[0] != 0x2e315eb6)
            throw new InvalidDataException("Unverified opening flag mapping.");
        var expected = events.Select(e => HAL_GameDataHash.Name("HavePlayedEvent." + e)).ToHashSet();
        expected.UnionWith([0x9b1b27d2u, 0x20fdd9e1u, 0x2e315eb6u]);
        var slots = new Dictionary<uint, int>();
        foreach (var field in byml.Array(tables["Bool"]))
        {
            var fields = byml.Fields(Node(field)); uint hash = byml.Child(fields["Hash"], 0xd3);
            if (!expected.Contains(hash)) continue;
            if (!slots.TryAdd(hash, fields["DefaultValue"].Slot) || byml.Child(fields["DefaultValue"], 0xd0) != 0 ||
                byml.Child(fields["SaveFileIndex"], 0xd1) != 0 || byml.Child(fields["ResetTypeValue"], 0xd1) != 8)
                throw new InvalidDataException("Unexpected opening default or persistence policy.");
        }
        if (slots.Count != expected.Count) throw new InvalidDataException("Missing opening defaults.");
        var result = (byte[])data.Clone();
        foreach (int slot in slots.Values)
            if (byml.LittleEndian) BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(slot), 1);
            else BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(slot), 1);
        return result;
    }
}
