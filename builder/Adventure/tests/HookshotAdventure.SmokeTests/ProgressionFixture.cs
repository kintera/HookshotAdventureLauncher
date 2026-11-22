using HookshotAdventure.Core;
using System.Text.Json;

static class ProgressionFixture
{
    private static HAL_ProgressionPlacement.Access Access(int tokens=0, params (string Item,int Count)[] required) =>
        new(required.Length==0 ? [] : [required.Select(x=>new HAL_ProgressionPlacement.Requirement(x.Item,x.Count)).ToArray()],tokens);
    public static void Check()
    {
        var checks = new HAL_ProgressionPlacement.Check[] {
            new("start",Access(),true), new("locked",Access(0,("sword",1)),true),
            new("skull-2",Access(2),true,SkulltulaRewardThreshold:2),
            new("token-a",Access(0,("sword",1)),false,GrantsToken:true),
            new("token-b",Access(0,("sword",1)),false,GrantsToken:true) };
        var pool = new[] { new HAL_ProgressionPlacement.Item("sword-id","sword",true),
            new HAL_ProgressionPlacement.Item("key-id","key",true), new HAL_ProgressionPlacement.Item("filler-id","rupee",false) };
        var goal=Access(0,("sword",1),("key",1)); var start=new Dictionary<string,int>();
        var result=HAL_ProgressionPlacement.Place(checks,pool,start,goal,"1",2);
        var again=HAL_ProgressionPlacement.Place(checks,pool,start,goal,"1",2);
        AssertTest.True(result.Placements.Count==3 && result.Placements.Values.Distinct().Count()==3 &&
            result.Placements.OrderBy(x=>x.Key).SequenceEqual(again.Placements.OrderBy(x=>x.Key)) && result.Placements["start"]=="sword-id",
            "progression placement is deterministic, conserves rewards and avoids a self-locked sword");
        var capChecks = new HAL_ProgressionPlacement.Check[] {
            new("skull-2",Access(2),true,SkulltulaRewardThreshold:2),
            new("token-a",Access(),false,GrantsToken:true), new("token-b",Access(),false,GrantsToken:true) };
        bool rejected=false;
        try { HAL_ProgressionPlacement.Place(capChecks,[new("key-id","key",true)],start,Access(0,("key",1)),"2",1); }
        catch(InvalidDataException) { rejected=true; }
        AssertTest.True(rejected,"progression cannot require more tokens than the configured maximum");
        var indirect = new HAL_ProgressionPlacement.Check[] {
            new("start",Access(),true), new("late",Access(2),true),
            new("token-a",Access(),false,GrantsToken:true), new("token-b",Access(),false,GrantsToken:true) };
        rejected=false;
        try { HAL_ProgressionPlacement.Place(indirect,[new("key-1","key",true),new("key-2","key",true)],start,Access(0,("key",2)),"3",1); }
        catch(InvalidDataException) { rejected=true; }
        AssertTest.True(rejected,"maximum token count also covers indirect progression requirements");
        var optionalChecks = new HAL_ProgressionPlacement.Check[] {
            new("start",Access(),true), new("skull-2",Access(2),true,SkulltulaRewardThreshold:2),
            new("token-a",Access(),false,GrantsToken:true), new("token-b",Access(),false,GrantsToken:true) };
        var optionalPool = new[] { new HAL_ProgressionPlacement.Item("required","sword",true),
            new HAL_ProgressionPlacement.Item("optional","optional-upgrade",true) };
        var optionalResult=HAL_ProgressionPlacement.Place(optionalChecks,optionalPool,start,Access(0,("sword",1)),"5",1);
        AssertTest.True(optionalResult.Placements["start"]=="required" && optionalResult.Placements["skull-2"]=="optional",
            "progression that is not needed for the goal can appear above the Skulltula maximum");
        AssertTest.True(HAL_ProgressionPlacement.IsGoalReachable(optionalChecks,optionalPool,start,Access(0,("sword",1)),1,optionalResult.Placements) &&
            !HAL_ProgressionPlacement.IsGoalReachable(optionalChecks,optionalPool,start,Access(0,("sword",1)),1,
                new Dictionary<string,string>{{"start","optional"},{"skull-2","required"}}) &&
            HAL_ProgressionPlacement.IsGoalReachable(optionalChecks,optionalPool,start,Access(0,("sword",1)),2,
                new Dictionary<string,string>{{"start","optional"},{"skull-2","required"}}),
            "goal validation permits optional progression above the cap but rejects a required item until enough tokens are allowed");
        bool duplicatePlacement=false;try{HAL_ProgressionPlacement.IsGoalReachable(optionalChecks,optionalPool,start,Access(),1,
            new Dictionary<string,string>{{"start","required"},{"skull-2","required"}});}catch(InvalidDataException){duplicatePlacement=true;}
        AssertTest.True(duplicatePlacement,"goal validation rejects duplicate item instances");
        var goalAlreadyMet=HAL_ProgressionPlacement.Place(optionalChecks,optionalPool,new Dictionary<string,int>{{"sword",1}},
            Access(0,("sword",1)),"6",0);
        AssertTest.True(goalAlreadyMet.Placements.Count==2 && goalAlreadyMet.Placements.Values.Distinct().Count()==2,
            "an already reachable goal still assigns every remaining progression item exactly once");
        var alternatives = new HAL_ProgressionPlacement.Access([[new("sword")],[new("bow")]]);
        var world = new[] { new HAL_ProgressionPlacement.Check("item",alternatives,true) };
        var viaBow=HAL_ProgressionPlacement.Place(world,[new("key","key",true)],new Dictionary<string,int>{{"bow",1}},Access(0,("key",1)),"4",0);
        AssertTest.True(viaBow.Placements["item"]=="key","access rules support alternative item paths and starting items");
        const string seedPlan="""
            {"opening_audit":{"status":"opening_progression_verified"},"assignments":[
              {"id":"forest","placement":1,"resources":["Banc/KokiriForest/map"],"reward":"Item_Sword_Kokiri"},
              {"id":"deku","placement":821372075822700622,"resources":["Banc/InsideTheDekuTree/map"],"reward":"Item_Range_SlingShot"}]}
            """;
        using var good=JsonDocument.Parse(seedPlan);
        HAL_SeedOpeningSolver.Validate(good.RootElement,false,false);
        AssertTest.True(true,"seed opening solver accepts sword in Kokiri and slingshot in its Deku chest");
        using var lobbySling = JsonDocument.Parse(seedPlan.Replace("821372075822700622", "8579518566955184384", StringComparison.Ordinal)
            .Replace("Banc/InsideTheDekuTree/map", "Banc/InsideTheDekuTree/InsideTheDekuTree_Static.bcett.byml.zs", StringComparison.Ordinal));
        HAL_SeedOpeningSolver.Validate(lobbySling.RootElement, false, false);
        AssertTest.True(true, "opening solver accepts the slingshot in the reviewed Deku lobby map chest");
        using var wrongLobby = JsonDocument.Parse(lobbySling.RootElement.GetRawText().Replace("InsideTheDekuTree_Static", "InsideTheDekuTree_Dynamic", StringComparison.Ordinal));
        bool wrongLobbyRejected = false;
        try { HAL_SeedOpeningSolver.Validate(wrongLobby.RootElement, false, false); } catch (InvalidDataException) { wrongLobbyRejected = true; }
        AssertTest.True(wrongLobbyRejected, "Deku lobby placement requires the reviewed map resource as well as the actor identity");
        using var locked=JsonDocument.Parse(seedPlan.Replace("Item_Sword_Kokiri","TEMP",StringComparison.Ordinal)
            .Replace("Item_Range_SlingShot","Item_Sword_Kokiri",StringComparison.Ordinal).Replace("TEMP","Item_Range_SlingShot",StringComparison.Ordinal));
        bool openingRejected=false;try{HAL_SeedOpeningSolver.Validate(locked.RootElement,false,false);}catch(InvalidDataException){openingRejected=true;}
        AssertTest.True(openingRejected,"seed opening solver rejects a sword locked behind entering Deku Tree");
        using var outside=JsonDocument.Parse(seedPlan.Replace("Banc/KokiriForest/map","Banc/HyruleField/map",StringComparison.Ordinal));
        bool outsideRejected=false;try{HAL_SeedOpeningSolver.Validate(outside.RootElement,false,false);}catch(InvalidDataException){outsideRejected=true;}
        AssertTest.True(outsideRejected,"unreviewed outside locations cannot satisfy the opening solver");
        using var starts=JsonDocument.Parse(seedPlan.Replace("Item_Sword_Kokiri","TBox_Rupee_Blue",StringComparison.Ordinal));
        HAL_SeedOpeningSolver.Validate(starts.RootElement,true,false);
        AssertTest.True(true,"starting sword is available to the seed opening solver");
        using var excludedPlan = JsonDocument.Parse("""{"opening_audit":{"status":"opening_progression_verified"},"assignments":[]}""");
        using var fixedCatalog = JsonDocument.Parse("""
            {"locations":[
              {"id":"forest","placement":1,"resources":["Banc/KokiriForest/map"],"reward":"Item_Sword_Kokiri"},
              {"id":"deku","placement":821372075822700622,"resources":["Banc/InsideTheDekuTree/map"],"reward":"Item_Range_SlingShot"}]}
            """);
        HAL_SeedOpeningSolver.Validate(excludedPlan.RootElement, false, false, fixedCatalog.RootElement);
        AssertTest.True(true, "opening solver counts excluded vanilla sword and slingshot checks");
        using var invalidFixed = JsonDocument.Parse(fixedCatalog.RootElement.GetRawText().Replace("Banc/KokiriForest/map", "Banc/HyruleField/map", StringComparison.Ordinal));
        bool invalidExcluded = false;
        try { HAL_SeedOpeningSolver.Validate(excludedPlan.RootElement, false, false, invalidFixed.RootElement); }
        catch (InvalidDataException) { invalidExcluded = true; }
        AssertTest.True(invalidExcluded, "excluded sword outside the reviewed opening cannot satisfy the opening solver");
        var childChecks = new[] {
            new HAL_WorldReachability.Location("forest-sword", HAL_ChildForestLogic.Forest, new([]), HAL_ChildForestLogic.Sword),
            new HAL_WorldReachability.Location("tree-sling", HAL_ChildForestLogic.Deku, new([]), HAL_ChildForestLogic.Slingshot),
            new HAL_WorldReachability.Location(HAL_EventRewardPlan.CheckId, HAL_ChildForestLogic.LostWoods,
                HAL_ChildForestLogic.EventAccess(HAL_EventRewardPlan.CheckId), "target_reward"),
            new HAL_WorldReachability.Location(HAL_EventRewardPlan.SariaCheckId, HAL_ChildForestLogic.Bridge,
                HAL_ChildForestLogic.EventAccess(HAL_EventRewardPlan.SariaCheckId), "saria_reward") };
        var forestWorld = HAL_ChildForestLogic.Build(childChecks);
        var noShield = HAL_WorldReachability.Evaluate(forestWorld, new Dictionary<string,int>(), 0);
        AssertTest.True(!noShield.GoalReached && !noShield.Collected.Contains("tree-sling") &&
            !noShield.Collected.Contains(HAL_EventRewardPlan.CheckId) && !noShield.Collected.Contains(HAL_EventRewardPlan.SariaCheckId),
            "closed forest keeps Mido, the target and bridge gift gated without their prerequisites");
        var shield = new Dictionary<string,int> { [HAL_ChildForestLogic.Shield] = 1 };
        var openedTree = HAL_WorldReachability.Evaluate(forestWorld, shield, 0);
        AssertTest.True(openedTree.GoalReached && openedTree.Events.Contains("mido_passed") &&
            openedTree.Collected.Contains(HAL_EventRewardPlan.CheckId) && !openedTree.Collected.Contains(HAL_EventRewardPlan.SariaCheckId) &&
            openedTree.Reachable.Contains((HAL_ChildForestLogic.Meadow, HAL_WorldReachability.Ages.Child, HAL_WorldReachability.Times.Day)),
            "sword and shield open Mido, slingshot opens the target, and entering Deku alone does not open the forest");
        AssertTest.True(!HAL_WorldReachability.Evaluate(HAL_ChildForestLogic.Build(Array.Empty<HAL_WorldReachability.Location>()),
                new Dictionary<string,int>(), 0).Reachable.Contains((HAL_ChildForestLogic.Meadow, HAL_WorldReachability.Ages.Child, HAL_WorldReachability.Times.Day)) &&
            HAL_WorldReachability.Evaluate(HAL_ChildForestLogic.Build(Array.Empty<HAL_WorldReachability.Location>()),
                new Dictionary<string,int> { ["ability:defeat_wolfos"] = 1 }, 0).Events.Contains("meadow_gate_open"),
            "Sacred Forest Meadow stays behind the Wolfos gate unless a combat capability opens it");
        var defeated = HAL_WorldReachability.Evaluate(forestWorld,
            new Dictionary<string,int>(shield) { [HAL_ChildForestLogic.DefeatGohma] = 1 }, 0);
        AssertTest.True(defeated.Events.Contains("deku_tree_clear") && defeated.Collected.Contains(HAL_EventRewardPlan.SariaCheckId) &&
            defeated.Reachable.Contains((HAL_ChildForestLogic.Field, HAL_WorldReachability.Ages.Child, HAL_WorldReachability.Times.Night)),
            "a separately established Gohma defeat opens Saria's bridge and day/night access to Hyrule Field");
        var open = HAL_WorldReachability.Evaluate(HAL_ChildForestLogic.Build(childChecks, openForest:true, openDeku:true),
            new Dictionary<string,int>(shield), 0);
        AssertTest.True(open.GoalReached && open.Collected.Contains(HAL_EventRewardPlan.SariaCheckId),
            "open forest and open Deku rules independently bypass their corresponding gates");
        var openTree = HAL_ChildForestLogic.Build([], openDeku: true);
        AssertTest.True(HAL_WorldReachability.Evaluate(openTree, new Dictionary<string,int>(), 0).Reachable.Contains(
                (HAL_ChildForestLogic.DekuLobby, HAL_WorldReachability.Ages.Child, HAL_WorldReachability.Times.Day)) &&
            !HAL_WorldReachability.Evaluate(openTree, new Dictionary<string,int>(), 0).Reachable.Contains(
                (HAL_ChildForestLogic.Deku, HAL_WorldReachability.Ages.Child, HAL_WorldReachability.Times.Day)),
            "open Deku opens the lobby but does not bypass the Slingshot Room scrub");
        foreach (string item in new[] { HAL_ChildForestLogic.Shield, HAL_ChildForestLogic.Slingshot, "Item_Boomerang" })
            AssertTest.True(HAL_WorldReachability.Evaluate(openTree, new Dictionary<string,int> { [item] = 1 }, 0).Reachable.Contains(
                    (HAL_ChildForestLogic.Deku, HAL_WorldReachability.Ages.Child, HAL_WorldReachability.Times.Day)),
                "a reviewed scrub interaction opens the Slingshot Room");
        var bossRoute = HAL_ChildForestLogic.Build([]);
        var bossItems = new Dictionary<string,int> { [HAL_ChildForestLogic.Sword] = 1, [HAL_ChildForestLogic.Shield] = 1,
            [HAL_ChildForestLogic.Slingshot] = 1 };
        AssertTest.True(!HAL_WorldReachability.Evaluate(bossRoute, bossItems, 0).Events.Contains("gohma_defeated") &&
            !HAL_WorldReachability.Evaluate(bossRoute, bossItems, 0).Events.Contains("deku_tree_clear"),
            "Deku basement does not infer fire or a Gohma defeat from sword and slingshot alone");
        bossItems["ability:light_torch"] = 1;
        var bossCleared = HAL_WorldReachability.Evaluate(bossRoute, bossItems, 0);
        AssertTest.True(bossCleared.Reachable.Contains((HAL_ChildForestLogic.GohmaRoom, HAL_WorldReachability.Ages.Child, HAL_WorldReachability.Times.Day)) &&
            bossCleared.Events.Contains("gohma_defeated") && bossCleared.Events.Contains("deku_tree_clear") &&
            bossCleared.Reachable.Contains((HAL_ChildForestLogic.Field, HAL_WorldReachability.Ages.Child, HAL_WorldReachability.Times.Day)),
            "a reviewed fire, projectile and sword route defeats Gohma and opens the closed-forest bridge");
        bossItems.Remove(HAL_ChildForestLogic.Slingshot);
        AssertTest.True(!HAL_WorldReachability.Evaluate(bossRoute, bossItems, 0).Events.Contains("gohma_defeated"),
            "Deku boss route does not infer a missing projectile or Gohma stun");
        AssertTest.True(new[] { HAL_ChildForestLogic.Kakariko, HAL_ChildForestLogic.MarketEntrance, HAL_ChildForestLogic.ZoraRiverFront,
            HAL_ChildForestLogic.Ranch, HAL_ChildForestLogic.GerudoValley, HAL_ChildForestLogic.LakeHylia }.All(region =>
                open.Reachable.Contains((region, HAL_WorldReachability.Ages.Child, HAL_WorldReachability.Times.Day)) &&
                !noShield.Reachable.Contains((region, HAL_WorldReachability.Ages.Child, HAL_WorldReachability.Times.Day))),
            "the field's child overworld exits become reachable only after forest access");
        AssertTest.True(new[] { HAL_ChildForestLogic.Market, HAL_ChildForestLogic.MarketBackAlley, HAL_ChildForestLogic.MarketGuardHouse,
                HAL_ChildForestLogic.TempleEntrance, HAL_ChildForestLogic.Temple }.All(region =>
                open.Reachable.Contains((region, HAL_WorldReachability.Ages.Child, HAL_WorldReachability.Times.Day))),
            "child Market connects to the back alley and the Temple of Time entrance");
        var nightMarket = HAL_ChildForestLogic.Build([]) with {
            Starts = [new(HAL_ChildForestLogic.Market, HAL_WorldReachability.Ages.Child, HAL_WorldReachability.Times.Night)] };
        var nightMarketResult = HAL_WorldReachability.Evaluate(nightMarket, new Dictionary<string,int>(), 0);
        AssertTest.True(nightMarketResult.Reachable.Contains((HAL_ChildForestLogic.Temple, HAL_WorldReachability.Ages.Child, HAL_WorldReachability.Times.Night)) &&
            !nightMarketResult.Reachable.Contains((HAL_ChildForestLogic.Field, HAL_WorldReachability.Ages.Child, HAL_WorldReachability.Times.Night)) &&
            !nightMarketResult.Reachable.Contains((HAL_ChildForestLogic.Market, HAL_WorldReachability.Ages.Child, HAL_WorldReachability.Times.Day)),
            "the child Market does not provide a night exit to the field or let time pass indoors");
        var guardToken = HAL_ReviewedSkulltulas.FromActors(HAL_ReviewedSkulltulas.MarketChildMap,
            [(HAL_ReviewedSkulltulas.GuardHousePlacement, "Enemy_Skulltula_Gold")]);
        var guardWorld = HAL_ChildForestLogic.Build(guardToken, openForest: true);
        AssertTest.True(guardToken.Count == 1 &&
            !HAL_WorldReachability.Evaluate(guardWorld, new Dictionary<string,int> { [HAL_ChildForestLogic.Sword] = 1 }, 1).Collected.Contains(HAL_ReviewedSkulltulas.GuardHouseId) &&
            HAL_WorldReachability.Evaluate(guardWorld, new Dictionary<string,int> { [HAL_ChildForestLogic.Sword] = 1, ["ability:break_crate"] = 1 }, 1).Collected.Contains(HAL_ReviewedSkulltulas.GuardHouseId) &&
            HAL_ReviewedSkulltulas.FromActors(HAL_ReviewedSkulltulas.KokiriChildMap, [(HAL_ReviewedSkulltulas.GuardHousePlacement, "Enemy_Skulltula_Gold")]).Count == 0,
            "Market guard-house token requires the reviewed map, crate access and a close-range attack");
        var lakeToken = HAL_ReviewedSkulltulas.FromActors(HAL_ReviewedSkulltulas.FieldChildMap,
            [(HAL_ReviewedSkulltulas.LakeBeanPlacement, "Enemy_Skulltula_Gold")]);
        var lakeWorld = HAL_ChildForestLogic.Build(lakeToken, openForest: true);
        AssertTest.True(lakeToken.Count == 1 &&
            !HAL_WorldReachability.Evaluate(lakeWorld, new Dictionary<string,int> { [HAL_ChildForestLogic.Sword] = 1 }, 1).Collected.Contains(HAL_ReviewedSkulltulas.LakeBeanId) &&
            HAL_WorldReachability.Evaluate(lakeWorld, new Dictionary<string,int> { [HAL_ChildForestLogic.Sword] = 1, ["ability:release_bugs"] = 1 }, 1).Collected.Contains(HAL_ReviewedSkulltulas.LakeBeanId),
            "Lake Hylia bean-patch token requires bugs and a child attack");
        var riverToken = HAL_ReviewedSkulltulas.FromActors(HAL_ReviewedSkulltulas.RiverChildMap,
            [(HAL_ReviewedSkulltulas.RiverLadderPlacement, "Enemy_Skulltula_Gold")]);
        var riverTokenWorld = HAL_ChildForestLogic.Build(riverToken, openForest: true);
        AssertTest.True(riverToken.Count == 1 &&
            !HAL_WorldReachability.Evaluate(riverTokenWorld, new Dictionary<string,int> { [HAL_ChildForestLogic.Sword] = 1 }, 1).Collected.Contains(HAL_ReviewedSkulltulas.RiverLadderId) &&
            HAL_WorldReachability.Evaluate(riverTokenWorld, new Dictionary<string,int> { [HAL_ChildForestLogic.Sword] = 1, ["ability:blast_obstacle"] = 1 }, 1).Collected.Contains(HAL_ReviewedSkulltulas.RiverLadderId),
            "Zora River ladder token requires the river route, night and a child attack");
        AssertTest.True(open.Reachable.Contains((HAL_ChildForestLogic.LakeLab, HAL_WorldReachability.Ages.Child, HAL_WorldReachability.Times.Day)) &&
            open.Reachable.Contains((HAL_ChildForestLogic.LakeFishingIsland, HAL_WorldReachability.Ages.Child, HAL_WorldReachability.Times.Day)) &&
            !open.Reachable.Contains((HAL_ChildForestLogic.ZoraDomain, HAL_WorldReachability.Ages.Child, HAL_WorldReachability.Times.Day)),
            "child Lake Hylia reaches the lab and fishing island but not the underwater passage without a scale");
        foreach (string scale in new[] { "Item_Scale_Silver", "Item_Scale_Gold" })
            AssertTest.True(HAL_WorldReachability.Evaluate(HAL_ChildForestLogic.Build(childChecks, openForest: true),
                    new Dictionary<string,int> { [scale] = 1 }, 0).Reachable.Contains(
                        (HAL_ChildForestLogic.ZoraDomain, HAL_WorldReachability.Ages.Child, HAL_WorldReachability.Times.Day)),
                "either scale permits the child Lake Hylia to Zora's Domain passage");
        var domainGraph = HAL_ChildForestLogic.Build([], openForest: false);
        var domainStart = domainGraph with {
            Starts = [new(HAL_ChildForestLogic.ZoraDomain, HAL_WorldReachability.Ages.Child)],
            Connections = domainGraph.Connections.Where(c => c.From != HAL_ChildForestLogic.Field || c.To != HAL_ChildForestLogic.LakeHylia).ToArray() };
        AssertTest.True(!HAL_WorldReachability.Evaluate(domainStart, new Dictionary<string,int>(), 0).Reachable.Contains(
                (HAL_ChildForestLogic.LakeHylia, HAL_WorldReachability.Ages.Child, HAL_WorldReachability.Times.Day)) &&
            HAL_WorldReachability.Evaluate(domainStart, new Dictionary<string,int> { ["Item_Scale_Silver"] = 1 }, 0).Reachable.Contains(
                (HAL_ChildForestLogic.LakeHylia, HAL_WorldReachability.Ages.Child, HAL_WorldReachability.Times.Day)),
            "the reverse underwater passage also requires a scale");
        var adultDomain = domainStart with { Starts = [new(HAL_ChildForestLogic.ZoraDomain, HAL_WorldReachability.Ages.Adult)] };
        AssertTest.True(!HAL_WorldReachability.Evaluate(adultDomain, new Dictionary<string,int> { ["Item_Scale_Gold"] = 1 }, 0).Reachable.Contains(
                (HAL_ChildForestLogic.LakeHylia, HAL_WorldReachability.Ages.Adult, HAL_WorldReachability.Times.Day)),
            "the reviewed underwater passage does not assume adult access");
        var riverGraph = HAL_ChildForestLogic.Build([], openForest: true);
        var blast = new Dictionary<string,int> { ["ability:blast_obstacle"] = 1 };
        AssertTest.True(!open.Reachable.Contains((HAL_ChildForestLogic.ZoraRiver, HAL_WorldReachability.Ages.Child, HAL_WorldReachability.Times.Day)) &&
            HAL_WorldReachability.Evaluate(riverGraph, blast, 0).Reachable.Contains(
                (HAL_ChildForestLogic.ZoraRiver, HAL_WorldReachability.Ages.Child, HAL_WorldReachability.Times.Day)) &&
            !HAL_WorldReachability.Evaluate(riverGraph, blast, 0).Reachable.Contains(
                (HAL_ChildForestLogic.ZoraDomain, HAL_WorldReachability.Ages.Child, HAL_WorldReachability.Times.Day)),
            "the front obstacle requires an established blasting capability and the waterfall remains closed without the song");
        blast["Item_Melody_ZeldaLullaby"] = 1;
        AssertTest.True(!HAL_WorldReachability.Evaluate(riverGraph, blast, 0).Reachable.Contains(
                (HAL_ChildForestLogic.ZoraDomain, HAL_WorldReachability.Ages.Child, HAL_WorldReachability.Times.Day)),
            "Zelda's Lullaby needs an ocarina to open the waterfall");
        blast["Item_Ocarina_Fairy"] = 1;
        AssertTest.True(HAL_WorldReachability.Evaluate(riverGraph, blast, 0).Reachable.Contains(
                (HAL_ChildForestLogic.ZoraDomain, HAL_WorldReachability.Ages.Child, HAL_WorldReachability.Times.Day)) &&
            HAL_WorldReachability.Evaluate(domainStart, new Dictionary<string,int>(), 0).Reachable.Contains(
                (HAL_ChildForestLogic.ZoraRiverFront, HAL_WorldReachability.Ages.Child, HAL_WorldReachability.Times.Day)),
            "the played lullaby opens the waterfall, and the exit back to the field does not need blasting");
        HAL_WorldReachability.World IsolateForestShortcut(bool forestOpen)
        {
            var graph = HAL_ChildForestLogic.Build([], openForest: forestOpen);
            return graph with { Connections = graph.Connections.Where(c => c.From != HAL_ChildForestLogic.Field ||
                c.To is not (HAL_ChildForestLogic.LakeHylia or HAL_ChildForestLogic.ZoraRiverFront)).ToArray() };
        }
        var silver = new Dictionary<string,int> { ["Item_Scale_Silver"] = 1 };
        AssertTest.True(!HAL_WorldReachability.Evaluate(IsolateForestShortcut(false), silver, 0).Reachable.Contains(
                (HAL_ChildForestLogic.ZoraRiver, HAL_WorldReachability.Ages.Child, HAL_WorldReachability.Times.Day)) &&
            HAL_WorldReachability.Evaluate(IsolateForestShortcut(true), silver, 0).Reachable.Contains(
                (HAL_ChildForestLogic.ZoraRiver, HAL_WorldReachability.Ages.Child, HAL_WorldReachability.Times.Day)),
            "the Lost Woods shortcut needs forest access as well as a scale");
        silver[HAL_ChildForestLogic.Sword] = 1; silver[HAL_ChildForestLogic.Shield] = 1;
        silver[HAL_ChildForestLogic.DefeatGohma] = 1;
        AssertTest.True(HAL_WorldReachability.Evaluate(IsolateForestShortcut(false), silver, 0).Reachable.Contains(
                (HAL_ChildForestLogic.ZoraRiver, HAL_WorldReachability.Ages.Child, HAL_WorldReachability.Times.Day)),
            "the reviewed Gohma-clear event permits the scaled forest shortcut in closed forest");
        var targetLoop = HAL_ChildForestLogic.Build(new[] {
            new HAL_WorldReachability.Location("forest-sword", HAL_ChildForestLogic.Forest, new([]), HAL_ChildForestLogic.Sword),
            new HAL_WorldReachability.Location(HAL_EventRewardPlan.CheckId, HAL_ChildForestLogic.LostWoods,
                HAL_ChildForestLogic.EventAccess(HAL_EventRewardPlan.CheckId), HAL_ChildForestLogic.Slingshot) });
        AssertTest.True(!HAL_WorldReachability.Evaluate(targetLoop, shield, 0).GoalReached,
            "slingshot cannot be justified by its own Lost Woods target check");
        using var targetLoopPlan = JsonDocument.Parse("""
            {"opening_audit":{"status":"opening_progression_verified"},"assignments":[
              {"id":"forest","placement":1,"resources":["Banc/KokiriForest/map"],"reward":"Item_Sword_Kokiri"}],
              "event_assignments":[{"id":"event:lost_woods_target_first_clear","reward":"Item_Range_SlingShot"}]}
            """);
        bool eventLoopRejected = false;
        try { HAL_SeedOpeningSolver.Validate(targetLoopPlan.RootElement, false, false); }
        catch (InvalidDataException) { eventLoopRejected = true; }
        AssertTest.True(eventLoopRejected, "final seed opening validation rejects a self-locked target reward");
        using var stormLockedPlan = JsonDocument.Parse("""
            {"opening_audit":{"status":"opening_progression_verified"},"assignments":[
              {"id":"storm","placement":14818846682364661178,"resources":["Banc/KokiriForest/map"],"reward":"Item_Sword_Kokiri"},
              {"id":"sling","placement":2,"resources":["Banc/KokiriForest/map"],"reward":"Item_Range_SlingShot"}]}
            """);
        // Use the numeric constant rather than relying on a decimal transcription.
        var stormJson = System.Text.Json.Nodes.JsonNode.Parse(stormLockedPlan.RootElement.GetRawText())!;
        stormJson["assignments"]![0]!["placement"] = HAL_OpeningAccess.StormsGrottoChest;
        bool stormRejected = false;
        try { HAL_SeedOpeningSolver.Validate(JsonSerializer.SerializeToElement(stormJson), false, false); }
        catch (InvalidDataException) { stormRejected = true; }
        AssertTest.True(stormRejected, "seed validation cannot reach the sword inside the inaccessible Storms grotto");
        var grotto = HAL_ChildForestLogic.Build(new[] { new HAL_WorldReachability.Location("grotto", HAL_ChildForestLogic.StormsGrotto, new([]), "grotto_item") });
        AssertTest.True(!HAL_WorldReachability.Evaluate(grotto, new Dictionary<string,int> { ["Item_Ocarina_Fairy"] = 1 }, 0).Collected.Contains("grotto") &&
            HAL_WorldReachability.Evaluate(grotto, new Dictionary<string,int> { ["Item_Ocarina_Fairy"] = 1, ["Item_Melody_StormSong"] = 1 }, 0).Collected.Contains("grotto"),
            "Storms grotto requires both an ocarina and Song of Storms");
        var tokenChecks = Enumerable.Range(0, 10).Select(i => new HAL_WorldReachability.Location("token-" + i,
            HAL_ChildForestLogic.Field, new([], Age: HAL_WorldReachability.Ages.Child), Token: true)).Append(
                new HAL_WorldReachability.Location("skull-10", HAL_ChildForestLogic.SkullHouse,
                    HAL_ChildForestLogic.SkulltulaAccess(10), "house_reward")).ToArray();
        var houseWorld = HAL_ChildForestLogic.Build(tokenChecks, openForest: true) with {
            Goal = new(HAL_ChildForestLogic.SkullHouse, new([[new("house_reward")]])) };
        AssertTest.True(!HAL_WorldReachability.Evaluate(houseWorld, new Dictionary<string,int>(), 9).GoalReached &&
            HAL_WorldReachability.Evaluate(houseWorld, new Dictionary<string,int>(), 10).GoalReached,
            "Skulltula House rewards respect their thresholds and the configured goal token cap");
        var knownToken = HAL_ReviewedSkulltulas.FromActors(HAL_ReviewedSkulltulas.KokiriChildMap,
            [(HAL_ReviewedSkulltulas.KnowItAllPlacement, "Enemy_Skulltula_Gold")]);
        var knownWorld = HAL_ChildForestLogic.Build(knownToken, openForest: true);
        AssertTest.True(knownToken.Count == 1 &&
            !HAL_WorldReachability.Evaluate(knownWorld, new Dictionary<string,int>(), 1).Collected.Contains(HAL_ReviewedSkulltulas.KnowItAllId) &&
            HAL_WorldReachability.Evaluate(knownWorld, new Dictionary<string,int> { [HAL_ChildForestLogic.Sword] = 1 }, 1).Tokens == 1 &&
            !HAL_WorldReachability.Evaluate(HAL_ChildForestLogic.Build(knownToken),
                new Dictionary<string,int> { [HAL_ChildForestLogic.Sword] = 1 }, 1).Collected.Contains(HAL_ReviewedSkulltulas.KnowItAllId),
            "reviewed Kokiri token needs night access and a close-range attack");
        bool conflict = false;
        try { HAL_ReviewedSkulltulas.FromActors(HAL_ReviewedSkulltulas.KokiriChildMap,
            [(HAL_ReviewedSkulltulas.KnowItAllPlacement, "DifferentActor")]); }
        catch (InvalidDataException) { conflict = true; }
        AssertTest.True(conflict && HAL_ReviewedSkulltulas.FromActors("Banc/other", [(HAL_ReviewedSkulltulas.KnowItAllPlacement, "Enemy_Skulltula_Gold")]).Count == 0,
            "token mapping rejects actor mismatches and does not reuse an identity from another map");
        var beanToken = HAL_ReviewedSkulltulas.FromActors(HAL_ReviewedSkulltulas.KokiriChildMap,
            [(HAL_ReviewedSkulltulas.BeanPatchPlacement, "Enemy_Skulltula_Gold")]);
        var beanWorld = HAL_ChildForestLogic.Build(beanToken);
        AssertTest.True(!HAL_WorldReachability.Evaluate(beanWorld, new Dictionary<string,int> { [HAL_ChildForestLogic.Sword] = 1 }, 1).Collected.Contains(HAL_ReviewedSkulltulas.BeanPatchId) &&
            HAL_WorldReachability.Evaluate(beanWorld, new Dictionary<string,int> { [HAL_ChildForestLogic.Sword] = 1, ["ability:release_bugs"] = 1 }, 1).Tokens == 1,
            "Kokiri bean patch token requires a way to release bugs and a close-range attack");
        var twinsToken = HAL_ReviewedSkulltulas.FromActors(HAL_ReviewedSkulltulas.KokiriAdultMap,
            [(HAL_ReviewedSkulltulas.TwinsPlacement, "Enemy_Skulltula_Gold")]);
        var twinsWorld = HAL_ChildForestLogic.Build(twinsToken) with {
            Starts = [new(HAL_ChildForestLogic.Forest, HAL_WorldReachability.Ages.Adult, HAL_WorldReachability.Times.Night)] };
        AssertTest.True(!HAL_WorldReachability.Evaluate(twinsWorld, new Dictionary<string,int>(), 1).Collected.Contains(HAL_ReviewedSkulltulas.TwinsId) &&
            HAL_WorldReachability.Evaluate(twinsWorld, new Dictionary<string,int> { ["Item_Hookshot"] = 1 }, 1).Tokens == 1 &&
            HAL_WorldReachability.Evaluate(twinsWorld, new Dictionary<string,int> { ["Item_Longshot"] = 1 }, 1).Tokens == 1 &&
            !HAL_WorldReachability.Evaluate(twinsWorld with { Starts = [new(HAL_ChildForestLogic.Forest, HAL_WorldReachability.Ages.Child, HAL_WorldReachability.Times.Night)] },
                new Dictionary<string,int> { ["Item_Hookshot"] = 1 }, 1).Collected.Contains(HAL_ReviewedSkulltulas.TwinsId),
            "House of Twins token requires adult night access and hookshot or longshot");
    }
}
