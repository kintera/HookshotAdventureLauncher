// Authored event-flow fixture: no game data.
using System.Buffers.Binary;
using System.Text;
using HookshotAdventure.Core;

static class OpenForestOpeningFixture
{
    public static void Check()
    {
        CheckDekuMeetingQuest();
        const int pool=0x1800, end=pool+24, relocation=end, length=relocation+48;
        var b=new byte[length]; int cursor=0x700;
        void U16(int at,int n)=>BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(at),(ushort)n);
        void U32(int at,uint n)=>BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(at),n);
        void U64(int at,int n)=>BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(at),(ulong)n);
        int Str(string s) { int p=cursor; byte[] v=Encoding.UTF8.GetBytes(s); U16(p,v.Length); v.CopyTo(b,p+2); cursor+=v.Length+3; return p; }
        const int flow=0x100, actor=0x200, ev=0x400, container=0x500, dict=0x540, item1=0x600, item2=0x620;
        "BFEVFL\0\0"u8.CopyTo(b); U16(8,0x400); U16(12,0xfeff); U16(32,1); U64(40,0x60); U64(0x60,flow);
        "EVFL"u8.CopyTo(b.AsSpan(flow)); U16(flow+16,1); U16(flow+22,1); U64(flow+32,Str("OpeningEvent"));
        U64(flow+40,actor); U64(flow+48,ev); U64(actor,Str("ChallengeSystemActor")); U16(actor+48,1);
        int actions=cursor; cursor+=8; U64(actions,Str("EventExecuteProgressChallengeStep")); U64(actor+24,actions);
        U16(ev+10,0xffff); U64(ev+16,container); b[container]=1; U16(container+2,2); U64(container+8,dict);
        U64(container+16,item1); U64(container+24,item2); "DIC "u8.CopyTo(b.AsSpan(dict)); U32(dict+4,2);
        U64(dict+32,Str("ChallengeName")); U64(dict+48,Str("StepName")); b[item1]=b[item2]=5;
        U64(item1+16,Str("Meet_DekuTree")); U64(item2+16,Str("Step01"));
        "STR "u8.CopyTo(b.AsSpan(pool)); U16(pool+20,0);
        "RELT"u8.CopyTo(b.AsSpan(relocation)); U32(24,relocation); U32(28,length); U32(relocation+4,relocation);
        U32(relocation+8,1); U32(relocation+28,end); U32(relocation+36,1); U32(relocation+40,item2+16); U32(relocation+44,1);
        byte[] result=HAL_OpenForestOpening.PatchQuestStep(b);
        AssertTest.True(new HAL_EventFlow(result,"OpeningEvent").StringParameters(0)["StepName"]=="Step03" &&
            new HAL_EventFlow(b,"OpeningEvent").StringParameters(0)["StepName"]=="Step01",
            "open-forest opening quest edit changes only its private step string");
        bool rejected=false; try { HAL_OpenForestOpening.PatchQuestStep(result); } catch(InvalidDataException) { rejected=true; }
        AssertTest.True(rejected,"open-forest opening quest edit rejects a changed source flow");
    }

    private static void CheckDekuMeetingQuest()
    {
        var b = new byte[0x4000]; int cursor = 0x2000;
        void U16(int p, int n) => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(p), (ushort)n);
        void U32(int p, uint n) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(p), n);
        void U64(int p, int n) => BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(p), (ulong)n);
        int Str(string s) { int p = cursor; byte[] v = Encoding.UTF8.GetBytes(s); U16(p, v.Length); v.CopyTo(b, p + 2); cursor += v.Length + 3; return p; }
        const int flow = 0x100, actor = 0x200, events = 0x400, dict = 0x1600, entries = 0x1700, actions = 0x1800;
        "BFEVFL\0\0"u8.CopyTo(b); U16(8, 0x400); U16(12, 0xfeff); U16(32, 1); U64(40, 0x60); U64(0x60, flow);
        "EVFL"u8.CopyTo(b.AsSpan(flow)); U16(flow + 16, 1); U16(flow + 22, 95); U16(flow + 24, 1);
        U64(flow + 32, Str("Dm_Kokiri_0075")); U64(flow + 40, actor); U64(flow + 48, events); U64(flow + 56, dict); U64(flow + 64, entries);
        U64(actor, Str("ChallengeSystemActor")); U16(actor + 48, 1); U64(actor + 24, actions); U64(actions, Str("EventExecuteProgressChallengeStep"));
        "DIC "u8.CopyTo(b.AsSpan(dict)); U32(dict + 4, 1); U64(dict + 32, Str("EntryPoint0")); U16(entries + 28, 94);
        void Action(int index, int next, int offset, string challenge, string step)
        {
            int ev = events + index * 48, container = offset, keys = offset + 0x40, one = offset + 0x80, two = offset + 0xa0;
            U16(ev + 10, next); U64(ev + 16, container); b[container] = 1; U16(container + 2, 2); U64(container + 8, keys);
            U64(container + 16, one); U64(container + 24, two); "DIC "u8.CopyTo(b.AsSpan(keys)); U32(keys + 4, 2);
            U64(keys + 32, Str("ChallengeName")); U64(keys + 48, Str("StepName")); b[one] = b[two] = 5;
            U64(one + 16, Str(challenge)); U64(two + 16, Str(step));
        }
        Action(56, 57, 0x1900, "Meet_DekuTree", "Complete");
        Action(57, 0xffff, 0x1a00, "Get_StoneOfForest", "GoInside");
        var changed = HAL_ConversationSkips.PatchDekuMeetingQuest(b);
        AssertTest.True(new HAL_EventFlow(changed, "Dm_Kokiri_0075").EntryPoints()["EntryPoint0"].EventIndex == 56 &&
            new HAL_EventFlow(b, "Dm_Kokiri_0075").EntryPoints()["EntryPoint0"].EventIndex == 94,
            "Deku meeting skip executes both original quest progress actions");
        bool rejected = false; try { HAL_ConversationSkips.PatchDekuMeetingQuest(changed); } catch (InvalidDataException) { rejected = true; }
        AssertTest.True(rejected, "Deku meeting quest patch rejects altered resources");
    }
}
