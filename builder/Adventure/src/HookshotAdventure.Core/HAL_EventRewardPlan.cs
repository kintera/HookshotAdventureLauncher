using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HookshotAdventure.Core;

/// <summary>Experimental exchanges between reviewed one-time events and rupee chests.</summary>
public static class HAL_EventRewardPlan
{
    public const string CheckId = "event:lost_woods_target_first_clear";
    public const string Resource = "Event/EventFlow/Dm_LostWood_0040.bfevfl.zs";
    public const string TargetReward = "TBox_Rupee_Green";
    public const string ChestReward = "Item_BulletBag_Medium";
    public const string SariaCheckId = "event:saria_fairy_ocarina";
    public const string SariaResource = "Event/EventFlow/Dm_Kokiri_0155.bfevfl.zs";
    public static bool IsRupee(string? actor) => actor is "TBox_Rupee_Green" or "TBox_Rupee_Blue" or "TBox_Rupee_Red" or "TBox_Rupee_Purple" or "TBox_Rupee_Gold";

    /// <summary>Exchange the five one-time House prizes with ordinary rupee chests.</summary>
    public static IReadOnlyDictionary<int, string> AddSkulltulaExchanges(JsonObject plan, string seed)
    {
        if (string.IsNullOrEmpty(seed) || seed.Length > 16 || seed.Any(c => !Uri.IsHexDigit(c)))
            throw new ArgumentException("Invalid event reward seed.");
        seed = seed.ToUpperInvariant().PadLeft(16, '0');
        var assignments = plan["assignments"]?.AsArray() ?? throw new InvalidDataException("Missing chest assignments.");
        var events = plan["event_assignments"] as JsonArray;
        if (events?.Any(row => row?["id"]?.GetValue<string>()?.StartsWith("event:skulltula_house_", StringComparison.Ordinal) == true) == true)
            throw new InvalidDataException("Skulltula rewards are already assigned.");
        var candidates = assignments.Select(node => node?.AsObject() ?? throw new InvalidDataException("Invalid chest assignment."))
            .Where(row => row["reward"]?.GetValue<string>() is "TBox_Rupee_Green" or "TBox_Rupee_Blue" or "TBox_Rupee_Red" or "TBox_Rupee_Purple" or "TBox_Rupee_Gold")
            .Where(row => row["event"] is null && row["runtime_blockers"]?.AsArray().Count == 0 && row["progression_excluded"]?.GetValue<bool>() != true)
            .OrderBy(row => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                $"HookshotSkulltulaHouseV1:{seed}:{row["id"]!.GetValue<string>()}"))), StringComparer.Ordinal)
            .ThenBy(row => row["id"]!.GetValue<string>(), StringComparer.Ordinal).ToArray();
        if (candidates.Length < HAL_SkulltulaRewards.Checks.Count)
            throw new InvalidDataException("Experimental Skulltula rewards need five ordinary shuffled rupee chests in the selected scope.");
        if (events is null) { events = new JsonArray(); plan["event_assignments"] = events; }
        var ledger = plan["reward_ledger"]!.AsObject(); var replacements = new Dictionary<int, string>();
        void Add(string section, string actor, int delta)
        {
            if (ledger[section] is not JsonObject counts) { counts = new JsonObject(); ledger[section] = counts; }
            int total = (counts[actor]?.GetValue<int>() ?? 0) + delta;
            if (total < 0) throw new InvalidDataException("Invalid Skulltula reward ledger.");
            if (total == 0) counts.Remove(actor); else counts[actor] = total;
        }
        for (int i = 0; i < HAL_SkulltulaRewards.Checks.Count; i++)
        {
            var check = HAL_SkulltulaRewards.Checks[i]; var chest = candidates[i];
            string id = $"event:skulltula_house_{check.Tokens}", reward = chest["reward"]!.GetValue<string>();
            string donor = chest["donor_id"]!.GetValue<string>(), chestId = chest["id"]!.GetValue<string>();
            chest["reward"] = check.Actor; chest["donor_id"] = id; replacements.Add(check.Tokens, reward);
            events.Add(new JsonObject { ["id"] = id, ["resource"] = HAL_SkulltulaRewards.Resource, ["tokens"] = check.Tokens,
                ["expected_reward"] = check.Actor, ["reward"] = reward, ["donor_id"] = donor, ["exchanged_chest"] = chestId });
            Add("shuffled_chests", reward, -1); Add("shuffled_chests", check.Actor, 1);
            Add("event_source", check.Actor, 1); Add("shuffled_events", reward, 1);
        }
        plan["settings"]!["shuffle_skulltulas"] = true;
        plan.Remove("plan_hash"); using var doc = JsonDocument.Parse(plan.ToJsonString());
        plan["plan_hash"] = HAL_CanonicalJson.Digest(doc.RootElement);
        return replacements;
    }

    public static string AddSariaExchange(JsonObject plan, string seed, bool startingOcarina = false)
    {
        if (string.IsNullOrEmpty(seed) || seed.Length > 16 || seed.Any(c => !Uri.IsHexDigit(c)))
            throw new ArgumentException("Invalid event reward seed.");
        seed = seed.ToUpperInvariant().PadLeft(16, '0');
        var assignments = plan["assignments"]?.AsArray() ?? throw new InvalidDataException("Missing chest assignments.");
        var events = plan["event_assignments"] as JsonArray;
        if (events?.Any(row => row?["id"]?.GetValue<string>() == SariaCheckId) == true)
            throw new InvalidDataException("Saria's reward is already assigned.");
        var candidates = assignments.Select(node => node?.AsObject() ?? throw new InvalidDataException("Invalid chest assignment."))
            .Where(row => IsRupee(row["reward"]?.GetValue<string>()) && row["progression_excluded"]?.GetValue<bool>() != true && row["event"] is null &&
                row["runtime_blockers"]?.AsArray().Count == 0)
            .OrderBy(row => row["reward"]!.GetValue<string>() == TargetReward ? 0 : 1)
            .ThenBy(row => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                $"HookshotSariaGiftV1:{seed}:{row["id"]!.GetValue<string>()}"))), StringComparer.Ordinal)
            .ThenBy(row => row["id"]!.GetValue<string>(), StringComparer.Ordinal).ToArray();
        if (candidates.Length == 0) throw new InvalidDataException("Saria's experimental reward requires a rupee chest without a special event.");
        var chosen = candidates[0]; string donor = chosen["donor_id"]!.GetValue<string>(), chest = chosen["id"]!.GetValue<string>(), replacement = chosen["reward"]!.GetValue<string>();
        chosen["reward"] = startingOcarina ? "TBox_Rupee_Blue" : "Item_Ocarina_Fairy"; chosen["donor_id"] = SariaCheckId;
        if (events is null) { events = new JsonArray(); plan["event_assignments"] = events; }
        events.Add(new JsonObject { ["id"] = SariaCheckId, ["resource"] = SariaResource,
            ["expected_reward"] = "Item_Ocarina_Fairy", ["reward"] = replacement, ["donor_id"] = donor, ["exchanged_chest"] = chest });
        var ledger = plan["reward_ledger"]!.AsObject();
        void Add(string section, string item, int delta)
        {
            if (ledger[section] is not JsonObject counts) { counts = new JsonObject(); ledger[section] = counts; }
            int total = (counts[item]?.GetValue<int>() ?? 0) + delta;
            if (total < 0) throw new InvalidDataException("Invalid event reward ledger.");
            if (total == 0) counts.Remove(item); else counts[item] = total;
        }
        Add("shuffled_chests", replacement, -1); Add("shuffled_chests", startingOcarina ? "TBox_Rupee_Blue" : "Item_Ocarina_Fairy", 1);
        Add("event_source", "Item_Ocarina_Fairy", 1); Add("shuffled_events", replacement, 1);
        if (startingOcarina)
        {
            Add("added_filler", "TBox_Rupee_Blue", 1); Add("starting_inventory", "Item_Ocarina_Fairy", 1);
            (plan["starting_inventory"]?.AsArray() ?? throw new InvalidDataException("Missing starting inventory ledger."))
                .Add(new JsonObject { ["dungeon"] = "KokiriForest", ["item"] = "Item_Ocarina_Fairy",
                    ["source_check"] = SariaCheckId, ["delivery_status"] = "development_pouch_grant" });
        }
        plan["settings"]!["shuffle_saria_ocarina"] = true;
        plan.Remove("plan_hash"); using var doc = JsonDocument.Parse(plan.ToJsonString());
        plan["plan_hash"] = HAL_CanonicalJson.Digest(doc.RootElement);
        return replacement;
    }

    public static string AddTargetExchange(JsonObject plan, string seed)
    {
        if (string.IsNullOrEmpty(seed) || seed.Length > 16 || seed.Any(c => !Uri.IsHexDigit(c)))
            throw new ArgumentException("Invalid event reward seed.");
        var events = plan["event_assignments"] as JsonArray;
        if (events?.Any(row => row?["id"]?.GetValue<string>() == CheckId) == true) throw new InvalidDataException("Event rewards are already assigned.");
        seed = seed.ToUpperInvariant().PadLeft(16, '0');
        var assignments = plan["assignments"]?.AsArray() ?? throw new InvalidDataException("Missing chest assignments.");
        var candidates = assignments.Select(node => node?.AsObject() ?? throw new InvalidDataException("Invalid chest assignment."))
            .Where(row => IsRupee(row["reward"]?.GetValue<string>()) && row["progression_excluded"]?.GetValue<bool>() != true && row["event"] is null &&
                row["runtime_blockers"]?.AsArray().Count == 0)
            .OrderBy(row => row["reward"]!.GetValue<string>() == TargetReward ? 0 : 1)
            .ThenBy(row => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                $"HookshotEventTargetV1:{seed}:{row["id"]!.GetValue<string>()}"))), StringComparer.Ordinal)
            .ThenBy(row => row["id"]!.GetValue<string>(), StringComparer.Ordinal).ToArray();
        if (candidates.Length == 0) throw new InvalidDataException("The experimental target reward needs a shuffled rupee chest with no special event.");
        var chosen = candidates[0];
        string donor = chosen["donor_id"]!.GetValue<string>(), chest = chosen["id"]!.GetValue<string>(), replacement = chosen["reward"]!.GetValue<string>();
        chosen["reward"] = ChestReward; chosen["donor_id"] = CheckId;
        if (events is null) { events = new JsonArray(); plan["event_assignments"] = events; }
        events.Add(new JsonObject { ["id"] = CheckId, ["resource"] = Resource,
            ["expected_reward"] = ChestReward, ["reward"] = replacement, ["donor_id"] = donor,
            ["exchanged_chest"] = chest, ["scope"] = "first clear; both bag ownership branches" });
        var ledger = plan["reward_ledger"]!.AsObject();
        static void Add(JsonObject counts, string item, int delta)
        {
            int n = (counts[item]?.GetValue<int>() ?? 0) + delta;
            if (n < 0) throw new InvalidDataException("Invalid reward ledger.");
            if (n == 0) counts.Remove(item); else counts[item] = n;
        }
        Add(ledger["shuffled_chests"]!.AsObject(), replacement, -1);
        Add(ledger["shuffled_chests"]!.AsObject(), ChestReward, 1);
        if (ledger["event_source"] is not JsonObject) ledger["event_source"] = new JsonObject();
        if (ledger["shuffled_events"] is not JsonObject) ledger["shuffled_events"] = new JsonObject();
        Add(ledger["event_source"]!.AsObject(), ChestReward, 1);
        Add(ledger["shuffled_events"]!.AsObject(), replacement, 1);
        plan["settings"]!["shuffle_npc_rewards"] = true;
        plan.Remove("plan_hash");
        using var doc = JsonDocument.Parse(plan.ToJsonString());
        plan["plan_hash"] = HAL_CanonicalJson.Digest(doc.RootElement);
        return replacement;
    }
}
