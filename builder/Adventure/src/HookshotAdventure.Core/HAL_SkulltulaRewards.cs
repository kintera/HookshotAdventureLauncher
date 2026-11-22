using System.Buffers.Binary;
using System.Text.Json;

namespace HookshotAdventure.Core;

/// <summary>Reviewed Skulltula House reward sites; reward delivery is still experimental.</summary>
public static class HAL_SkulltulaRewards
{
    public const string Resource = "Event/EventFlow/GatheringTokenGold.bfevfl.zs";
    public const int DefaultMaximum = 50;
    public static int ValidateMaximum(int maximum) => maximum is >= 0 and <= 100 ? maximum :
        throw new ArgumentOutOfRangeException(nameof(maximum), "Maximum Skulltulas must be from 0 to 100.");
    public sealed record Check(int Tokens, int Event, int FlagEvent, string Actor);
    public sealed record Preview(int Tokens, string Name, string Reward);
    public static IReadOnlyList<Check> Checks { get; } = Array.AsReadOnly(new[] {
        new Check(10,245,145,"Item_Wallet_Medium"), new Check(20,246,101,"Item_AgonyStone"),
        new Check(30,247,104,"Item_Wallet_Large"), new Check(40,248,109,"Item_BombchuBundle_Large"),
        new Check(50,249,107,"Obj_Heart_Piece") });

    /// <summary>Returns the one-time checks at or below the maximum token requirement.</summary>
    public static IReadOnlyList<Check> WithinMaximum(int maximum)
    {
        ValidateMaximum(maximum);
        return Checks.Where(check => check.Tokens <= maximum).ToArray();
    }

    public static IReadOnlyList<Preview> LoadPreview(string path)
    {
        using var data=JsonDocument.Parse(File.ReadAllBytes(path));
        if (data.RootElement.GetProperty("schema_version").GetInt32()!=1) throw new InvalidDataException("Unsupported Skulltula resource version.");
        var checks=data.RootElement.GetProperty("checks").EnumerateArray().Select(row => new Preview(
            row.GetProperty("tokens").GetInt32(),row.GetProperty("name").GetString() ?? "",row.GetProperty("reward").GetString() ?? "")).ToArray();
        if (!checks.Select(x=>x.Tokens).SequenceEqual(new[]{10,20,30,40,50,100}) ||
            checks.Any(x=>string.IsNullOrWhiteSpace(x.Name) || string.IsNullOrWhiteSpace(x.Reward)))
            throw new InvalidDataException("Invalid Skulltula reward resource.");
        return checks;
    }

    /// <summary>Swap only existing reward strings; does not edit gates or the repeatable 100-token prize.</summary>
    public static byte[] PatchExistingActors(byte[] data, IReadOnlyDictionary<int, string> replacements)
    {
        if (replacements.Keys.Any(tokens => Checks.All(check => check.Tokens != tokens)) ||
            replacements.Values.Any(actor => Checks.All(check => check.Actor != actor)))
            throw new InvalidDataException("Unsupported Skulltula reward or replacement.");
        var resulting = Checks.Select(check => replacements.TryGetValue(check.Tokens, out var actor) ? actor : check.Actor).ToArray();
        if (!resulting.Order(StringComparer.Ordinal).SequenceEqual(Checks.Select(check => check.Actor).Order(StringComparer.Ordinal)))
            throw new InvalidDataException("Skulltula reward swap does not conserve the item pool.");
        var flow = new HAL_EventFlow(data, "GatheringTokenGold");
        var slots = new Dictionary<int,int>(); var donor = new Dictionary<string,ulong>(StringComparer.Ordinal);
        foreach (var check in Checks)
        {
            int at=flow.EventOffset(check.Event), flag=flow.EventOffset(check.FlagEvent);
            if (flow.UInt(at+8,1)!=4 || flow.UInt(at+10,2)!=(ulong)check.FlagEvent ||
                flow.Subflow(check.Event)!=("", "TakenReward_Common_Event") ||
                !flow.StringParameters(check.Event).TryGetValue("TakenRewardActor",out var actor) || actor!=check.Actor ||
                flow.UInt(flag+8,1)!=0 || flow.Action(check.FlagEvent)!=("GameDataActor","EventTriggerSetGameDataBool") ||
                !flow.StringParameters(check.FlagEvent).TryGetValue("GameDataName",out var name) ||
                name!=$"KakarikoVillage_GatheringTokenGold_GetReward{check.Tokens}" ||
                flow.ScalarParameter(check.FlagEvent,"Value")!=((byte)3,0x80000001u))
                throw new InvalidDataException("Skulltula reward flow differs from the reviewed version.");
            int slot=flow.StringParameterPointerSlot(check.Event,"TakenRewardActor");
            if (slots.Values.Contains(slot)) throw new InvalidDataException("Skulltula reward parameters are shared.");
            slots.Add(check.Tokens,slot); donor.Add(check.Actor,flow.UInt(slot,8));
        }
        foreach(int index in Enumerable.Range(0,flow.Count))
        {
            if (Checks.Any(check=>check.Event==index) || flow.UInt(flow.EventOffset(index)+8,1) is not (0 or 1 or 4)) continue;
            foreach(var (name, _) in flow.StringParameters(index))
                if(slots.Values.Contains(flow.StringParameterPointerSlot(index,name)))
                    throw new InvalidDataException("Skulltula reward parameter is shared with another event.");
        }
        byte[] result=(byte[])data.Clone();
        foreach(var (tokens,actor) in replacements)
            BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(slots[tokens],8),donor[actor]);
        return result;
    }

    /// <summary>
    /// Prepare individual one-time rewards for an experimental green-rupee exchange.
    /// The caller must account for each displaced reward in the seed's item pool.
    /// The repeatable 100-token reward is deliberately excluded.
    /// </summary>
    public static byte[] PatchGreenRupeeExchanges(byte[] data, IReadOnlyCollection<int> thresholds)
    {
        if (thresholds.Count == 0 || thresholds.Distinct().Count() != thresholds.Count ||
            thresholds.Any(tokens => Checks.All(check => check.Tokens != tokens)))
            throw new InvalidDataException("Invalid Skulltula reward exchange.");
        return PatchRupeeExchanges(data, thresholds.ToDictionary(x => x, _ => "TBox_Rupee_Green"));
    }

    /// <summary>Patch reviewed one-time reward sites using a rupee-only exchange plan.</summary>
    public static byte[] PatchRupeeExchanges(byte[] data, IReadOnlyDictionary<int, string> replacements)
    {
        if (replacements.Count == 0 || replacements.Keys.Any(tokens => Checks.All(check => check.Tokens != tokens)) ||
            replacements.Values.Any(actor => actor is not ("TBox_Rupee_Green" or "TBox_Rupee_Blue" or "TBox_Rupee_Red" or "TBox_Rupee_Purple" or "TBox_Rupee_Gold")))
            throw new InvalidDataException("Invalid Skulltula rupee exchange.");
        byte[] result = PatchExistingActors(data, new Dictionary<int, string>());
        foreach (var (tokens, actor) in replacements.OrderBy(row => row.Key))
        {
            var flow = new HAL_EventFlow(result, "GatheringTokenGold");
            var check = Checks.Single(row => row.Tokens == tokens);
            int slot = flow.StringParameterPointerSlot(check.Event, "TakenRewardActor");
            result = HAL_EventFlowStrings.AppendAndPoint(result, slot, actor);
        }
        return result;
    }
}
