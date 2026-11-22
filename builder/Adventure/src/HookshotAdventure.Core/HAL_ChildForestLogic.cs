namespace HookshotAdventure.Core;

/// <summary>Reviewed child-forest connections and event checks. This is not a full-game world.</summary>
public static class HAL_ChildForestLogic
{
    public const string Forest = "KokiriForest", LostWoods = "LostWoods", Deku = "DekuSlingshotRoom", Bridge = "ForestBridge", Field = "HyruleField";
    public const string DekuLobby = "DekuLobby";
    public const string DekuBasement = "DekuBasement", DekuBackRoom = "DekuBasementBackRoom", DekuLedge = "DekuBasementLedge";
    public const string DekuBeforeBoss = "DekuBeforeBoss", GohmaRoom = "QueenGohmaRoom";
    public const string BeyondMido = "LostWoodsBeyondMido", MeadowEntry = "SacredForestMeadowEntry", Meadow = "SacredForestMeadow";
    public const string Kakariko = "KakarikoVillage", SkullHouse = "SkulltulaHouse";
    public const string StormsGrotto = "KokiriStormsGrotto";
    public const string MarketEntrance = "MarketEntrance", ZoraRiverFront = "ZoraRiverFront", Ranch = "LonLonRanch", GerudoValley = "GerudoValley";
    public const string Market = "Market", MarketBackAlley = "MarketBackAlley", MarketGuardHouse = "MarketGuardHouse", TempleEntrance = "TempleOfTimeEntrance", Temple = "TempleOfTime";
    public const string LakeHylia = "LakeHylia", LakeLab = "LakeHyliaLab", LakeFishingIsland = "LakeFishingIsland", ZoraDomain = "ZoraDomain";
    public const string ZoraRiver = "ZoraRiver", ZoraWaterfall = "ZoraRiverWaterfall";
    public const string Sword = "Item_Sword_Kokiri", Slingshot = "Item_Range_SlingShot", Shield = "TBox_Shield_Deku";
    public const string DefeatGohma = "ability:defeat_gohma", ClearedDeku = "event:deku_tree_clear";

    /// <summary>
    /// The caller supplies actual chest/event rewards and any assumed capabilities.
    /// DefeatGohma must be established by dungeon logic; merely entering the tree
    /// or finding the slingshot does not grant it.
    /// </summary>
    public static HAL_WorldReachability.World Build(IReadOnlyList<HAL_WorldReachability.Location> checks,
        bool openForest = false, bool openDeku = false)
    {
        var child = HAL_WorldReachability.Ages.Child;
        HAL_WorldReachability.Rule Free() => new([], Age: child);
        HAL_WorldReachability.Rule Require(params string[] capabilities) =>
            new([capabilities.Select(capability => new HAL_WorldReachability.Requirement(capability)).ToArray()], Age: child);
        var connections = new List<HAL_WorldReachability.Connection>
        {
            new(Forest, LostWoods, Free()), new(LostWoods, Forest, Free()),
            new(Forest, DekuLobby, openDeku ? Free() : Require("event:mido_passed")),
            new(DekuLobby, Forest, Free()),
            new(DekuLobby, Deku, new([[new(Shield)], [new(Slingshot)], [new("Item_Boomerang")]], Age: child)),
            new(Deku, DekuLobby, Free()),
            new(DekuLobby, DekuBasement, new([[new(Sword)], [new("ability:child_attack")]], Age: child)),
            new(DekuBasement, DekuBackRoom, new([
                [new("ability:light_torch"), new(Slingshot)], [new("ability:light_torch"), new("Item_Boomerang")]], Age: child)),
            new(DekuBackRoom, DekuLedge, Free()), new(DekuLedge, DekuBackRoom, Free()),
            new(DekuLedge, DekuBasement, Free()),
            new(DekuLedge, DekuBeforeBoss, Require("ability:light_torch")),
            new(DekuBeforeBoss, GohmaRoom, new([[new(Shield)], [new(Slingshot)], [new("Item_Boomerang")]], Age: child)),
            new(GohmaRoom, DekuBeforeBoss, Free()),
            new(Forest, Bridge, openForest ? Free() : Require(ClearedDeku)),
            new(Bridge, Forest, Free()), new(Bridge, Field, Free()), new(Field, Bridge, Free()),
            new(LostWoods, BeyondMido, Free()), new(BeyondMido, LostWoods, Free()),
            new(BeyondMido, MeadowEntry, Free()), new(MeadowEntry, BeyondMido, Free()),
            new(MeadowEntry, Meadow, Require("event:meadow_gate_open")), new(Meadow, MeadowEntry, Free()),
            new(Field, Kakariko, Free()), new(Kakariko, Field, Free()),
            new(Kakariko, SkullHouse, Free()), new(SkullHouse, Kakariko, Free()),
            new(Field, MarketEntrance, Free()), new(MarketEntrance, Field, new([], Age: child, Time: HAL_WorldReachability.Times.Day)),
            new(MarketEntrance, Market, Free()), new(Market, MarketEntrance, Free()),
            new(MarketEntrance, MarketGuardHouse, Free()), new(MarketGuardHouse, MarketEntrance, Free()),
            new(Market, MarketBackAlley, Free()), new(MarketBackAlley, Market, Free()),
            new(Market, TempleEntrance, Free()), new(TempleEntrance, Market, Free()),
            new(TempleEntrance, Temple, Free()), new(Temple, TempleEntrance, Free()),
            new(Field, ZoraRiverFront, Free()), new(ZoraRiverFront, Field, Free()),
            new(Field, Ranch, Free()), new(Ranch, Field, Free()),
            new(Field, GerudoValley, Free()), new(GerudoValley, Field, Free()),
            new(Field, LakeHylia, Free()), new(LakeHylia, Field, Free()),
            new(LakeHylia, LakeLab, Free()), new(LakeLab, LakeHylia, Free()),
            new(LakeHylia, LakeFishingIsland, Free()), new(LakeFishingIsland, LakeHylia, Free())
        };
        // The child underwater passage requires a scale in both directions.
        var dive = new HAL_WorldReachability.Rule([[new("Item_Scale_Silver")], [new("Item_Scale_Gold")]], Age: child);
        connections.Add(new(LakeHylia, ZoraDomain, dive));
        connections.Add(new(ZoraDomain, LakeHylia, dive));
        // Until consumable access is modeled, only an explicitly established
        // blasting capability opens the river's front obstacle.
        connections.Add(new(ZoraRiverFront, ZoraRiver, Require("ability:blast_obstacle")));
        connections.Add(new(ZoraRiver, ZoraRiverFront, Free()));
        connections.Add(new(ZoraRiver, ZoraWaterfall, new([
            [new("Item_Ocarina_Fairy"), new("Item_Melody_ZeldaLullaby")],
            [new("Item_Ocarina_Time"), new("Item_Melody_ZeldaLullaby")]], Age: child)));
        connections.Add(new(ZoraWaterfall, ZoraRiver, Free()));
        connections.Add(new(ZoraWaterfall, ZoraDomain, Free()));
        connections.Add(new(ZoraDomain, ZoraWaterfall, Free()));
        // Leaving the forest through the Lost Woods shortcut also observes the
        // forest gate. The return passage only requires the ability to dive.
        var forestShortcut = openForest ? dive : new HAL_WorldReachability.Rule([
            [new("Item_Scale_Silver"), new(ClearedDeku)],
            [new("Item_Scale_Gold"), new(ClearedDeku)]], Age: child);
        connections.Add(new(LostWoods, ZoraRiver, forestShortcut));
        connections.Add(new(ZoraRiver, LostWoods, dive));
        connections.Add(new(Forest, StormsGrotto, new([
            [new("Item_Ocarina_Fairy"), new("Item_Melody_StormSong")],
            [new("Item_Ocarina_Time"), new("Item_Melody_StormSong")]], Age: child)));
        connections.Add(new(StormsGrotto, Forest, Free()));
        var events = new[]
        {
            new HAL_WorldReachability.Event("mido_passed", Forest, Require(Sword, Shield)),
            new HAL_WorldReachability.Event("gohma_defeated", GohmaRoom, Require(Sword, Slingshot)),
            new HAL_WorldReachability.Event("deku_tree_clear", DekuLobby,
                new([[new(DefeatGohma)], [new("event:gohma_defeated")]], Age: child)),
            new HAL_WorldReachability.Event("meadow_gate_open", MeadowEntry,
                new([[new(Sword)], [new("ability:defeat_wolfos")]], Age: child))
        };
        return new HAL_WorldReachability.World([Forest, LostWoods, DekuLobby, Deku, DekuBasement, DekuBackRoom, DekuLedge, DekuBeforeBoss, GohmaRoom, Bridge, Field, BeyondMido, MeadowEntry, Meadow, Kakariko, SkullHouse,
                MarketEntrance, Market, MarketBackAlley, MarketGuardHouse, TempleEntrance, Temple, ZoraRiverFront, ZoraRiver, ZoraWaterfall, Ranch, GerudoValley, StormsGrotto,
                LakeHylia, LakeLab, LakeFishingIsland, ZoraDomain],
            [new(Forest, child)], connections, events, checks,
            new(Forest, Require(Sword, Slingshot)), new HashSet<string>(StringComparer.Ordinal) { Field });
    }

    public static HAL_WorldReachability.Rule EventAccess(string id) => id switch
    {
        HAL_EventRewardPlan.CheckId => new([[new(Slingshot)]], Age: HAL_WorldReachability.Ages.Child),
        HAL_EventRewardPlan.SariaCheckId => new([], Age: HAL_WorldReachability.Ages.Child),
        _ => throw new InvalidDataException("Unknown child-forest event check.")
    };

    public static HAL_WorldReachability.Rule SkulltulaAccess(int tokens)
    {
        if (HAL_SkulltulaRewards.Checks.All(check => check.Tokens != tokens))
            throw new InvalidDataException("Unknown one-time Skulltula reward threshold.");
        return new([], Tokens: tokens, Age: HAL_WorldReachability.Ages.Child);
    }
}
