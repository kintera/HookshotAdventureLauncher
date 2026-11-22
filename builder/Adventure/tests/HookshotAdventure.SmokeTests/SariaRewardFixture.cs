// Synthetic event flow; no game resource is included in this fixture.
using System.Buffers.Binary;
using System.Text;
using HookshotAdventure.Core;

static class SariaRewardFixture
{
    public static void Check()
    {
        byte[] data=new byte[0x9000]; int cursor=0x5000;
        void U16(int at,int n)=>BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(at),(ushort)n);
        void U32(int at,uint n)=>BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at),n);
        void U64(int at,int n)=>BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(at),(ulong)n);
        int Str(string text){int at=cursor;byte[] bytes=Encoding.UTF8.GetBytes(text);U16(at,bytes.Length);bytes.CopyTo(data,at+2);cursor+=bytes.Length+3;return at;}
        const int root=0x100, actors=0x200, events=0x400;
        int E(int index)=>events+index*48;
        "BFEVFL\0\0"u8.CopyTo(data);U16(8,0x400);U16(12,0xfeff);U16(32,1);U64(40,0x60);U64(0x60,root);
        "EVFL"u8.CopyTo(data.AsSpan(root));U16(root+16,3);U16(root+22,92);U64(root+32,Str("Dm_Kokiri_0155"));U64(root+40,actors);U64(root+48,events);
        U64(actors,Str("PouchSystemActor"));U16(actors+48,1);int acts=cursor;cursor+=8;U64(actors+24,acts);U64(acts,Str("EventTriggerAddOrRemovePouchContent"));
        U64(actors+56,Str("GameSystemActor"));U16(actors+56+48,1);acts=cursor;cursor+=8;U64(actors+56+24,acts);U64(acts,Str("EventTriggerZeldaNotesRequestSendDailyScreenShot"));
        U64(actors+112,Str("EventSystemActor"));U16(actors+112+48,1);acts=cursor;cursor+=8;U64(actors+112+24,acts);U64(acts,Str("EventWait"));
        void Parameters(int index,params (string Name,string? Value,uint Number)[] values)
        {
            int c=cursor;cursor+=16+values.Length*8;int d=cursor;cursor+=32+values.Length*16;
            data[c]=1;U16(c+2,values.Length);U64(c+8,d);"DIC "u8.CopyTo(data.AsSpan(d));U32(d+4,(uint)values.Length);
            for(int i=0;i<values.Length;i++){var (name,value,number)=values[i];U64(d+32+i*16,Str(name));int item=cursor;cursor+=24;U64(c+16+i*8,item);data[item]=(byte)(value is null?2:5);if(value is null)U32(item+16,number);else U64(item+16,Str(value));}
            U64(E(index)+16,c);
        }
        U16(E(51)+10,0xffff);Parameters(51,("Actor","Item_Ocarina_Fairy",0),("Num",null,1));
        U16(E(59)+10,0xffff);U16(E(59)+12,2);
        data[E(52)+8]=4;U16(E(52)+10,53);U64(E(52)+24,Str("GetPouchContent"));U64(E(52)+32,Str("DisplayGetByActorName"));Parameters(52,("ActorName","Item_Ocarina_Fairy",0));
        data[E(70)+8]=2;U16(E(70)+10,4);U16(E(70)+12,71);int branches=cursor;cursor+=8;U64(E(70)+16,branches);
        foreach(var (n,i) in new[]{(66,0),(72,1),(51,2),(74,3)}) U16(branches+i*2,n);
        U16(E(90)+10,52);U16(E(90)+12,1);
        var original=new HAL_EventFlow(data,"Dm_Kokiri_0155");int pointer=original.StringParameterPointerSlot(52,"ActorName");
        int pool=data.Length,end=pool+24,relocation=end;Array.Resize(ref data,relocation+56);
        "STR "u8.CopyTo(data.AsSpan(pool));"RELT"u8.CopyTo(data.AsSpan(relocation));
        U32(24,(uint)relocation);U32(28,(uint)data.Length);U32(relocation+4,(uint)relocation);U32(relocation+8,1);
        U32(relocation+28,(uint)end);U32(relocation+36,2);U32(relocation+40,(uint)pointer);U32(relocation+44,1);
        U32(relocation+48,(uint)(E(52)+32));U32(relocation+52,1);
        byte[] changed=HAL_EventRewardPatch.SariaFairyOcarina(data,"TBox_Rupee_Green");var result=new HAL_EventFlow(changed,"Dm_Kokiri_0155");
        var blue = new HAL_EventFlow(HAL_EventRewardPatch.SariaFairyOcarina(data,"TBox_Rupee_Blue"), "Dm_Kokiri_0155");
        AssertTest.True(blue.StringParameters(52)["ActorName"] == "TBox_Rupee_Blue" && !blue.ForkRoutes(70).Branches.Contains(51),
            "Saria's exchange supports another rupee and suppresses the vanilla ocarina grant");
        AssertTest.True(result.StringParameters(52)["ActorName"]=="TBox_Rupee_Green" && result.Subflow(52)==("GetPouchContent","GetPouchContentByActorName") && result.ScalarParameter(51,"Num")==((byte)2,1u) &&
            result.ForkRoutes(70).Branches.SequenceEqual(new[]{66,72,59,74}) && new HAL_EventFlow(data,"Dm_Kokiri_0155").ForkRoutes(70).Branches.Contains(51),
            "Saria exchange bypasses the pouch action entirely while preserving the source and other concurrent events");
        var wrong=(byte[])data.Clone();BinaryPrimitives.WriteUInt16LittleEndian(wrong.AsSpan(branches+4),50);
        bool rejected=false;try{HAL_EventRewardPatch.SariaFairyOcarina(wrong,"TBox_Rupee_Green");}catch(InvalidDataException){rejected=true;}
        AssertTest.True(rejected,"Saria exchange refuses changed fork routing");
        CheckBridgeTutorial();
    }

    private static void CheckBridgeTutorial()
    {
        byte[] data = new byte[0x10000]; int cursor = 0x8000;
        void U16(int at, int n) => BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(at), (ushort)n);
        void U32(int at, uint n) => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at), n);
        void U64(int at, int n) => BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(at), (ulong)n);
        int Str(string s) { int p = cursor; byte[] b = Encoding.UTF8.GetBytes(s); U16(p, b.Length); b.CopyTo(data, p + 2); cursor += b.Length + 3; return p; }
        const int root = 0x100, actors = 0x200, events = 0x400;
        int E(int i) => events + i * 48;
        "BFEVFL\0\0"u8.CopyTo(data); U16(8, 0x400); U16(12, 0xfeff); U16(32, 1); U64(40, 0x60); U64(0x60, root);
        "EVFL"u8.CopyTo(data.AsSpan(root)); U16(root + 16, 2); U16(root + 22, 480); U64(root + 32, Str("Dm_SY_BancChangeArrive")); U64(root + 40, actors); U64(root + 48, events);
        void Actor(int i, string name, string action) { int a = actors + i * 56; U64(a, Str(name)); U16(a + 48, 1); int list = cursor; cursor += 8; U64(a + 24, list); U64(list, Str(action)); }
        Actor(0, "GameSystemActor", "EventWait"); Actor(1, "GameSystemActor", "EventTriggerRequestAutoSave");
        U16(E(479) + 10, 88); U16(E(226) + 12, 1);
        data[E(88) + 8] = 4; U16(E(88) + 10, 226); U64(E(88) + 24, Str("ActionGuide")); U64(E(88) + 32, Str("OpenActionGuideSceenForOcarina"));
        int container = cursor; cursor += 24; int dictionary = cursor; cursor += 48; int item = cursor; cursor += 24;
        data[container] = 1; U16(container + 2, 1); U64(container + 8, dictionary); U64(container + 16, item);
        "DIC "u8.CopyTo(data.AsSpan(dictionary)); U32(dictionary + 4, 1); U64(dictionary + 32, Str("Frames"));
        data[item] = 2; U32(item + 16, 70); U64(E(479) + 16, container);
        var result = new HAL_EventFlow(HAL_EventRewardPatch.SariaBridgeTutorial(data), "Dm_SY_BancChangeArrive");
        AssertTest.True(result.UInt(E(479) + 10, 2) == 226 && result.Subflow(88) == ("ActionGuide", "OpenActionGuideSceenForOcarina") &&
            new HAL_EventFlow(data, "Dm_SY_BancChangeArrive").UInt(E(479) + 10, 2) == 88,
            "shuffled Saria gift skips the ocarina guide while retaining the arrival autosave and source");
        U32(item + 16, 69); bool rejected = false;
        try { HAL_EventRewardPatch.SariaBridgeTutorial(data); } catch (InvalidDataException) { rejected = true; }
        AssertTest.True(rejected, "Saria bridge patch refuses an unexpected action guide");
    }
}
