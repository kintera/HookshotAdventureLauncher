// Synthetic BFEVFL flow; contains no game data.
using System.Buffers.Binary;
using System.Text;
using HookshotAdventure.Core;

static class BossIntroFixture
{
    public static void Check(string root)
    {
        byte[] b=new byte[0x10000]; int cursor=0x4000;
        void U16(int at,int n)=>BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(at),(ushort)n);
        void U32(int at,uint n)=>BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(at),n);
        void U64(int at,int n)=>BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(at),(ulong)n);
        int Str(string text){int p=cursor;byte[] s=Encoding.UTF8.GetBytes(text);U16(p,s.Length);s.CopyTo(b,p+2);cursor+=s.Length+3;return p;}
        const int flow=0x100, actors=0x200, events=0x400;
        int E(int n)=>events+n*48;
        "BFEVFL\0\0"u8.CopyTo(b);U16(8,0x400);U16(12,0xfeff);U16(32,1);U64(40,0x60);U64(0x60,flow);
        "EVFL"u8.CopyTo(b.AsSpan(flow));U16(flow+16,4);U16(flow+22,227);U16(flow+24,1);
        U64(flow+32,Str("Get_StoneOfForest"));U64(flow+40,actors);U64(flow+48,events);
        int dict=cursor;cursor+=48;"DIC "u8.CopyTo(b.AsSpan(dict));U32(dict+4,1);U64(dict+32,Str("Boss_Appear"));U64(flow+56,dict);
        int entries=cursor;cursor+=32;U16(entries+28,99);U64(flow+64,entries);
        var names=new[]{("GameDataActor","EventTriggerSetGameDataBool"),("Player_Event","EventPlayerUnequip"),
            ("EventSystemActor","EventTransition"),("Enemy_DungeonBoss_Gohma","EventSetBattlePresentationOnPreState")};
        for(int i=0;i<names.Length;i++){int a=actors+i*56;U64(a,Str(names[i].Item1));U16(a+48,1);int list=cursor;cursor+=8;U64(list,Str(names[i].Item2));U64(a+24,list);}
        int playerActions=cursor;cursor+=24;U64(playerActions,Str("EventPlayerUnequip"));U64(playerActions+8,Str("EventTriggerPlayerEventStartWithDrawn"));
        U64(playerActions+16,Str("EventPlayerWarpToStarterLinkAnchor"));U64(actors+56+24,playerActions);U16(actors+56+48,3);
        void Action(int index,int actor,int next){int e=E(index);b[e+8]=0;U16(e+10,next);U16(e+12,actor);}
        void Param(int index,string name,string value){int c=cursor;cursor+=24;int d=cursor;cursor+=48;int item=cursor;cursor+=24;
            b[c]=1;U16(c+2,1);U64(c+8,d);U64(c+16,item);"DIC "u8.CopyTo(b.AsSpan(d));U32(d+4,1);U64(d+32,Str(name));b[item]=5;U64(item+16,Str(value));U64(E(index)+16,c);}
        Action(99,0,98);Param(99,"GameDataName","Gohma_IsLightingForBattle");
        Action(98,1,114);U16(E(98)+14,1);Action(192,1,97);U16(E(192)+14,2);Param(192,"InstanceName","StartPos_Get_StoneOfForest");
        Action(97,1,0);Action(0,2,191);Param(0,"EventName","Dm_Deku_BossIn");Action(191,3,37);
        var output=HAL_BossIntroSkip.PatchGohma(b);var f=new HAL_EventFlow(output,"Get_StoneOfForest");
        AssertTest.True(f.UInt(E(97)+10,2)==191 && f.UInt(E(191)+10,2)==37 && new HAL_EventFlow(b,"Get_StoneOfForest").UInt(E(97)+10,2)==0,
            "Gohma intro bypass keeps encounter setup and the source unchanged");
        var invalid=(byte[])b.Clone();BinaryPrimitives.WriteUInt16LittleEndian(invalid.AsSpan(E(0)+10),37);
        bool rejected=false;try{HAL_BossIntroSkip.PatchGohma(invalid);}catch(InvalidDataException){rejected=true;}
        AssertTest.True(rejected,"Gohma intro skip rejects an altered battle-state route");

        // An enhancement must build on a seed's existing resource, including its allocation budget.
        string test=Path.Combine(root,"boss-overlay"), game=Path.Combine(test,"game"), rom=Path.Combine(game,"romfs"), overlay=Path.Combine(test,"seed");
        const string path="Event/EventFlow/Get_StoneOfForest.bfevfl.zs";
        Directory.CreateDirectory(Path.Combine(rom,"Pack")); Directory.CreateDirectory(Path.Combine(rom,"System/Resource"));
        Directory.CreateDirectory(Path.Combine(rom,"Event/EventFlow")); Directory.CreateDirectory(Path.Combine(overlay,"Event/EventFlow"));
        byte[] archive=new byte[0x20]; BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(0x0c),0x20);
        File.WriteAllBytes(Path.Combine(rom,"Pack/ZsDic.pack.zs"),HAL_Zstd.Compress(archive));
        File.WriteAllBytes(Path.Combine(rom,path),HAL_Zstd.Compress(b));
        byte[] seeded=b.Concat(Enumerable.Repeat((byte)0x5a,16)).ToArray(); byte[] packed=HAL_Zstd.Compress(seeded);
        File.WriteAllBytes(Path.Combine(overlay,path),packed);
        string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(packed)).ToLowerInvariant();
        File.WriteAllText(Path.Combine(overlay,"manifest.json"),$"{{\"output_hashes\":{{\"{path}\":\"{hash}\"}},\"plan\":{{}}}}");
        byte[] table=new byte[22+128+4]; "RESTBL"u8.CopyTo(table); BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(6),1);
        BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(10),128); BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(18),1);
        Encoding.UTF8.GetBytes(path[..^3]).CopyTo(table.AsSpan(22)); BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(150),(uint)b.Length);
        const string tableName="System/Resource/ResourceSizeTable.test.rsizetable.zs";
        File.WriteAllBytes(Path.Combine(rom,tableName),HAL_Zstd.Compress(table));
        var preferences=new HAL_EnhancementPreferences(SkipOpening:false,SkipTutorialConversations:false,SkipDekuFirstEntrance:false,
            SkipDekuTreeMeeting:false,SkipKokiriSwordCutscene:false,SkipDekuTreeClear:false,SkipBossIntroductions:true);
        string runtime=HAL_RuntimeOverlay.Prepare(overlay,game,preferences);
        byte[] result=HAL_Zstd.Unpack(File.ReadAllBytes(Path.Combine(runtime,path)),Array.Empty<byte[]>());
        byte[] resultTable=HAL_Zstd.Unpack(File.ReadAllBytes(Path.Combine(runtime,tableName)),Array.Empty<byte[]>());
        AssertTest.True(result.AsSpan(b.Length).SequenceEqual(seeded.AsSpan(b.Length)) &&
            new HAL_EventFlow(result,"Get_StoneOfForest").UInt(E(97)+10,2)==191 &&
            BinaryPrimitives.ReadUInt32LittleEndian(resultTable.AsSpan(150))>=result.Length,
            "runtime enhancement preserves seed event edits and budgets the combined resource");
    }
}
