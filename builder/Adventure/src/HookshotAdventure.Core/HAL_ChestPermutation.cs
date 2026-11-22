using System.Security.Cryptography;
using System.Text;

namespace HookshotAdventure.Core;

public sealed record HAL_ChestPoolEntry(string Id, ulong Placement, string Reward, IReadOnlyList<string> Resources);
public sealed record HAL_ChestDonor(string TargetId, string DonorId);

/// <summary>Deterministic donor ordering for one already-reviewed chest pool.</summary>
public static class HAL_ChestPermutation
{
    private const ulong Slingshot = 821372075822700622;

    // Conservative until full world logic classifies every reward: only currency,
    // ammunition and magic refills are admitted to filler-only locations.
    public static bool IsSafeFiller(string reward) => reward is
        "TBox_Rupee_Green" or "TBox_Rupee_Blue" or "TBox_Rupee_Red" or "TBox_Rupee_Purple" or "TBox_Rupee_Gold" or
        "TBox_ArrowBundle_Medium" or "TBox_ArrowBundle_Large" or "Item_BombBundle_Small" or "Item_BombchuBundle_Small" or
        "TBox_DekuNutBundle" or "Item_DekuStickBundle_Small" or "TBox_DekuSeedBundle" or "Tbox_MagicJar_Large";

    public static IReadOnlyList<HAL_ChestDonor> Assign(IEnumerable<HAL_ChestPoolEntry> pool, string seed, bool expanded,
        IReadOnlySet<string>? progressionExcluded = null)
    {
        if (string.IsNullOrEmpty(seed) || seed.Length > 16 || !seed.All(Uri.IsHexDigit))
            throw new ArgumentException("Seed must contain 1–16 hexadecimal digits.");
        seed = seed.ToUpperInvariant().PadLeft(16, '0');
        var targets = pool.OrderBy(row => row.Id, StringComparer.Ordinal).ToArray();
        if (targets.Any(row => string.IsNullOrEmpty(row.Id)) || targets.Select(row => row.Id).Distinct(StringComparer.Ordinal).Count() != targets.Length)
            throw new InvalidDataException("Duplicate or missing chest identity.");
        progressionExcluded ??= new HashSet<string>(StringComparer.Ordinal);
        if (progressionExcluded.Any(id => targets.All(row => row.Id != id))) throw new InvalidDataException("Unknown filler-only chest.");
        // Large pools make random retries unlikely to hit both opening constraints.
        // Keep the established ordering for normal pools, then solve the few
        // constrained placements directly before filling the remaining checks.
        int attempts = targets.Length >= 128 || progressionExcluded.Count > 0 ? 128 : 4096;
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            var ordered = targets.Select(row => (Row: row, Key: Convert.ToHexString(SHA256.HashData(
                    Encoding.UTF8.GetBytes($"HookshotCombinedV4:{seed}:{attempt}:{row.Id}")))))
                .OrderBy(row => row.Key, StringComparer.Ordinal).ThenBy(row => row.Row.Id, StringComparer.Ordinal)
                .Select(row => row.Row).ToList();
            var donors = ordered.ToArray();
            bool permitted = true;
            for (int i = 0; i < targets.Length; i++)
            {
                if (donors[i].Reward == "Item_Sword_Kokiri" &&
                    !HAL_OpeningAccess.ForestBeforeDeku(targets[i].Placement, targets[i].Resources)) permitted = false;
                if (expanded && donors[i].Reward == "Item_Range_SlingShot" && targets[i].Placement != Slingshot &&
                    !HAL_OpeningAccess.ForestBeforeDeku(targets[i].Placement, targets[i].Resources) &&
                    !HAL_OpeningAccess.DekuLobbyMap(targets[i].Placement, targets[i].Resources)) permitted = false;
                if (progressionExcluded.Contains(targets[i].Id) && !IsSafeFiller(donors[i].Reward)) permitted = false;
            }
            if (permitted) return targets.Select((target, i) => new HAL_ChestDonor(target.Id, donors[i].Id)).ToArray();
        }
        string Key(string kind, string id) => Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes($"HookshotCombinedFallbackV1:{seed}:{kind}:{id}")));
        bool Allows(HAL_ChestPoolEntry target, HAL_ChestPoolEntry donor) =>
            donor.Reward != "Item_Sword_Kokiri" || HAL_OpeningAccess.ForestBeforeDeku(target.Placement, target.Resources);
        bool Allowed(HAL_ChestPoolEntry target, HAL_ChestPoolEntry donor) => Allows(target, donor) &&
            (!progressionExcluded.Contains(target.Id) || IsSafeFiller(donor.Reward)) &&
            (!expanded || donor.Reward != "Item_Range_SlingShot" || target.Placement == Slingshot ||
             HAL_OpeningAccess.ForestBeforeDeku(target.Placement, target.Resources) ||
             HAL_OpeningAccess.DekuLobbyMap(target.Placement, target.Resources));
        var constrained = targets.Where(row => row.Reward == "Item_Sword_Kokiri" || expanded && row.Reward == "Item_Range_SlingShot" ||
                progressionExcluded.Count > 0 && !IsSafeFiller(row.Reward))
            .OrderBy(row => row.Reward == "Item_Sword_Kokiri" ? 0 : row.Reward == "Item_Range_SlingShot" ? 1 : 2)
            .ThenBy(row => Key("donor", row.Id), StringComparer.Ordinal).ToArray();
        var chosen = new Dictionary<string, HAL_ChestPoolEntry>(StringComparer.Ordinal);
        bool Match(HAL_ChestPoolEntry donor, HashSet<string> visited)
        {
            foreach (var target in targets.Where(target => Allowed(target, donor))
                         .OrderBy(target => Key(donor.Id, target.Id), StringComparer.Ordinal).ThenBy(target => target.Id, StringComparer.Ordinal))
            {
                if (!visited.Add(target.Id)) continue;
                if (!chosen.TryGetValue(target.Id, out var previous) || Match(previous, visited))
                { chosen[target.Id] = donor; return true; }
            }
            return false;
        }
        foreach (var donor in constrained)
            if (!Match(donor, new HashSet<string>(StringComparer.Ordinal)))
                throw new InvalidDataException("No placement satisfies the early sword/slingshot constraints.");
        var used = chosen.Values.Select(row => row.Id).ToHashSet(StringComparer.Ordinal);
        var remainingTargets = targets.Where(row => !chosen.ContainsKey(row.Id)).OrderBy(row => Key("target", row.Id), StringComparer.Ordinal).ToArray();
        var remainingDonors = targets.Where(row => !used.Contains(row.Id)).OrderBy(row => Key("remaining", row.Id), StringComparer.Ordinal).ToArray();
        for (int i = 0; i < remainingTargets.Length; i++) chosen.Add(remainingTargets[i].Id, remainingDonors[i]);
        return targets.Select(target => new HAL_ChestDonor(target.Id, chosen[target.Id].Id)).ToArray();
    }
}
