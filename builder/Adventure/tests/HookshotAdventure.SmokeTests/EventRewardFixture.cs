// Synthetic event flow: no game resources are used in these checks.
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HookshotAdventure.Core;

static class EventRewardFixture
{
    public static void Check()
    {
        CheckExpansion();
        CheckDictionary();
        CheckEventTables();
        var container = new byte[160];
        "BFEVFL\0\0"u8.CopyTo(container); "STR "u8.CopyTo(container.AsSpan(80));
        BinaryPrimitives.WriteUInt32LittleEndian(container.AsSpan(96),1);
        BinaryPrimitives.WriteUInt16LittleEndian(container.AsSpan(104),1); container[106]=(byte)'A';
        BinaryPrimitives.WriteUInt64LittleEndian(container.AsSpan(64),104);
        "RELT"u8.CopyTo(container.AsSpan(112));
        BinaryPrimitives.WriteUInt32LittleEndian(container.AsSpan(24),112); BinaryPrimitives.WriteUInt32LittleEndian(container.AsSpan(28),160);
        BinaryPrimitives.WriteUInt32LittleEndian(container.AsSpan(116),112); BinaryPrimitives.WriteUInt32LittleEndian(container.AsSpan(120),1);
        BinaryPrimitives.WriteUInt32LittleEndian(container.AsSpan(140),108); BinaryPrimitives.WriteUInt32LittleEndian(container.AsSpan(148),1);
        BinaryPrimitives.WriteUInt32LittleEndian(container.AsSpan(152),64); BinaryPrimitives.WriteUInt32LittleEndian(container.AsSpan(156),1);
        byte[] appended=HAL_EventFlowStrings.AppendAndPoint(container,64,"TBox_Rupee_Green");
        int pointer=checked((int)BinaryPrimitives.ReadUInt64LittleEndian(appended.AsSpan(64)));
        int relocation=checked((int)BinaryPrimitives.ReadUInt32LittleEndian(appended.AsSpan(24)));
        AssertTest.True(pointer==108 && Encoding.UTF8.GetString(appended,pointer+2,16)=="TBox_Rupee_Green" &&
            BinaryPrimitives.ReadUInt32LittleEndian(appended.AsSpan(28))==appended.Length &&
            BinaryPrimitives.ReadUInt32LittleEndian(appended.AsSpan(96))==2 &&
            BinaryPrimitives.ReadUInt32LittleEndian(appended.AsSpan(relocation+28))>108 && container[64]==104,
            "event string insertion preserves the original and extends its pool and relocation section");
        bool unregistered=false; try { HAL_EventFlowStrings.AppendAndPoint(container,72,"test"); } catch(InvalidDataException) { unregistered=true; }
        AssertTest.True(unregistered,"event string insertion rejects a pointer absent from the relocation table");
        var b = new byte[0x8000]; int cursor = 0x4000;
        void U16(int at, int value) => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(at), (ushort)value);
        void U32(int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(at), value);
        void U64(int at, int value) => BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(at), (ulong)value);
        int Str(string text) { int p=cursor; byte[] s=Encoding.UTF8.GetBytes(text); U16(p,s.Length); s.CopyTo(b,p+2); cursor+=s.Length+3; return p; }
        const int flow=0x100, actors=0x200, events=0x400;
        "BFEVFL\0\0"u8.CopyTo(b); U16(8,0x400); U16(12,0xfeff); U16(32,1); U64(40,0x60); U64(0x60,flow);
        "EVFL"u8.CopyTo(b.AsSpan(flow)); U16(flow+16,1); U16(flow+22,113);
        U64(flow+32,Str("Dm_LostWood_0040")); U64(flow+40,actors); U64(flow+48,events);
        U64(actors,Str("GameDataActor")); U16(actors+48,1); U16(actors+50,1);
        int actionArray=cursor; cursor+=8; U64(actionArray,Str("EventTriggerSetGameDataBool")); U64(actors+24,actionArray);
        int queryArray=cursor; cursor+=8; U64(queryArray,Str("EventQueryIsGameDataBool")); U64(actors+32,queryArray);
        int Event(int n) => events+n*48;
        void Parameters(int index, params (string Name, string? Value, uint Scalar)[] values)
        {
            int paramContainer=cursor; cursor+=16+values.Length*8; int dictionary=cursor; cursor+=32+values.Length*16;
            b[paramContainer]=1; U16(paramContainer+2,values.Length); U64(paramContainer+8,dictionary); "DIC "u8.CopyTo(b.AsSpan(dictionary)); U32(dictionary+4,(uint)values.Length);
            for(int i=0;i<values.Length;i++) { var (name,value,scalar)=values[i]; U64(dictionary+32+i*16,Str(name)); int item=cursor; cursor+=24; U64(paramContainer+16+i*8,item); b[item]=(byte)(value is null?3:5); if(value is null) U32(item+16,scalar); else U64(item+16,Str(value)); }
            U64(Event(index)+16,paramContainer);
        }
        void Switch(int index, string flag, int first, int second)
        {
            int e=Event(index); b[e+8]=1; U16(e+10,2); int routes=cursor; cursor+=16; U64(e+24,routes);
            U32(routes,0); U16(routes+4,first); U32(routes+8,1); U16(routes+12,second);
            Parameters(index,("GameDataName",flag,0));
        }
        Switch(45,"LostWood_HitTargetOfDekuScrub_FirstClear",46,39);
        Switch(56,"IsGet.Item_BulletBag_Medium",84,85);
        foreach(var (index,next,actor) in new[] {(84,88,"Item_BulletBag_Large"),(85,89,"Item_BulletBag_Medium"),(86,87,"TBox_Rupee_Green")})
        { int e=Event(index); b[e+8]=4; U16(e+10,next); Parameters(index,("ActorName",actor,0)); }
        U16(Event(88)+10,57); U16(Event(89)+10,57);
        int forkOffset=cursor; cursor+=4; U16(forkOffset,84); U16(forkOffset+2,85);
        b[Event(90)+8]=2; U16(Event(90)+10,2); U16(Event(90)+12,91); U64(Event(90)+16,forkOffset);
        Parameters(57,("GameDataName","LostWood_HitTargetOfDekuScrub_FirstClear",0),("Value",null,0x80000001));
        var original=new HAL_EventFlow(b,"Dm_LostWood_0040");
        var fork=original.ForkRoutes(90);
        AssertTest.True(fork.Branches.SequenceEqual(new[]{84,85}) && fork.Join==91,
            "event fork reader preserves both concurrent branches and the join");
        var badFork=(byte[])b.Clone(); BinaryPrimitives.WriteUInt16LittleEndian(badFork.AsSpan(forkOffset),113);
        bool rejectedFork=false; try { new HAL_EventFlow(badFork,"Dm_LostWood_0040").ForkRoutes(90); } catch(InvalidDataException) { rejectedFork=true; }
        AssertTest.True(rejectedFork,"event fork reader rejects out-of-range branch targets");
        // The target fixture above uses direct pointers; give its two editable
        // slots a small string pool and relocation table for appended actors.
        int targetSlot = original.StringParameterPointerSlot(85, "ActorName");
        int largeSlot = original.StringParameterPointerSlot(84, "ActorName");
        int pool = (cursor + 7) & ~7, end = pool + 24, reloc = (end + 7) & ~7;
        Array.Resize(ref b, reloc + 56);
        "STR "u8.CopyTo(b.AsSpan(pool)); "RELT"u8.CopyTo(b.AsSpan(reloc));
        U32(24, (uint)reloc); U32(28, (uint)b.Length); U32(reloc + 4, (uint)reloc); U32(reloc + 8, 1);
        U32(reloc + 28, (uint)end); U32(reloc + 36, 2);
        U32(reloc + 40, (uint)targetSlot); U32(reloc + 44, 1);
        U32(reloc + 48, (uint)largeSlot); U32(reloc + 52, 1);
        byte[] changed=HAL_EventRewardPatch.LostWoodsTargetGreenRupee(b); var result=new HAL_EventFlow(changed,"Dm_LostWood_0040");
        AssertTest.True(result.StringParameters(84)["ActorName"]=="TBox_Rupee_Green" && result.StringParameters(85)["ActorName"]=="TBox_Rupee_Green" &&
            result.StringParameters(86)["ActorName"]=="TBox_Rupee_Green" && original.StringParameters(84)["ActorName"]=="Item_BulletBag_Large" &&
            result.ScalarParameter(57,"Value")==((byte)3,0x80000001u), "target reward replaces both first-clear branches and preserves source, repeat, and completion");
        var colored = new HAL_EventFlow(HAL_EventRewardPatch.LostWoodsTargetRupee(b,"TBox_Rupee_Blue"), "Dm_LostWood_0040");
        AssertTest.True(colored.StringParameters(84)["ActorName"] == "TBox_Rupee_Blue" && colored.StringParameters(85)["ActorName"] == "TBox_Rupee_Blue" &&
            colored.StringParameters(86)["ActorName"] == "TBox_Rupee_Green", "target exchange supports a blue rupee without changing its repeat reward");
        var wrong=(byte[])b.Clone(); BinaryPrimitives.WriteUInt16LittleEndian(wrong.AsSpan(Event(88)+10),58);
        bool rejected=false; try { HAL_EventRewardPatch.LostWoodsTargetGreenRupee(wrong); } catch(InvalidDataException) { rejected=true; }
        AssertTest.True(rejected,"target reward rejects a changed completion route");
        var wrongBranch=(byte[])b.Clone();
        int branchRoutes=checked((int)original.UInt(Event(56)+24,8));
        BinaryPrimitives.WriteUInt16LittleEndian(wrongBranch.AsSpan(branchRoutes+4),85);
        bool rejectedBranch=false; try { HAL_EventRewardPatch.LostWoodsTargetGreenRupee(wrongBranch); } catch(InvalidDataException) { rejectedBranch=true; }
        AssertTest.True(rejectedBranch,"target reward rejects changed ownership routing");
        const string planJson = "{\"assignments\":[{\"id\":\"chest-a\",\"reward\":\"TBox_Rupee_Green\",\"event\":null,\"donor_id\":\"chest-b\",\"runtime_blockers\":[]},{\"id\":\"chest-b\",\"reward\":\"TBox_Rupee_Blue\",\"event\":null,\"donor_id\":\"chest-a\",\"runtime_blockers\":[]}],\"reward_ledger\":{\"shuffled_chests\":{\"TBox_Rupee_Green\":1,\"TBox_Rupee_Blue\":1}},\"settings\":{}}";
        var plan=JsonNode.Parse(planJson)!.AsObject(); HAL_EventRewardPlan.AddTargetExchange(plan,"1234");
        using var doc=JsonDocument.Parse(plan.ToJsonString());
        AssertTest.True(plan["assignments"]![0]!["reward"]!.GetValue<string>()=="Item_BulletBag_Medium" &&
            plan["assignments"]![0]!["donor_id"]!.GetValue<string>()==HAL_EventRewardPlan.CheckId &&
            plan["event_assignments"]![0]!["donor_id"]!.GetValue<string>()=="chest-b" &&
            plan["reward_ledger"]!["shuffled_events"]!["TBox_Rupee_Green"]!.GetValue<int>()==1 &&
            plan["plan_hash"]!.GetValue<string>()==HAL_CanonicalJson.DigestWithoutProperty(doc.RootElement,"plan_hash"),
            "event exchange conserves the original chest reward, records the target and rehashes the plan");
        var second=JsonNode.Parse(planJson)!.AsObject(); HAL_EventRewardPlan.AddTargetExchange(second,"1234");
        AssertTest.True(second.ToJsonString()==plan.ToJsonString(),"event reward exchange is deterministic for the seed");
        bool duplicate=false; try { HAL_EventRewardPlan.AddTargetExchange(plan,"1234"); } catch(InvalidDataException) { duplicate=true; }
        AssertTest.True(duplicate,"event reward exchange cannot be applied twice");
        var sariaPlan=JsonNode.Parse(planJson)!.AsObject();HAL_EventRewardPlan.AddSariaExchange(sariaPlan,"1234");
        using var sariaDocument=JsonDocument.Parse(sariaPlan.ToJsonString());
        AssertTest.True(sariaPlan["assignments"]![0]!["reward"]!.GetValue<string>()=="Item_Ocarina_Fairy" &&
            sariaPlan["event_assignments"]![0]!["id"]!.GetValue<string>()==HAL_EventRewardPlan.SariaCheckId &&
            sariaPlan["reward_ledger"]!["event_source"]!["Item_Ocarina_Fairy"]!.GetValue<int>()==1 &&
            sariaPlan["reward_ledger"]!["shuffled_events"]!["TBox_Rupee_Green"]!.GetValue<int>()==1 &&
            sariaPlan["plan_hash"]!.GetValue<string>()==HAL_CanonicalJson.DigestWithoutProperty(sariaDocument.RootElement,"plan_hash"),
            "Saria exchange conserves the ocarina and chest reward and binds the updated plan hash");
        string targetReplacement = HAL_EventRewardPlan.AddTargetExchange(sariaPlan,"1234");
        AssertTest.True(targetReplacement == "TBox_Rupee_Blue" && sariaPlan["event_assignments"]!.AsArray().Count == 2 &&
            sariaPlan["event_assignments"]![0]!["exchanged_chest"]!.GetValue<string>() != sariaPlan["event_assignments"]![1]!["exchanged_chest"]!.GetValue<string>() &&
            sariaPlan["reward_ledger"]!["shuffled_events"]!["TBox_Rupee_Green"]!.GetValue<int>() == 1 &&
            sariaPlan["reward_ledger"]!["shuffled_events"]!["TBox_Rupee_Blue"]!.GetValue<int>() == 1,
            "Saria and target exchanges use distinct rupee chests and preserve both reward ledgers");
        var excludedEventPlan = JsonNode.Parse(planJson)!.AsObject();
        excludedEventPlan["assignments"]![0]!["progression_excluded"] = true;
        AssertTest.True(HAL_EventRewardPlan.AddTargetExchange(excludedEventPlan, "1234") == "TBox_Rupee_Blue" &&
            excludedEventPlan["assignments"]![0]!["reward"]!.GetValue<string>() == "TBox_Rupee_Green",
            "event reward exchanges cannot place progression in a filler-only chest");
        var excludedSariaPlan = JsonNode.Parse(planJson)!.AsObject();
        excludedSariaPlan["assignments"]![0]!["progression_excluded"] = true;
        AssertTest.True(HAL_EventRewardPlan.AddSariaExchange(excludedSariaPlan, "1234") == "TBox_Rupee_Blue" &&
            excludedSariaPlan["assignments"]![0]!["reward"]!.GetValue<string>() == "TBox_Rupee_Green",
            "Saria's ocarina cannot be placed at a filler-only chest");
        var startingPlan=JsonNode.Parse(planJson)!.AsObject();startingPlan["starting_inventory"]=new JsonArray();
        HAL_EventRewardPlan.AddSariaExchange(startingPlan,"1234",true);
        AssertTest.True(startingPlan["assignments"]![0]!["reward"]!.GetValue<string>()=="TBox_Rupee_Blue" &&
            startingPlan["starting_inventory"]![0]!["item"]!.GetValue<string>()=="Item_Ocarina_Fairy" &&
            startingPlan["reward_ledger"]!["added_filler"]!["TBox_Rupee_Blue"]!.GetValue<int>()==1,
            "starting with the Fairy Ocarina replaces its shuffled source with a blue rupee");
        JsonObject SkullPlan()
        {
            var chests=new JsonArray();
            for(int i=0;i<6;i++) chests.Add(new JsonObject { ["id"]=$"chest-{i}", ["reward"]="TBox_Rupee_Blue",
                ["event"]=null, ["donor_id"]=$"chest-{i}", ["runtime_blockers"]=new JsonArray() });
            return new JsonObject { ["assignments"]=chests, ["event_assignments"]=new JsonArray(), ["settings"]=new JsonObject(),
                ["reward_ledger"]=new JsonObject { ["shuffled_chests"]=new JsonObject { ["TBox_Rupee_Blue"]=6 } } };
        }
        var housePlan=SkullPlan();var exchanges=HAL_EventRewardPlan.AddSkulltulaExchanges(housePlan,"1234");
        using var houseDocument=JsonDocument.Parse(housePlan.ToJsonString());
        AssertTest.True(exchanges.Count==5 && exchanges.Values.All(x=>x=="TBox_Rupee_Blue") &&
            housePlan["event_assignments"]!.AsArray().Count==5 &&
            HAL_SkulltulaRewards.Checks.All(check=>housePlan["assignments"]!.AsArray().Any(row=>row!["reward"]!.GetValue<string>()==check.Actor)) &&
            housePlan["plan_hash"]!.GetValue<string>()==HAL_CanonicalJson.DigestWithoutProperty(houseDocument.RootElement,"plan_hash"),
            "Skulltula exchange moves all five unique House rewards into separate chests and conserves their rupee donors");
        var again=SkullPlan();HAL_EventRewardPlan.AddSkulltulaExchanges(again,"1234");
        AssertTest.True(again.ToJsonString()==housePlan.ToJsonString(),"Skulltula exchanges are deterministic for a seed");
        var excludedHouse = SkullPlan(); excludedHouse["assignments"]![0]!["progression_excluded"] = true;
        HAL_EventRewardPlan.AddSkulltulaExchanges(excludedHouse, "1234");
        AssertTest.True(excludedHouse["assignments"]![0]!["reward"]!.GetValue<string>() == "TBox_Rupee_Blue" &&
            excludedHouse["event_assignments"]!.AsArray().All(row => row!["exchanged_chest"]!.GetValue<string>() != "chest-0"),
            "Skulltula rewards cannot move into a filler-only chest");
        excludedHouse["assignments"]![0]!["reward"] = "Item_Wallet_Medium";
        bool finalValidation = false;
        try { HAL_ChestPlanDocument.ValidateProgressionExclusions(JsonSerializer.SerializeToElement(excludedHouse)); }
        catch (InvalidDataException) { finalValidation = true; }
        AssertTest.True(finalValidation, "final seed validation rejects a later progression assignment at a filler-only check");
        var insufficient=SkullPlan();insufficient["assignments"]!.AsArray().RemoveAt(0);insufficient["assignments"]!.AsArray().RemoveAt(0);
        bool rejectedShort=false;try{HAL_EventRewardPlan.AddSkulltulaExchanges(insufficient,"1234");}catch(InvalidDataException){rejectedShort=true;}
        AssertTest.True(rejectedShort,"Skulltula exchange rejects a pool without five eligible chests");
        int eventContainer=checked((int)original.UInt(Event(84)+16,8));
        int dictionary=checked((int)original.UInt(eventContainer+8,8));
        U64(dictionary+32,Str("TakenRewardActor"));
        AssertTest.True(HAL_EventRewardScan.Inspect("fixture", "Dm_LostWood_0040", b)
            .Any(x => x.EventIndex==84 && x.Parameter=="TakenRewardActor" && x.Actor=="Item_BulletBag_Large"),
            "event scanner discovers reward actors passed to shared event subflows");
    }

    private static void CheckExpansion()
    {
        var original = new byte[288];
        void U16(int p, ushort v) => BinaryPrimitives.WriteUInt16LittleEndian(original.AsSpan(p), v);
        void U32(int p, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(original.AsSpan(p), v);
        void U64(int p, ulong v) => BinaryPrimitives.WriteUInt64LittleEndian(original.AsSpan(p), v);
        "BFEVFL\0\0"u8.CopyTo(original); U16(8, 0x400); U16(12, 0xfeff); U16(32, 1);
        U32(16, 226); U32(24, 232); U32(28, (uint)original.Length); U64(40, 64); U64(64, 80);
        "EVFL"u8.CopyTo(original.AsSpan(80)); U32(84, 120); U16(102, 1); U64(112, 224); U64(128, 144);
        U64(144, 224); original[152] = 3; U16(154, 0xffff);
        "STR "u8.CopyTo(original.AsSpan(200)); U32(216, 1); U16(224, 1); original[226] = (byte)'A';
        "RELT"u8.CopyTo(original.AsSpan(232)); U32(236, 232); U32(240, 1); U32(260, 228); U32(268, 2);
        U32(272, 40); U32(276, 9); U32(280, 112); U32(284, 21);
        _ = new HAL_EventFlow(original, "A");
        var expanded = HAL_EventFlowExpansion.Reserve(original, 32);
        _ = new HAL_EventFlow(expanded.Data, "A");
        byte[] appended = HAL_EventFlowStrings.AppendAndPoint(expanded.Data, 112, "AnotherName");
        _ = new HAL_EventFlow(appended, "AnotherName");
        AssertTest.True(expanded.Offset == 200 && expanded.Length == 32 &&
            expanded.Data.AsSpan(200, 32).ToArray().All(b => b == 0) &&
            BinaryPrimitives.ReadUInt32LittleEndian(expanded.Data.AsSpan(16)) == 258 &&
            BinaryPrimitives.ReadUInt64LittleEndian(expanded.Data.AsSpan(112)) == 256 &&
            BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(24)) == 232,
            "event expansion moves string references and relocation metadata without modifying its source");
        var bad = (byte[])original.Clone(); BinaryPrimitives.WriteUInt64LittleEndian(bad.AsSpan(112), 300);
        bool rejected = false; try { HAL_EventFlowExpansion.Reserve(bad, 32); } catch (InvalidDataException) { rejected = true; }
        AssertTest.True(rejected, "event expansion rejects relocation pointers outside the resource");
        BinaryPrimitives.WriteUInt64LittleEndian(expanded.Data.AsSpan(expanded.Offset), 256);
        byte[] registered = HAL_EventFlowExpansion.RegisterPointers(expanded.Data, new[] { expanded.Offset });
        byte[] movedAgain = HAL_EventFlowExpansion.Reserve(registered, 16).Data;
        byte[] changedNewPointer = HAL_EventFlowStrings.AppendAndPoint(movedAgain, expanded.Offset, "NewActor");
        int newString = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(changedNewPointer.AsSpan(expanded.Offset)));
        AssertTest.True(Encoding.UTF8.GetString(changedNewPointer, newString + 2, 8) == "NewActor" &&
            BinaryPrimitives.ReadUInt64LittleEndian(movedAgain.AsSpan(expanded.Offset)) == 272,
            "newly registered event pointers relocate again and support later string edits");
        bool badSlot = false; try { HAL_EventFlowExpansion.RegisterPointers(expanded.Data, new[] { expanded.Offset + 1 }); }
        catch (InvalidDataException) { badSlot = true; }
        AssertTest.True(badSlot, "event expansion rejects unaligned new pointer slots");
        var cloned = HAL_EventFlowExpansion.ExpandEvents(original, 2);
        var clonedFlow = new HAL_EventFlow(cloned.Data, "A");
        int cloneEvent = clonedFlow.EventOffset(0);
        byte[] renamed = HAL_EventFlowStrings.AppendAndPoint(cloned.Data, cloneEvent, "EventCopy");
        AssertTest.True(cloned.FirstNewEvent == 1 && clonedFlow.Count == 3 && clonedFlow.UInt(cloneEvent + 8, 1) == 3 &&
            clonedFlow.UInt(cloneEvent + 10, 2) == 0xffff && new HAL_EventFlow(renamed, "A").Count == 3 &&
            BinaryPrimitives.ReadUInt64LittleEndian(original.AsSpan(128)) == 144,
            "event array expansion preserves old records and registers copied name pointers");

        var actorSpace = HAL_EventFlowExpansion.Reserve(original, 64);
        var querySpace = HAL_EventFlowExpansion.Reserve(actorSpace.Data, 8);
        byte[] actorData = querySpace.Data;
        BinaryPrimitives.WriteUInt16LittleEndian(actorData.AsSpan(96), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(actorData.AsSpan(100), 1);
        BinaryPrimitives.WriteUInt64LittleEndian(actorData.AsSpan(120), (ulong)actorSpace.Offset);
        BinaryPrimitives.WriteUInt16LittleEndian(actorData.AsSpan(actorSpace.Offset + 50), 1);
        BinaryPrimitives.WriteUInt64LittleEndian(actorData.AsSpan(actorSpace.Offset + 32), (ulong)querySpace.Offset);
        actorData = HAL_EventFlowExpansion.RegisterPointers(actorData, new[] { 120, actorSpace.Offset, actorSpace.Offset + 32, querySpace.Offset });
        actorData = HAL_EventFlowStrings.AppendAndPoint(actorData, actorSpace.Offset, "GameDataActor");
        actorData = HAL_EventFlowStrings.AppendAndPoint(actorData, querySpace.Offset, "OriginalQuery");
        var query = HAL_EventFlowExpansion.EnsureActorQuery(actorData, "GameDataActor", "EventQueryCheckIsGet");
        var eventAt = new HAL_EventFlow(query.Data, "A").EventOffset(0);
        query.Data[eventAt + 8] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(query.Data.AsSpan(eventAt + 12), (ushort)query.Actor);
        BinaryPrimitives.WriteUInt16LittleEndian(query.Data.AsSpan(eventAt + 14), (ushort)query.Query);
        var queryFlow = new HAL_EventFlow(query.Data, "A");
        AssertTest.True(queryFlow.Query(0) == ("GameDataActor", "EventQueryCheckIsGet") && query.Query == 1 &&
            BinaryPrimitives.ReadUInt16LittleEndian(query.Data.AsSpan(100)) == 2 &&
            HAL_EventFlowExpansion.EnsureActorQuery(query.Data, "GameDataActor", "EventQueryCheckIsGet").Query == 1,
            "new actor query is addressable and adding it again preserves its index");
        BinaryPrimitives.WriteUInt16LittleEndian(query.Data.AsSpan(eventAt + 14), 0);
        AssertTest.True(new HAL_EventFlow(query.Data, "A").Query(0) == ("GameDataActor", "OriginalQuery"),
            "adding an actor query preserves the original query indexes");

        var withEvents = HAL_EventFlowExpansion.ExpandEvents(original, 1);
        var dictionarySpace = HAL_EventFlowExpansion.Reserve(withEvents.Data, 24);
        byte[] dictionaryData = dictionarySpace.Data;
        "DIC "u8.CopyTo(dictionaryData.AsSpan(dictionarySpace.Offset));
        BinaryPrimitives.WriteUInt64LittleEndian(dictionaryData.AsSpan(136), (ulong)dictionarySpace.Offset);
        BinaryPrimitives.WriteUInt64LittleEndian(dictionaryData.AsSpan(144), 0);
        BinaryPrimitives.WriteUInt64LittleEndian(dictionaryData.AsSpan(dictionarySpace.Offset + 16), (ulong)(dictionarySpace.Offset + 44));
        dictionaryData = HAL_EventFlowExpansion.RegisterPointers(dictionaryData, new[] { 136, 144, dictionarySpace.Offset + 16 });
        var entryData = HAL_EventFlowExpansion.AddEntry(dictionaryData, "TestEntry", 1, new[] { 0 }, 7);
        var entryFlow = new HAL_EventFlow(entryData, "A");
        AssertTest.True(entryFlow.EntryPoints()["TestEntry"].EventIndex == 1 &&
            new HAL_EventFlow(dictionaryData, "A").EntryPoints().Count == 0,
            "adding an event entry keeps the original resource and points to the requested event");
        bool duplicateEntry = false; try { HAL_EventFlowExpansion.AddEntry(entryData, "TestEntry", 1, Array.Empty<int>(), 0); }
        catch (InvalidDataException) { duplicateEntry = true; }
        AssertTest.True(duplicateEntry, "event entry creation rejects duplicate names");

        var guardEvents = HAL_EventFlowExpansion.ExpandEvents(actorData, 1);
        var guardDictionary = HAL_EventFlowExpansion.Reserve(guardEvents.Data, 24);
        byte[] guardSource = guardDictionary.Data;
        "DIC "u8.CopyTo(guardSource.AsSpan(guardDictionary.Offset));
        BinaryPrimitives.WriteUInt64LittleEndian(guardSource.AsSpan(136), (ulong)guardDictionary.Offset);
        BinaryPrimitives.WriteUInt64LittleEndian(guardSource.AsSpan(144), 0);
        BinaryPrimitives.WriteUInt64LittleEndian(guardSource.AsSpan(guardDictionary.Offset + 16), (ulong)(guardDictionary.Offset + 44));
        guardSource = HAL_EventFlowExpansion.RegisterPointers(guardSource, new[] { 136, 144, guardDictionary.Offset + 16 });
        guardSource = HAL_EventFlowExpansion.AddEntry(guardSource, "Success_", 0, Array.Empty<int>(), 0);
        byte[] guarded = HAL_AmmoGuardFlow.Add(guardSource, "A", "TBox_ArrowBundle_Medium");
        var guardFlow = new HAL_EventFlow(guarded, "A");
        int current = guardFlow.EntryPoints()[HAL_AmmoGuardFlow.EntryName("TBox_ArrowBundle_Medium")].EventIndex;
        var bags = HAL_AmmoGuardFlow.RequiredBags("TBox_ArrowBundle_Medium")!;
        bool routesCorrect = true;
        for (int i = bags.Count - 1; i >= 0; i--)
        {
            var routes = guardFlow.SwitchRoutes(current);
            routesCorrect &= guardFlow.Query(current) == ("GameDataActor", "EventQueryCheckIsGet") &&
                guardFlow.StringParameters(current)["ActorName"] == bags[i] &&
                guardFlow.Subflow(routes[0]) == ("", "Success_") &&
                guardFlow.StringParameters(routes[0])["ContentName"] == "TBox_ArrowBundle_Medium";
            current = routes[1];
        }
        routesCorrect &= guardFlow.Subflow(current) == ("", "Success_") &&
            guardFlow.StringParameters(current)["ContentName"] == HAL_AmmoGuardFlow.Fallback;
        AssertTest.True(routesCorrect && guardFlow.EntryPoints()["Success_"].EventIndex == 0 &&
            HAL_AmmoGuardFlow.RequiredBags("Item_BombchuBundle_Small") is null,
            "ammo guard tests every bag tier, selects the original reward or rupee and leaves bombchus independent");
    }

    private static void CheckDictionary()
    {
        var names = new[] { "Success_", "Open", "DisplayContent", "Success_Prog_AmmoGuard_Item_Bomb", "日本語" };
        var nodes = HAL_EventFlowDictionary.Build(names);
        bool Contains(string name)
        {
            byte[] key = Encoding.UTF8.GetBytes(name);
            int Bit(uint n) => n == uint.MaxValue || n / 8 >= key.Length ? 0 : (key[^(checked((int)n / 8) + 1)] >> ((int)n & 7)) & 1;
            long Index(uint n) => n == uint.MaxValue ? -1 : n;
            int parent = 0, at = nodes[0].Left;
            while (Index(nodes[parent].Bit) < Index(nodes[at].Bit))
            {
                parent = at;
                at = Bit(nodes[at].Bit) == 0 ? nodes[at].Left : nodes[at].Right;
            }
            return at != 0 && names[at - 1] == name;
        }
        AssertTest.True(names.All(Contains) && !Contains("Missing") && !Contains("Success"),
            "event dictionary tree resolves each inserted entry and rejects other names");
        var one = HAL_EventFlowDictionary.Build(new[] { "Talk" });
        AssertTest.True(one[0] == new HAL_EventFlowDictionary.Node(uint.MaxValue, 1, 0) &&
            one[1] == new HAL_EventFlowDictionary.Node(0, 0, 1), "single-entry event dictionary has the expected binary tree");
        bool duplicate = false; try { HAL_EventFlowDictionary.Build(new[] { "Same", "Same" }); } catch (InvalidDataException) { duplicate = true; }
        AssertTest.True(duplicate, "event dictionary rejects duplicate names");
    }

    private static void CheckEventTables()
    {
        foreach (bool little in new[] { true, false })
        {
            var bytes = new List<byte>(new byte[16]); bytes[0] = (byte)(little ? 'Y' : 'B'); bytes[1] = (byte)(little ? 'B' : 'Y');
            void Put(int at, uint n, int size = 4) { for (int i = 0; i < size; i++) bytes[at + (little ? i : size - 1 - i)] = (byte)(n >> (8 * i)); }
            void Write(uint n, int size = 4) { int at = bytes.Count; bytes.AddRange(new byte[size]); Put(at, n, size); }
            void Align() { while ((bytes.Count & 3) != 0) bytes.Add(0); }
            int Table(params string[] values)
            {
                Align(); int start = bytes.Count; bytes.Add(0xc2); Write((uint)values.Length, 3);
                var encoded = values.Select(Encoding.UTF8.GetBytes).ToArray(); int offset = 4 + 4 * (values.Length + 1);
                foreach (var text in encoded) { Write((uint)offset); offset += text.Length + 1; }
                Write((uint)offset); foreach (var text in encoded) { bytes.AddRange(text); bytes.Add(0); }
                return start;
            }
            int keys = Table("EntryPointName", "Other", "__RowId");
            int values = Table("Dm_SY_TreasureboxSmall", "Success_", "kept");
            Align(); int row = bytes.Count; bytes.Add(0xc1); Write(3, 3);
            int[] valueIndex = { 1, 2, 0 };
            for (int i = 0; i < 3; i++) { Write((uint)i, 3); bytes.Add(0xa0); Write((uint)valueIndex[i]); }
            Align(); int array = bytes.Count; bytes.Add(0xc0); Write(1, 3); bytes.Add(0xc1); Align(); Write((uint)row);
            Put(4, (uint)keys); Put(8, (uint)values); Put(12, (uint)array);
            byte[] source = bytes.ToArray();
            var withKey = new HAL_Byml(HAL_BymlKeys.Ensure(source, "GetEventName"));
            var keyRow = withKey.Fields((int)withKey.Child(withKey.Array(new HAL_Byml.Field(0xc0, 12))[0], 0xc1));
            AssertTest.True(withKey.Keys.SequenceEqual(new[] { "EntryPointName", "GetEventName", "Other", "__RowId" }) &&
                withKey.Text(keyRow["__RowId"]) == "Dm_SY_TreasureboxSmall" && withKey.Text(keyRow["Other"]) == "kept",
                "BYML key insertion remaps nested rows without changing values in " + (little ? "little" : "big") + " endian");
            var result = new HAL_Byml(HAL_EventTablePatch.AddRows(source, new[]
            { new HAL_EventTableRow("HAL_Ammo_Test", "Dm_SY_TreasureboxSmall", "Success_Guard") }));
            var rows = result.Array(new HAL_Byml.Field(0xc0, 12)).Select(f => result.Fields((int)result.Child(f, 0xc1))).ToArray();
            var added = rows.Single(r => result.Text(r["__RowId"]) == "HAL_Ammo_Test");
            AssertTest.True(rows.Length == 2 && result.Text(added["EntryPointName"]) == "Success_Guard" &&
                result.Text(added["Other"]) == "kept" && new HAL_Byml(source).Array(new HAL_Byml.Field(0xc0, 12)).Count == 1,
                "event table cloning preserves template fields and source in " + (little ? "little" : "big") + " endian");
            bool duplicate = false;
            try { HAL_EventTablePatch.AddRows(source, new[] { new HAL_EventTableRow("Dm_SY_TreasureboxSmall", "Dm_SY_TreasureboxSmall", null) }); }
            catch (InvalidDataException) { duplicate = true; }
            AssertTest.True(duplicate, "event table cloning rejects an existing row ID");
        }
    }
}
