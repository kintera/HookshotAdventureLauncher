namespace HookshotAdventure.Core;

/// <summary>
/// The opening quest step needed for an open forest. The blocker-flag actions
/// are a separate required edit; do not enable the option from this edit alone.
/// Behavior cross-checked against OOTRR, GPL-3.0-or-later:
/// https://github.com/ootrando/ootrr/blob/0fa64f4/randomizer/core/src/start_defaults/opening.rs
/// </summary>
public static class HAL_OpenForestOpening
{
    public static byte[] PatchQuestStep(byte[] data)
    {
        var flow = new HAL_EventFlow(data, "OpeningEvent");
        var matches = Enumerable.Range(0, flow.Count).Where(i => flow.UInt(flow.EventOffset(i) + 8, 1) == 0 &&
            flow.Action(i) == ("ChallengeSystemActor", "EventExecuteProgressChallengeStep") &&
            flow.StringParameters(i).GetValueOrDefault("ChallengeName") == "Meet_DekuTree").ToArray();
        if (matches.Length != 1 || flow.StringParameters(matches[0]).GetValueOrDefault("StepName") != "Step01")
            throw new InvalidDataException("Unexpected opening Deku Tree quest step.");
        int index = matches[0], slot = flow.StringParameterPointerSlot(index, "StepName");
        for (int i = 0; i < flow.Count; i++)
        {
            if (flow.UInt(flow.EventOffset(i) + 8, 1) is not (0 or 1 or 4)) continue;
            foreach (string key in flow.StringParameters(i).Keys)
                if ((i != index || key != "StepName") && flow.StringParameterPointerSlot(i, key) == slot)
                    throw new InvalidDataException("Opening quest parameter is shared by another action.");
        }
        byte[] result = HAL_EventFlowStrings.AppendAndPoint(data, slot, "Step03");
        var updated = new HAL_EventFlow(result, "OpeningEvent");
        if (updated.StringParameters(index).GetValueOrDefault("StepName") != "Step03")
            throw new InvalidDataException("Opening quest step did not round-trip.");
        return result;
    }
}
