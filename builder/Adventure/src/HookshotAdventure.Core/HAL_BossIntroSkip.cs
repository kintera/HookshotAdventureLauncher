using System.Buffers.Binary;

namespace HookshotAdventure.Core;

/// <summary>Version-checked skips that preserve the boss fight setup.</summary>
public static class HAL_BossIntroSkip
{
    public static byte[] PatchGohma(byte[] data)
    {
        var flow=new HAL_EventFlow(data,"Get_StoneOfForest");
        var entries=flow.EntryPoints();
        if (!entries.TryGetValue("Boss_Appear",out var entry) || entry.EventIndex!=99 ||
            flow.Action(99)!=("GameDataActor","EventTriggerSetGameDataBool") ||
            !flow.StringParameters(99).TryGetValue("GameDataName",out var lighting) || lighting!="Gohma_IsLightingForBattle" ||
            flow.UInt(flow.EventOffset(99)+10,2)!=98 ||
            flow.Action(98)!=("Player_Event","EventTriggerPlayerEventStartWithDrawn") || flow.UInt(flow.EventOffset(98)+10,2)!=114 ||
            flow.Action(192)!=("Player_Event","EventPlayerWarpToStarterLinkAnchor") || flow.UInt(flow.EventOffset(192)+10,2)!=97 ||
            !flow.StringParameters(192).TryGetValue("InstanceName",out var position) || position!="StartPos_Get_StoneOfForest" ||
            flow.Action(97)!=("Player_Event","EventPlayerUnequip") || flow.UInt(flow.EventOffset(97)+10,2)!=0 ||
            flow.Action(0)!=("EventSystemActor","EventTransition") ||
            !flow.StringParameters(0).TryGetValue("EventName",out var movie) || movie!="Dm_Deku_BossIn" ||
            flow.UInt(flow.EventOffset(0)+10,2)!=191 ||
            flow.Action(191)!=("Enemy_DungeonBoss_Gohma","EventSetBattlePresentationOnPreState") ||
            flow.UInt(flow.EventOffset(191)+10,2)!=37)
            throw new InvalidDataException("Gohma introduction flow differs from the reviewed version.");
        // Retain the authored lighting, encounter setup, battle state and camera
        // cleanup; only bypass the transition that starts the introductory movie.
        var result=(byte[])data.Clone();
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(flow.EventOffset(97)+10),191);
        return result;
    }
}
