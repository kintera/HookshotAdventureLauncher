// Synthetic event flow: this fixture does not contain game resources.
using System.Buffers.Binary;
using System.Text;
using HookshotAdventure.Core;

static class SkulltulaFixture
{
    public static void CheckMap(byte[] source, ulong placement)
    {
        byte[] data=(byte[])source.Clone(); var map=new HAL_Byml(data);
        int index=map.Values.Select((value,index)=>(value,index)).Single(x=>x.value=="Item_BulletBag_Medium").index;
        int text=data.AsSpan().IndexOf("Item_BulletBag_Medium\0"u8);
        if(text<0) throw new InvalidDataException("Missing synthetic item string.");
        data.AsSpan(text,"Item_BulletBag_Medium".Length).Clear(); "Enemy_Skulltula_Gold"u8.CopyTo(data.AsSpan(text));
        var root=map.Fields(map.Root);
        foreach(var row in map.Array(root["Actors"]))
        {
            var actor=map.Fields(checked((int)map.Child(row,0xc1)));
            if(!actor.TryGetValue("Hash",out var hash) || map.UInt(checked((int)map.Child(hash,0xd5)),8)!=placement) continue;
            int slot=actor["Gyaml"].Slot;
            if(map.LittleEndian) BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(slot),(uint)index);
            else BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(slot),(uint)index);
        }
        var found=HAL_SkulltulaScan.Inspect("fixture",data);
        AssertTest.True(found.Count==1 && found[0].Placement==placement.ToString("X16"),
            "Skulltula scan finds the exact gold enemy placement without modifying its map");
    }
    public static void Check()
    {
        var preview=HAL_SkulltulaRewards.LoadPreview(Path.Combine(Environment.CurrentDirectory,"builder/Adventure/resources/HALr_skulltula_rewards.json"));
        AssertTest.True(preview.Select(x=>x.Tokens).SequenceEqual(new[]{10,20,30,40,50,100}),
            "HAL Skulltula resource lists each House of Skulltula milestone");
        byte[] b=new byte[0x20000]; int cursor=0x8000;
        void U16(int at,int n)=>BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(at),(ushort)n);
        void U32(int at,uint n)=>BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(at),n);
        void U64(int at,int n)=>BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(at),(ulong)n);
        int Str(string text) {int p=cursor;byte[] s=Encoding.UTF8.GetBytes(text);U16(p,s.Length);s.CopyTo(b,p+2);cursor+=s.Length+3;return p;}
        const int flow=0x100, actors=0x200, events=0x400;
        int E(int n)=>events+n*48;
        "BFEVFL\0\0"u8.CopyTo(b);U16(8,0x400);U16(12,0xfeff);U16(32,1);U64(40,0x60);U64(0x60,flow);
        "EVFL"u8.CopyTo(b.AsSpan(flow));U16(flow+16,1);U16(flow+22,586);U64(flow+32,Str("GatheringTokenGold"));U64(flow+40,actors);U64(flow+48,events);
        U64(actors,Str("GameDataActor"));U16(actors+48,1);int actions=cursor;cursor+=8;U64(actions,Str("EventTriggerSetGameDataBool"));U64(actors+24,actions);
        void Params(int index,params (string Name,string? Value,uint Scalar)[] values)
        {
            int c=cursor;cursor+=16+values.Length*8;int d=cursor;cursor+=32+values.Length*16;b[c]=1;U16(c+2,values.Length);U64(c+8,d);
            "DIC "u8.CopyTo(b.AsSpan(d));U32(d+4,(uint)values.Length);
            for(int i=0;i<values.Length;i++){var (name,value,scalar)=values[i];U64(d+32+i*16,Str(name));int item=cursor;cursor+=24;U64(c+16+i*8,item);b[item]=(byte)(value is null?3:5);if(value is null)U32(item+16,scalar);else U64(item+16,Str(value));}
            U64(E(index)+16,c);
        }
        foreach(var x in HAL_SkulltulaRewards.Checks){b[E(x.Event)+8]=4;U16(E(x.Event)+10,x.FlagEvent);U64(E(x.Event)+32,Str("TakenReward_Common_Event"));Params(x.Event,("TakenRewardActor",x.Actor,0));
            Params(x.FlagEvent,("GameDataName",$"KakarikoVillage_GatheringTokenGold_GetReward{x.Tokens}",0),("Value",null,0x80000001));}
        b[E(250)+8]=4;Params(250,("TakenRewardActor","Obj_Rupee_Gold",0));
        byte[] patched=HAL_SkulltulaRewards.PatchExistingActors(b,new Dictionary<int,string>{{10,"Item_Wallet_Large"},{30,"Item_Wallet_Medium"}});
        var f=new HAL_EventFlow(patched,"GatheringTokenGold");
        AssertTest.True(f.StringParameters(245)["TakenRewardActor"]=="Item_Wallet_Large" && f.StringParameters(247)["TakenRewardActor"]=="Item_Wallet_Medium" &&
            f.StringParameters(246)["TakenRewardActor"]=="Item_AgonyStone" && f.StringParameters(250)["TakenRewardActor"]=="Obj_Rupee_Gold" &&
            new HAL_EventFlow(b,"GatheringTokenGold").StringParameters(245)["TakenRewardActor"]=="Item_Wallet_Medium",
            "Skulltula reward swaps preserve unselected rewards, source, and repeatable 100-token prize");
        var invalid=(byte[])b.Clone();BinaryPrimitives.WriteUInt16LittleEndian(invalid.AsSpan(E(245)+10),101);
        bool rejected=false;try{HAL_SkulltulaRewards.PatchExistingActors(invalid,new Dictionary<int,string>{{10,"Item_Wallet_Large"},{30,"Item_Wallet_Medium"}});}catch(InvalidDataException){rejected=true;}
        AssertTest.True(rejected,"Skulltula reward patch rejects a changed completion route");
        bool lostItem=false;try{HAL_SkulltulaRewards.PatchExistingActors(b,new Dictionary<int,string>{{10,"Item_Wallet_Large"}});}catch(InvalidDataException){lostItem=true;}
        AssertTest.True(lostItem,"Skulltula reward patch rejects a duplicate or lost pool item");
        // Supply a valid private string pool and relocation table around the synthetic flow.
        int pool=b.Length, end=pool+24, relocation=end;
        byte[] packed=new byte[relocation+40+HAL_SkulltulaRewards.Checks.Count*8]; b.CopyTo(packed,0);
        "STR "u8.CopyTo(packed.AsSpan(pool)); "RELT"u8.CopyTo(packed.AsSpan(relocation));
        BinaryPrimitives.WriteUInt32LittleEndian(packed.AsSpan(24),(uint)relocation);
        BinaryPrimitives.WriteUInt32LittleEndian(packed.AsSpan(28),(uint)packed.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(packed.AsSpan(relocation+4),(uint)relocation);
        BinaryPrimitives.WriteUInt32LittleEndian(packed.AsSpan(relocation+8),1);
        BinaryPrimitives.WriteUInt32LittleEndian(packed.AsSpan(relocation+28),(uint)end);
        BinaryPrimitives.WriteUInt32LittleEndian(packed.AsSpan(relocation+36),(uint)HAL_SkulltulaRewards.Checks.Count);
        var packedFlow=new HAL_EventFlow(packed,"GatheringTokenGold");
        for(int i=0;i<HAL_SkulltulaRewards.Checks.Count;i++) {
            int slot=packedFlow.StringParameterPointerSlot(HAL_SkulltulaRewards.Checks[i].Event,"TakenRewardActor");
            BinaryPrimitives.WriteUInt32LittleEndian(packed.AsSpan(relocation+40+i*8),(uint)slot);
            BinaryPrimitives.WriteUInt32LittleEndian(packed.AsSpan(relocation+44+i*8),1);
        }
        var exchanged=new HAL_EventFlow(HAL_SkulltulaRewards.PatchGreenRupeeExchanges(packed,new[]{10,30}),"GatheringTokenGold");
        AssertTest.True(exchanged.StringParameters(245)["TakenRewardActor"]=="TBox_Rupee_Green" &&
            exchanged.StringParameters(247)["TakenRewardActor"]=="TBox_Rupee_Green" &&
            exchanged.StringParameters(246)["TakenRewardActor"]=="Item_AgonyStone" &&
            exchanged.StringParameters(250)["TakenRewardActor"]=="Obj_Rupee_Gold",
            "Skulltula exchanges patch only the selected one-time rewards and preserve the repeatable reward");
        bool repeatRejected=false;try{HAL_SkulltulaRewards.PatchGreenRupeeExchanges(packed,new[]{100});}catch(InvalidDataException){repeatRejected=true;}
        AssertTest.True(repeatRejected,"Skulltula exchanges cannot consume the repeatable reward");
        AssertTest.True(HAL_SkulltulaRewards.WithinMaximum(35).Select(x=>x.Tokens).SequenceEqual(new[]{10,20,30}) &&
            HAL_SkulltulaRewards.WithinMaximum(0).Count==0 && HAL_SkulltulaRewards.WithinMaximum(100).Count==5,
            "Skulltula maximum selects only eligible one-time reward thresholds");
    }
}
