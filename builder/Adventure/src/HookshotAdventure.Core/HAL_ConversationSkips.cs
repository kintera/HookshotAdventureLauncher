using System.Buffers.Binary;

namespace HookshotAdventure.Core;

/// <summary>Validated edits to the native Deku Tree event flows.</summary>
public static class HAL_ConversationSkips
{
    private sealed record Hint(int Start, int Branch, int Edge, int Complete, int Next, string Flag, int Message1, string Text1, int Message2, string Text2);
    private static readonly Dictionary<string, Hint> Hints = new(StringComparer.Ordinal)
    {
        ["Scrub_Deku"] = new(223,37,1,40,41,"ScrubDeku",38,"Enemy_Scrub_Deku",39,"Enemy_Scrub_Deku_02"),
        ["Dekubaba_Withered"] = new(202,0,1,3,4,"DekubabaWithered",1,"Enemy_Dekubaba_Withered",2,"Enemy_Dekubaba_Withered_00"),
        ["Dekubaba"] = new(201,5,1,8,9,"Dekubaba",6,"Enemy_Dekubaba",7,"Enemy_Dekubaba_00"),
        ["Skullwalltula"] = new(205,10,1,13,14,"Skullwalltula",11,"Enemy_Skullwalltula",12,"Enemy_Skullwalltula_00"),
        ["Gohma_Egg"] = new(203,214,0,19,15,"GohmaEgg",17,"Enemy_Gohma_Egg",18,"Enemy_Gohma_Egg_00"),
        ["Gohma_Larva"] = new(204,215,0,32,34,"GohmaLarva",31,"Enemy_Gohma_Larva",33,"Enemy_Gohma_Larva_00")
    };
    public static byte[] PatchDekuNaviHints(byte[] data, IReadOnlyList<string> names)
    {
        if (names.Count == 0 || names.Distinct(StringComparer.Ordinal).Count() != names.Count || names.Any(n => !Hints.ContainsKey(n)))
            throw new ArgumentException("Unsupported Navi hint selection.", nameof(names));
        var flow = new HAL_EventFlow(data, "Navi_EnemyHint_Normal_1"); var entries = flow.EntryPoints();
        var changes = new List<(int Slot, int Target)>();
        foreach (string name in names)
        {
            var h = Hints[name];
            if (!entries.TryGetValue(name, out var entry) || entry.EventIndex != h.Start) throw new InvalidDataException("Unexpected Navi hint entry.");
            int start = flow.EventOffset(h.Start), branch = flow.EventOffset(h.Branch);
            if (flow.UInt(start + 8, 1) != 4 || flow.UInt(start + 10, 2) != (ulong)h.Branch ||
                flow.UInt(branch + 8, 1) != 1 || flow.UInt(branch + 10, 2) != 2 || flow.UInt(branch + 12, 2) != (ulong)h.Edge)
                throw new InvalidDataException("Unexpected Navi hint branch.");
            void Routes(int at, params (uint Key, ushort Target)[] expected)
            {
                ulong pointer = flow.UInt(at + 24, 8);
                if (pointer > int.MaxValue) throw new InvalidDataException("Invalid Navi hint routes.");
                for (int i = 0; i < expected.Length; i++)
                    if (flow.UInt(checked((int)pointer + i * 8), 4) != expected[i].Key || flow.UInt(checked((int)pointer + i * 8 + 4), 2) != expected[i].Target)
                        throw new InvalidDataException("Unexpected Navi hint route.");
            }
            if (name == "Skullwalltula") Routes(branch, (0,12));
            if (name == "Gohma_Egg") Routes(branch, (0,210), (1,16));
            if (name == "Gohma_Larva")
            {
                Routes(branch, (0,212), (1,35)); int nextBranch = flow.EventOffset(35);
                if (flow.UInt(nextBranch + 8, 1) != 1 || flow.UInt(nextBranch + 10, 2) != 2 || flow.UInt(nextBranch + 12, 2) != 1)
                    throw new InvalidDataException("Unexpected Navi hint first-time branch.");
                Routes(nextBranch, (0,254), (1,30));
            }
            foreach (var (index, text) in new[] { (h.Message1, h.Text1), (h.Message2, h.Text2) })
            {
                RequireAction(flow, index, "Npc_Navi", "EventTalk");
                if (!flow.StringParameters(index).TryGetValue("MessageId", out var message) || message != "EventFlowMsg/NaviHint_Enemy:" + text)
                    throw new InvalidDataException("Unexpected Navi hint dialogue.");
            }
            RequireAction(flow, h.Complete, "GameDataActor", "EventTriggerSetGameDataBool", h.Next);
            if (!flow.StringParameters(h.Complete).TryGetValue("GameDataName", out var flag) || flag != "NaviHint_Enemy_" + h.Flag + "_FirstTime_HasRead" ||
                flow.ScalarParameter(h.Complete, "Value") != ((byte)3, 0x80000001u))
                throw new InvalidDataException("Unexpected Navi hint completion.");
            changes.Add((entry.Slot, h.Complete));
        }
        var result = (byte[])data.Clone();
        foreach (var (slot, target) in changes) BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(slot), checked((ushort)target));
        return result;
    }
    public static byte[] PatchTutorial(byte[] data, string name)
    {
        var (entryName, action, flag) = name switch
        {
            "Npc_Kokiri_010_Child" => ("DekuShield_TutorialTalk", "EventTriggerSetModelLodManual", "Npc_Kokiri_010_Child_Teach"),
            "Npc_Kokiri_011_Child" => ("KokiriForest_Kokiri_011_StopInFrontOfShop_00", "EventNPCSwitchTemporarilyLookIKVariationStart", "KokiriForest_Kokiri_011_StopInFrontOfShop_00"),
            _ => throw new ArgumentException("Unsupported tutorial flow.", nameof(name))
        };
        var flow = new HAL_EventFlow(data, name); var entries = flow.EntryPoints();
        if (!entries.TryGetValue(entryName, out var entry) || entries.Values.Count(e => e.EventIndex == entry.EventIndex) != 1)
            throw new InvalidDataException("Unexpected tutorial entry.");
        RequireAction(flow, entry.EventIndex, name, action);
        int? match = null;
        for (int i = 0; i < flow.Count; i++)
        {
            int at = flow.EventOffset(i);
            if (flow.UInt(at + 8, 1) != 0 || flow.Action(i) != ("GameDataActor", "EventTriggerSetGameDataBool")) continue;
            if (!flow.StringParameters(i).TryGetValue("GameDataName", out var actual) || actual != flag) continue;
            if (flow.ScalarParameter(i, "Value") != ((byte)3, 0x80000001u) || match is not null)
                throw new InvalidDataException("Unexpected tutorial completion action.");
            match = i;
        }
        if (match is null) throw new InvalidDataException("Missing tutorial completion action.");
        var result = (byte[])data.Clone(); int target = flow.EventOffset(entry.EventIndex);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(target + 10), 0xffff);
        data.AsSpan(flow.EventOffset(match.Value) + 12, 36).CopyTo(result.AsSpan(target + 12));
        return result;
    }
    private static void RequireAction(HAL_EventFlow flow, int index, string actor, string action, int? next = null)
    {
        int at = flow.EventOffset(index);
        if (flow.UInt(at + 8, 1) != 0 || flow.Action(index) != (actor, action) ||
            (next is not null && flow.UInt(at + 10, 2) != (ulong)next.Value))
            throw new InvalidDataException("Event flow differs from the supported version.");
    }

    public static byte[] PatchDekuEntrance(byte[] data)
    {
        var flow = new HAL_EventFlow(data, "Dm_Deku_0000");
        var entries = flow.EntryPoints();
        if (entries.Count != 1 || !entries.TryGetValue("EntryPoint0", out var entry) ||
            flow.UInt(flow.EventOffset(entry.EventIndex) + 8, 1) != 2)
            throw new InvalidDataException("Unexpected Deku entrance entry.");
        RequireAction(flow, 16, "GameSystemActor", "EventTriggerAddPreComputedCullingBasePos", 4);
        RequireAction(flow, 4, "UISystemActor", "EventEventFadeIn", 14);
        RequireAction(flow, 6, "Player_Event", "EventPlayerWarpToTargetPosAndRot", 0xffff);
        RequireAction(flow, 0, "UISystemActor", "EventTriggerOpenLocationUI", 10);
        RequireAction(flow, 5, "EventCamera", "EventTriggerSendReserveCameraResetMessageToPlayerCamera", 17);
        RequireAction(flow, 17, "PostProcessingActor", "EventNorifyCutChangeForGraphics", 0xffff);
        var result = (byte[])data.Clone();
        void Write(int at, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(at), value);
        Write(entry.Slot, 16); Write(flow.EventOffset(4) + 10, 6);
        Write(flow.EventOffset(6) + 10, 0); Write(flow.EventOffset(0) + 10, 5);
        return result;
    }

    public static byte[] PatchDekuMeeting(byte[] data)
    {
        var flow = new HAL_EventFlow(data, "Meet_DekuTree");
        var entries = flow.EntryPoints();
        if (entries.Count != 1 || !entries.TryGetValue("KokiriForest_Meet_DekuTree_MeetDekuTree_01", out var entry) || entry.EventIndex != 5)
            throw new InvalidDataException("Unexpected Deku Tree meeting entry.");
        RequireAction(flow, 5, "Player_Event", "EventTriggerPlayerEventStartWithDrawn", 3);
        RequireAction(flow, 3, "Player_Event", "EventTriggerParticipateEvent", 12);
        RequireAction(flow, 12, "EventCamera", "EventChangeTemporarilyToPlayerCamera", 9);
        RequireAction(flow, 0, "EventSystemActor", "EventTransition", 4);
        RequireAction(flow, 1, "EventSystemActor", "EventTransition", 0xffff);
        RequireAction(flow, 7, "Player_Event", "EventPlayerSlowWalkStop", 0xffff);
        static bool Transition(HAL_EventFlow f, int index, string name) =>
            f.StringParameters(index) is var p && p.Count == 1 && p.TryGetValue("EventName", out var value) && value == name;
        if (!Transition(flow, 0, "Dm_Kokiri_0070") || !Transition(flow, 1, "Dm_Kokiri_0075"))
            throw new InvalidDataException("Unexpected Deku Tree meeting transitions.");
        var result = (byte[])data.Clone();
        // Enter the second meeting flow, whose patched entry performs only the
        // original quest updates and skips its presentation.
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(entry.Slot), 1);
        return result;
    }

    public static byte[] PatchDekuMeetingQuest(byte[] data)
    {
        var flow = new HAL_EventFlow(data, "Dm_Kokiri_0075");
        if (!flow.EntryPoints().TryGetValue("EntryPoint0", out var entry) || entry.EventIndex != 94)
            throw new InvalidDataException("Unexpected Deku meeting quest entry.");
        foreach (var (index, next, challenge, step) in new[] {
            (56, 57, "Meet_DekuTree", "Complete"), (57, 0xffff, "Get_StoneOfForest", "GoInside") })
        {
            RequireAction(flow, index, "ChallengeSystemActor", "EventExecuteProgressChallengeStep", next);
            var values = flow.StringParameters(index);
            if (values.Count != 2 || values.GetValueOrDefault("ChallengeName") != challenge || values.GetValueOrDefault("StepName") != step)
                throw new InvalidDataException("Unexpected Deku meeting quest completion.");
        }
        var result = (byte[])data.Clone();
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(entry.Slot), 56);
        return result;
    }

    public static byte[] PatchSwordCutscene(byte[] data)
    {
        var flow = new HAL_EventFlow(data, "Get_StoneOfForest");
        if (!flow.EntryPoints().TryGetValue("GetSword", out var entry) || entry.EventIndex != 48)
            throw new InvalidDataException("Unexpected Kokiri Sword reward entry.");
        RequireAction(flow, 48, "Player_Event", "EventTriggerParticipateEvent", 47);
        RequireAction(flow, 47, "EventSystemActor", "EventTransition", 88);
        RequireAction(flow, 88, "EventSystemActor", "EventTransition", 211);
        RequireAction(flow, 36, "EventCamera", "EventTriggerSendReserveCameraResetMessageToPlayerCamera");
        static bool Transition(HAL_EventFlow f, int index, string name) =>
            f.StringParameters(index) is var p && p.Count == 1 && p.TryGetValue("EventName", out var value) && value == name;
        if (!Transition(flow, 47, "Dm_SY_Treasurebox") || !Transition(flow, 88, "Dm_Kokiri_0063"))
            throw new InvalidDataException("Unexpected Kokiri Sword transitions.");
        var result = (byte[])data.Clone();
        data.AsSpan(flow.EventOffset(36) + 12, 4).CopyTo(result.AsSpan(flow.EventOffset(88) + 12));
        result.AsSpan(flow.EventOffset(88) + 16, 8).Clear();
        return result;
    }

    public static byte[] PatchDungeonNaviTutorial(byte[] data)
    {
        var flow = new HAL_EventFlow(data, "NaviHint");
        if (!flow.EntryPoints().TryGetValue("Hint_Dungeon_Tutorial_Event", out var entry) || entry.EventIndex != 22)
            throw new InvalidDataException("Unexpected dungeon Navi tutorial entry.");
        RequireAction(flow, 22, "EventCamera", "EventMoveToTargetPosCamera", 23);
        RequireAction(flow, 56, "Npc_Navi", "EventDoNothing", 0xffff);
        var result = (byte[])data.Clone();
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(entry.Slot), 56);
        return result;
    }

    public static byte[] PatchDekuNaviStop(byte[] data)
    {
        var flow = new HAL_EventFlow(data, "Dm_SY_BancChangeArrive");
        for (int i = 167; i <= 169; i++)
        {
            int at = flow.EventOffset(i);
            var parameters = flow.StringParameters(i);
            if (flow.UInt(at + 8, 1) != 4 || flow.UInt(at + 10, 2) != 137 ||
                flow.Action(i) != ("UISystemActor", "EventWaitCloseLoadingFade") || parameters.Count != 1 ||
                !parameters.TryGetValue("MessageId", out var message) ||
                message != $"EventFlowMsg/Get_StoneOfForest:NaviStop_0{i - 166}")
                throw new InvalidDataException("Deku Navi stop flow differs from the supported version.");
        }
        var result = (byte[])data.Clone();
        for (int i = 167; i <= 169; i++) result.AsSpan(flow.EventOffset(i) + 16, 8).Clear();
        return result;
    }

    public static byte[] PatchMidoFarewell(byte[] data)
    {
        var flow = new HAL_EventFlow(data, "Npc_Kokiri_Mido_Child");
        if (!flow.EntryPoints().TryGetValue("KokiriForest_Get_StoneOfForest_Completed_Event", out var entry) || entry.EventIndex != 204)
            throw new InvalidDataException("Unexpected post-Deku Mido entry.");
        int start = flow.EventOffset(204), fork = flow.EventOffset(80);
        if (flow.UInt(start + 8, 1) != 0 || flow.UInt(start + 10, 2) != 64 ||
            flow.UInt(fork + 8, 1) != 2 || flow.UInt(fork + 10, 2) != 2 || flow.UInt(fork + 12, 2) != 79)
            throw new InvalidDataException("Unexpected post-Deku Mido flow.");
        RequireAction(flow, 10, "GameDataActor", "EventTriggerSetGameDataBool", 32);
        RequireAction(flow, 32, "GameDataActor", "EventTriggerSetGameDataBool", 80);
        RequireAction(flow, 2, "ChallengeSystemActor", "EventExecuteProgressChallengeStep", 0xffff);
        // This branch is a type-3 event in the released flow; authored fixtures can use type 0.
        // It is inspected to identify the fork, but is bypassed and never rewritten here.
        int playerBranch = flow.EventOffset(79);
        if (flow.UInt(playerBranch + 8, 1) is not (0 or 3) || flow.Action(79) != ("Player_Event", "EventPlayerPlayASForFlowchart"))
            throw new InvalidDataException("Unexpected post-Deku Mido player branch.");
        RequireAction(flow, 163, "EventCamera", "EventTriggerSendReserveCameraResetMessageToPlayerCamera");
        var blocker = flow.StringParameters(10); var complete = flow.StringParameters(32); var challenge = flow.StringParameters(2);
        if (!blocker.TryGetValue("GameDataName", out var blockerName) || blockerName != "KokiriForest_Npc_Kokiri_Mido_Child_Blocker" ||
            !complete.TryGetValue("GameDataName", out var completeName) || completeName != "Get_StoneOfForest_Complete_Mido_TalkEnd" ||
            challenge.Count != 2 || !challenge.TryGetValue("ChallengeName", out var challengeName) || challengeName != "Meet_DekuTree" ||
            !challenge.TryGetValue("StepName", out var step) || step != "Step02")
            throw new InvalidDataException("Unexpected post-Deku Mido completion.");
        var result = (byte[])data.Clone();
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(entry.Slot), 10);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(flow.EventOffset(32) + 10), 2);
        return result;
    }

    public static byte[] PatchDekuTreeClear(byte[] data)
    {
        var f = new HAL_EventFlow(data, "Dm_SY_BancChange_EscapeDungeon");
        if (!f.EntryPoints().TryGetValue("Warp_Kokiri", out var entry) || entry.EventIndex != 28)
            throw new InvalidDataException("Unexpected Deku Tree exit entry.");
        RequireAction(f, 28, "Npc_Kokiri_Mido_Child", "EventTriggerParticipateEvent", 246);
        RequireAction(f, 246, "Land_Kokiri_DekuTree_A_04", "EventTriggerParticipateEvent", 247);
        RequireAction(f, 247, "Land_Kokiri_DekuTree_A_04", "EventDoNothing", 24);
        void Parameters(int index, params (string Key, string Value)[] expected)
        {
            var p = f.StringParameters(index);
            if (p.Count != expected.Length || expected.Any(x => !p.TryGetValue(x.Key, out var value) || value != x.Value))
                throw new InvalidDataException("Unexpected Deku Tree exit parameters.");
        }
        foreach (var (index, name, next) in new[] { (24,"Dm_Kokiri_0080",216), (216,"Dm_Kokiri_0095",149),
            (149,"Dm_Kokiri_0090",19), (19,"Dm_Kokiri_0130",20), (20,"Dm_Kokiri_0140",237), (3,"Dm_Kokiri_0162",176) })
        {
            RequireAction(f, index, "EventSystemActor", "EventTransition", next); Parameters(index, ("EventName", name));
        }
        RequireAction(f, 27, "EventCamera", "EventTriggerSendReserveCameraResetMessageToPlayerCamera", 26);
        RequireAction(f, 155, "Player_Event", "EventPlayerWarpToTargetPosAndRot", 161);
        RequireAction(f, 161, "Player_Event", "EventTriggerPlayerRequestLookAtTheFrontLock", 156);
        RequireAction(f, 156, "Npc_Navi", "EventNaviWarpToPlayerHomePosition", 163);
        int fork = f.EventOffset(159), reward = f.EventOffset(160);
        if (f.UInt(fork + 8, 1) != 2 || f.UInt(fork + 10, 2) != 3 || f.UInt(fork + 12, 2) != 160 ||
            f.UInt(reward + 8, 1) != 3 || f.UInt(reward + 10, 2) != 18)
            throw new InvalidDataException("Unexpected Deku Tree exit reward branch.");
        RequireAction(f, 18, "ChallengeSystemActor", "EventExecuteProgressChallengeStep", 21);
        Parameters(18, ("StepName", "Complete"), ("ChallengeName", "Get_StoneOfForest"));
        RequireAction(f, 21, "ChallengeSystemActor", "EventExecuteProgressChallengeStep", 240);
        Parameters(21, ("ChallengeName", "Goto_HyruleCastle"), ("StepName", "Step01"));
        RequireAction(f, 217, "GameDataActor", "EventTriggerSetGameDataBool", 236);
        Parameters(217, ("GameDataName", "NaviHint_Disp_FieldHint_01"));
        var result = (byte[])data.Clone();
        void Replace(int index, int source)
        {
            int target = f.EventOffset(index); data.AsSpan(f.EventOffset(source) + 12, 4).CopyTo(result.AsSpan(target + 12));
            result.AsSpan(target + 16, 32).Clear();
        }
        // Skip the stone presentation as well as the later farewell. The
        // native fallback grants the Emerald after the completion branch runs.
        Replace(20, 27); Replace(3, 27);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(f.EventOffset(247) + 10), 20);
        return result;
    }
}
