namespace HookshotAdventure.Core;

/// <summary>Conservative token identities checked against the selected local game maps.</summary>
public static class HAL_ReviewedSkulltulas
{
    public const string KokiriChildMap = "Banc/KokiriForest/KokiriForest_Child_Static.bcett.byml.zs";
    public const string KokiriAdultMap = "Banc/KokiriForest/KokiriForest_Adult_Static.bcett.byml.zs";
    public const string MarketChildMap = "Banc/Market/Market_Child_Static.bcett.byml.zs";
    public const string FieldChildMap = "Banc/HyruleField/HyruleField_Child_Static.bcett.byml.zs";
    public const string RiverChildMap = "Banc/ZoraRiver/ZoraRiver_Child_Static.bcett.byml.zs";
    public const ulong KnowItAllPlacement = 0xECDC46B409ED7D38UL;
    public const string KnowItAllId = "token:kokiri_know_it_all_house";
    public const ulong BeanPatchPlacement = 0x858F5000AD57B2CCUL;
    public const string BeanPatchId = "token:kokiri_bean_patch";
    public const ulong TwinsPlacement = 0x62C340F0B876C195UL;
    public const string TwinsId = "token:kokiri_house_of_twins";
    public const ulong GuardHousePlacement = 0x7C1A05315F83C34DUL;
    public const string GuardHouseId = "token:market_guard_house";
    public const ulong LakeBeanPlacement = 0x61576BEFD16C499AUL, RiverLadderPlacement = 0x78AE4FD90B3CFAAAUL;
    public const string LakeBeanId = "token:lake_bean_patch", RiverLadderId = "token:river_ladder";

    public static IReadOnlyList<HAL_WorldReachability.Location> FromActors(string resource, IEnumerable<(ulong Id, string Actor)> actors)
    {
        if (resource is not (KokiriChildMap or KokiriAdultMap or MarketChildMap or FieldChildMap or RiverChildMap)) return [];
        var matches = actors.Where(actor => resource switch {
            KokiriChildMap => actor.Id is KnowItAllPlacement or BeanPatchPlacement,
            KokiriAdultMap => actor.Id == TwinsPlacement,
            MarketChildMap => actor.Id == GuardHousePlacement,
            FieldChildMap => actor.Id == LakeBeanPlacement,
            RiverChildMap => actor.Id == RiverLadderPlacement,
            _ => false }).ToArray();
        if (matches.GroupBy(actor => actor.Id).Any(group => group.Count() != 1) ||
            matches.Any(actor => actor.Actor != "Enemy_Skulltula_Gold"))
            throw new InvalidDataException("Kokiri Skulltula identity conflicts with the reviewed map.");
        // The sword is a conservative known close-range method. Other attacks
        // can be added when their token-collection reach is reviewed.
        var locations = new List<HAL_WorldReachability.Location>();
        if (matches.Any(actor => actor.Id == KnowItAllPlacement))
            locations.Add(new(KnowItAllId, HAL_ChildForestLogic.Forest,
                new([[new(HAL_ChildForestLogic.Sword)]], Age: HAL_WorldReachability.Ages.Child,
                    Time: HAL_WorldReachability.Times.Night), Token: true));
        if (matches.Any(actor => actor.Id == BeanPatchPlacement))
            locations.Add(new(BeanPatchId, HAL_ChildForestLogic.Forest,
                new([[new(HAL_ChildForestLogic.Sword), new("ability:release_bugs")]], Age: HAL_WorldReachability.Ages.Child), Token: true));
        if (matches.Any(actor => actor.Id == TwinsPlacement))
            locations.Add(new(TwinsId, HAL_ChildForestLogic.Forest,
                new([[new("Item_Hookshot")], [new("Item_Longshot")]], Age: HAL_WorldReachability.Ages.Adult,
                    Time: HAL_WorldReachability.Times.Night), Token: true));
        if (matches.Any(actor => actor.Id == GuardHousePlacement))
            locations.Add(new(GuardHouseId, HAL_ChildForestLogic.MarketGuardHouse,
                new([[new(HAL_ChildForestLogic.Sword), new("ability:break_crate")]], Age: HAL_WorldReachability.Ages.Child), Token: true));
        if (matches.Any(actor => actor.Id == LakeBeanPlacement))
            locations.Add(new(LakeBeanId, HAL_ChildForestLogic.LakeHylia,
                new([[new(HAL_ChildForestLogic.Sword), new("ability:release_bugs")]], Age: HAL_WorldReachability.Ages.Child), Token: true));
        if (matches.Any(actor => actor.Id == RiverLadderPlacement))
            locations.Add(new(RiverLadderId, HAL_ChildForestLogic.ZoraRiver,
                new([[new(HAL_ChildForestLogic.Sword)]], Age: HAL_WorldReachability.Ages.Child,
                    Time: HAL_WorldReachability.Times.Night), Token: true));
        return locations;
    }

    public static IReadOnlyList<HAL_WorldReachability.Location> Scan(string resource, byte[] data)
    {
        if (resource is not (KokiriChildMap or KokiriAdultMap or MarketChildMap or FieldChildMap or RiverChildMap)) return [];
        var b = new HAL_Byml(data); var root = b.Fields(b.Root);
        if (!root.TryGetValue("Actors", out var actors)) return [];
        var result = new List<(ulong Id, string Actor)>();
        foreach (var field in b.Array(actors))
        {
            var actor = b.Fields(checked((int)b.Child(field, 0xc1)));
            if (!actor.TryGetValue("Hash", out var hash) || !actor.TryGetValue("Gyaml", out var type)) continue;
            ulong id = b.UInt(checked((int)b.Child(hash, 0xd5)), 8);
            if (id is KnowItAllPlacement or BeanPatchPlacement or TwinsPlacement or GuardHousePlacement or LakeBeanPlacement or RiverLadderPlacement) result.Add((id, b.Text(type)));
        }
        return FromActors(resource, result);
    }
}
