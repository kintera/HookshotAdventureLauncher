// Authored actor archives and BYML, with no game resources.
using System.Text;
using System.Buffers.Binary;
using System.Text.Json;
using HookshotAdventure.Core;

static class ConditionalLargeChestFixture
{
    private static byte[] Actor(string parent, bool little)
    {
        var result = new List<byte>(new byte[16]); result[0] = (byte)(little ? 'Y' : 'B'); result[1] = (byte)(little ? 'B' : 'Y');
        void Put(int at, uint value, int size) { for (int i = 0; i < size; i++) result[at + (little ? i : size - 1 - i)] = (byte)(value >> (8 * i)); }
        void Write(uint value, int size) { int at = result.Count; result.AddRange(new byte[size]); Put(at, value, size); }
        void Align() { while ((result.Count & 3) != 0) result.Add(0); }
        int Strings(string value)
        {
            Align(); int at = result.Count; result.Add(0xc2); Write(1, 3); byte[] text = Encoding.UTF8.GetBytes(value);
            Write(12, 4); Write((uint)(13 + text.Length), 4); result.AddRange(text); result.Add(0); return at;
        }
        int keys = Strings("$parent"), values = Strings(parent); Align(); int root = result.Count;
        result.Add(0xc1); Write(1, 3); Write(0, 3); result.Add(0xa0); Write(0, 4);
        Put(4, (uint)keys, 4); Put(8, (uint)values, 4); Put(12, (uint)root, 4); return result.ToArray();
    }

    public static void Check()
    {
        const string large = "Obj_TBox_A_02", parent = "Work/Actor/Obj_TBox_A_01.engine__actor__ActorParam.gyml";
        string path(string actor) => "Actor/" + actor + ".engine__actor__ActorParam.bgyml";
        foreach (bool little in new[] { true, false })
        {
            var largeMembers = new Dictionary<string, byte[]> { [path(large)] = Actor(parent, little), ["shared"] = new byte[] { 1, 2 }, ["large"] = new byte[] { 3 } };
            var conditionalMembers = new Dictionary<string, byte[]> { [path(HAL_ConditionalLargeChest.Conditional)] = Actor(parent, little), ["shared"] = new byte[] { 1, 2 }, ["conditional"] = new byte[] { 4 } };
            byte[] a = HAL_ActorArchive.Write(largeMembers), b = HAL_ActorArchive.Write(conditionalMembers);
            byte[] merged = HAL_ConditionalLargeChest.Build(a, b, large); var files = HAL_ChestEvidence.SarcFiles(merged);
            var alias = new HAL_Byml(files[path(HAL_ConditionalLargeChest.Regular)]);
            AssertTest.True(alias.Text(alias.Fields(alias.Root)["$parent"]) == "Work/Actor/Obj_TBox_A_02.engine__actor__ActorParam.gyml" &&
                files["large"].SequenceEqual(new byte[] { 3 }) && files["conditional"].SequenceEqual(new byte[] { 4 }) &&
                merged.SequenceEqual(HAL_ConditionalLargeChest.Build(a, b, large)),
                "large conditional actor preserves both archives and inherits the large chest deterministically in " + (little ? "little" : "big") + " endian");
            conditionalMembers["shared"] = new byte[] { 9 }; bool refused = false;
            try { HAL_ConditionalLargeChest.Build(a, HAL_ActorArchive.Write(conditionalMembers), large); } catch (InvalidDataException) { refused = true; }
            AssertTest.True(refused, "large conditional actor rejects conflicting archive dependencies");
        }
        AssertTest.True(HAL_ConditionalLargeChest.Alias("Obj_TBox_A_02_NoBake") == HAL_ConditionalLargeChest.NoBake,
            "large no-bake chest uses a separate private actor");
    }

    private static byte[] Table(string id)
    {
        var b = new List<byte>(new byte[16]); b[0] = (byte)'Y'; b[1] = (byte)'B';
        void Put(int at, uint n, int size = 4) { for (int i = 0; i < size; i++) b[at + i] = (byte)(n >> (8 * i)); }
        void Write(uint n, int size = 4) { int at = b.Count; b.AddRange(new byte[size]); Put(at, n, size); }
        void Align() { while ((b.Count & 3) != 0) b.Add(0); }
        int Strings(string text) { Align(); int at = b.Count; b.Add(0xc2); Write(1, 3); var s = Encoding.UTF8.GetBytes(text); Write(12); Write((uint)(13 + s.Length)); b.AddRange(s); b.Add(0); return at; }
        int keys = Strings("__RowId"), values = Strings(id); Align(); int row = b.Count;
        b.Add(0xc1); Write(1, 3); Write(0, 3); b.Add(0xa0); Write(0); Align(); int array = b.Count;
        b.Add(0xc0); Write(1, 3); b.Add(0xc1); Align(); Write((uint)row); Put(4, (uint)keys); Put(8, (uint)values); Put(12, (uint)array); return b.ToArray();
    }

    public static void CheckRuntime(string root, byte[] originalMap, byte[] seedMap, bool little)
    {
        string test = Path.Combine(root, "large-runtime-" + little), game = Path.Combine(test, "game"), rom = Path.Combine(game, "romfs"), seed = Path.Combine(test, "seed");
        const string map = "Banc/KokiriForest/test.bcett.byml.zs", big = "Obj_TBox_A_02", conditional = HAL_ConditionalLargeChest.Conditional;
        string ActorPath(string actor) => "Actor/" + actor + ".engine__actor__ActorParam.bgyml";
        const string parent = "Work/Actor/Obj_TBox_A_01.engine__actor__ActorParam.gyml";
        void FileAt(string baseDir, string path, byte[] data) { string dest = Path.Combine(baseDir, path.Replace('/', Path.DirectorySeparatorChar)); Directory.CreateDirectory(Path.GetDirectoryName(dest)!); File.WriteAllBytes(dest, data); }
        byte[] dictionary = new byte[0x20]; BinaryPrimitives.WriteUInt32LittleEndian(dictionary.AsSpan(0x0c), 0x20);
        FileAt(rom, "Pack/ZsDic.pack.zs", HAL_Zstd.Compress(dictionary));
        FileAt(rom, map, HAL_Zstd.Compress(originalMap));
        var bigPack = HAL_ActorArchive.Write(new Dictionary<string, byte[]> { [ActorPath(big)] = Actor(parent, little), ["big"] = new byte[] { 3 } });
        var conditionalPack = HAL_ActorArchive.Write(new Dictionary<string, byte[]> { [ActorPath(conditional)] = Actor(parent, little), ["conditional"] = new byte[] { 4 } });
        FileAt(rom, HAL_ConditionalLargeChest.Pack(big), HAL_Zstd.Compress(bigPack));
        FileAt(rom, HAL_ConditionalLargeChest.Pack(conditional), HAL_Zstd.Compress(conditionalPack));
        string[] tables = { "RSDB/ActorInfo.Product.111.rstbl.byml.zs", "RSDB/GameActorInfo.Product.111.rstbl.byml.zs" };
        foreach (string table in tables) FileAt(rom, table, HAL_Zstd.Compress(Table(big)));
        string[] resources = new[] { map, HAL_ConditionalLargeChest.Pack(big) }.Concat(tables).ToArray();
        const int width = 160; byte[] budgets = new byte[22 + resources.Length * (width + 4)];
        "RESTBL"u8.CopyTo(budgets); BinaryPrimitives.WriteUInt32LittleEndian(budgets.AsSpan(6), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(budgets.AsSpan(10), width); BinaryPrimitives.WriteUInt32LittleEndian(budgets.AsSpan(18), (uint)resources.Length);
        for (int i = 0; i < resources.Length; i++) { int at = 22 + i * (width + 4); Encoding.UTF8.GetBytes(resources[i][..^3]).CopyTo(budgets.AsSpan(at)); BinaryPrimitives.WriteUInt32LittleEndian(budgets.AsSpan(at + width), 65536); }
        FileAt(rom, "System/Resource/ResourceSizeTable.test.rsizetable.zs", HAL_Zstd.Compress(budgets));
        byte[] compressed = HAL_Zstd.Compress(seedMap); FileAt(seed, map, compressed);
        string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(compressed)).ToLowerInvariant();
        byte[] manifest = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { output_hashes = new Dictionary<string, string> { [map] = hash },
            plan = new { assignments = new[] { new { placement = 16757992849254467386UL, actor = big, reward = "TBox_ArrowBundle_Medium", resources = new[] { map } } } } }));
        FileAt(seed, "manifest.json", manifest);
        var prefs = new HAL_EnhancementPreferences(SkipOpening: false, SkipTutorialConversations: false, SkipDekuFirstEntrance: false,
            SkipDekuTreeMeeting: false, SkipKokiriSwordCutscene: false, SkipDekuTreeClear: false);
        string runtime = HAL_RuntimeOverlay.Prepare(seed, game, prefs, nativeAmmoGuard: true);
        var patched = new HAL_Byml(HAL_Zstd.Unpack(File.ReadAllBytes(Path.Combine(runtime, map)), Array.Empty<byte[]>()));
        var chest = patched.Array(patched.Fields(patched.Root)["Actors"]).Select(x => patched.Fields((int)patched.Child(x, 0xc1)))
            .Single(x => x.TryGetValue("Hash", out var h) && patched.UInt((int)patched.Child(h, 0xd5), 8) == 16757992849254467386UL);
        var fields = patched.Fields((int)patched.Child(chest["Dynamic"], 0xc1));
        var pack = HAL_ChestEvidence.SarcFiles(HAL_Zstd.Unpack(File.ReadAllBytes(Path.Combine(runtime, HAL_ConditionalLargeChest.Pack(HAL_ConditionalLargeChest.Regular))), Array.Empty<byte[]>()));
        AssertTest.True(patched.Text(chest["Gyaml"]) == HAL_ConditionalLargeChest.Regular && patched.Text(fields["Drop__DropActor"]) == "TBox_Rupee_Blue" &&
            patched.Text(fields["Drop__DropActor_Substitute"]) == "TBox_ArrowBundle_Medium" && pack.ContainsKey(ActorPath(HAL_ConditionalLargeChest.Regular)) &&
            File.ReadAllBytes(Path.Combine(seed, map)).SequenceEqual(compressed) && File.ReadAllBytes(Path.Combine(seed, "manifest.json")).SequenceEqual(manifest) &&
            HAL_RuntimeOverlay.Prepare(seed, game, prefs, nativeAmmoGuard: true) == runtime,
            "large ammo runtime composes its private actor and keeps seed files immutable in " + (little ? "little" : "big") + " endian");
    }
}
