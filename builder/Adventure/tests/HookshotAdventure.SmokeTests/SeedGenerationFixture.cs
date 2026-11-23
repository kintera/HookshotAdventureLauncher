// Synthetic seed-generation fixture; no game data.
using HookshotAdventure.Core;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.Json;
static class SeedGenerationFixture
{
    public static void Check(string root)
    {
        string test = Path.Combine(root, "seed-generation"), game = Path.Combine(test, "game"), profile = Path.Combine(test, "profile");
        string romfs = Path.Combine(game, "romfs"), pack = Path.Combine(romfs, "Pack"), region = Path.Combine(romfs, "Banc", "KokiriForest");
        string gameData = Path.Combine(romfs, "GameData"), resource = Path.Combine(romfs, "System", "Resource");
        foreach (string dir in new[] { pack, region, gameData, resource, profile }) Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(game, "hookshot_randomizer_capability.txt"), "HOOKSHOT_DEV_RANDOMIZER=1\n");
        File.WriteAllText(Path.Combine(game, "hookshot_bombchu_capacity_capability.txt"), "HOOKSHOT_BOMBCHU_CAPACITY=1\n");
        var prefs = new HAL_EnhancementPreferences(SkipOpening: false, SkipTutorialConversations: false, SkipDekuFirstEntrance: false, SkipDekuTreeMeeting: false, SkipKokiriSwordCutscene: false, SkipDekuTreeClear: false);
        HAL_EnhancementPreferences.Save(game, prefs);
        var archive = new byte[0x20]; BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(0x0c), 0x20);
        File.WriteAllBytes(Path.Combine(pack, "ZsDic.pack.zs"), HAL_Zstd.Compress(archive));
        using var compressed = new ZLibStream(new MemoryStream(Convert.FromBase64String("eJyLdGJnEGBgYGgA4gwmBoZDHAwMGkC2LhAbAbE9ELsAcTAQJ4LUAHEeEDvl5+cwuCSWJDK4pKYlluaUhCXmlKYyeCQWZzAEpRanloRUFqRCxIITy1LdMnNSPfNSUisYgkuKSpNLGCBSQPuAGGglw0EWBgag9ReATAZmBobL6inbu4FCF0FyrECaAYsaf58ZRYTUzOtaVE5IzSV16dmE1Dy8+VeBkJptcYZ6hNRUhJkIoas5wA5UBwEMHUD+GgaIhi9ALMHIwGADxAlAfJAJYsbsWh0TdmgYwcR4zjW8ZoeGCUzstVjkWnZoGBxgBtvBsAZozgEgvgI1D4gOvGCEqN95mlUbpjfJ5vsTdqifQOoPAt0iwITQowJVt3nW68gDIHGggj9AdQZQNSBvtQD5bEDaBSQGZIOMCQGyAVdOhBA=")), CompressionMode.Decompress);
        using var output = new MemoryStream(); compressed.CopyTo(output);
        File.WriteAllBytes(Path.Combine(gameData, "GameDataList.Product.110.byml.zs"), HAL_Zstd.Compress(output.ToArray()));
        var pouchFile = Path.Combine(romfs, HAL_BombchuCapacity.Resource.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(pouchFile)!);
        File.WriteAllBytes(pouchFile, HAL_Zstd.Compress(BombchuCapacityFixture.Create()));
        const string map = "Banc/KokiriForest/fixture.bcett.byml.zs";
        File.WriteAllBytes(Path.Combine(region, "fixture.bcett.byml.zs"), HAL_Zstd.Compress(Convert.FromBase64String("WUIHABAAAABoAAAA6AEAAMIGAAAgAAAAJwAAADcAAAA/AAAATAAAAFIAAABXAAAAQWN0b3JzAERyb3BfX0Ryb3BBY3RvcgBEeW5hbWljAEdldEV2ZW50TmFtZQBHeWFtbABIYXNoAADCBwAAJAAAADIAAABCAAAAVAAAAF8AAABtAAAAkgAAAK0AAABPYmpfVEJveF9BXzAxAFRCb3hfUnVwZWVfQmx1ZQBJdGVtX1N3b3JkX0tva2lyaQBVbmV4cGVjdGVkAE9ial9UQm94X0FfMDIAS29raXJpRm9yZXN0X1RyZWFzdXJlQm94X05hdmlDaGF0T2ZmAEdldF9TdG9uZU9mRm9yZXN0X0dldFN3b3JkAAAAAPwYLOe/APeGwQIAAAEAAKABAAAAAwAAoAUAAADBAwAAAgAAwSABAAAEAACgAAAAAAUAANUYAQAAewAAAAAAAADBAwAAAgAAwSABAAAEAACgAAAAAAUAANVQAQAAOkdZcLVZkOjBAgAAAQAAoAIAAAADAACgBgAAAMEDAAACAADBfAEAAAQAAKAEAAAABQAA1XQBAAAVAwAAAAAAAMEDAAACAADBfAEAAAQAAKAEAAAABQAA1awBAADABAAAwcHBwTQBAABYAQAAkAEAALQBAADBAQAAAAAAwNABAAA=")));
        var table = new byte[22 + 128 + 4]; "RESTBL"u8.CopyTo(table);
        BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(6), 1); BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(10), 128); BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(18), 1);
        System.Text.Encoding.UTF8.GetBytes(map[..^3]).CopyTo(table.AsSpan(22)); BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(150), 4096);
        File.WriteAllBytes(Path.Combine(resource, "ResourceSizeTable.fixture.rsizetable.zs"), HAL_Zstd.Compress(table));
        const string id = "0123456789abcdef0123456789abcdef";
        bool startingBuildRequired = false;
        try { HAL_RandomizerSeedService.Generate(game, profile, id, "0000000000001234", new HAL_RandomizerSettings("kokiri_deku", false, false, false, true, false)); }
        catch (InvalidOperationException ex) { startingBuildRequired = ex.Message.Contains("updated Hookshot-Dev"); }
        AssertTest.True(startingBuildRequired && !Directory.Exists(Path.Combine(profile, "native-randomizer")),
            "starting items reject older runtimes before creating seed files");
        HAL_SeedGenerator.Generate(game, profile, id, "0000000000001234", new HAL_RandomizerSettings("kokiri_deku", false, false, false));
        string rando = Path.Combine(profile, "native-randomizer"); using var record = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(rando, "seed.json")));
        string overlay = HAL_SeedIdentity.ValidateOverlay(record.RootElement, id, rando);
        AssertTest.True(record.RootElement.GetProperty("content_hash").GetString() == HAL_CanonicalJson.DigestWithoutProperty(record.RootElement, "content_hash"), "generated seed has a valid content identity");
        AssertTest.True(record.RootElement.GetProperty("settings").GetProperty("requested_maximum_skulltulas").GetInt32()==50 &&
            !record.RootElement.GetProperty("settings").GetProperty("maximum_skulltulas_enforced").GetBoolean(),
            "generated seed records the requested Skulltula cap without claiming it is enforced");
        HAL_SeedIdentity.ValidateSourceResources(record.RootElement, romfs, HAL_RuntimeOverlay.Dictionaries(romfs));
        AssertTest.True(Directory.Exists(overlay) && record.RootElement.GetProperty("seed").GetString() == "0000000000001234", "C# seed generation produces a valid profile-bound overlay");
        string excludedProfile = Path.Combine(test, "excluded-profile"); Directory.CreateDirectory(excludedProfile);
        HAL_SeedGenerator.Generate(game, excludedProfile, id, "0000000000001234",
            new HAL_RandomizerSettings("kokiri_deku", false, false, false, ExcludedChecks: new[] { "86F700BFE72C18FC" }));
        string excludedRoot = Path.Combine(excludedProfile, "native-randomizer");
        using var excludedRecord = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(excludedRoot, "seed.json")));
        var fixedCheck = excludedRecord.RootElement.GetProperty("locations").EnumerateObject().Select(x => x.Value)
            .Single(row => row.GetProperty("check_id").GetString() == "86F700BFE72C18FC");
        HAL_SeedIdentity.ValidateOverlay(excludedRecord.RootElement, id, excludedRoot);
        AssertTest.True(fixedCheck.GetProperty("shuffled").GetBoolean() && fixedCheck.GetProperty("progression_excluded").GetBoolean() &&
            HAL_ChestPermutation.IsSafeFiller(fixedCheck.GetProperty("pickup_actor").GetString()!),
            "generated seed binds an excluded chest as a shuffled filler-only check");
        var env = HAL_RandomizerSeedService.PrepareLaunchEnvironment(game, profile, id);
        AssertTest.True(Directory.Exists(env["HOS_FS_OVERLAY"]), "C# seed preparation reloads the generated profile");
        AssertTest.True(env["HOS_RANDO_BOMBCHU_CAPACITY"] == "1", "randomized launches enable dynamic bombchu capacity");
        using (var runtime = JsonDocument.Parse(File.ReadAllText(Path.Combine(env["HOS_FS_OVERLAY"], "runtime.json"))))
            AssertTest.True(runtime.RootElement.GetProperty("independent_bombchus").GetBoolean() &&
                runtime.RootElement.GetProperty("native_small_chest_ammo_guard").GetBoolean() &&
                File.Exists(Path.Combine(env["HOS_FS_OVERLAY"], HAL_BombchuCapacity.Resource.Replace('/', Path.DirectorySeparatorChar))),
                "randomized launch composes bombchu capacity and small-chest ammo guards");
        string guardOverlay = Path.Combine(test, "ammo-seed"), guardResource = Path.Combine(guardOverlay, map.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(guardResource)!);
        byte[] baseMap = HAL_Zstd.Unpack(File.ReadAllBytes(Path.Combine(romfs, map.Replace('/', Path.DirectorySeparatorChar))), HAL_RuntimeOverlay.Dictionaries(romfs));
        const ulong guardPlacement = 9725242744547055868UL;
        var ammoEdit = new HAL_ChestAssignment(guardPlacement, "Obj_TBox_A_01", "TBox_Rupee_Blue", "KokiriForest_TreasureBox_NaviChatOff",
            "TBox_ArrowBundle_Medium", "KokiriForest_TreasureBox_NaviChatOff", new[] { map });
        byte[] seedMap = HAL_Zstd.Compress(HAL_ChestPatcher.Patch(baseMap, new[] { ammoEdit }));
        File.WriteAllBytes(guardResource, seedMap);
        string seedHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(seedMap)).ToLowerInvariant();
        File.WriteAllText(Path.Combine(guardOverlay, "manifest.json"), JsonSerializer.Serialize(new
        {
            output_hashes = new Dictionary<string, string> { [map] = seedHash },
            plan = new { assignments = new[] { new { placement = guardPlacement, actor = ammoEdit.Actor, reward = ammoEdit.Reward, resources = new[] { map } } } }
        }));
        byte[] seedManifest = File.ReadAllBytes(Path.Combine(guardOverlay, "manifest.json"));
        string guarded = HAL_RuntimeOverlay.Prepare(guardOverlay, game, prefs, nativeAmmoGuard: true);
        var guardedMap = new HAL_Byml(HAL_Zstd.Unpack(File.ReadAllBytes(Path.Combine(guarded, map.Replace('/', Path.DirectorySeparatorChar))), HAL_RuntimeOverlay.Dictionaries(romfs)));
        var guardedActor = guardedMap.Array(guardedMap.Fields(guardedMap.Root)["Actors"]).Select(x => guardedMap.Fields((int)guardedMap.Child(x, 0xc1)))
            .Single(x => x.TryGetValue("Hash", out var h) && guardedMap.UInt((int)guardedMap.Child(h, 0xd5), 8) == guardPlacement);
        var guardedFields = guardedMap.Fields((int)guardedMap.Child(guardedActor["Dynamic"], 0xc1));
        AssertTest.True(guardedMap.Text(guardedActor["Gyaml"]) == "Obj_TBox_A_06" &&
            guardedMap.Text(guardedFields["Drop__DropActor_Substitute"]) == ammoEdit.Reward &&
            guardedMap.Text(guardedFields["Drop__DropActor"]) == "TBox_Rupee_Blue" &&
            File.ReadAllBytes(guardResource).SequenceEqual(seedMap) && File.ReadAllBytes(Path.Combine(guardOverlay, "manifest.json")).SequenceEqual(seedManifest),
            "launch composition patches the assigned ammo chest while preserving the immutable seed and manifest");
        File.WriteAllText(Path.Combine(game, "hookshot_starting_inventory_capability.txt"), "HOOKSHOT_STARTING_INVENTORY=6\n");
        bool conflictRejected = false;
        try { HAL_ChestPlanner.Plan(new[] { new HAL_PlannerLocation("E89059B57059473A", 16757992849254467386UL,
            "Obj_TBox_A_02", "Item_Sword_Kokiri", "Get_StoneOfForest_GetSword", new[] { map }, false,
            new[] { "disabled_in_location_json" }) }, "1234", false, true, new HashSet<string> { "Item_Sword_Kokiri" }); }
        catch (InvalidDataException ex) { conflictRejected = ex.Message.Contains("starting item source", StringComparison.OrdinalIgnoreCase); }
        AssertTest.True(conflictRejected, "starting with an excluded item source is rejected");
        string songProfile = Path.Combine(test, "starting-song-profile"); Directory.CreateDirectory(songProfile);
        HAL_RandomizerSeedService.Generate(game, songProfile, id, "0000000000001234",
            new HAL_RandomizerSettings("kokiri_deku", false, false, false, StartingShield: true, StartingOcarina: true,
                StartingSongs: new[] { "ZeldaLullaby", "TimeSong" }, StartingConsumables: new[] { 4, 8, 0, 0 }));
        var startEnv = HAL_RandomizerSeedService.PrepareLaunchEnvironment(game, songProfile, id);
        AssertTest.True(startEnv["HOS_RANDO_STARTING_EQUIPMENT"] == "96" && startEnv["HOS_RANDO_STARTING_SONGS"] == "17" &&
                        startEnv["HOS_RANDO_STARTING_CONSUMABLES"] == "4,8,0,0",
            "starting shield, ocarina, songs and consumable counts survive seed generation and launch validation");
        string capability = Path.Combine(game, "hookshot_starting_inventory_capability.txt");
        File.WriteAllText(capability, "HOOKSHOT_STARTING_INVENTORY=5\n");
        bool oldSlingshotRejected = false;
        try { HAL_RandomizerSeedService.Generate(game, Path.Combine(test, "old-slingshot"), id, "0000000000001234",
            new HAL_RandomizerSettings("kokiri_deku", false, false, false, StartingSlingshot: true)); }
        catch (InvalidOperationException ex) { oldSlingshotRejected = ex.Message.Contains("slingshot", StringComparison.OrdinalIgnoreCase); }
        AssertTest.True(oldSlingshotRejected && !Directory.Exists(Path.Combine(test, "old-slingshot", "native-randomizer")),
            "older starting inventory builds reject slingshot seeds before writing them");
        AssertTest.True(HAL_RandomizerSeedService.PrepareLaunchEnvironment(game, songProfile, id)["HOS_RANDO_STARTING_EQUIPMENT"] == "96",
            "older starting inventory builds remain compatible with other starting equipment");
        File.WriteAllText(capability, "HOOKSHOT_STARTING_INVENTORY=6\n");
        var research = HAL_ChestResearch.Scan(romfs, "kokiri_deku", false, "0000000000001234", false);
        AssertTest.True(research.Catalog["locations"]!.AsArray().Count == 4 && research.Plan is not null &&
            research.Items["source_instances"]!.AsArray().Count == 4, "C# research scan builds catalog, plan and item reports");
        var editable = research.Locations.DeepClone().AsObject();
        var editableRow = editable["locations"]!.AsArray().First(x => x!["enabled"]!.GetValue<bool>())!;
        editableRow["enabled"] = false; editableRow["name"] = "Player label";
        var rescanned = HAL_ChestResearch.Scan(romfs, "kokiri_deku", false, "0000000000001234", false, existingLocations: JsonSerializer.SerializeToElement(editable));
        AssertTest.True(rescanned.Locations["locations"]!.AsArray().Any(x => x!["name"]!.GetValue<string>() == "Player label" && !x["enabled"]!.GetValue<bool>()),
            "C# research scan retains edited location labels and exclusions");
        string adultDir = Path.Combine(romfs, "Banc", "ForestTemple"); Directory.CreateDirectory(adultDir);
        string adult = Path.Combine(adultDir, "ForestTemple_Adult.bcett.byml.zs");
        var adultMap = HAL_Zstd.Unpack(File.ReadAllBytes(Path.Combine(region, "fixture.bcett.byml.zs")), HAL_RuntimeOverlay.Dictionaries(romfs));
        adultMap = HAL_ChestPatcher.Patch(adultMap, new[] { new HAL_ChestAssignment(16757992849254467386UL,
            "Obj_TBox_A_02", "Item_Sword_Kokiri", "Get_StoneOfForest_GetSword", "TBox_Rupee_Blue", null,
            new[] { "Banc/ForestTemple/ForestTemple_Adult.bcett.byml.zs" }) });
        File.WriteAllBytes(adult, HAL_Zstd.Compress(adultMap));
        string demo = Path.Combine(adultDir, "Demo_Example.bcett.byml.zs"), dream = Path.Combine(adultDir, "ForestTemple_Dream.bcett.byml.zs");
        File.Copy(adult, demo); File.Copy(adult, dream);
        AssertTest.True(HAL_ChestScope.Files(romfs, "full_game").Contains(adult) && !HAL_ChestScope.Files(romfs, "childhood").Contains(adult),
            "full-game scope includes adult maps outside the childhood regions");
        AssertTest.True(!HAL_ChestScope.Files(romfs, "full_game").Contains(demo) && !HAL_ChestScope.Files(romfs, "full_game").Contains(dream),
            "full-game scope excludes demo and dream-named maps");
        var full = HAL_ChestResearch.Scan(romfs, "full_game", false, null, false);
        AssertTest.True(full.Catalog["scope"]!.GetValue<string>() == "full_game" &&
            full.Catalog["resources"]!.AsObject().ContainsKey("Banc/ForestTemple/ForestTemple_Adult.bcett.byml.zs"),
            "full-game catalog records discovered adult maps");
        var preview = HAL_LocationPreview.Scan(romfs, "kokiri_deku", false);
        AssertTest.True(preview.Locations.Count == full.Catalog["locations"]!.AsArray().Count &&
            preview.Locations.Any(x => x.Status != "Outside scope"),
            "location preview includes the full discovered catalog alongside the selected scope");
        var fullPreview = HAL_LocationPreview.Scan(romfs, "full_game", false);
        AssertTest.True(fullPreview.Locations.Count == full.Catalog["locations"]!.AsArray().Count &&
            fullPreview.Locations.All(x => x.Status != "Outside scope"),
            "full-game location preview contains every discovered catalog check");
        string fullProfile = Path.Combine(test, "full-profile"); Directory.CreateDirectory(fullProfile);
        HAL_SeedGenerator.Generate(game, fullProfile, id, "0000000000001234", new HAL_RandomizerSettings("full_game", false, false, false, MaximumSkulltulas:30));
        using var fullSeed = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(fullProfile,"native-randomizer","seed.json")));
        AssertTest.True(fullSeed.RootElement.GetProperty("settings").GetProperty("chest_scope").GetString() == "full_game" &&
            fullSeed.RootElement.GetProperty("source_hashes").TryGetProperty("Banc/ForestTemple/ForestTemple_Adult.bcett.byml.zs",out _) &&
            fullSeed.RootElement.GetProperty("settings").GetProperty("requested_maximum_skulltulas").GetInt32()==30 &&
            !fullSeed.RootElement.GetProperty("settings").GetProperty("maximum_skulltulas_enforced").GetBoolean(),
            "full-game seed binds adult maps and records the requested, not-yet-enforced Skulltula maximum");
        Directory.CreateDirectory(Path.Combine(test, "full-start-preferences"));
        var startPrefs = new PreferenceStore(Path.Combine(test, "full-start-preferences"));
        startPrefs.SetRandomizer(new HAL_RandomizerSettings("full_game", true, false, false, true, true, true, true, true, true, true,
            new[] { "ZeldaLullaby", "TimeSong" }));
        var loadedStarts = new PreferenceStore(Path.Combine(test, "full-start-preferences")).Value;
        AssertTest.True(loadedStarts.RandomizerStartingMaps && loadedStarts.RandomizerStartingSword && loadedStarts.RandomizerStartingSlingshot &&
            loadedStarts.RandomizerStartingBombBag && loadedStarts.RandomizerStartingBoomerang && loadedStarts.RandomizerStartingLens &&
            loadedStarts.RandomizerStartingShield && loadedStarts.RandomizerStartingOcarina &&
            loadedStarts.RandomizerStartingSongs.SequenceEqual(new[] { "ZeldaLullaby", "TimeSong" }),
            "full-game starting-item preferences persist across restart");
        bool rejected = false; try { HAL_SeedGenerator.Generate(game, profile, id, "0000000000001234", new HAL_RandomizerSettings("kokiri_deku", false, false, false)); } catch (InvalidDataException) { rejected = true; }
        AssertTest.True(rejected, "C# seed generation refuses to replace an existing seed");
        File.WriteAllText(Path.Combine(game, "profile-manager", "enhancements.json"), "{\"schema_version\":1,\"unknown\":true}");
        bool invalidPreferences = false; try { HAL_EnhancementPreferences.Load(game); } catch (InvalidDataException) { invalidPreferences = true; }
        AssertTest.True(invalidPreferences, "C# enhancement preferences reject unknown settings");
    }
}
