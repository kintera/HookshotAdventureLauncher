using System.Buffers.Binary;

namespace HookshotAdventure.Core;

/// <summary>Port of the opening camera handoff edit; validates the authored flow before changing it.</summary>
public static class HAL_OpeningCamera
{
    /// <summary>Legacy standalone-join experiment retained for research; not used for gameplay.</summary>
    public static byte[] PatchIntroExperiment(byte[] data)
    {
        var flow = new HAL_EventFlow(data, "OpeningEvent"); int? match = null;
        for (int i = 0; i < flow.Count; i++)
        {
            int at = flow.EventOffset(i);
            if (flow.UInt(at + 8, 1) != 0 || flow.Action(i) != ("EventSystemActor", "EventTransition")) continue;
            if (!flow.StringParameters(i).TryGetValue("EventName", out var target) || target != "Dm_Kokiri_0000") continue;
            int next = (int)flow.UInt(at + 10, 2);
            if (match is not null || next >= flow.Count || next == i) throw new InvalidDataException("Invalid opening cinematic transition.");
            match = at;
        }
        if (match is null) throw new InvalidDataException("Expected the original opening cinematic transition.");
        var result = (byte[])data.Clone(); result[match.Value + 8] = 3; result.AsSpan(match.Value + 12, 36).Clear(); return result;
    }
    public static byte[] Patch(byte[] data)
    {
        var flow = new HAL_EventFlow(data, "OpeningEvent");
        if (flow.Count <= 28) throw new InvalidDataException("Opening handoff events are missing.");
        bool Match(int index, string actor, string action, int? next = null) =>
            flow.Action(index) == (actor, action) &&
            (next is null || flow.UInt(flow.EventOffset(index) + 10, 2) == (ulong)next.Value);
        if (!Match(2, "Npc_Navi", "EventTriggerSetHaveTalked", 3) ||
            !Match(3, "EventCamera", "EventTriggerSendKeepStateMessageToPlayerCamera", 4) ||
            !Match(4, "GameSystemActor", "EventTriggerRequestAutoSave") ||
            !Match(27, "TerrainSystemActor", "EventTerrainPinResource", 28) ||
            !Match(28, "EventCamera", "EventMoveToTargetPosCamera", 23) ||
            !Match(23, "EventSystemActor", "EventWait"))
            throw new InvalidDataException("Opening camera handoff differs from the supported flow.");
        var result = (byte[])data.Clone();
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(flow.EventOffset(2) + 10), 4);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(flow.EventOffset(27) + 10), 23);
        return result;
    }
}
