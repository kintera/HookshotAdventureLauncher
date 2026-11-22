// Synthetic local textures and models; no game data.
using System.Buffers.Binary;
using HookshotAdventure.Core;
static class CosmeticFixture
{
    private static void U16(byte[] b, int at, int value) => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(at), (ushort)value);
    private static void U32(byte[] b, int at, int value) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(at), (uint)value);
    private static void U64(byte[] b, int at, int value) => BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(at), (ulong)value);
    private static byte[] Texture(string name, ushort color)
    {
        var b = new byte[0x300]; "BNTX"u8.CopyTo(b); "Ounc"u8.CopyTo(b.AsSpan(0x20)); U32(b,0x24,1); U64(b,0x28,0x40); U64(b,0x40,0x80);
        "BRTI"u8.CopyTo(b.AsSpan(0x80)); U16(b,0x96,1); U32(b,0x9c,0x1a06); U32(b,0xa4,4); U32(b,0xa8,4); U32(b,0xac,1); U32(b,0xb0,1);
        U32(b,0xd0,8); U64(b,0xe0,0x180); U64(b,0xf0,0x1c0); byte[] text = System.Text.Encoding.UTF8.GetBytes(name); U16(b,0x180,text.Length); text.CopyTo(b,0x182);
        U64(b,0x1c0,0x200); U16(b,0x200,color); U16(b,0x202,0); return b;
    }
    private static byte[] Model(params (string Material,string Texture)[] values)
    {
        var b = new byte[0x1000]; new byte[] { 0x46,0x52,0x45,0x53,32,32,32,32,0,4,10,0,255,254 }.CopyTo(b,0);
        U64(b,40,0x80); U64(b,48,0x70); U32(b,0x74,1); "FMDL"u8.CopyTo(b.AsSpan(0x80)); U64(b,0xb8,0x100); U16(b,0xec,values.Length);
        for(int i=0;i<values.Length;i++)
        {
            int m=0x100+i*176, title=0x500+i*0x100, names=title+0x50, text=title+0x70; "FMAT"u8.CopyTo(b.AsSpan(m)); U64(b,m+8,title); U64(b,m+32,names); b[m+163]=1;
            byte[] label=System.Text.Encoding.UTF8.GetBytes(values[i].Material), texture=System.Text.Encoding.UTF8.GetBytes(values[i].Texture);
            U16(b,title,label.Length); label.CopyTo(b,title+2); U64(b,names,text); U16(b,text,texture.Length); texture.CopyTo(b,text+2);
        }
        return b;
    }
    private static byte[] Effect()
    {
        var b = new byte[512]; "BNSH"u8.CopyTo(b); "grsc"u8.CopyTo(b.AsSpan(96)); U32(b,28,b.Length);
        for (int i = 0; i < 3; i++)
        {
            int at = 128 + i * 16;
            U32(b,at,unchecked((int)(0x12107a20u + ((uint)i << 16))));
            U32(b,at+4,unchecked((int)(0x03805c00u + ((uint)i << 8))));
            U32(b,at+8,0x00410000); U32(b,at+12,0x000fe200);
        }
        return b;
    }
    public static void Check(string root, string codec)
    {
        string baseDir=Path.Combine(root,"cosmetics"),game=Path.Combine(baseDir,"game"),romfs=Path.Combine(game,"romfs"),profile=Path.Combine(baseDir,"profile");
        foreach(string p in new[]{"Pack","Tex","Model","Effect","System/Resource"}) Directory.CreateDirectory(Path.Combine(romfs,p)); Directory.CreateDirectory(profile);
        var archive=new byte[0x20]; U32(archive,0x0c,0x20); File.WriteAllBytes(Path.Combine(romfs,"Pack/ZsDic.pack.zs"),HAL_Zstd.Compress(archive));
        var textures=new (string Name,ushort Color)[] { ("Mt_Vest_Player_Child_Alb",0x07e0),("Mt_Cloth_Parts_Player_Child_Alb",0x07e0),
            ("Mt_Vest_Player_Adult_Alb",0x07e0),("Mt_Angin_Acs_Player_Adult_Alb",0x07e0),("Angin_Tile_A_Alb",0x07e0),("Angin_Tile_A_Dark_Alb",0x07e0),
            ("Mt_Angin_Armor_Adult_Goron_Alb",0xf800),("Mt_Angin_Armor_Adult_Zora_Alb",0x001f),("Mt_Weapon_Sword_Kokiri_Alb",0xffff),
            ("Mt_Weapon_Sword_Master_Alb",0xffff),("Mt_Weapon_Sword_Biggoron_Alb.00",0xffff),("Mt_Weapon_Sword_Biggoron_Alb.01",0xffff),
            ("Mt_Item_Hammer_Alb",0xffff) };
        foreach(var (name,color) in textures) File.WriteAllBytes(Path.Combine(romfs,"Tex",name+".bntx.zs"),HAL_Zstd.Compress(Texture(name,color)));
        foreach (string sword in new[] { "Kokiri", "Master", "Biggoron" })
            File.WriteAllBytes(Path.Combine(romfs,"Effect",$"Weapon_Sword_{sword}.Nin_Ounce_NVN2.vsetb.byml.zs"), HAL_Zstd.Compress(Effect()));
        File.WriteAllBytes(Path.Combine(romfs,"Model/Player_Child.Player_Child.bfres.zs"),HAL_Zstd.Compress(Model(("Mt_Cloth","Angin_Tile_A_Alb"),("Mt_Cloth_Pants","Angin_Tile_A_Dark_Alb"))));
        File.WriteAllBytes(Path.Combine(romfs,"Model/Player_Adult.Player_Adult.bfres.zs"),HAL_Zstd.Compress(Model(("Mt_Angin","Angin_Tile_A_Alb"))));
        const int width=128;var table=new byte[22+2*(width+4)];"RESTBL"u8.CopyTo(table);U32(table,6,1);U32(table,10,width);U32(table,18,2);
        System.Text.Encoding.UTF8.GetBytes("Tex/Angin_Tile_A_Alb.bntx").CopyTo(table,22);U32(table,22+width,4096);
        System.Text.Encoding.UTF8.GetBytes("Tex/Angin_Tile_A_Dark_Alb.bntx").CopyTo(table,22+width+4);U32(table,22+2*width+4,4096);
        File.WriteAllBytes(Path.Combine(romfs,"System/Resource/ResourceSizeTable.test.rsizetable.zs"),HAL_Zstd.Compress(table));
        Directory.CreateDirectory(Path.Combine(game,"prompts"));
        byte[] promptTable = (byte[])table.Clone(); U32(promptTable,22+width,8192);
        File.WriteAllBytes(Path.Combine(game,"prompts/rsizetable.zs"), HAL_Zstd.Compress(promptTable));
        string overlay=HAL_CosmeticOverlay.Prepare(game,profile,new HAL_TunicCosmetics(),null,codec);
        AssertTest.True(File.Exists(Path.Combine(overlay,"cosmetics.json")) && File.Exists(Path.Combine(overlay,"Tex/HS_Kokiri_01_Alb.bntx.zs")),"C# cosmetic composer builds a synthetic resource overlay");
        byte[] masterTrail = HAL_Zstd.Unpack(File.ReadAllBytes(Path.Combine(overlay,"Effect/Weapon_Sword_Master.Nin_Ounce_NVN2.vsetb.byml.zs")), []);
        AssertTest.True(Math.Abs(BinaryPrimitives.ReadSingleLittleEndian(masterTrail.AsSpan(132)) - 0x77 / 255f) < 0.00001f,
            "cosmetic overlay applies the selected sword trail color");
        byte[] mergedTable = HAL_Zstd.Unpack(File.ReadAllBytes(Path.Combine(overlay,"System/Resource/ResourceSizeTable.test.rsizetable.zs")), []);
        AssertTest.True(BinaryPrimitives.ReadUInt32LittleEndian(mergedTable.AsSpan(22 + 2 * 8 + width)) == 8192,
            "standalone cosmetics retain the game's prompt resource budgets");
        AssertTest.True(HAL_CosmeticOverlay.Prepare(game,profile,new HAL_TunicCosmetics(),null,codec)==overlay,"C# cosmetic composer validates and reuses its overlay");
        AssertTest.True(!File.Exists(Path.Combine(overlay,"Tex/Mt_Item_Hammer_Alb.bntx.zs")), "Hammer texture stays unchanged when custom color is off");
        var hammer = new HAL_TunicCosmetics { HammerEnabled = true, Hammer = new HAL_TunicSetting("#FF0000", "locked") };
        string hammerOverlay = HAL_CosmeticOverlay.Prepare(game,profile,hammer,null,codec);
        byte[] hammerRaw = HAL_Zstd.Unpack(File.ReadAllBytes(Path.Combine(hammerOverlay,"Tex/Mt_Item_Hammer_Alb.bntx.zs")), []);
        var hammerTex = new HAL_TunicTexture(hammerRaw);
        var hammerPixels = HAL_BcTextures.Decode(hammerTex.Linear(0), hammerTex.Width, hammerTex.Height, false);
        AssertTest.True(hammerPixels[0] > hammerPixels[1] && hammerPixels[0] > hammerPixels[2], "Hammer color changes the hammer texture");
        string baseOverlay = Path.Combine(baseDir,"base-overlay"), baseProfile = Path.Combine(baseDir,"base-profile");
        Directory.CreateDirectory(Path.Combine(baseOverlay,"Effect")); Directory.CreateDirectory(baseProfile);
        string effectName = "Effect/Weapon_Sword_Kokiri.Nin_Ounce_NVN2.vsetb.byml.zs";
        byte[] modifiedEffect = Effect(); modifiedEffect[400] = 7;
        File.WriteAllBytes(Path.Combine(baseOverlay,effectName), HAL_Zstd.Compress(modifiedEffect));
        File.WriteAllText(Path.Combine(baseOverlay,"runtime.json"), "{\"output_hashes\":{\"" + effectName + "\":\"old\"}}");
        string composed = HAL_CosmeticOverlay.Prepare(game,baseProfile,new HAL_TunicCosmetics(),baseOverlay,codec);
        byte[] composedEffect = File.ReadAllBytes(Path.Combine(composed,effectName));
        using var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(composed,"runtime.json")));
        string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(composedEffect)).ToLowerInvariant();
        AssertTest.True(HAL_Zstd.Unpack(composedEffect, [])[400] == 7 &&
            manifest.RootElement.GetProperty("output_hashes").GetProperty(effectName).GetString() == hash,
            "trail colors preserve existing effect overlays and update their runtime hash");
    }
}
