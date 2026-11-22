// Authored Deku exit fixture; no game data.
using HookshotAdventure.Core;
using System.IO.Compression;
static class DekuExitFixture
{
    public static void Check()
    {
        using var input = new ZLibStream(new MemoryStream(Convert.FromBase64String("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")), CompressionMode.Decompress);
        using var output = new MemoryStream(); input.CopyTo(output);
        var patched = HAL_ConversationSkips.PatchDekuTreeClear(output.ToArray());
        var flow = new HAL_EventFlow(patched, "Dm_SY_BancChange_EscapeDungeon");
        AssertTest.True(flow.UInt(flow.EventOffset(247) + 10, 2) == 20 &&
            flow.Action(19) == ("EventSystemActor", "EventTransition") &&
            flow.StringParameters(19).TryGetValue("EventName", out var scene) && scene == "Dm_Kokiri_0130" &&
            flow.UInt(flow.EventOffset(19) + 10, 2) == 20, "Deku exit bypasses the native stone presentation");
        AssertTest.True(flow.Action(28) == ("Npc_Kokiri_Mido_Child", "EventTriggerParticipateEvent") &&
            flow.Action(246) == ("Land_Kokiri_DekuTree_A_04", "EventTriggerParticipateEvent"), "Deku exit retains stone scene participants");
        AssertTest.True(flow.Action(20) == ("EventCamera", "EventTriggerSendReserveCameraResetMessageToPlayerCamera") &&
            flow.Action(3) == ("EventCamera", "EventTriggerSendReserveCameraResetMessageToPlayerCamera"), "Deku exit still skips later conversations");
        AssertTest.True(flow.Action(18) == ("ChallengeSystemActor", "EventExecuteProgressChallengeStep") &&
            flow.StringParameters(18)["ChallengeName"] == "Get_StoneOfForest" && flow.StringParameters(18)["StepName"] == "Complete" &&
            flow.Action(21) == ("ChallengeSystemActor", "EventExecuteProgressChallengeStep") &&
            flow.StringParameters(21)["ChallengeName"] == "Goto_HyruleCastle", "Deku exit preserves the completion and next-quest actions");
        bool rejected = false;
        try { HAL_ConversationSkips.PatchDekuTreeClear(patched); } catch (InvalidDataException) { rejected = true; }
        AssertTest.True(rejected, "Deku exit patch rejects altered flow");
    }
}
