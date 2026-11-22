namespace HookshotAdventure.Core;

public sealed record HAL_ChestPlacement(ulong Placement, string Actor, string? Reward,
    string? Event, IReadOnlyList<float>? Position, uint? DynamicOffset, bool SharedParameters)
{
    public string LocationId => Placement.ToString("X16");
}

/// <summary>Reads chest placements without modifying the source BYML.</summary>
public static class HAL_ChestScanner
{
    public static IReadOnlyList<HAL_ChestPlacement> Scan(byte[] data)
    {
        var byml = new HAL_Byml(data);
        var root = byml.Fields(byml.Root);
        if (!root.TryGetValue("Actors", out var actors)) return Array.Empty<HAL_ChestPlacement>();
        var found = new List<HAL_ChestPlacement>();
        foreach (var entry in byml.Array(actors))
        {
            var actor = byml.Fields(checked((int)byml.Child(entry, 0xc1)));
            if (!actor.TryGetValue("Gyaml", out var gyaml)) continue;
            string name = byml.Text(gyaml);
            if (!name.StartsWith("Obj_TBox_", StringComparison.Ordinal)) continue;
            uint? offset = actor.TryGetValue("Dynamic", out var dynamicField) ? byml.Child(dynamicField, 0xc1) : null;
            var parameters = offset.HasValue ? byml.Fields(checked((int)offset.Value)) : null;
            string? reward = parameters is not null && parameters.TryGetValue("Drop__DropActor", out var item) ? byml.Text(item) : null;
            string? gameEvent = parameters is not null && parameters.TryGetValue("GetEventName", out var action) ? byml.Text(action) : null;
            float[]? position = null;
            if (actor.TryGetValue("Translate", out var translate))
            {
                var components = byml.Array(translate);
                position = new float[components.Count];
                for (int i = 0; i < components.Count; i++)
                {
                    if (components[i].Type != 0xd2) throw new InvalidDataException("Unexpected position type.");
                    position[i] = BitConverter.Int32BitsToSingle(unchecked((int)byml.UInt(components[i].Slot)));
                }
            }
            ulong identity = byml.UInt(checked((int)byml.Child(actor["Hash"], 0xd5)), 8);
            found.Add(new HAL_ChestPlacement(identity, name, reward, gameEvent, position, offset, false));
        }
        var counts = found.Where(row => row.DynamicOffset.HasValue).GroupBy(row => row.DynamicOffset!.Value)
            .ToDictionary(group => group.Key, group => group.Count());
        return found.Select(row => row with { SharedParameters = row.DynamicOffset is uint at && counts[at] > 1 }).ToArray();
    }
}
