using System.Buffers.Binary;

namespace HookshotAdventure.Core;

/// <summary>Validated edits for first-entrance presentation flows.</summary>
public static class HAL_FieldSkips
{
    private static HAL_EventFlow Flow(byte[] data, string name, string entry, int start)
    {
        var flow = new HAL_EventFlow(data, name);
        if (!flow.EntryPoints().TryGetValue(entry, out var found) || found.EventIndex != start)
            throw new InvalidDataException("Unexpected entrance event entry.");
        return flow;
    }
    private static void Expect(HAL_EventFlow f, int index, string actor, string action, int next,
        (string, string)[] parameters, int type = 0)
    {
        int at = f.EventOffset(index); var actual = f.StringParameters(index);
        if (f.UInt(at + 8, 1) != (ulong)type || f.UInt(at + 10, 2) != (ulong)next ||
            f.Action(index) != (actor, action) || actual.Count != parameters.Length ||
            parameters.Any(p => !actual.TryGetValue(p.Item1, out var value) || value != p.Item2))
            throw new InvalidDataException("Entrance flow differs from the supported version.");
    }
    private static byte[] Set(byte[] data, int slot, ushort value)
    {
        var result = (byte[])data.Clone(); BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(slot), value); return result;
    }
    private static HAL_EventFlow Field(byte[] data) => Flow(data, "Goto_HyruleCastle", "Goto_HyruleCastle_Kaepora_00", 148);

    public static byte[] PatchFieldEntrance(byte[] data)
    {
        var f = Field(data);
        Expect(f, 132, "GameDataActor", "EventTriggerSetGameDataBool", 90, [("GameDataName", "HyruleField_ArriveEntrance")]);
        Expect(f, 90, "EventSystemActor", "EventTransition", 26, [("EventName", "Dm_HrlField_0000")]);
        Expect(f, 26, "EventSystemActor", "EventTransition", 181, [("EventName", "Dm_HrlField_0005")]);
        Expect(f, 181, "GameDataActor", "EventTriggerSetGameDataBool", 183, [("GameDataName", "HyruleField_ChangeENV")]);
        return Set(data, f.EventOffset(132) + 10, 181);
    }
    public static byte[] PatchFirstOwl(byte[] data)
    {
        var f = Field(data);
        Expect(f, 181, "GameDataActor", "EventTriggerSetGameDataBool", 183, [("GameDataName", "HyruleField_ChangeENV")]);
        Expect(f, 183, "EventSystemActor", "EventWait", 184, []);
        var paletteAction = f.Action(184);
        if (paletteAction != ("EventWorldManagerController", "Event_ResetEnvPaletteForFlowchart") &&
            paletteAction != ("EventWorldManagerController", "Event_RebuildConditionEnvTimePalette"))
            throw new InvalidDataException("First owl environment cleanup differs from the supported version.");
        Expect(f, 184, paletteAction.Actor, paletteAction.Action, 0, []);
        Expect(f, 0, "EventSystemActor", "EventTransition", 172, [("EventName", "Dm_HrlField_0010")]);
        var p = f.StringParameters(3);
        if (f.UInt(f.EventOffset(172) + 8, 1) != 2 || f.Action(3) != ("ChallengeSystemActor", "EventExecuteProgressChallengeStep") ||
            p.Count != 2 || !p.TryGetValue("ChallengeName", out var challenge) || challenge != "Goto_HyruleCastle" ||
            !p.TryGetValue("StepName", out var step) || step != "Complete")
            throw new InvalidDataException("First owl completion differs from the supported version.");
        return Set(data, f.EventOffset(184) + 10, 172);
    }
    public static byte[] PatchCastleTown(byte[] data)
    {
        var f = Flow(data, "Dm_SY_BancChangeArrive", "Dm_SY_BancChangeArrive_Market", 87);
        Expect(f, 87, "Player_Event", "EventTriggerParticipateEvent", 86, []);
        Expect(f, 86, "EventSystemActor", "EventTransition", 467, [("EventName", "Dm_HrlCastle_0005")]);
        Expect(f, 467, "PostProcessingActor", "EventSetAdvancedDoFForFlowchart", 142, []);
        Expect(f, 142, "UISystemActor", "EventWaitCloseLoadingFade", 0xffff, [], 4);
        return Set(data, f.EventOffset(86) + 10, 467);
    }
    private static readonly (string, string)[] Ducking = [("DuckingName", "DM:PlayerSE下げ(初回突入デモ)"), ("DuckingName2", ""), ("DuckingName3", "")];
    public static byte[] PatchHyruleCastle(byte[] data)
    {
        var f = Flow(data, "Dm_HrlCastle_0000", "EntryPoint0", 103);
        Expect(f, 103, "SoundSystemActor", "EventSoundRequestDucking", 42, Ducking);
        Expect(f, 2, "UISystemActor", "EventTriggerOpenLocationUI", 86, [("LocationName", "Work/Location/HyruleCastle.game__location__Location.gyml")]);
        Expect(f, 86, "EventSystemActor", "EventWait", 0xffff, []);
        if (f.UInt(f.EventOffset(42) + 8, 1) != 2 || f.Action(9) != ("Npc_Kaepora", "EventTalk"))
            throw new InvalidDataException("Hyrule Castle conversation differs from the supported version.");
        return Set(data, f.EntryPoints()["EntryPoint0"].Slot, 2);
    }
    public static byte[] PatchKakariko(byte[] data)
    {
        var f = Flow(data, "Dm_Kakariko_0000", "EntryPoint0", 17);
        Expect(f, 17, "SoundSystemActor", "EventSoundRequestDucking", 2, Ducking);
        Expect(f, 5, "UISystemActor", "EventTriggerOpenLocationUI", 0xffff, [("LocationName", "Work/Location/KakarikoVillage.game__location__Location.gyml")]);
        return Set(data, f.EntryPoints()["EntryPoint0"].Slot, 5);
    }
}
