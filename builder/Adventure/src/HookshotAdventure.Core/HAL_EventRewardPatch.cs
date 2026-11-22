using System.Buffers.Binary;

namespace HookshotAdventure.Core;

/// <summary>Version-checked experimental event reward edits.</summary>
public static class HAL_EventRewardPatch
{
    public const string SariaBridgeResource = "Event/EventFlow/Dm_SY_BancChangeArrive.bfevfl.zs";

    /// <summary>Skip only the bridge's ocarina tutorial when its gift is shuffled.</summary>
    public static byte[] SariaBridgeTutorial(byte[] data)
    {
        var flow = new HAL_EventFlow(data, "Dm_SY_BancChangeArrive");
        int wait = flow.EventOffset(479), tutorial = flow.EventOffset(88), after = flow.EventOffset(226);
        if (flow.UInt(wait + 8, 1) != 0 || flow.UInt(wait + 10, 2) != 88 ||
            flow.Action(479) != ("GameSystemActor", "EventWait") ||
            flow.ScalarParameter(479, "Frames") != ((byte)2, 70u) ||
            flow.UInt(tutorial + 8, 1) != 4 || flow.UInt(tutorial + 10, 2) != 226 ||
            flow.Subflow(88) != ("ActionGuide", "OpenActionGuideSceenForOcarina") ||
            flow.UInt(after + 8, 1) != 0 ||
            flow.Action(226) != ("GameSystemActor", "EventTriggerRequestAutoSave"))
            throw new InvalidDataException("Saria's bridge tutorial differs from the reviewed version.");
        byte[] result = (byte[])data.Clone();
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(wait + 10), 226);
        return result;
    }

    /// <summary>Replace Saria's displayed gift and suppress the separate vanilla pouch grant.</summary>
    public static byte[] SariaFairyOcarina(byte[] data, string replacement)
    {
        if (!HAL_EventRewardPlan.IsRupee(replacement)) throw new InvalidDataException("Unreviewed Saria reward.");
        var flow = new HAL_EventFlow(data, "Dm_Kokiri_0155");
        int gift = flow.EventOffset(52), grant = flow.EventOffset(51), screenshot = flow.EventOffset(90), wait = flow.EventOffset(59);
        var fork = flow.ForkRoutes(70);
        if (flow.UInt(gift + 8, 1) != 4 || flow.UInt(gift + 10, 2) != 53 ||
            flow.Subflow(52) != ("GetPouchContent", "DisplayGetByActorName") ||
            !flow.StringParameters(52).TryGetValue("ActorName", out var actor) || actor != "Item_Ocarina_Fairy" ||
            flow.UInt(grant + 8, 1) != 0 || flow.UInt(grant + 10, 2) != 0xffff ||
            flow.Action(51) != ("PouchSystemActor", "EventTriggerAddOrRemovePouchContent") ||
            !flow.StringParameters(51).TryGetValue("Actor", out var pouchActor) || pouchActor != "Item_Ocarina_Fairy" ||
            flow.ScalarParameter(51, "Num") != ((byte)2, 1u) ||
            !fork.Branches.SequenceEqual(new[] { 66, 72, 51, 74 }) || fork.Join != 71 ||
            flow.UInt(wait + 8, 1) != 0 || flow.UInt(wait + 10, 2) != 0xffff ||
            flow.Action(59) != ("EventSystemActor", "EventWait") ||
            flow.UInt(screenshot + 8, 1) != 0 || flow.UInt(screenshot + 10, 2) != 52 ||
            flow.Action(90) != ("GameSystemActor", "EventTriggerZeldaNotesRequestSendDailyScreenShot"))
            throw new InvalidDataException("Saria's gift flow differs from the reviewed version.");
        int giftSlot = flow.StringParameterPointerSlot(52, "ActorName");
        int forkSlot = checked((int)flow.UInt(flow.EventOffset(70) + 16, 8));
        for (int index = 0; index < flow.Count; index++)
        {
            if (flow.UInt(flow.EventOffset(index) + 8, 1) == 2 && index != 70 &&
                flow.UInt(flow.EventOffset(index) + 16, 8) == (ulong)forkSlot)
                throw new InvalidDataException("Saria gift fork is shared with another event.");
            if (flow.UInt(flow.EventOffset(index) + 8, 1) is not (0 or 1 or 4)) continue;
            foreach (string name in flow.ParameterNames(index))
            {
                var value = flow.ScalarParameter(index, name);
                if (index != 52 && value is { Type: 5 } && flow.StringParameterPointerSlot(index, name) == giftSlot)
                    throw new InvalidDataException("Saria gift parameters are shared with another event.");
            }
        }
        byte[] result = HAL_EventFlowStrings.AppendAndPoint(data, giftSlot, replacement);
        // The original subflow only displays the item. Use the game's generic
        // display-and-grant entry before bypassing the separate vanilla grant.
        result = HAL_EventFlowStrings.AppendAndPoint(result, gift + 32, "GetPouchContentByActorName");
        // A zero-count pouch action can still change equipped-item state. Route the
        // fork through the scene's existing terminal wait instead of calling it.
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(forkSlot + 4), 59);
        return result;
    }

    public static byte[] LostWoodsTargetGreenRupee(byte[] data) => LostWoodsTargetRupee(data, "TBox_Rupee_Green");

    public static byte[] LostWoodsTargetRupee(byte[] data, string reward)
    {
        if (!HAL_EventRewardPlan.IsRupee(reward)) throw new InvalidDataException("Unreviewed target reward.");
        var flow = new HAL_EventFlow(data, "Dm_LostWood_0040");
        int first = flow.EventOffset(85), large = flow.EventOffset(84), repeat = flow.EventOffset(86);
        var branch = flow.SwitchRoutes(56);
        var completion = flow.SwitchRoutes(45);
        if (flow.Query(45) != ("GameDataActor", "EventQueryIsGameDataBool") ||
            !flow.StringParameters(45).TryGetValue("GameDataName", out var completed) || completed != "LostWood_HitTargetOfDekuScrub_FirstClear" ||
            completion.Count != 2 || !completion.TryGetValue(0, out int repeated) || repeated != 46 ||
            !completion.TryGetValue(1, out int initial) || initial != 39 ||
            flow.Query(56) != ("GameDataActor", "EventQueryIsGameDataBool") ||
            !flow.StringParameters(56).TryGetValue("GameDataName", out var ownership) || ownership != "IsGet.Item_BulletBag_Medium" ||
            branch.Count != 2 || !branch.TryGetValue(0, out int largeBranch) || largeBranch != 84 ||
            !branch.TryGetValue(1, out int firstBranch) || firstBranch != 85 ||
            flow.UInt(large + 8, 1) != 4 || flow.UInt(large + 10, 2) != 88 ||
            flow.UInt(flow.EventOffset(88) + 10, 2) != 57 ||
            flow.UInt(first + 8, 1) != 4 || flow.UInt(first + 10, 2) != 89 ||
            flow.UInt(repeat + 8, 1) != 4 || flow.UInt(repeat + 10, 2) != 87 ||
            flow.UInt(flow.EventOffset(89) + 10, 2) != 57 ||
            !flow.StringParameters(85).TryGetValue("ActorName", out var original) || original != "Item_BulletBag_Medium" ||
            !flow.StringParameters(84).TryGetValue("ActorName", out var originalLarge) || originalLarge != "Item_BulletBag_Large" ||
            !flow.StringParameters(86).TryGetValue("ActorName", out var replacement) || replacement != "TBox_Rupee_Green" ||
            flow.Action(57) != ("GameDataActor", "EventTriggerSetGameDataBool") ||
            flow.ScalarParameter(57, "Value") != ((byte)3, 0x80000001u) ||
            !flow.StringParameters(57).TryGetValue("GameDataName", out var flag) || flag != "LostWood_HitTargetOfDekuScrub_FirstClear")
            throw new InvalidDataException("Lost Woods target reward flow differs from the reviewed version.");
        int slot = flow.StringParameterPointerSlot(85, "ActorName");
        int largeSlot = flow.StringParameterPointerSlot(84, "ActorName");
        if (slot == largeSlot) throw new InvalidDataException("Lost Woods target reward parameters are shared.");
        for (int index = 0; index < flow.Count; index++)
        {
            if (index is 84 or 85 || flow.UInt(flow.EventOffset(index) + 8, 1) is not (0 or 1 or 4)) continue;
            var parameters = flow.StringParameters(index);
            if (parameters.ContainsKey("ActorName") && (flow.StringParameterPointerSlot(index, "ActorName") == slot ||
                flow.StringParameterPointerSlot(index, "ActorName") == largeSlot))
                throw new InvalidDataException("Lost Woods target reward parameter is shared.");
        }
        var result = HAL_EventFlowStrings.AppendAndPoint(data, slot, reward);
        result = HAL_EventFlowStrings.AppendAndPoint(result, largeSlot, reward);
        return result;
    }
}
