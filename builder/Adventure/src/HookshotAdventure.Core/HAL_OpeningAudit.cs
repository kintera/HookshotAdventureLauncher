namespace HookshotAdventure.Core;

public sealed record HAL_OpeningCheck(string Id, ulong Placement, string? Reward, IReadOnlyList<string> Resources);
public sealed record HAL_OpeningAuditResult(string Status, bool FullBeatability, string? SwordCheck = null,
    string? SlingshotCheck = null, string? SlingshotRoute = null);

/// <summary>Checks only the reviewed sword and slingshot opening route.</summary>
public static class HAL_OpeningAudit
{
    private const ulong Sword = 16757992849254467386, Slingshot = 821372075822700622;

    public static HAL_OpeningAuditResult Check(IEnumerable<HAL_OpeningCheck> checks,
        IReadOnlyDictionary<string, string> assignedRewards, IReadOnlySet<string>? startingEquipment = null)
    {
        var locations = checks.ToArray();
        var sword = locations.FirstOrDefault(row => row.Placement == Sword);
        var slingshotLocation = locations.FirstOrDefault(row => row.Placement == Slingshot);
        if (sword is null || slingshotLocation is null) return new("not_applicable", false);
        string? Reward(HAL_OpeningCheck row) => assignedRewards.TryGetValue(row.Id, out var reward) ? reward : row.Reward;
        bool startingSword = startingEquipment?.Contains("Item_Sword_Kokiri") == true;
        bool startingSlingshot = startingEquipment?.Contains("Item_Range_SlingShot") == true;
        if (startingSword && locations.Any(row => Reward(row) == "Item_Sword_Kokiri"))
            throw new InvalidDataException("Starting sword remains in the chest pool.");
        var swords = locations.Where(row => Reward(row) == "Item_Sword_Kokiri").ToArray();
        if (!startingSword && (swords.Length != 1 || !HAL_OpeningAccess.ForestBeforeDeku(swords[0].Placement, swords[0].Resources)))
            throw new InvalidDataException("Opening progression requires one sword check in Kokiri Forest.");
        var slingshots = locations.Where(row => Reward(row) == "Item_Range_SlingShot").ToArray();
        if (startingSlingshot)
        {
            if (slingshots.Length != 0) throw new InvalidDataException("Starting slingshot remains in the chest pool.");
            return new("opening_progression_verified", false, startingSword ? "starting_inventory" : swords[0].Id, "starting_inventory", "starting_inventory");
        }
        if (slingshots.Length != 1) throw new InvalidDataException("Opening progression requires exactly one slingshot check.");
        var location = slingshots[0];
        bool kokiri = HAL_OpeningAccess.ForestBeforeDeku(location.Placement, location.Resources);
        bool dekuLobby = HAL_OpeningAccess.DekuLobbyMap(location.Placement, location.Resources);
        bool deku = dekuLobby || location.Placement == Slingshot && location.Resources.Count > 0 && location.Resources.All(path => path.Contains("/InsideTheDekuTree/", StringComparison.Ordinal));
        if (!kokiri && !deku) throw new InvalidDataException("Slingshot must be in Kokiri Forest or the Deku Tree.");
        return new("opening_progression_verified", false, startingSword ? "starting_inventory" : swords[0].Id, location.Id,
            kokiri ? "KokiriForest" : dekuLobby ? "Deku_lobby_map_chest" : "authored_Deku_slingshot_chest");
    }
}
