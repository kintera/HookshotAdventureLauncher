
namespace HookshotAdventure.Core;

public sealed record HAL_PlannerLocation(string Id, ulong Placement, string Actor, string Reward, string? Event,
    IReadOnlyList<string> Resources, bool Eligible, IReadOnlyList<string> Exclusions, bool ProgressionExcluded = false);
public sealed record HAL_PlannedChest(string Id, ulong Placement, string Actor, IReadOnlyList<string> Resources,
    string ExpectedReward, string? ExpectedEvent, string DonorId, string Reward, string? Event, bool ProgressionExcluded = false);
public sealed record HAL_StartingDungeonItem(string Dungeon, string Item, string SourceCheck);
public sealed record HAL_FillerItem(string SourceCheck, string Removed, string Added);
public sealed record HAL_ChestPlan(IReadOnlyList<HAL_PlannedChest> Assignments,
    IReadOnlyList<HAL_StartingDungeonItem> StartingInventory, IReadOnlyList<HAL_FillerItem> Filler,
    HAL_OpeningAuditResult OpeningAudit);

/// <summary>Builds reviewed chest placements; catalog and plan hashes are handled separately.</summary>
public static class HAL_ChestPlanner
{
    private const ulong Sword = 16757992849254467386, Slingshot = 821372075822700622;
    private const string ChestEvent = "KokiriForest_TreasureBox_NaviChatOff";
    private static string Region(string path)
    {
        int index = path.IndexOf("Banc/", StringComparison.Ordinal);
        if (index < 0) throw new InvalidDataException("Invalid chest resource path.");
        return path[(index + 5)..].Split('/')[0];
    }

    public static HAL_ChestPlan Plan(IReadOnlyList<HAL_PlannerLocation> locations, string seed, bool startingMaps, bool expanded,
        IReadOnlySet<string>? startingEquipment = null)
    {
        startingEquipment ??= new HashSet<string>(StringComparer.Ordinal);
        if (startingEquipment.Any(item => item is not ("Item_Sword_Kokiri" or "Item_Range_SlingShot" or "Item_BombBag_Small" or "Item_Boomerang" or "Item_GlassTruth")))
            throw new InvalidDataException("Unsupported starting equipment.");
        if (string.IsNullOrEmpty(seed) || seed.Length > 16 || !seed.All(Uri.IsHexDigit))
            throw new ArgumentException("Seed must contain 1–16 hexadecimal digits.");
        seed = seed.ToUpperInvariant().PadLeft(16, '0');
        if (locations.Select(row => row.Id).Distinct(StringComparer.Ordinal).Count() != locations.Count)
            throw new InvalidDataException("Duplicate chest identity.");
        var starting = new List<HAL_StartingDungeonItem>(); var freed = new HashSet<string>(StringComparer.Ordinal);
        var selected = new List<HAL_PlannerLocation>();
        foreach (var location in locations)
        {
            if (location.Resources.Count == 0) throw new InvalidDataException("Chest has no resource.");
            string expectedEvent = Region(location.Resources[0]) + (location.Reward == "Item_Dungeon_Map" ? "_GetMap" : "_GetCompass");
            bool reviewed = location.Event is null || location.Event == ChestEvent || location.Event == expectedEvent ||
                (location.Placement == Sword && location.Event == "Get_StoneOfForest_GetSword") ||
                (location.Placement == Slingshot && location.Event == "InsideTheDekuTree_GetSlingShot") ||
                HAL_ChestCatalog.ReviewedEquipmentEvent(location.Placement, location.Reward, location.Event);
            bool dungeonItem = location.Reward is "Item_Dungeon_Map" or "Item_Dungeon_Compass";
            var blockers = location.Exclusions.Where(reason => reason != "progression_or_unreviewed_reward" &&
                !(reviewed && reason == "unreviewed_special_event")).ToArray();
            bool equipment = startingEquipment.Contains(location.Reward);
            if (equipment && location.Reward is "Item_BombBag_Small" or "Item_Boomerang" or "Item_GlassTruth")
            {
                bool exact = location.Id == location.Placement.ToString("X16", System.Globalization.CultureInfo.InvariantCulture) &&
                    HAL_ChestCatalog.ReviewedEquipmentEvent(location.Placement, location.Reward, location.Event);
                if (!exact) throw new InvalidDataException("Starting experimental equipment requires its reviewed chest and event.");
            }
            if (equipment && (!reviewed || (location.Reward is "Item_BombBag_Small" or "Item_Boomerang" or "Item_GlassTruth") && !location.Eligible || (location.Reward is "Item_Sword_Kokiri" or "Item_Range_SlingShot" &&
                !((location.Reward == "Item_Sword_Kokiri" && location.Placement == Sword && location.Event == "Get_StoneOfForest_GetSword") ||
                  (location.Reward == "Item_Range_SlingShot" && location.Placement == Slingshot && location.Event == "InsideTheDekuTree_GetSlingShot")))))
                throw new InvalidDataException("Starting equipment requires its authored chest and event.");
            bool grant = (startingMaps && dungeonItem && HAL_StartingDungeonFlags.IsSupported(Region(location.Resources[0]), location.Reward) || equipment) && blockers.Length == 0;
            if ((startingMaps && dungeonItem && HAL_StartingDungeonFlags.IsSupported(Region(location.Resources[0]), location.Reward) || equipment) &&
                location.Exclusions.Contains("disabled_in_location_json"))
                throw new InvalidDataException("A starting item source is excluded. Include its check or turn off the starting item.");
            if (grant && !reviewed) throw new InvalidDataException("Starting dungeon item uses an unreviewed chest event.");
            if (grant)
            {
                var regions = location.Resources.Select(Region).Distinct(StringComparer.Ordinal).ToArray();
                if (regions.Length != 1) throw new InvalidDataException("Ambiguous dungeon identity for starting item.");
                starting.Add(new(regions[0], location.Reward, location.Id)); freed.Add(location.Id);
            }
            if (location.Eligible || grant) selected.Add(location);
        }
        foreach (string item in startingEquipment)
            if (starting.Count(row => row.Item == item) != 1)
                throw new InvalidDataException("Starting equipment requires exactly one reviewed source check.");
        // Resource names cannot contain NUL. The separator preserves tuple grouping
        // and ordinal ordering for the map-local mode.
        var groups = selected.GroupBy(row => expanded ? "" : string.Join('\0', row.Resources), StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal);
        var assignments = new List<HAL_PlannedChest>(); var fillers = new List<HAL_FillerItem>();
        foreach (var group in groups)
        {
            var targets = group.OrderBy(row => row.Id, StringComparer.Ordinal).ToArray();
            var rewards = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var row in targets)
            {
                if (!freed.Contains(row.Id)) { rewards.Add(row.Id, row.Reward); continue; }
                const string filler = "TBox_Rupee_Blue"; rewards.Add(row.Id, filler);
                fillers.Add(new(row.Id, row.Reward, filler));
            }
            var pool = targets.Select(row => new HAL_ChestPoolEntry(row.Id, row.Placement, rewards[row.Id], row.Resources));
            var excludedChecks = targets.Where(row => row.ProgressionExcluded).Select(row => row.Id).ToHashSet(StringComparer.Ordinal);
            var donors = HAL_ChestPermutation.Assign(pool, seed, expanded, excludedChecks).ToDictionary(row => row.TargetId, row => row.DonorId);
            foreach (var target in targets)
            {
                string donor = donors[target.Id], reward = rewards[donor];
                bool normalize = (target.Placement == Sword && reward != "Item_Sword_Kokiri") ||
                    (expanded && target.Placement == Slingshot && reward != "Item_Range_SlingShot") ||
                    (HAL_OpeningAccess.DekuLobbyMap(target.Placement, target.Resources) &&
                     target.Reward == "Item_Dungeon_Map" && reward == "Item_Range_SlingShot");
                assignments.Add(new(target.Id, target.Placement, target.Actor, target.Resources.ToArray(), target.Reward,
                    target.Event, donor, reward, normalize || (freed.Contains(target.Id) && (target.Reward is "Item_Dungeon_Map" or "Item_Dungeon_Compass")) ? null : target.Event, target.ProgressionExcluded));
            }
        }
        if (assignments.Select(row => row.Id).ToHashSet(StringComparer.Ordinal).Count != assignments.Count ||
            !assignments.Select(row => row.Id).ToHashSet(StringComparer.Ordinal).SetEquals(assignments.Select(row => row.DonorId)) ||
            assignments.Select(row => row.DonorId).Distinct(StringComparer.Ordinal).Count() != assignments.Count)
            throw new InvalidDataException("Chest reward instances were not assigned exactly once.");
        var ledger = new Dictionary<string, int>(StringComparer.Ordinal);
        void Count(string item, int delta) => ledger[item] = ledger.GetValueOrDefault(item) + delta;
        foreach (var row in assignments) { Count(row.ExpectedReward, 1); Count(row.Reward, -1); }
        foreach (var row in fillers) Count(row.Added, 1);
        foreach (var row in starting) Count(row.Item, -1);
        if (ledger.Values.Any(value => value != 0) || !freed.SetEquals(fillers.Select(row => row.SourceCheck)) ||
            !freed.SetEquals(starting.Select(row => row.SourceCheck)))
            throw new InvalidDataException("Reward conservation failed across chests and starting inventory.");
        HAL_StartingDungeonFlags.Resolve(starting.Where(row => row.Item is "Item_Dungeon_Map" or "Item_Dungeon_Compass")
            .Select(row => (row.Dungeon, row.Item)), false);
        var audit = HAL_OpeningAudit.Check(locations.Select(row => new HAL_OpeningCheck(row.Id, row.Placement, row.Reward, row.Resources)),
            assignments.ToDictionary(row => row.Id, row => row.Reward, StringComparer.Ordinal), startingEquipment);
        return new(assignments.OrderBy(row => row.Id, StringComparer.Ordinal).ToArray(),
            starting.OrderBy(row => row.Dungeon, StringComparer.Ordinal).ThenBy(row => row.Item, StringComparer.Ordinal)
                .ThenBy(row => row.SourceCheck, StringComparer.Ordinal).ToArray(), fillers.ToArray(), audit);
    }
}
