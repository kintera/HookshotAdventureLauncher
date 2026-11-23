using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HookshotAdventure.Core;

/// <summary>Composes verified seed and enhancement resources into a profile-local runtime overlay.</summary>
public static class HAL_RuntimeOverlay
{
    private const string EventPrefix = "Event/EventFlow/";
    private const string GameData = "GameData/GameDataList.Product.110.byml.zs";
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
    private static string Event(string name) => EventPrefix + name + ".bfevfl.zs";
    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    private static string SafePath(string root, string name)
    {
        if (string.IsNullOrEmpty(name) || Path.IsPathRooted(name) || name.Contains('\\') || name.Split('/').Any(p => p is "" or "." or ".."))
            throw new InvalidDataException("Invalid overlay resource path.");
        string path = root;
        foreach (string part in name.Split('/'))
        {
            path = Path.Combine(path, part);
            if (ProfileRepository.IsLink(path)) throw new InvalidDataException("Linked overlay resource.");
        }
        return path;
    }
    public static IReadOnlyList<byte[]> Dictionaries(string romfs) =>
        HAL_Zstd.ReadDictionaryArchive(File.ReadAllBytes(SafePath(romfs, "Pack/ZsDic.pack.zs")));

    public static string Prepare(string overlay, string gameDirectory, HAL_EnhancementPreferences preferences, bool experimentalAmmoGuard = false, bool independentBombchus = false, bool nativeAmmoGuard = false)
    {
        if (ProfileRepository.IsLink(overlay) || !Directory.Exists(overlay)) throw new InvalidDataException("Invalid seed overlay.");
        string romfs = Path.Combine(gameDirectory, "romfs"); var dictionaries = Dictionaries(romfs);
        byte[] Read(string root, string resource) => File.ReadAllBytes(SafePath(root, resource));
        byte[] Unpack(byte[] packed) => HAL_Zstd.Unpack(packed, dictionaries);
        var manifestBytes = Read(overlay, "manifest.json");
        using var manifest = JsonDocument.Parse(manifestBytes); var metadata = manifest.RootElement;
        var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        var sizes = new Dictionary<string, (long Original, long Patched)>(StringComparer.Ordinal);
        var resourceAliases = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in metadata.GetProperty("output_hashes").EnumerateObject())
        {
            byte[] packed = Read(overlay, item.Name);
            if (Hash(packed) != item.Value.GetString()) throw new InvalidDataException("Seed overlay resource was modified.");
            files.Add(item.Name, packed);
            if (item.Name.EndsWith(".bcett.byml.zs", StringComparison.Ordinal))
                sizes.Add(item.Name, (Unpack(Read(romfs, item.Name)).Length, Unpack(packed).Length));
            else if (item.Name.EndsWith(".bfevfl.zs", StringComparison.Ordinal))
            {
                long original = Unpack(Read(romfs, item.Name)).Length, patched = Unpack(packed).Length;
                if (patched > original) sizes.Add(item.Name, (original, patched));
            }
        }
        void Patch(string path, Func<byte[], byte[]> edit)
        {
            byte[] original = Unpack(Read(romfs, path));
            byte[] raw = files.TryGetValue(path, out var current) ? Unpack(current) : original;
            byte[] patched = edit(raw);
            files[path] = HAL_Zstd.Compress(patched);
            if (path.EndsWith(".bcett.byml.zs", StringComparison.Ordinal) ||
                ((path.EndsWith(".bfevfl.zs", StringComparison.Ordinal) || path.EndsWith(".rstbl.byml.zs", StringComparison.Ordinal)) && patched.Length > original.Length))
                sizes[path] = (original.Length, patched.Length);
            else
                sizes.Remove(path);
        }
        // Older shuffled-Saria seeds called the original pouch action with Num=0
        // and did not bypass the bridge lesson. Upgrade their runtime copy while
        // keeping the original, profile-bound seed and its assigned reward intact.
        if (files.ContainsKey(HAL_EventRewardPlan.SariaResource) && !files.ContainsKey(HAL_EventRewardPatch.SariaBridgeResource))
        {
            var legacy = new HAL_EventFlow(Unpack(files[HAL_EventRewardPlan.SariaResource]), "Dm_Kokiri_0155");
            if (legacy.Subflow(52) != ("GetPouchContent", "GetPouchContentByActorName") ||
                !legacy.StringParameters(52).TryGetValue("ActorName", out var reward) || !HAL_EventRewardPlan.IsRupee(reward) ||
                legacy.Action(51) != ("PouchSystemActor", "EventTriggerAddOrRemovePouchContent") ||
                legacy.ScalarParameter(51, "Num") != ((byte)2, 0u) ||
                !legacy.ForkRoutes(70).Branches.SequenceEqual(new[] { 66, 72, 51, 74 }))
                throw new InvalidDataException("Unrecognized legacy Saria reward overlay.");
            Patch(HAL_EventRewardPlan.SariaResource, _ => HAL_EventRewardPatch.SariaFairyOcarina(
                Unpack(Read(romfs, HAL_EventRewardPlan.SariaResource)), reward));
            Patch(HAL_EventRewardPatch.SariaBridgeResource, HAL_EventRewardPatch.SariaBridgeTutorial);
        }
        if (preferences.SkipOpening)
        {
            Patch(GameData, HAL_OpeningDefaults.Patch);
            Patch(Event("OpeningEvent"), HAL_OpeningCamera.Patch);
        }
        if (preferences.OpenKokiriForest || preferences.OpenDekuTree)
            Patch(GameData, raw => HAL_OpenForestDefaults.Patch(raw, preferences.OpenKokiriForest, preferences.OpenDekuTree));
        if (preferences.OpenDekuTree)
            Patch(Event("OpeningEvent"), HAL_OpenForestOpening.PatchQuestStep);
        if (preferences.SkipTutorialConversations)
            foreach (string name in new[] { "Npc_Kokiri_010_Child", "Npc_Kokiri_011_Child" })
                Patch(Event(name), data => HAL_ConversationSkips.PatchTutorial(data, name));
        if (preferences.SkipDekuFirstEntrance)
        {
            Patch(Event("Dm_Deku_0000"), HAL_ConversationSkips.PatchDekuEntrance);
            Patch(Event("NaviHint"), HAL_ConversationSkips.PatchDungeonNaviTutorial);
        }
        var hints = new List<string>();
        if (preferences.SkipTutorialConversations || preferences.SkipDekuFirstEntrance) hints.Add("Scrub_Deku");
        if (preferences.SkipDekuFirstEntrance) hints.AddRange(["Dekubaba_Withered", "Dekubaba", "Skullwalltula", "Gohma_Egg", "Gohma_Larva"]);
        if (hints.Count != 0) Patch(Event("Navi_EnemyHint_Normal_1"), data => HAL_ConversationSkips.PatchDekuNaviHints(data, hints));
        if (preferences.SkipDekuFirstEntrance || preferences.SkipDekuTreeClear) Patch(Event("Dm_SY_BancChangeArrive"), HAL_ConversationSkips.PatchDekuNaviStop);
        if (preferences.SkipDekuTreeMeeting)
        {
            Patch(Event("Meet_DekuTree"), HAL_ConversationSkips.PatchDekuMeeting);
            Patch(Event("Dm_Kokiri_0075"), HAL_ConversationSkips.PatchDekuMeetingQuest);
        }
        if (preferences.SkipKokiriSwordCutscene) Patch(Event("Get_StoneOfForest"), HAL_ConversationSkips.PatchSwordCutscene);
        if (preferences.SkipBossIntroductions)
            Patch(Event("Get_StoneOfForest"), HAL_BossIntroSkip.PatchGohma);
        if (preferences.SkipDekuTreeClear)
        {
            Patch(Event("Dm_SY_BancChange_EscapeDungeon"), HAL_ConversationSkips.PatchDekuTreeClear);
            Patch(Event("Npc_Kokiri_Mido_Child"), HAL_ConversationSkips.PatchMidoFarewell);
        }
        bool entrances = preferences.SkipEntranceCutscenes || preferences.SkipHyruleFieldEntrance || preferences.SkipCastleTownArrival ||
            preferences.SkipHyruleCastleIntro || preferences.SkipKakarikoIntro;
        string field = Event("Goto_HyruleCastle"), arrive = Event("Dm_SY_BancChangeArrive");
        if (entrances || preferences.SkipFirstOwl)
        {
            if (entrances) Patch(field, HAL_FieldSkips.PatchFieldEntrance);
            if (preferences.SkipFirstOwl) Patch(field, HAL_FieldSkips.PatchFirstOwl);
            if (entrances)
                Patch(arrive, HAL_FieldSkips.PatchCastleTown);
            Patch(Event("Dm_HrlCastle_0000"), HAL_FieldSkips.PatchHyruleCastle);
            if (entrances) Patch(Event("Dm_Kakariko_0000"), HAL_FieldSkips.PatchKakariko);
        }
        if (preferences.SkipChildStealth)
        {
            Patch(field, HAL_ChildStealth.PatchCastleEntrance);
            foreach (var (path, castle) in new[] { ("Banc/HyruleCastle/HyruleCastle_Static.bcett.byml.zs", true),
                ("Banc/HyruleField/HyruleField_Child_Static.bcett.byml.zs", false) })
                Patch(path, raw => HAL_ChildStealth.PatchMap(raw, castle));
        }
        if (independentBombchus)
            Patch(HAL_BombchuCapacity.Resource, HAL_BombchuCapacity.Patch);
        if (nativeAmmoGuard && metadata.TryGetProperty("plan", out var ammoPlan))
        {
            var ammoRoutes = HAL_NativeAmmoChest.Routes(ammoPlan);
            var largeActors = ammoRoutes.Values.SelectMany(x => x).Select(x => x.Actor)
                .Where(x => x is "Obj_TBox_A_02" or "Obj_TBox_A_02_NoBake").Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            if (largeActors.Length != 0)
            {
                byte[] conditional = Unpack(Read(romfs, HAL_ConditionalLargeChest.Pack(HAL_ConditionalLargeChest.Conditional)));
                foreach (string actor in largeActors)
                {
                    string alias = HAL_ConditionalLargeChest.Alias(actor), path = HAL_ConditionalLargeChest.Pack(alias), originalPath = HAL_ConditionalLargeChest.Pack(actor);
                    if (files.ContainsKey(path) || File.Exists(SafePath(romfs, path))) throw new InvalidDataException("Private ammunition actor conflicts with an existing resource.");
                    byte[] original = Unpack(Read(romfs, originalPath));
                    byte[] expanded = HAL_ConditionalLargeChest.Build(original, conditional, actor);
                    files.Add(path, HAL_Zstd.Compress(expanded)); resourceAliases.Add(path, originalPath); sizes.Add(path, (original.Length, expanded.Length));
                }
                foreach (string name in new[] { "ActorInfo", "GameActorInfo" })
                {
                    var additions = largeActors.Select(actor => new HAL_EventTableRow(HAL_ConditionalLargeChest.Alias(actor), actor, null)).ToArray();
                    Patch("RSDB/" + name + ".Product.111.rstbl.byml.zs", raw => HAL_EventTablePatch.AddRows(raw, additions));
                }
            }
            foreach (var (path, routes) in ammoRoutes)
                Patch(path, raw => { foreach (var route in routes) raw = HAL_NativeAmmoChest.Patch(raw, route); return raw; });
        }
        if (experimentalAmmoGuard)
        {
            var used = new SortedDictionary<string, (string Reward, bool Large)>(StringComparer.Ordinal);
            foreach (string map in HAL_ChestScope.Files(romfs, "full_game"))
            {
                string path = Path.GetRelativePath(romfs, map).Replace('\\', '/');
                byte[] raw = files.TryGetValue(path, out var current) ? Unpack(current) : Unpack(Read(romfs, path));
                var routes = HAL_ChestScanner.Scan(raw).Where(x => x.Event is null && x.Reward is not null &&
                    HAL_AmmoGuardFlow.RequiredBags(x.Reward) is not null)
                    .Select(x => new HAL_AmmoChestRoute(x.Placement, x.Actor, x.Reward!)).ToArray();
                if (routes.Length == 0) continue;
                foreach (var route in routes)
                {
                    string eventName = HAL_AmmoChestRoutes.EventName(route.Actor, route.Reward);
                    bool large = eventName.EndsWith("_Big", StringComparison.Ordinal);
                    used[eventName] = (route.Reward, large);
                }
                Patch(path, bytes => HAL_AmmoChestRoutes.Patch(bytes, routes));
            }
            if (used.Count != 0)
            {
                foreach (bool large in new[] { false, true })
                {
                    string[] rewards = used.Values.Where(x => x.Large == large).Select(x => x.Reward).Distinct(StringComparer.Ordinal).ToArray();
                    if (rewards.Length == 0) continue;
                    string name = large ? "Dm_SY_Treasurebox" : "Dm_SY_TreasureboxSmall";
                    Patch(Event(name), raw => { foreach (string reward in rewards) raw = HAL_AmmoGuardFlow.Add(raw, name, reward); return raw; });
                }
                foreach (string tableName in new[] { "EventSetting", "GameAnalyzedEventInfo" })
                {
                    var additions = used.Select(x => new HAL_EventTableRow(x.Key, x.Value.Large ? "Dm_SY_Treasurebox" : "Dm_SY_TreasureboxSmall",
                        tableName == "EventSetting" ? HAL_AmmoGuardFlow.EntryName(x.Value.Reward) : null)).ToArray();
                    Patch("RSDB/" + tableName + ".Product.111.rstbl.byml.zs", raw => HAL_EventTablePatch.AddRows(raw, additions));
                }
            }
        }
        string[] tables = Directory.GetFiles(Path.Combine(romfs, "System", "Resource"), "ResourceSizeTable.*.rsizetable.zs");
        if (tables.Length != 1) throw new InvalidDataException("Expected one resource size table.");
        string tablePath = Path.GetRelativePath(romfs, tables[0]).Replace('\\', '/');
        byte[] table = Unpack(Read(romfs, tablePath)); string prompts = Path.Combine(gameDirectory, "prompts", "rsizetable.zs");
        byte[]? extra = File.Exists(prompts) ? Unpack(File.ReadAllBytes(prompts)) : null;
        byte[] mergedTable = new HAL_ResourceSizeTable(table).Patch(new Dictionary<string, (long, long)>(), extra);
        if (resourceAliases.Count != 0) mergedTable = new HAL_ResourceSizeTable(mergedTable).CloneEntries(resourceAliases);
        files[tablePath] = HAL_Zstd.Compress(new HAL_ResourceSizeTable(mergedTable).Patch(sizes));
        files["manifest.json"] = manifestBytes;
        var checksums = new JsonObject(); foreach (var (name, bytes) in files) checksums[name] = Hash(bytes);
        var starting = metadata.TryGetProperty("plan", out var plan) && plan.TryGetProperty("starting_inventory", out var inventory)
            ? inventory.EnumerateArray().Where(x => x.GetProperty("item").GetString() is "Item_Dungeon_Map" or "Item_Dungeon_Compass")
                .Select(x => (x.GetProperty("dungeon").GetString()!, x.GetProperty("item").GetString()!)).ToArray() : [];
        var record = new JsonObject { ["format"] = "chest-map-size-support-v1", ["seed_overlay"] = Path.GetFileName(overlay),
            ["output_hashes"] = checksums, ["starting_dungeon_flags"] = JsonSerializer.SerializeToNode(HAL_StartingDungeonFlags.Resolve(starting, false)),
            ["child_stealth_start"] = preferences.SkipChildStealth, ["opening_skip"] = "native-opening-v2",
            ["skip_opening"] = preferences.SkipOpening, ["skip_boss_introductions"] = preferences.SkipBossIntroductions,
            ["skip_tutorial_conversations"] = preferences.SkipTutorialConversations, ["skip_deku_first_entrance"] = preferences.SkipDekuFirstEntrance,
            ["skip_deku_tree_meeting"] = preferences.SkipDekuTreeMeeting, ["skip_kokiri_sword_cutscene"] = preferences.SkipKokiriSwordCutscene,
            ["skip_deku_tree_clear"] = preferences.SkipDekuTreeClear, ["open_deku_tree"] = preferences.OpenDekuTree,
            ["open_kokiri_forest"] = preferences.OpenKokiriForest, ["skip_hyrule_field_entrance"] = entrances,
            ["skip_first_owl"] = preferences.SkipFirstOwl, ["skip_castle_town_arrival"] = entrances,
            ["skip_hyrule_castle_intro"] = entrances || preferences.SkipFirstOwl, ["skip_kakariko_intro"] = entrances,
            ["skip_entrance_cutscenes"] = entrances, ["stone_reward_fallback"] = preferences.SkipDekuTreeClear,
            ["experimental_ammo_guard"] = experimentalAmmoGuard, ["independent_bombchus"] = independentBombchus,
            ["native_small_chest_ammo_guard"] = nativeAmmoGuard, ["native_large_chest_ammo_guard"] = nativeAmmoGuard };
        if (preferences.SkipChildStealth)
        {
            string? seed = metadata.TryGetProperty("binding", out var binding) && binding.TryGetProperty("seed", out var seedValue) ? seedValue.GetString() : null;
            bool shuffled = false; string seedPath = Path.Combine(Path.GetDirectoryName(overlay)!, "seed.json");
            if (ProfileRepository.IsLink(seedPath)) throw new InvalidDataException("Linked seed metadata.");
            if (File.Exists(seedPath))
            {
                using var seedDocument = JsonDocument.Parse(File.ReadAllBytes(seedPath)); var seedRecord = seedDocument.RootElement;
                if (seedRecord.TryGetProperty("seed", out var s)) seed = s.GetString();
                if (seedRecord.TryGetProperty("settings", out var settings) && settings.TryGetProperty("shuffle_songs", out var shuffle)) shuffled = shuffle.GetBoolean();
            }
            record["child_stealth_song"] = HAL_ChildStealth.StartingSong(seed, shuffled);
        }
        using var recordDoc = JsonDocument.Parse(record.ToJsonString()); string digest = HAL_CanonicalJson.Digest(recordDoc.RootElement);
        string root = Path.Combine(Path.GetDirectoryName(overlay)!, "runtime-resources");
        if (ProfileRepository.IsLink(root)) throw new InvalidDataException("Linked runtime resource directory.");
        Directory.CreateDirectory(root); string target = Path.Combine(root, digest);
        if (Directory.Exists(target))
        {
            if (ProfileRepository.IsLink(target)) throw new InvalidDataException("Linked runtime overlay.");
            var actual = new HashSet<string>(StringComparer.Ordinal); var pending = new Stack<string>(); pending.Push(target);
            while (pending.Count != 0) foreach (string path in Directory.EnumerateFileSystemEntries(pending.Pop()))
            {
                if (ProfileRepository.IsLink(path)) throw new InvalidDataException("Linked runtime overlay entry.");
                if (Directory.Exists(path)) pending.Push(path); else actual.Add(Path.GetRelativePath(target, path).Replace('\\', '/'));
            }
            if (!actual.SetEquals(files.Keys.Append("runtime.json"))) throw new InvalidDataException("Runtime overlay contents differ.");
            using var existing = JsonDocument.Parse(Read(target, "runtime.json"));
            if (HAL_CanonicalJson.Serialize(existing.RootElement) != HAL_CanonicalJson.Serialize(recordDoc.RootElement) ||
                files.Any(x => Hash(Read(target, x.Key)) != Hash(x.Value))) throw new InvalidDataException("Runtime overlay was modified.");
            return target;
        }
        string stage = Path.Combine(root, ".sizes-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage);
        try
        {
            foreach (var (name, bytes) in files) { string path = SafePath(stage, name); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, bytes); }
            File.WriteAllText(Path.Combine(stage, "runtime.json"), record.ToJsonString(Indented) + "\n");
            Directory.Move(stage, target);
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
        return target;
    }
}
