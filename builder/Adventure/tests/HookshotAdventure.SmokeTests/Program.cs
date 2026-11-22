using System.Buffers.Binary;
using System.IO.Compression;
using HookshotAdventure.Core;

if (args.FirstOrDefault() == "--logic-only")
{
    ProgressionFixture.Check();
    return;
}

if (args.FirstOrDefault() == "--deku-exit-only")
{
    DekuExitFixture.Check();
    return;
}

if (args.FirstOrDefault() == "--deku-meeting-only")
{
    OpenForestOpeningFixture.Check();
    ConversationFixtures.Check();
    return;
}

if (args.FirstOrDefault() == "--f9-log-fixture")
{
    Console.Error.WriteLine("[hos test] gameplay trace: GameData Bool hash=12345678 value=1");
    Console.Error.WriteLine("[hos test] [DEBUG] f9 Stopped");
    return;
}

if (args.FirstOrDefault() is "--vk-ok" or "--vk-fail" or "--vk-hang")
{
    if (args[0] == "--vk-hang") Thread.Sleep(30000);
    if (args[0] == "--vk-fail")
    {
        Console.Error.WriteLine("vkEnumeratePhysicalDevices failed with ERROR_INITIALIZATION_FAILED");
        Environment.ExitCode = 1;
    }
    return;
}

var root = Path.Combine(Path.GetTempPath(), "hookshot-dev01-test-" + Guid.NewGuid().ToString("N"));
try
{
    AssertTest.True(HAL_RandomizerPreview.Create("test")["settings_hash"]!.GetValue<string>() == "E22056E4F45C",
        "configuration preview hash matches Python");
    bool midoRestricted = false; try { HAL_MidoExperiment.Prepare(Path.Combine(root,"not-dev")); } catch (InvalidDataException) { midoRestricted = true; }
    AssertTest.True(midoRestricted, "research Mido overlay refuses an unapproved development directory");
    BombchuCapacityFixture.Check();
    using (var itemCatalog = System.Text.Json.JsonDocument.Parse("{\"catalog_hash\":\"fixture\",\"locations\":[{\"id\":\"1\",\"reward\":\"Item_Dungeon_Map\",\"resources\":[\"Banc/InsideTheDekuTree/map\"],\"eligible\":false}]}"))
    {
        var report = HAL_ItemPoolReport.Build(itemCatalog.RootElement);
        AssertTest.True(report["definitions"]!.AsArray().Count == 34 && report["source_instances"]![0]!["dungeon"]!.GetValue<string>() == "InsideTheDekuTree" &&
            report["source_counts"]!["0008"]!.GetValue<int>() == 1, "item pool report retains qualified dungeon identity");
    }
    {
        var unknown = new HAL_ChestPlacement(9,"Obj_TBox_A_01","Unknown_Adult_Reward",null,null,null,false);
        var catalog = HAL_ChestCatalogDocument.Build([new HAL_ChestVariant("Banc/ForestTemple/Adult.bcett.byml.zs",unknown)],
            new Dictionary<string,string> { ["Banc/ForestTemple/Adult.bcett.byml.zs"] = new string('a',64) }, null, true, fullGame: true);
        using var json = System.Text.Json.JsonDocument.Parse(catalog.ToJsonString());
        var locations = HAL_LocationDocument.FromCatalog(json.RootElement);
        AssertTest.True(!catalog["locations"]![0]!["eligible"]!.GetValue<bool>() && locations["locations"]![0]!["vanilla_item_id"] is null,
            "full-game scope leaves unreviewed reward actors outside the shuffle");
        var heart = new HAL_ChestPlacement(0x2B702A67D25F1421UL, "Obj_TBox_A_02", "Obj_Heart_Piece", null, null, null, false);
        var heartVariant = new HAL_ChestVariant("Banc/HyruleField/HyruleField_Child.bcett.byml.zs", heart);
        AssertTest.True(HAL_ChestCatalog.Build([heartVariant], expanded: true, childhood: true, heartPieces: true, fullGame: true).Single().Eligible &&
            !HAL_ChestCatalog.Build([heartVariant], expanded: true, childhood: true, heartPieces: false, fullGame: true).Single().Eligible &&
            !HAL_ChestCatalog.Build([heartVariant with { Chest = heart with { Placement = 1 } }], expanded: true, childhood: true, heartPieces: true, fullGame: true).Single().Eligible,
            "full-game heart-piece option includes only reviewed checks");
        foreach (var actor in new[] { "TBox_Shield_Deku", "Item_GlassTruth", "Item_BombBag_Small", "Item_Boomerang" })
        {
            var chest = new HAL_ChestPlacement(10, "Obj_TBox_A_01", actor, null, null, null, false);
            var full = HAL_ChestCatalog.Build([new HAL_ChestVariant("Banc/ForestTemple/Adult.bcett.byml.zs", chest)],
                expanded: true, experimentalEquipment: true);
            var normal = HAL_ChestCatalog.Build([new HAL_ChestVariant("Banc/ForestTemple/Adult.bcett.byml.zs", chest)], expanded: true);
            AssertTest.True(full.Single().Eligible && !normal.Single().Eligible, "experimental full-game catalog includes equipment chest " + actor);
            var childPath = "Banc/KokiriForest/Child.bcett.byml.zs";
            var childCatalog = HAL_ChestCatalogDocument.Build([new HAL_ChestVariant(childPath, chest)],
                new Dictionary<string, string> { [childPath] = new string('a', 64) }, null, true, childhood: true);
            AssertTest.True(childCatalog["scope"]!.GetValue<string>() == "childhood" &&
                childCatalog["locations"]![0]!["eligible"]!.GetValue<bool>(), "childhood pool includes supported equipment " + actor);
            var eventChest = chest with { Event = "Unreviewed_Event" };
            AssertTest.True(!HAL_ChestCatalog.Build([new HAL_ChestVariant("Banc/ForestTemple/Adult.bcett.byml.zs", eventChest)],
                expanded: true, experimentalEquipment: true).Single().Eligible,
                "experimental equipment chest with an unreviewed special event stays vanilla");
        }
        foreach (var (placement, reward, story) in new[] {
            (0xA8C7127DD81A8BB9UL, "Item_BombBag_Small", "Get_StoneOfFire_GetBombBag"),
            (0x4CB5467D30E9D84AUL, "Item_Boomerang", "InsideJabuJabuBelly_TreasureBoxWithRuto09"),
            (0xE721A50E0511D8BEUL, "Item_GlassTruth", "BottomOfTheWell_GetGlassTruth") })
        {
            var reviewed = new HAL_ChestPlacement(placement, "Obj_TBox_A_01", reward, story, null, null, false);
            AssertTest.True(HAL_ChestCatalog.Build([new HAL_ChestVariant("Banc/KokiriForest/Child.bcett.byml.zs", reviewed)],
                expanded: true, experimentalEquipment: true).Single().Eligible &&
                !HAL_ChestCatalog.Build([new HAL_ChestVariant("Banc/KokiriForest/Child.bcett.byml.zs", reviewed with { Placement = placement + 1 })],
                    expanded: true, experimentalEquipment: true).Single().Eligible,
                "reviewed equipment event is bound to its exact chest and reward");
        }
        foreach (ulong id in new[] { 0x3C5BB811B4CF54DDUL, 0xF2948997EDE7B25FUL })
        {
            var bridge = new HAL_ChestPlacement(id, "Obj_TBox_A_01", "TBox_Rupee_Purple", null, null, null, false);
            var state = HAL_ChestCatalog.Build([new HAL_ChestVariant("Banc/KokiriForest/Child.bcett.byml.zs", bridge)], expanded: true).Single();
            AssertTest.True(!state.Eligible && state.Exclusions.Contains("bridge_cross_map_collection_unverified"),
                "bridge chest copies remain vanilla until their shared collection behavior is established");
        }
    }
    foreach (string fixture in new[] { "U0FSQxQA//6DAAAAgAAAAAAAAABTRkFUDAABAGUAAAAAAAAAAAAAAQAAAAADAAAAU0ZOVAgAAABhLmJpbgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABhYmM=",
        "U0FSQwAU/v8AAACDAAAAgAAAAABTRkFUAAwAAQAAAGUAAAAAAQAAAAAAAAAAAAADU0ZOVAAIAABhLmJpbgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABhYmM=" })
    {
        byte[] bytes = Convert.FromBase64String(fixture);
        AssertTest.True(HAL_ChestEvidence.SarcFiles(bytes)["a.bin"].SequenceEqual("abc"u8.ToArray()), "SARC evidence reader handles both byte orders");
        bool truncated = false; try { HAL_ChestEvidence.SarcFiles(bytes[..^1]); } catch (InvalidDataException) { truncated = true; }
        AssertTest.True(truncated, "SARC evidence reader rejects truncated members");
    }
    var eventTokens = new Dictionary<string, IReadOnlySet<string>> { ["Event/EventFlow/Story.bfevfl.zs"] = HAL_ChestEvidence.Strings("\0Entry\0EquipWeapon\0"u8.ToArray()) };
    AssertTest.True(HAL_ChestEvidence.EventEvidence("Story_Entry", eventTokens).Single().Match == "file_and_entry_candidate" &&
        HAL_ChestEvidence.EventEvidence("Story", eventTokens).Count == 0, "event audit only reports exact or entry-name evidence");
    using (var canonical = System.Text.Json.JsonDocument.Parse("{\"𐀀\":2,\"é\":\"星\\n😀\",\"z\":[1.0,-0.0,1e-6,1e-5,1e16,1e20,1e-7,1e-4,1.234567,12345678901234567890],\"\":1}"))
        AssertTest.True(HAL_CanonicalJson.Digest(canonical.RootElement) == "de18858e27ab559e467e577c1080b9e5f62aff24880f19991e2d89f3951883c9",
            "canonical JSON matches Python for Unicode ordering, escapes, integer and floating values");
    using (var numericVectors = System.Text.Json.JsonDocument.Parse("[-5.443358752714351e-65,-4.752837180108051e-283,-1.6838052105920757e-34,-4.3741833740538186e-204,3.454585566854502e-99,-9.88263136507831e-292,1.992218543113405e-177,-1.0320849710190267e-286,6.418108016139014e+29,1.0943081228259262e-187,2.9983189068584376e+91,-1.750305441668362e+234,-2.469355570099242e+175,-1.0400198071123855e+154,-10271.035416509896,1.6562957731452293e-178,2.538717938432224e-56,-7.173043922347743e-30,1.249387821147966e+221,-1.7923856121919926e+68,1.1736554906283911e-184,-0.012701506532088,-1.4892537244832358e-30,-3.0980219436002468e+137,-1.2656395788150461e+123,8.307716466994032e+92,2.7015532132824732e+23,-1.7109041489585712e+270,3.146782519730512e-306,1.066643308709158e+240,1.4929248215123858e+54,-1.0300622856026049e-256,1.3981907905883288e-90,-5.5185641222621135e-230,1.08029595309394e-245,-9.978770407642317e+216,9.695019928283058e+255,6.270738885569384e-93,2.026988625780111e-147,-1.1034700330248582e-266,8.198954816624031e+63,1.960709344912162e-142,5.73241554889183e+101,0.0007036839697790907,1.934285814515896e-102,1.5234593366626454,-2.468173687348829e+26,-4.141637847514701e+299,-9.870508285306668e+64,-1.820450664680491e+59,-7.773481551797144e-57,1.3359161877058191e-14,-1993754298765094.0,-1.3562039469649627e+214,4.237685240455057e-243,-4.590939207985341e+165,2.7830678134010383e-295,-3.2196464060486626e+118,9.298763089839354e+139,1.5781445745702694e+109,-4.176878793314991e+234,4.540337703543818e+207,6.622340590165696e-52,1.2107028777221706e-168,-2.949477485318269e-195,-1.3940117819992364e-177,-2.130804665102337e-157,8.867526823010259e-104,-1.5965751245268283e-252,-3.4172216366861733e+62,-2.31338277192296e-40,-8.629398833205233e+100,6.751006873515936e+260,4.9330660992802104e+302,-8.413976661820701e+138,-612352235294.3535,-5.737936429441457e-109,-3.4067296689267247e-158,-2.1207544581101085e-277,1.2521764380223822e+185,-6.989393031171971e+163,5.891207668961916e-123,-2.1005640472621764e-22,-3.991438752658859e+98,-2.306469420392763e-87,-1.0089920248406778e+286,-1.4668760412720168e+279,-3.841449569309167e+148,7.58926404685939e+144,2.2418303590561344e-79,-1.614901192770675e-292,1.6298661969437252e-239,2.9435223615774036e-15,1.173598969469871e-163,-1.3897660872068834e+133,-2.5166986145059493e+57,3.0548876382620834e+291,-6.533980228139894e-91,-4.0444540060850096e+132,-2.5654656649196402e-142,2.2983730001979746e-178,-4.3707246514960097e+291,3.786252581952205e+272,1.5904836210920605e-182,2.571388624086124e+206,-1.0679672202064962e+234,2.4955137452123433e+239,4.510832215166921e-74,-9.863036228488896e-290,-1.6207657269242263e+73,4.38599560563453e+21,2.137640551543107e-139,3.421557263829104e-48,1.0961637845155142e+114,6.30005473900239e-88,5.976834862944154e+149,-1.0111287369944041e-130,-5.61141298365884e+61,1.288209752370932e+78,-1.4512596934749237e-222,-1.579187135554614e-281,2.892092964072643e-305,-5.1131114889331987e+290,1.7151101548338211e-155,-3.081583866605882e+294,7.120024196570365e-252,-1.4583476280350841e-80,-1.268688758990892e+90,-6.318853250809491e+16,5.767042179699581e-111,3.317696084346145e-292,9.331346195651269e-79,4.6415365903321136e+111,4.936577452174305e-201,8.904173851345552e-96,-1.3755667879591634e+230,1.2281586704248872e-185,-1.441813431581695e+220,1.5851372771255675e-210,4.668070282003434e-230,157940809733717.0,2.7002643906539688e+295,-1.956734149452848e-274,4.4423145942893344e+30,1.9335158269436042e+185,-6.312416436486702e-155,9.238636963989014e+188,2.2864352365055547e+134,-1.1249266545744228e-108,2.3301477870929523e+139,6.628437118633367e+130,9.790491376042862e+143,1.3512569699208425e+241,-3.2705143378552405e+208,3.3318696346994897e+302,-1.432694517174896e+178,-2.2851141487265063e-172,2.313668342399529e-84,-3.755706593598552e-15,-4.210195054708899e+67]"))
        AssertTest.True(HAL_CanonicalJson.Digest(numericVectors.RootElement) == "edad7d7ca0d448311d50c6894663f72a1d8752fe7eb8634b993943faebcfbbdb", "canonical JSON matches Python for 160 deterministic floating-point values");
    using (var seedIdentity = System.Text.Json.JsonDocument.Parse("{\"schema_version\":1,\"file_type\":\"Hookshot generated chest seed\",\"seed\":\"0000000000001234\",\"overlay\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"binding\":{\"profile_id\":\"0123456789abcdef0123456789abcdef\",\"seed\":\"0000000000001234\",\"plan_hash\":\"3ae87871e6b2b2b4d0a6338fb8beaf73ab4b97e5758420bbdce51213f56e478c\"},\"metadata\":{\"binding\":{\"profile_id\":\"0123456789abcdef0123456789abcdef\",\"seed\":\"0000000000001234\",\"plan_hash\":\"3ae87871e6b2b2b4d0a6338fb8beaf73ab4b97e5758420bbdce51213f56e478c\"},\"plan\":{\"schema_version\":4,\"seed\":\"0000000000001234\",\"assignments\":[],\"plan_hash\":\"3ae87871e6b2b2b4d0a6338fb8beaf73ab4b97e5758420bbdce51213f56e478c\"}},\"content_hash\":\"9e65445639666f4addaae48d29142d110e76dd0809437d6a1c97644acea89c1f\"}"))
    {
        HAL_SeedIdentity.Validate(seedIdentity.RootElement, "0123456789abcdef0123456789abcdef");
        bool wrongProfile = false;
        try { HAL_SeedIdentity.Validate(seedIdentity.RootElement, "ffffffffffffffffffffffffffffffff"); } catch (InvalidDataException) { wrongProfile = true; }
        AssertTest.True(wrongProfile, "C# seed identity validates Python hashes and rejects another profile");
        using var tamperedSeed = System.Text.Json.JsonDocument.Parse(seedIdentity.RootElement.GetRawText().Replace("0000000000001234", "0000000000001235", StringComparison.Ordinal));
        bool tamperedRejected = false;
        try { HAL_SeedIdentity.Validate(tamperedSeed.RootElement, "0123456789abcdef0123456789abcdef"); } catch (InvalidDataException) { tamperedRejected = true; }
        AssertTest.True(tamperedRejected, "C# seed identity rejects modified content");
    }
    {
        string fixtureRoot = Path.Combine(root, "seed-identity");
        string overlay = Path.Combine(fixtureRoot, new string('a', 64));
        Directory.CreateDirectory(Path.Combine(overlay, "Banc"));
        File.WriteAllText(Path.Combine(fixtureRoot, "binding.json"), "{\"profile_id\":\"0123456789abcdef0123456789abcdef\",\"seed\":\"0000000000001234\",\"plan_hash\":\"3ae87871e6b2b2b4d0a6338fb8beaf73ab4b97e5758420bbdce51213f56e478c\"}");
        File.WriteAllText(Path.Combine(overlay, "manifest.json"), "{\"binding\":{\"profile_id\":\"0123456789abcdef0123456789abcdef\",\"seed\":\"0000000000001234\",\"plan_hash\":\"3ae87871e6b2b2b4d0a6338fb8beaf73ab4b97e5758420bbdce51213f56e478c\"},\"plan\":{\"schema_version\":4,\"seed\":\"0000000000001234\",\"assignments\":[],\"plan_hash\":\"3ae87871e6b2b2b4d0a6338fb8beaf73ab4b97e5758420bbdce51213f56e478c\"},\"output_hashes\":{\"Banc/example.zs\":\"0e9ad53d18d55ef9370d6020ed9b7665881ca9cc37bbcde63957b55c5df552ed\"}}");
        File.WriteAllText(Path.Combine(overlay, "Banc", "example.zs"), "authored fixture");
        using var record = System.Text.Json.JsonDocument.Parse("{\"schema_version\":1,\"file_type\":\"Hookshot generated chest seed\",\"seed\":\"0000000000001234\",\"overlay\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"binding\":{\"profile_id\":\"0123456789abcdef0123456789abcdef\",\"seed\":\"0000000000001234\",\"plan_hash\":\"3ae87871e6b2b2b4d0a6338fb8beaf73ab4b97e5758420bbdce51213f56e478c\"},\"metadata\":{\"binding\":{\"profile_id\":\"0123456789abcdef0123456789abcdef\",\"seed\":\"0000000000001234\",\"plan_hash\":\"3ae87871e6b2b2b4d0a6338fb8beaf73ab4b97e5758420bbdce51213f56e478c\"},\"plan\":{\"schema_version\":4,\"seed\":\"0000000000001234\",\"assignments\":[],\"plan_hash\":\"3ae87871e6b2b2b4d0a6338fb8beaf73ab4b97e5758420bbdce51213f56e478c\"},\"output_hashes\":{\"Banc/example.zs\":\"0e9ad53d18d55ef9370d6020ed9b7665881ca9cc37bbcde63957b55c5df552ed\"}},\"content_hash\":\"1c7c5289078a1c747383c2a4ace12f9c2de655062934c04eca2aa1b058c8af7c\"}");
        AssertTest.True(HAL_SeedIdentity.ValidateOverlay(record.RootElement, "0123456789abcdef0123456789abcdef", fixtureRoot) == overlay, "C# overlay validation accepts a Python-hashed authored fixture");
        File.WriteAllText(Path.Combine(overlay, "Banc", "example.zs"), "modified");
        bool modifiedRejected = false;
        try { HAL_SeedIdentity.ValidateOverlay(record.RootElement, "0123456789abcdef0123456789abcdef", fixtureRoot); } catch (InvalidDataException) { modifiedRejected = true; }
        AssertTest.True(modifiedRejected, "C# overlay validation rejects modified files");
        File.WriteAllText(Path.Combine(overlay, "Banc", "example.zs"), "authored fixture");
        File.WriteAllText(Path.Combine(overlay, "unexpected"), "extra");
        bool extraRejected = false;
        try { HAL_SeedIdentity.ValidateOverlay(record.RootElement, "0123456789abcdef0123456789abcdef", fixtureRoot); } catch (InvalidDataException) { extraRejected = true; }
        AssertTest.True(extraRejected, "C# overlay validation rejects unexpected files");
        using var sourceRecord = System.Text.Json.JsonDocument.Parse("{\"source_hashes\":{\"Banc/example.zs\":\"0e9ad53d18d55ef9370d6020ed9b7665881ca9cc37bbcde63957b55c5df552ed\"}}");
        HAL_SeedIdentity.ValidateSourceResources(sourceRecord.RootElement, overlay, Array.Empty<byte[]>());
        File.WriteAllText(Path.Combine(overlay, "Banc", "example.zs"), "modified source");
        bool sourceRejected = false;
        try { HAL_SeedIdentity.ValidateSourceResources(sourceRecord.RootElement, overlay, Array.Empty<byte[]>()); } catch (InvalidDataException) { sourceRejected = true; }
        AssertTest.True(sourceRejected, "C# seed validation rejects changed source resources");
    }
    using (var duplicateJson = System.Text.Json.JsonDocument.Parse("{\"id\":1,\"id\":2}"))
    {
        bool duplicateRejected = false;
        try { HAL_CanonicalJson.Digest(duplicateJson.RootElement); } catch (InvalidDataException) { duplicateRejected = true; }
        AssertTest.True(duplicateRejected, "canonical JSON rejects duplicate object keys");
    }
    try
    {
        var plain = System.Text.Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("Hookshot randomizer resource ", 1000)));
        var compressed = HAL_Zstd.Compress(plain);
        AssertTest.True(HAL_Zstd.Unpack(compressed, Array.Empty<byte[]>()).SequenceEqual(plain) &&
            HAL_Zstd.Unpack(plain, Array.Empty<byte[]>()).SequenceEqual(plain),
            "Zstandard resource codec round-trips frames and preserves uncompressed input");
        bool limited = false;
        try { HAL_Zstd.Unpack(compressed, Array.Empty<byte[]>(), 100); } catch (InvalidDataException) { limited = true; }
        AssertTest.True(limited, "Zstandard decoder enforces its output limit");
        var dictionary = Convert.FromBase64String("N6Qw7BZgx3UqEFg9HPyg/6D/oP+g/6D/oP+g/6DfN5lkkkkmmWSSCYeUUqYk/5lhhrlGAwIAAAArVHMCAAAkQICAGgcHBwIMPAICAgICCAQCAgICAgICAgICQA4VAgEXQdAUBEGoRGUlSdIB5BljhRBCCBERERERERERSZIkHQEAAAAEAAAACAAAAG1wYXNzIGV2ZW50IGZpeHR1cmUgY2hlc3QgcmVzcGFzcyBldmVudCBmaXh0dXJlIGNoZXN0IHJlc291cmNlIHJlZ2lvbiA1NSBhY3RvciBtcGFzcyBldmVudCBmaXh0dXJlIGNoZXN0IHJlc291cmNlIHJlZ2lvbiA1MCBhY3RvcnBhc3MgZXZlbnQgZml4dHVyZSBjaGVzdCByZXNvdXJjZSByZWdpb24gNDQgYWN0b3IgbXBhc3MgZXZlbnQgZml4dHVyZSBjaGVzdCByZXNvdXJjZSByZWdpb24gNDAgYWN0b3JwYXNzIGV2ZW50IGZpeHR1cmUgY2hlc3QgcmVzb3VyY2UgcmVnaW9uIDMzIGFjdG9yIG1wYXNzIGV2ZW50IGZpeHR1cmUgY2hlc3QgcmVzb3VyY2UgcmVnaW9uIDMwIGFjdG9ycGFzcyBldmVudCBmaXh0dXJlIGNoZXN0IHJlc291cmNlIHJlZ2lvbiAyMSA=");
        var dictionaryFrame = Convert.FromBase64String("KLUv/WMWYMd18gG9AQBUAjQyIGl0ZW0gcGxhY2VtYmx1ZSBydXBlZSBzd29yZCBtYXAgY28F/JZMBNClh0YaB6mCwGo3");
        var expectedDictionaryData = System.Text.Encoding.UTF8.GetBytes("fixture chest resource region 42 actor item placement " +
            string.Concat(Enumerable.Repeat("blue rupee sword map compass event ", 20)));
        AssertTest.True(HAL_Zstd.Unpack(dictionaryFrame, new[] { dictionary }).SequenceEqual(expectedDictionaryData),
            "Zstandard decodes a Python-produced full-dictionary frame");
        var dictionaryArchive = new byte[0x34];
        BinaryPrimitives.WriteUInt32LittleEndian(dictionaryArchive.AsSpan(0x0c), 0x30);
        BinaryPrimitives.WriteUInt16LittleEndian(dictionaryArchive.AsSpan(0x1a), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(dictionaryArchive.AsSpan(0x28), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(dictionaryArchive.AsSpan(0x2c), 4);
        "dict"u8.CopyTo(dictionaryArchive.AsSpan(0x30));
        AssertTest.True(HAL_Zstd.ReadDictionaryArchive(HAL_Zstd.Compress(dictionaryArchive)).Single().SequenceEqual("dict"u8.ToArray()),
            "dictionary archive extraction reads bounded entries");
        string overlayRoot = Path.Combine(root, "runtime-composition"), game = Path.Combine(overlayRoot, "game"), romfs = Path.Combine(game, "romfs");
        string seedOverlay = Path.Combine(overlayRoot, "runtime-base");
        Directory.CreateDirectory(Path.Combine(romfs, "Pack")); Directory.CreateDirectory(Path.Combine(romfs, "System", "Resource"));
        Directory.CreateDirectory(seedOverlay);
        File.WriteAllBytes(Path.Combine(romfs, "Pack", "ZsDic.pack.zs"), HAL_Zstd.Compress(dictionaryArchive));
        var emptyTable = new byte[22]; "RESTBL"u8.CopyTo(emptyTable);
        BinaryPrimitives.WriteUInt32LittleEndian(emptyTable.AsSpan(6), 1); BinaryPrimitives.WriteUInt32LittleEndian(emptyTable.AsSpan(10), 16);
        File.WriteAllBytes(Path.Combine(romfs, "System", "Resource", "ResourceSizeTable.test.rsizetable.zs"), HAL_Zstd.Compress(emptyTable));
        File.WriteAllText(Path.Combine(seedOverlay, "manifest.json"), "{\"format\":\"vanilla-runtime-base-v1\",\"output_hashes\":{},\"plan\":{}}\n");
        var noSkips = new HAL_EnhancementPreferences(SkipOpening: false, SkipTutorialConversations: false, SkipDekuFirstEntrance: false,
            SkipDekuTreeMeeting: false, SkipKokiriSwordCutscene: false, SkipDekuTreeClear: false);
        string runtime = HAL_RuntimeOverlay.Prepare(seedOverlay, game, noSkips);
        AssertTest.True(File.Exists(Path.Combine(runtime, "runtime.json")) && HAL_RuntimeOverlay.Prepare(seedOverlay, game, noSkips) == runtime,
            "runtime overlay composition creates and validates a reusable overlay");
        File.WriteAllText(Path.Combine(runtime, "unexpected"), "changed"); bool badRuntime = false;
        try { HAL_RuntimeOverlay.Prepare(seedOverlay, game, noSkips); } catch (InvalidDataException) { badRuntime = true; }
        AssertTest.True(badRuntime, "runtime overlay composition rejects unexpected files");
        SeedGenerationFixture.Check(root);
    }
    catch (DllNotFoundException) { Console.WriteLine("SKIP Zstandard native library is not packaged on this machine"); }
    var kokiri = new[] { "Banc/KokiriForest/KokiriForest_Child_Dynamic.bcett.byml.zs" };
    var deku = new[] { "Banc/InsideTheDekuTree/InsideTheDekuTree_Static.bcett.byml.zs" };
    var pool = new[]
    {
        new HAL_ChestPoolEntry("E89059B57059473A", 16757992849254467386UL, "Item_Sword_Kokiri", kokiri),
        new HAL_ChestPoolEntry("0B66198D17D6584E", 821372075822700622UL, "Item_Range_SlingShot", deku),
        new HAL_ChestPoolEntry("0000000000000001", 1, "TBox_Rupee_Blue", kokiri),
        new HAL_ChestPoolEntry("0000000000000002", 2, "TBox_Rupee_Blue", kokiri),
        new HAL_ChestPoolEntry("0000000000000003", 3, "TBox_Rupee_Blue", kokiri),
        new HAL_ChestPoolEntry("0000000000000004", 4, "TBox_Rupee_Purple", kokiri),
    };
    var donors = HAL_ChestPermutation.Assign(pool, "1234", true);
    AssertTest.True(donors.SequenceEqual(HAL_ChestPermutation.Assign(pool.Reverse(), "0000000000001234", true)) &&
        donors.Select(row => row.DonorId).Distinct().Count() == pool.Length,
        "chest permutation is deterministic and conserves reward instances");
    var largePool = Enumerable.Range(0, 512).Select(i => new HAL_ChestPoolEntry(i.ToString("X16"),
        i == 1 ? 821372075822700622UL : (ulong)i,
        i == 500 ? "Item_Sword_Kokiri" : i == 501 ? "Item_Range_SlingShot" : "TBox_Rupee_Blue",
        i == 0 ? kokiri : i == 1 ? deku : new[] { "Banc/HyruleField/map" })).ToArray();
    var largePlacement = HAL_ChestPermutation.Assign(largePool, "12", true);
    AssertTest.True(largePlacement.Single(row => row.DonorId == 500.ToString("X16")).TargetId == 0.ToString("X16") &&
        largePlacement.Single(row => row.DonorId == 501.ToString("X16")).TargetId == 1.ToString("X16") &&
        largePlacement.Select(row => row.DonorId).Distinct().Count() == largePool.Length &&
        largePlacement.SequenceEqual(HAL_ChestPermutation.Assign(largePool.Reverse(), "12", true)),
        "large pools find the only legal opening placement deterministically without losing rewards");
    var fillerOnly = new HashSet<string> { "E89059B57059473A", "0000000000000001" };
    foreach (int seedNumber in Enumerable.Range(0, 16))
    {
        var result = HAL_ChestPermutation.Assign(pool, seedNumber.ToString("X"), true, fillerOnly);
        var actors = pool.ToDictionary(item => item.Id, item => item.Reward, StringComparer.Ordinal);
        AssertTest.True(result.Where(row => fillerOnly.Contains(row.TargetId)).All(row => HAL_ChestPermutation.IsSafeFiller(actors[row.DonorId])) &&
            result.Select(row => row.DonorId).Distinct(StringComparer.Ordinal).Count() == pool.Length,
            "excluded checks receive filler while all reward instances remain in the pool");
    }
    bool impossibleExclusions = false;
    try { HAL_ChestPermutation.Assign(pool, "12", true, pool.Select(row => row.Id).ToHashSet(StringComparer.Ordinal)); }
    catch (InvalidDataException) { impossibleExclusions = true; }
    AssertTest.True(impossibleExclusions, "seed placement rejects more filler-only checks than the pool can support");
    var gatedForestPool = pool.Concat(new[] {
        new HAL_ChestPoolEntry("CDA7447B91AB4DBA", HAL_OpeningAccess.StormsGrottoChest, "TBox_Rupee_Green", kokiri),
        new HAL_ChestPoolEntry("3C5BB811B4CF54DD", HAL_OpeningAccess.BridgeChest, "TBox_Rupee_Purple", kokiri) }).ToArray();
    foreach (int seedNumber in Enumerable.Range(0, 64))
    {
        var placement = HAL_ChestPermutation.Assign(gatedForestPool, seedNumber.ToString("X"), true);
        AssertTest.True(placement.Where(row => row.TargetId is "CDA7447B91AB4DBA" or "3C5BB811B4CF54DD")
            .All(row => row.DonorId is not ("E89059B57059473A" or "0B66198D17D6584E")),
            "opening progression stays out of the Storms grotto and the forest bridge chest");
    }
    AssertTest.True(Enumerable.Range(0, 32).Select(i => HAL_ChestPermutation.Assign(pool, i.ToString("X"), true))
        .Any(rows => rows.Single(row => row.DonorId == "E89059B57059473A").TargetId != "E89059B57059473A"),
        "the Kokiri Sword can move to another chest");
    var openingChecks = pool.Select(row => new HAL_OpeningCheck(row.Id, row.Placement, row.Reward, row.Resources)).ToArray();
    foreach (var gated in new[] { HAL_OpeningAccess.StormsGrottoChest, HAL_OpeningAccess.BridgeChest })
    {
        var checks = openingChecks.Append(new HAL_OpeningCheck(gated.ToString("X16"), gated, "TBox_Rupee_Blue", kokiri)).ToArray();
        bool gatedRejected = false;
        try { HAL_OpeningAudit.Check(checks, new Dictionary<string,string> {
            ["E89059B57059473A"] = "TBox_Rupee_Blue", [gated.ToString("X16")] = "Item_Sword_Kokiri" }); }
        catch (InvalidDataException) { gatedRejected = true; }
        AssertTest.True(gatedRejected, "opening audit rejects a sword behind a gated Kokiri check");
    }
    var rewardsById = pool.ToDictionary(row => row.Id, row => row.Reward);
    foreach (var i in Enumerable.Range(0, 64))
    {
        var sample = HAL_ChestPermutation.Assign(pool, i.ToString("X"), true);
        AssertTest.True(sample.Where(row => rewardsById[row.DonorId] is "Item_Sword_Kokiri" or "Item_Range_SlingShot")
            .All(row => { var target = pool.Single(target => target.Id == row.TargetId);
                return target.Resources.All(path => path.Contains("/KokiriForest/", StringComparison.Ordinal)) ||
                    rewardsById[row.DonorId] == "Item_Range_SlingShot" && target.Placement == 821372075822700622UL; }),
            "sword stays in Kokiri and slingshot stays in Kokiri or its Deku Tree chest");
    }
    AssertTest.True(HAL_OpeningAudit.Check(openingChecks, new Dictionary<string, string>()).SlingshotRoute == "authored_Deku_slingshot_chest",
        "opening audit accepts the slingshot at its Deku Tree chest");
    var lobbyResource = new[] { "Banc/InsideTheDekuTree/InsideTheDekuTree_Static.bcett.byml.zs" };
    var lobbyChecks = openingChecks.Append(new HAL_OpeningCheck("lobby-map", HAL_OpeningAccess.DekuMapChest,
        "TBox_Rupee_Blue", lobbyResource)).ToArray();
    var lobbyAudit = HAL_OpeningAudit.Check(lobbyChecks, new Dictionary<string,string> {
        ["lobby-map"] = "Item_Range_SlingShot", ["0B66198D17D6584E"] = "TBox_Rupee_Blue" });
    AssertTest.True(lobbyAudit.SlingshotRoute == "Deku_lobby_map_chest", "opening audit accepts the freed Deku lobby chest for slingshot");
    var lobbyPool = pool.Append(new HAL_ChestPoolEntry("lobby-map", HAL_OpeningAccess.DekuMapChest, "TBox_Rupee_Blue", lobbyResource)).ToArray();
    bool sawLobbySling = false, swordAlwaysForest = true;
    foreach (int seedIndex in Enumerable.Range(0, 128)) {
        var sample = HAL_ChestPermutation.Assign(lobbyPool, seedIndex.ToString("X"), true);
        var slingDonor = lobbyPool.Single(row => row.Reward == "Item_Range_SlingShot");
        var swordDonor = lobbyPool.Single(row => row.Reward == "Item_Sword_Kokiri");
        var slingTarget = lobbyPool.Single(row => row.Id == sample.Single(x => x.DonorId == slingDonor.Id).TargetId);
        var swordTarget = lobbyPool.Single(row => row.Id == sample.Single(x => x.DonorId == swordDonor.Id).TargetId);
        sawLobbySling |= slingTarget.Id == "lobby-map";
        swordAlwaysForest &= HAL_OpeningAccess.ForestBeforeDeku(swordTarget.Placement, swordTarget.Resources);
    }
    AssertTest.True(sawLobbySling && swordAlwaysForest, "seed permutation can place slingshot in the freed Deku lobby chest while keeping the sword in Kokiri");
    var audit = HAL_OpeningAudit.Check(openingChecks, donors.ToDictionary(row => row.TargetId, row => rewardsById[row.DonorId]));
    AssertTest.True(audit.Status == "opening_progression_verified" && !audit.FullBeatability && audit.SlingshotCheck is not null,
        "opening audit checks the sword and slingshot without claiming full beatability");
    bool invalidOpeningRejected = false;
    try { HAL_OpeningAudit.Check(openingChecks, new Dictionary<string, string> { ["E89059B57059473A"] = "TBox_Rupee_Blue" }); }
    catch (InvalidDataException) { invalidOpeningRejected = true; }
    AssertTest.True(invalidOpeningRejected, "opening audit rejects a missing sword");
    var equipmentPool = pool.Select(row => new HAL_PlannerLocation(row.Id, row.Placement, "Obj_TBox_A_02", row.Reward,
        row.Reward == "Item_Sword_Kokiri" ? "Get_StoneOfForest_GetSword" :
        row.Reward == "Item_Range_SlingShot" ? "InsideTheDekuTree_GetSlingShot" : null,
        row.Resources, true, Array.Empty<string>())).ToArray();
    foreach (var requested in new[] { new[] { "Item_Sword_Kokiri" }, new[] { "Item_Range_SlingShot" },
        new[] { "Item_Sword_Kokiri", "Item_Range_SlingShot" } })
    {
        var startingEquipment = requested.ToHashSet(StringComparer.Ordinal);
        var equipmentPlan = HAL_ChestPlanner.Plan(equipmentPool, "1234", false, true, startingEquipment);
        AssertTest.True(equipmentPlan.StartingInventory.Select(row => row.Item).ToHashSet().SetEquals(requested) &&
            equipmentPlan.Assignments.All(row => !startingEquipment.Contains(row.Reward)) &&
            equipmentPlan.Filler.Count == requested.Length && equipmentPlan.Filler.All(row => row.Added == "TBox_Rupee_Blue") && equipmentPlan.OpeningAudit.Status == "opening_progression_verified",
            "starting sword and slingshot move from chest rewards to the starting ledger");
    }
    bool missingEquipmentRejected = false;
    try { HAL_ChestPlanner.Plan(equipmentPool.Where(row => row.Reward != "Item_Sword_Kokiri").ToArray(), "1234", false, true,
        new HashSet<string> { "Item_Sword_Kokiri" }); }
    catch (InvalidDataException) { missingEquipmentRejected = true; }
    AssertTest.True(missingEquipmentRejected, "starting equipment requires its reviewed source chest");
    var expandedEquipment = equipmentPool.Concat(new[] {
        new HAL_PlannerLocation("A8C7127DD81A8BB9", 0xA8C7127DD81A8BB9, "Obj_TBox_A_02", "Item_BombBag_Small", "Get_StoneOfFire_GetBombBag", new[] { "Banc/DodongoCavern/test" }, true, Array.Empty<string>()),
        new HAL_PlannerLocation("4CB5467D30E9D84A", 0x4CB5467D30E9D84A, "Obj_TBox_A_02", "Item_Boomerang", "InsideJabuJabuBelly_TreasureBoxWithRuto09", new[] { "Banc/InsideJabuJabuBelly/test" }, true, Array.Empty<string>()),
        new HAL_PlannerLocation("E721A50E0511D8BE", 0xE721A50E0511D8BE, "Obj_TBox_A_02", "Item_GlassTruth", "BottomOfTheWell_GetGlassTruth", new[] { "Banc/BottomOfTheWell/test" }, true, Array.Empty<string>())
    }).ToArray();
    var extraItems = new HashSet<string> { "Item_BombBag_Small", "Item_Boomerang", "Item_GlassTruth" };
    var extraPlan = HAL_ChestPlanner.Plan(expandedEquipment, "1234", false, true, extraItems);
    AssertTest.True(extraPlan.StartingInventory.Count == 3 && extraPlan.Filler.All(x => x.Added == "TBox_Rupee_Blue") &&
        extraPlan.Assignments.Where(x => extraItems.Contains(x.ExpectedReward)).All(x => x.Event == x.ExpectedEvent) &&
        extraPlan.Assignments.All(x => !extraItems.Contains(x.Reward)),
        "starting experimental equipment replaces each reward with blue rupees and retains chest events");
    var exclusions = new[] { "progression_or_unreviewed_reward", "unreviewed_special_event" };
    var dungeonPool = new[]
    {
        new HAL_PlannerLocation("0000000000000001", 1, "Obj_TBox_A_02", "Item_Dungeon_Map", "InsideTheDekuTree_GetMap", deku, false, exclusions),
        new HAL_PlannerLocation("0000000000000002", 2, "Obj_TBox_A_02", "Item_Dungeon_Compass", "InsideTheDekuTree_GetCompass", deku, false, exclusions),
        new HAL_PlannerLocation("0000000000000003", 3, "Obj_TBox_A_02", "TBox_DekuNutBundle", null, deku, true, Array.Empty<string>()),
        new HAL_PlannerLocation("0000000000000004", 4, "Obj_TBox_A_02", "TBox_Rupee_Blue", null, deku, true, Array.Empty<string>()),
    };
    var dungeonPlan = HAL_ChestPlanner.Plan(dungeonPool, "1234", true, false);
    AssertTest.True(dungeonPlan.StartingInventory.Count == 2 && dungeonPlan.Assignments.Select(row => row.DonorId).SequenceEqual(new[]
        { "0000000000000002", "0000000000000004", "0000000000000001", "0000000000000003" }) &&
        dungeonPlan.Filler.All(row => row.Added == "TBox_Rupee_Blue") &&
        dungeonPlan.Assignments.Count(row => row.Reward == "TBox_Rupee_Blue") == 3,
        "starting dungeon items are replaced with blue rupees while retaining donor assignments");
    AssertTest.True(HAL_ChestPlanner.Plan(dungeonPool, "1234", false, false).Assignments.Count == 2,
        "disabled starting maps leave unreviewed dungeon-item chests untouched");
    bool invalidDungeonRejected = false;
    try { HAL_ChestPlanner.Plan(dungeonPool.Select(row => row.Id == "0000000000000001" ? row with { Event = "Unreviewed_Event", Exclusions = Array.Empty<string>() } : row).ToArray(), "1234", true, false); }
    catch (InvalidDataException) { invalidDungeonRejected = true; }
    AssertTest.True(invalidDungeonRejected, "planner rejects unreviewed starting-item events");
    var disabledChest = dungeonPool.Select(row => row.Id == "0000000000000004" ? row with
        { Eligible = false, Exclusions = new[] { "disabled_in_location_json" } } : row).ToArray();
    AssertTest.True(HAL_ChestPlanner.Plan(disabledChest, "1234", false, false).Assignments.All(row => row.Id != "0000000000000004"),
        "planner leaves explicitly disabled ordinary chests unchanged");
    using (var catalogFixture0 = System.Text.Json.JsonDocument.Parse(Convert.FromBase64String("eyJzY2hlbWFfdmVyc2lvbiI6NCwicmVzb3VyY2VzIjp7IkJhbmMvSW5zaWRlVGhlRGVrdVRyZWUvSW5zaWRlVGhlRGVrdVRyZWVfU3RhdGljLmJjZXR0LmJ5bWwuenMiOiJmaXh0dXJlIiwiQmFuYy9Lb2tpcmlGb3Jlc3QvS29raXJpRm9yZXN0X0NoaWxkX0R5bmFtaWMuYmNldHQuYnltbC56cyI6ImZpeHR1cmUifSwibG9jYXRpb25zIjpbeyJpZCI6IjAwMDAwMDAwMDAwMDAwMDEiLCJwbGFjZW1lbnQiOjEsImFjdG9yIjoiT2JqX1RCb3hfQV8wMiIsInJld2FyZCI6IlRCb3hfUnVwZWVfQmx1ZSIsImV2ZW50IjpudWxsLCJyZXNvdXJjZXMiOlsiQmFuYy9Lb2tpcmlGb3Jlc3QvS29raXJpRm9yZXN0X0NoaWxkX0R5bmFtaWMuYmNldHQuYnltbC56cyJdLCJ2YXJpYW50cyI6W3sicGxhY2VtZW50IjoxLCJyZXNvdXJjZSI6IkJhbmMvS29raXJpRm9yZXN0L0tva2lyaUZvcmVzdF9DaGlsZF9EeW5hbWljLmJjZXR0LmJ5bWwuenMiLCJhY3RvciI6Ik9ial9UQm94X0FfMDIiLCJyZXdhcmQiOiJUQm94X1J1cGVlX0JsdWUiLCJldmVudCI6bnVsbH1dLCJjb2xsZWN0aW9uX2ZsYWdfY2FuZGlkYXRlcyI6W10sImZsYWdfc3RhdHVzIjoiY2FuZGlkYXRlX25vdF9ydW50aW1lX3ZlcmlmaWVkIiwiZWxpZ2libGUiOnRydWUsImV4Y2x1c2lvbnMiOltdfSx7ImlkIjoiMDAwMDAwMDAwMDAwMDAwMiIsInBsYWNlbWVudCI6MiwiYWN0b3IiOiJPYmpfVEJveF9BXzAyIiwicmV3YXJkIjoiVEJveF9SdXBlZV9CbHVlIiwiZXZlbnQiOm51bGwsInJlc291cmNlcyI6WyJCYW5jL0tva2lyaUZvcmVzdC9Lb2tpcmlGb3Jlc3RfQ2hpbGRfRHluYW1pYy5iY2V0dC5ieW1sLnpzIl0sInZhcmlhbnRzIjpbeyJwbGFjZW1lbnQiOjIsInJlc291cmNlIjoiQmFuYy9Lb2tpcmlGb3Jlc3QvS29raXJpRm9yZXN0X0NoaWxkX0R5bmFtaWMuYmNldHQuYnltbC56cyIsImFjdG9yIjoiT2JqX1RCb3hfQV8wMiIsInJld2FyZCI6IlRCb3hfUnVwZWVfQmx1ZSIsImV2ZW50IjpudWxsfV0sImNvbGxlY3Rpb25fZmxhZ19jYW5kaWRhdGVzIjpbXSwiZmxhZ19zdGF0dXMiOiJjYW5kaWRhdGVfbm90X3J1bnRpbWVfdmVyaWZpZWQiLCJlbGlnaWJsZSI6dHJ1ZSwiZXhjbHVzaW9ucyI6W119LHsiaWQiOiIwMDAwMDAwMDAwMDAwMDAzIiwicGxhY2VtZW50IjozLCJhY3RvciI6Ik9ial9UQm94X0FfMDIiLCJyZXdhcmQiOiJUQm94X1J1cGVlX0JsdWUiLCJldmVudCI6bnVsbCwicmVzb3VyY2VzIjpbIkJhbmMvS29raXJpRm9yZXN0L0tva2lyaUZvcmVzdF9DaGlsZF9EeW5hbWljLmJjZXR0LmJ5bWwuenMiXSwidmFyaWFudHMiOlt7InBsYWNlbWVudCI6MywicmVzb3VyY2UiOiJCYW5jL0tva2lyaUZvcmVzdC9Lb2tpcmlGb3Jlc3RfQ2hpbGRfRHluYW1pYy5iY2V0dC5ieW1sLnpzIiwiYWN0b3IiOiJPYmpfVEJveF9BXzAyIiwicmV3YXJkIjoiVEJveF9SdXBlZV9CbHVlIiwiZXZlbnQiOm51bGx9XSwiY29sbGVjdGlvbl9mbGFnX2NhbmRpZGF0ZXMiOltdLCJmbGFnX3N0YXR1cyI6ImNhbmRpZGF0ZV9ub3RfcnVudGltZV92ZXJpZmllZCIsImVsaWdpYmxlIjp0cnVlLCJleGNsdXNpb25zIjpbXX0seyJpZCI6IjAwMDAwMDAwMDAwMDAwMDQiLCJwbGFjZW1lbnQiOjQsImFjdG9yIjoiT2JqX1RCb3hfQV8wMiIsInJld2FyZCI6IlRCb3hfUnVwZWVfUHVycGxlIiwiZXZlbnQiOm51bGwsInJlc291cmNlcyI6WyJCYW5jL0tva2lyaUZvcmVzdC9Lb2tpcmlGb3Jlc3RfQ2hpbGRfRHluYW1pYy5iY2V0dC5ieW1sLnpzIl0sInZhcmlhbnRzIjpbeyJwbGFjZW1lbnQiOjQsInJlc291cmNlIjoiQmFuYy9Lb2tpcmlGb3Jlc3QvS29raXJpRm9yZXN0X0NoaWxkX0R5bmFtaWMuYmNldHQuYnltbC56cyIsImFjdG9yIjoiT2JqX1RCb3hfQV8wMiIsInJld2FyZCI6IlRCb3hfUnVwZWVfUHVycGxlIiwiZXZlbnQiOm51bGx9XSwiY29sbGVjdGlvbl9mbGFnX2NhbmRpZGF0ZXMiOltdLCJmbGFnX3N0YXR1cyI6ImNhbmRpZGF0ZV9ub3RfcnVudGltZV92ZXJpZmllZCIsImVsaWdpYmxlIjp0cnVlLCJleGNsdXNpb25zIjpbXX0seyJpZCI6IjBCNjYxOThEMTdENjU4NEUiLCJwbGFjZW1lbnQiOjgyMTM3MjA3NTgyMjcwMDYyMiwiYWN0b3IiOiJPYmpfVEJveF9BXzAyIiwicmV3YXJkIjoiSXRlbV9SYW5nZV9TbGluZ1Nob3QiLCJldmVudCI6Ikluc2lkZVRoZURla3VUcmVlX0dldFNsaW5nU2hvdCIsInJlc291cmNlcyI6WyJCYW5jL0luc2lkZVRoZURla3VUcmVlL0luc2lkZVRoZURla3VUcmVlX1N0YXRpYy5iY2V0dC5ieW1sLnpzIl0sInZhcmlhbnRzIjpbeyJwbGFjZW1lbnQiOjgyMTM3MjA3NTgyMjcwMDYyMiwicmVzb3VyY2UiOiJCYW5jL0luc2lkZVRoZURla3VUcmVlL0luc2lkZVRoZURla3VUcmVlX1N0YXRpYy5iY2V0dC5ieW1sLnpzIiwiYWN0b3IiOiJPYmpfVEJveF9BXzAyIiwicmV3YXJkIjoiSXRlbV9SYW5nZV9TbGluZ1Nob3QiLCJldmVudCI6Ikluc2lkZVRoZURla3VUcmVlX0dldFNsaW5nU2hvdCJ9XSwiY29sbGVjdGlvbl9mbGFnX2NhbmRpZGF0ZXMiOltdLCJmbGFnX3N0YXR1cyI6ImNhbmRpZGF0ZV9ub3RfcnVudGltZV92ZXJpZmllZCIsImVsaWdpYmxlIjp0cnVlLCJleGNsdXNpb25zIjpbXX0seyJpZCI6IkU4OTA1OUI1NzA1OTQ3M0EiLCJwbGFjZW1lbnQiOjE2NzU3OTkyODQ5MjU0NDY3Mzg2LCJhY3RvciI6Ik9ial9UQm94X0FfMDIiLCJyZXdhcmQiOiJJdGVtX1N3b3JkX0tva2lyaSIsImV2ZW50IjoiR2V0X1N0b25lT2ZGb3Jlc3RfR2V0U3dvcmQiLCJyZXNvdXJjZXMiOlsiQmFuYy9Lb2tpcmlGb3Jlc3QvS29raXJpRm9yZXN0X0NoaWxkX0R5bmFtaWMuYmNldHQuYnltbC56cyJdLCJ2YXJpYW50cyI6W3sicGxhY2VtZW50IjoxNjc1Nzk5Mjg0OTI1NDQ2NzM4NiwicmVzb3VyY2UiOiJCYW5jL0tva2lyaUZvcmVzdC9Lb2tpcmlGb3Jlc3RfQ2hpbGRfRHluYW1pYy5iY2V0dC5ieW1sLnpzIiwiYWN0b3IiOiJPYmpfVEJveF9BXzAyIiwicmV3YXJkIjoiSXRlbV9Td29yZF9Lb2tpcmkiLCJldmVudCI6IkdldF9TdG9uZU9mRm9yZXN0X0dldFN3b3JkIn1dLCJjb2xsZWN0aW9uX2ZsYWdfY2FuZGlkYXRlcyI6W10sImZsYWdfc3RhdHVzIjoiY2FuZGlkYXRlX25vdF9ydW50aW1lX3ZlcmlmaWVkIiwiZWxpZ2libGUiOnRydWUsImV4Y2x1c2lvbnMiOltdfV0sImNhdGFsb2dfaGFzaCI6IjhjMmE0MWFjYjU3NjRjZWZhMDJjOTA5ZWU3NmEyNDNmZDhiYTczOWViMjQwZGY5YzI2OGUyNjhiZjUzNzNhZjgifQ==")))
    {
        var planDocument = HAL_ChestPlanDocument.Build(catalogFixture0.RootElement, "1234", false, true);
        using var planJson = System.Text.Json.JsonDocument.Parse(planDocument.ToJsonString());
        AssertTest.True(planDocument["plan_hash"]!.GetValue<string>() == HAL_CanonicalJson.DigestWithoutProperty(planJson.RootElement, "plan_hash"), "plan document hash covers the generated plan");
        var equipmentDocument = HAL_ChestPlanDocument.Build(catalogFixture0.RootElement, "1234", false, true,
            new HashSet<string> { "Item_Sword_Kokiri", "Item_Range_SlingShot" });
        AssertTest.True(equipmentDocument["starting_inventory"]!.AsArray().Count == 2 &&
            equipmentDocument["starting_inventory"]!.AsArray().All(row => row!["delivery_status"]!.GetValue<string>() == "development_pouch_grant") &&
            equipmentDocument["settings"]!["starting_equipment"]!.AsArray().Count == 2,
            "equipment plan records selected items and development runtime grants");
    }
    using (var catalogFixture1 = System.Text.Json.JsonDocument.Parse(Convert.FromBase64String("eyJzY2hlbWFfdmVyc2lvbiI6NCwicmVzb3VyY2VzIjp7IkJhbmMvSW5zaWRlVGhlRGVrdVRyZWUvSW5zaWRlVGhlRGVrdVRyZWVfU3RhdGljLmJjZXR0LmJ5bWwuenMiOiJmaXh0dXJlIn0sImxvY2F0aW9ucyI6W3siaWQiOiIwMDAwMDAwMDAwMDAwMDAxIiwicGxhY2VtZW50IjoxLCJhY3RvciI6Ik9ial9UQm94X0FfMDIiLCJyZXdhcmQiOiJJdGVtX0R1bmdlb25fTWFwIiwiZXZlbnQiOiJJbnNpZGVUaGVEZWt1VHJlZV9HZXRNYXAiLCJyZXNvdXJjZXMiOlsiQmFuYy9JbnNpZGVUaGVEZWt1VHJlZS9JbnNpZGVUaGVEZWt1VHJlZV9TdGF0aWMuYmNldHQuYnltbC56cyJdLCJ2YXJpYW50cyI6W3sicGxhY2VtZW50IjoxLCJyZXNvdXJjZSI6IkJhbmMvSW5zaWRlVGhlRGVrdVRyZWUvSW5zaWRlVGhlRGVrdVRyZWVfU3RhdGljLmJjZXR0LmJ5bWwuenMiLCJhY3RvciI6Ik9ial9UQm94X0FfMDIiLCJyZXdhcmQiOiJJdGVtX0R1bmdlb25fTWFwIiwiZXZlbnQiOiJJbnNpZGVUaGVEZWt1VHJlZV9HZXRNYXAifV0sImNvbGxlY3Rpb25fZmxhZ19jYW5kaWRhdGVzIjpbXSwiZmxhZ19zdGF0dXMiOiJjYW5kaWRhdGVfbm90X3J1bnRpbWVfdmVyaWZpZWQiLCJlbGlnaWJsZSI6ZmFsc2UsImV4Y2x1c2lvbnMiOlsicHJvZ3Jlc3Npb25fb3JfdW5yZXZpZXdlZF9yZXdhcmQiLCJ1bnJldmlld2VkX3NwZWNpYWxfZXZlbnQiXX0seyJpZCI6IjAwMDAwMDAwMDAwMDAwMDIiLCJwbGFjZW1lbnQiOjIsImFjdG9yIjoiT2JqX1RCb3hfQV8wMiIsInJld2FyZCI6Ikl0ZW1fRHVuZ2Vvbl9Db21wYXNzIiwiZXZlbnQiOiJJbnNpZGVUaGVEZWt1VHJlZV9HZXRDb21wYXNzIiwicmVzb3VyY2VzIjpbIkJhbmMvSW5zaWRlVGhlRGVrdVRyZWUvSW5zaWRlVGhlRGVrdVRyZWVfU3RhdGljLmJjZXR0LmJ5bWwuenMiXSwidmFyaWFudHMiOlt7InBsYWNlbWVudCI6MiwicmVzb3VyY2UiOiJCYW5jL0luc2lkZVRoZURla3VUcmVlL0luc2lkZVRoZURla3VUcmVlX1N0YXRpYy5iY2V0dC5ieW1sLnpzIiwiYWN0b3IiOiJPYmpfVEJveF9BXzAyIiwicmV3YXJkIjoiSXRlbV9EdW5nZW9uX0NvbXBhc3MiLCJldmVudCI6Ikluc2lkZVRoZURla3VUcmVlX0dldENvbXBhc3MifV0sImNvbGxlY3Rpb25fZmxhZ19jYW5kaWRhdGVzIjpbXSwiZmxhZ19zdGF0dXMiOiJjYW5kaWRhdGVfbm90X3J1bnRpbWVfdmVyaWZpZWQiLCJlbGlnaWJsZSI6ZmFsc2UsImV4Y2x1c2lvbnMiOlsicHJvZ3Jlc3Npb25fb3JfdW5yZXZpZXdlZF9yZXdhcmQiLCJ1bnJldmlld2VkX3NwZWNpYWxfZXZlbnQiXX0seyJpZCI6IjAwMDAwMDAwMDAwMDAwMDMiLCJwbGFjZW1lbnQiOjMsImFjdG9yIjoiT2JqX1RCb3hfQV8wMiIsInJld2FyZCI6IlRCb3hfRGVrdU51dEJ1bmRsZSIsImV2ZW50IjpudWxsLCJyZXNvdXJjZXMiOlsiQmFuYy9JbnNpZGVUaGVEZWt1VHJlZS9JbnNpZGVUaGVEZWt1VHJlZV9TdGF0aWMuYmNldHQuYnltbC56cyJdLCJ2YXJpYW50cyI6W3sicGxhY2VtZW50IjozLCJyZXNvdXJjZSI6IkJhbmMvSW5zaWRlVGhlRGVrdVRyZWUvSW5zaWRlVGhlRGVrdVRyZWVfU3RhdGljLmJjZXR0LmJ5bWwuenMiLCJhY3RvciI6Ik9ial9UQm94X0FfMDIiLCJyZXdhcmQiOiJUQm94X0Rla3VOdXRCdW5kbGUiLCJldmVudCI6bnVsbH1dLCJjb2xsZWN0aW9uX2ZsYWdfY2FuZGlkYXRlcyI6W10sImZsYWdfc3RhdHVzIjoiY2FuZGlkYXRlX25vdF9ydW50aW1lX3ZlcmlmaWVkIiwiZWxpZ2libGUiOnRydWUsImV4Y2x1c2lvbnMiOltdfSx7ImlkIjoiMDAwMDAwMDAwMDAwMDAwNCIsInBsYWNlbWVudCI6NCwiYWN0b3IiOiJPYmpfVEJveF9BXzAyIiwicmV3YXJkIjoiVEJveF9SdXBlZV9CbHVlIiwiZXZlbnQiOm51bGwsInJlc291cmNlcyI6WyJCYW5jL0luc2lkZVRoZURla3VUcmVlL0luc2lkZVRoZURla3VUcmVlX1N0YXRpYy5iY2V0dC5ieW1sLnpzIl0sInZhcmlhbnRzIjpbeyJwbGFjZW1lbnQiOjQsInJlc291cmNlIjoiQmFuYy9JbnNpZGVUaGVEZWt1VHJlZS9JbnNpZGVUaGVEZWt1VHJlZV9TdGF0aWMuYmNldHQuYnltbC56cyIsImFjdG9yIjoiT2JqX1RCb3hfQV8wMiIsInJld2FyZCI6IlRCb3hfUnVwZWVfQmx1ZSIsImV2ZW50IjpudWxsfV0sImNvbGxlY3Rpb25fZmxhZ19jYW5kaWRhdGVzIjpbXSwiZmxhZ19zdGF0dXMiOiJjYW5kaWRhdGVfbm90X3J1bnRpbWVfdmVyaWZpZWQiLCJlbGlnaWJsZSI6dHJ1ZSwiZXhjbHVzaW9ucyI6W119XSwiY2F0YWxvZ19oYXNoIjoiYjM1Y2I2NjlkMTAwZTE4MDdiZDYyMTYzZjExOGRlM2NiNGZkNGJmYTAwYTljYWIyNzI4M2UzMWVhM2QxMTU1OSJ9")))
    {
        var planDocument = HAL_ChestPlanDocument.Build(catalogFixture1.RootElement, "1234", true, false);
        AssertTest.True(planDocument["filler_items"]!.AsArray().All(x => x!["added"]!.GetValue<string>() == "TBox_Rupee_Blue"), "starting dungeon plan uses blue rupee filler");
    }
    var dungeonFlags = HAL_StartingDungeonFlags.Resolve(new[]
        { ("InsideTheDekuTree", "Item_Dungeon_Map"), ("InsideTheDekuTree", "Item_Dungeon_Compass") }, true);
    AssertTest.True(dungeonFlags.SequenceEqual(new uint[] { 0x06c51ff2, 0x89787395 }),
        "starting dungeon item flags match the Python mapping and are sorted");
    AssertTest.True(HAL_StartingSongs.Mask(new[] { "ZeldaLullaby", "TimeSong", "LightPrelude" }) == (1 | 16 | 2048),
        "starting song mask matches the runtime song order");
    bool invalidStartingSong = false;
    try { HAL_StartingSongs.Mask(new[] { "TimeSong", "TimeSong" }); } catch (InvalidDataException) { invalidStartingSong = true; }
    AssertTest.True(invalidStartingSong, "starting songs reject duplicates");
    bool missingStartingBag = false;
    try { HAL_StartingConsumables.Validate(new[] { 0, 0, 0, 1 }, false, false); } catch (InvalidDataException) { missingStartingBag = true; }
    AssertTest.True(missingStartingBag && HAL_StartingConsumables.Validate(new[] { 10, 20, 30, 20 }, true, true).SequenceEqual(new[] { 10, 20, 30, 20 }),
        "starting consumable counts require their equipment and obey small-bag capacities");
    bool duplicateDungeonFlagRejected = false;
    try { HAL_StartingDungeonFlags.Resolve(new[] { ("SpiritTemple", "Item_Dungeon_Map"), ("SpiritTemple", "Item_Dungeon_Map") }, true); }
    catch (InvalidDataException) { duplicateDungeonFlagRejected = true; }
    AssertTest.True(duplicateDungeonFlagRejected, "duplicate starting dungeon grants are rejected");
    AssertTest.True(HAL_GameDataHash.Name("") == 0 && HAL_GameDataHash.Name("hello") == 0x248bfa47 &&
        HAL_GameDataHash.Name("JabJabOpenedMouth") == 0xa49cb68c && HAL_GameDataHash.Name("星") == 0x25a5486b,
        "GameData hashes match the Python implementation for empty, ASCII and Unicode names");
    byte[] BymlFixture(bool little)
    {
        var data = new byte[64]; data[0] = little ? (byte)'Y' : (byte)'B'; data[1] = little ? (byte)'B' : (byte)'Y';
        void Put(int at, uint value, int size = 4)
        {
            for (int i = 0; i < size; i++) data[at + (little ? i : size - i - 1)] = (byte)(value >> (8 * i));
        }
        Put(4, 16); Put(8, 36); Put(12, 52);
        data[16] = 0xc2; Put(17, 1, 3); Put(20, 12); Put(24, 18); "Value"u8.CopyTo(data.AsSpan(28));
        data[36] = 0xc2; Put(37, 1, 3); Put(40, 12); Put(44, 16); "yes"u8.CopyTo(data.AsSpan(48));
        data[52] = 0xc1; Put(53, 1, 3); Put(56, 0, 3); data[59] = 0xa0; Put(60, 0);
        return data;
    }
    foreach (bool little in new[] { true, false })
    {
        var bytes = BymlFixture(little); var byml = new HAL_Byml(bytes);
        AssertTest.True(byml.Text(byml.Fields(byml.Root)["Value"]) == "yes", "BYML reader handles both byte orders");
        bool rejected = false;
        try { _ = new HAL_Byml(bytes[..20]); } catch (InvalidDataException) { rejected = true; }
        AssertTest.True(rejected, "BYML reader rejects truncated tables");
    }
    foreach (string encoded in new[]
    {
        "WUIHABAAAABoAAAA6AEAAMIGAAAgAAAAJwAAADcAAAA/AAAATAAAAFIAAABXAAAAQWN0b3JzAERyb3BfX0Ryb3BBY3RvcgBEeW5hbWljAEdldEV2ZW50TmFtZQBHeWFtbABIYXNoAADCBwAAJAAAADIAAABCAAAAVAAAAF8AAABtAAAAkgAAAK0AAABPYmpfVEJveF9BXzAxAFRCb3hfUnVwZWVfQmx1ZQBJdGVtX1N3b3JkX0tva2lyaQBVbmV4cGVjdGVkAE9ial9UQm94X0FfMDIAS29raXJpRm9yZXN0X1RyZWFzdXJlQm94X05hdmlDaGF0T2ZmAEdldF9TdG9uZU9mRm9yZXN0X0dldFN3b3JkAAAAAPwYLOe/APeGwQIAAAEAAKABAAAAAwAAoAUAAADBAwAAAgAAwSABAAAEAACgAAAAAAUAANUYAQAAewAAAAAAAADBAwAAAgAAwSABAAAEAACgAAAAAAUAANVQAQAAOkdZcLVZkOjBAgAAAQAAoAIAAAADAACgBgAAAMEDAAACAADBfAEAAAQAAKAEAAAABQAA1XQBAAAVAwAAAAAAAMEDAAACAADBfAEAAAQAAKAEAAAABQAA1awBAADABAAAwcHBwTQBAABYAQAAkAEAALQBAADBAQAAAAAAwNABAAA=",
        "QlkABwAAABAAAABoAAAB6MIAAAYAAAAgAAAAJwAAADcAAAA/AAAATAAAAFIAAABXQWN0b3JzAERyb3BfX0Ryb3BBY3RvcgBEeW5hbWljAEdldEV2ZW50TmFtZQBHeWFtbABIYXNoAADCAAAHAAAAJAAAADIAAABCAAAAVAAAAF8AAABtAAAAkgAAAK1PYmpfVEJveF9BXzAxAFRCb3hfUnVwZWVfQmx1ZQBJdGVtX1N3b3JkX0tva2lyaQBVbmV4cGVjdGVkAE9ial9UQm94X0FfMDIAS29raXJpRm9yZXN0X1RyZWFzdXJlQm94X05hdmlDaGF0T2ZmAEdldF9TdG9uZU9mRm9yZXN0X0dldFN3b3JkAAAAAIb3AL/nLBj8wQAAAgAAAaAAAAABAAADoAAAAAXBAAADAAACwQAAASAAAASgAAAAAAAABdUAAAEYAAAAAAAAAHvBAAADAAACwQAAASAAAASgAAAAAAAABdUAAAFQ6JBZtXBZRzrBAAACAAABoAAAAAIAAAOgAAAABsEAAAMAAALBAAABfAAABKAAAAAEAAAF1QAAAXQAAAAAAAADFcEAAAMAAALBAAABfAAABKAAAAAEAAAF1QAAAazAAAAEwcHBwQAAATQAAAFYAAABkAAAAbTBAAABAAAAwAAAAdA=",
    })
    {
        var scanned = HAL_ChestScanner.Scan(Convert.FromBase64String(encoded));
        AssertTest.True(scanned.Count == 4 && scanned[0].Placement == 9725242744547055868UL &&
            scanned[0].Reward == "TBox_Rupee_Blue" && scanned[0].SharedParameters &&
            scanned[2].Reward == "Item_Sword_Kokiri" && scanned[2].SharedParameters,
            "chest scanner reads authored shared-parameter fixtures in both byte orders");
        var resource = "Banc/KokiriForest/KokiriForest_Child_Dynamic.bcett.byml.zs";
        var candidateFlags = new Dictionary<uint, IReadOnlyList<uint>>
        {
            [HAL_GameDataHash.Name(scanned[0].Placement.ToString())] = new uint[] { 0xf870d00b }
        };
        var catalog = HAL_ChestCatalog.Build(scanned.Select(row => new HAL_ChestVariant(resource, row)), candidateFlags, expanded: true);
        var catalogDocument = HAL_ChestCatalogDocument.Build(scanned.Select(row => new HAL_ChestVariant(resource, row)),
            new Dictionary<string, string> { [resource] = "fixture" }, null, expanded: true);
        AssertTest.True(catalogDocument["catalog_hash"]!.GetValue<string>() == "42f98ba08e081afda0af62ddcb1ab1aa8c71e84e56910b5433bc8059aa80ae6f",
            "C# full catalog document matches the Python fixture hash");
        using (var catalogJson = System.Text.Json.JsonDocument.Parse(catalogDocument.ToJsonString()))
        {
            var locationDocument = HAL_LocationDocument.FromCatalog(catalogJson.RootElement);
            using var locationJson = System.Text.Json.JsonDocument.Parse(locationDocument.ToJsonString());
            AssertTest.True(HAL_CanonicalJson.Digest(locationJson.RootElement) == "ddc05a8697c7551724c99cdf276f910b8c925bed8acaa4fcd3985866a3d602d3" &&
                HAL_LocationDocument.SelectCatalog(catalogJson.RootElement, locationJson.RootElement)["catalog_hash"]!.GetValue<string>() ==
                "f1ad99d7e95853d920a7f4351331de4dbbf6b227cd4b330bab5013724516297a",
                "C# location document and selection match Python hashes");
            var excluded = locationDocument["locations"]!.AsArray().Select(node => node!.AsObject())
                .First(row => row["enabled"]!.GetValue<bool>() && row["vanilla_item_id"]?.GetValue<string>() != "kokiri_sword");
            string excludedId = excluded["check_id"]!.GetValue<string>();
            HAL_LocationDocument.ApplyExclusions(locationDocument, new[] { excludedId });
            using var excludedJson = System.Text.Json.JsonDocument.Parse(locationDocument.ToJsonString());
            var selectedCatalog = HAL_LocationDocument.SelectCatalog(catalogJson.RootElement, excludedJson.RootElement);
            var selectedRow = selectedCatalog["locations"]!.AsArray().Select(node => node!.AsObject()).Single(row => row["id"]!.GetValue<string>() == excludedId);
            var excludedPlan = HAL_ChestPlanDocument.Build(System.Text.Json.JsonSerializer.SerializeToElement(selectedCatalog), "1234", false, true);
            AssertTest.True(!selectedRow["eligible"]!.GetValue<bool>() && selectedRow["exclusions"]!.AsArray().Any(node => node!.GetValue<string>() == "disabled_in_location_json") &&
                !excludedPlan["assignments"]!.AsArray().Any(row => row!["id"]!.GetValue<string>() == excludedId),
                "excluded chest is removed from generated assignments and keeps its vanilla reward");
            locationDocument["locations"]!.AsArray()[0]!["region"] = "Modified";
            using var edited = System.Text.Json.JsonDocument.Parse(locationDocument.ToJsonString());
            bool changedMappingRejected = false;
            try { HAL_LocationDocument.SelectCatalog(catalogJson.RootElement, edited.RootElement); } catch (InvalidDataException) { changedMappingRejected = true; }
            AssertTest.True(changedMappingRejected, "C# location selection rejects edited native mappings");
        }
        var positions = scanned.Select((row, index) => new HAL_ChestVariant(resource,
            index == 0 ? row with { Position = new float[] { 0f, -0f, 1.5f } } : row));
        var positionedCatalog = HAL_ChestCatalogDocument.Build(positions, new Dictionary<string, string> { [resource] = "fixture" }, null, true);
        AssertTest.True(positionedCatalog["catalog_hash"]!.GetValue<string>() == "0734ed560dd8c7f969b71706cce313774fc8e6c07511c91b8c41a18d3f477958",
            "C# catalog hashing preserves float values and negative zero");
        const string spiritResource = "Banc/SpiritTemple/SpiritTemple_Static.bcett.byml.zs";
        var childhoodVariants = scanned.Select((row, index) => new HAL_ChestVariant(index == 1 ? spiritResource : resource,
            index == 0 ? row with { Reward = "Obj_Heart_Piece" } : row));
        var childhoodCatalog = HAL_ChestCatalogDocument.Build(childhoodVariants,
            new Dictionary<string, string> { [resource] = "fixture", [spiritResource] = "fixture2" }, null, true, childhood: true, heartPieces: true);
        AssertTest.True(childhoodCatalog["catalog_hash"]!.GetValue<string>() == "7b5573c0339258ed23c3774eb22d1c256ac334f27c5369daf1f964151268e22d",
            "C# childhood catalog, heart-piece eligibility, and coverage match Python");
        var mido = catalog.Single(row => row.Placement == scanned[0].Placement);
        AssertTest.True(mido.Eligible && mido.CollectionFlagCandidates.SequenceEqual(new[] { "F870D00B" }) &&
            catalog.Single(row => row.Placement == 789).Exclusions.Contains("progression_or_unreviewed_reward"),
            "C# catalog applies reviewed rewards and collection-flag candidates");
        var conflicts = HAL_ChestCatalog.Build(new[] { new HAL_ChestVariant(resource, scanned[0]),
            new HAL_ChestVariant(resource, scanned[0] with { Reward = "Item_Sword_Kokiri" }) }, expanded: true).Single();
        AssertTest.True(!conflicts.Eligible && conflicts.Exclusions.Contains("variant_metadata_conflict") &&
            conflicts.Exclusions.Contains("duplicate_placement_in_resource"),
            "C# catalog rejects conflicting and duplicate placement variants");
        var bytes = Convert.FromBase64String(encoded);
        if (bytes[0] == 'Y') AssertTest.True(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(HAL_ChestPatcher.PatchMidoExperiment(bytes))) ==
            "DD7D0DB21DBB2E66A86A7F28DD9A235CC970AD04E1081E803C3C438B6C3CBDD3", "research Mido exchange matches Python fixture");
        var edit = new HAL_ChestAssignment(9725242744547055868UL, "Obj_TBox_A_01", "TBox_Rupee_Blue",
            "KokiriForest_TreasureBox_NaviChatOff", "TBox_Rupee_Green", "KokiriForest_TreasureBox_NaviChatOff",
            new[] { "Banc/KokiriForest/test" });
        var patched = HAL_ChestPatcher.Patch(bytes, new[] { edit });
        var expectedHash = bytes[0] == 'Y' ? "1AD34DBE3D81597EEA0EE56741339C848DCAFC1EB1D6B1C7AC1CBD23A490E2C4" :
            "9A483ECE5FE70E98A86A1214E18E0FECDD93CA2CEE4A9E47AA2F8B5BF66E206A";
        AssertTest.True(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(patched)) == expectedHash &&
            HAL_ChestScanner.Scan(patched)[1].Reward == "TBox_Rupee_Blue",
            "chest patch matches Python bytes and preserves shared parameter nodes");
        var bagPatch = HAL_ChestPatcher.Patch(bytes, new[] { edit with { Reward = HAL_EventRewardPlan.ChestReward } });
        AssertTest.True(HAL_ChestScanner.Scan(bagPatch).Single(row => row.Placement == edit.Placement).Reward == HAL_EventRewardPlan.ChestReward,
            "event reward bag can be written to a chest without changing shared source data");
        SkulltulaFixture.CheckMap(bagPatch,edit.Placement);
        bool rejectedPatch = false;
        try { HAL_ChestPatcher.Patch(bytes, new[] { edit, edit }); } catch (InvalidDataException) { rejectedPatch = true; }
        AssertTest.True(rejectedPatch, "chest patch rejects duplicate targets");
        var swordEdit = new HAL_ChestAssignment(16757992849254467386UL, "Obj_TBox_A_02", "Item_Sword_Kokiri",
            "Get_StoneOfForest_GetSword", "TBox_Rupee_Blue", null, new[] { "Banc/KokiriForest/test" });
        var swordPatch = HAL_ChestPatcher.Patch(bytes, new[] { swordEdit });
        var swordHash = bytes[0] == 'Y' ? "E9C64F655CB7256270885D9FA18F6E345F664BB1854B4901C64CBA6B522E6542" :
            "381727D08A16A0401A8FDE3728FB70C3AA85AA01311E1B19B8D527F25681159F";
        AssertTest.True(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(swordPatch)) == swordHash &&
            HAL_ChestScanner.Scan(swordPatch)[2].Event is null && HAL_ChestScanner.Scan(swordPatch)[3].Event is not null,
            "authored sword event removal matches Python and leaves shared nodes intact");
        var arrowPatch = HAL_ChestPatcher.Patch(swordPatch, new[] { swordEdit with { ExpectedReward = "TBox_Rupee_Blue", ExpectedEvent = null,
            Reward = "TBox_ArrowBundle_Medium", Event = null } });
        var ammoRoute = new HAL_AmmoChestRoute(swordEdit.Placement, swordEdit.Actor, "TBox_ArrowBundle_Medium");
        var guardedMap = HAL_ChestScanner.Scan(HAL_AmmoChestRoutes.Patch(arrowPatch, new[] { ammoRoute }));
        AssertTest.True(guardedMap[2].Reward == ammoRoute.Reward && guardedMap[2].Event == HAL_AmmoChestRoutes.EventName(ammoRoute.Actor, ammoRoute.Reward) &&
            guardedMap[3].Event == HAL_ChestScanner.Scan(arrowPatch)[3].Event && HAL_ChestScanner.Scan(arrowPatch)[2].Event is null,
            "ammo routing adds an event to one chest without altering its reward or shared sibling");
        bool guardedSpecial = false;
        try { HAL_AmmoChestRoutes.Patch(bytes, new[] { new HAL_AmmoChestRoute(swordEdit.Placement, swordEdit.Actor, "TBox_ArrowBundle_Medium") }); }
        catch (InvalidDataException) { guardedSpecial = true; }
        AssertTest.True(guardedSpecial, "ammo routing rejects changed rewards or existing special events");
        var smallArrow = HAL_ChestPatcher.Patch(bytes, new[] { edit with { Reward = "TBox_ArrowBundle_Medium" } });
        var nativeAmmo = HAL_NativeAmmoChest.Patch(smallArrow, new HAL_AmmoChestRoute(edit.Placement, edit.Actor, "TBox_ArrowBundle_Medium"));
        var nativeByml = new HAL_Byml(nativeAmmo);
        var nativeActor = nativeByml.Array(nativeByml.Fields(nativeByml.Root)["Actors"])
            .Select(x => nativeByml.Fields((int)nativeByml.Child(x, 0xc1)))
            .Single(x => x.TryGetValue("Hash", out var h) && nativeByml.UInt((int)nativeByml.Child(h, 0xd5), 8) == edit.Placement);
        var nativeDynamic = nativeByml.Fields((int)nativeByml.Child(nativeActor["Dynamic"], 0xc1));
        AssertTest.True(nativeByml.Text(nativeActor["Gyaml"]) == "Obj_TBox_A_06" &&
            nativeByml.Text(nativeDynamic["Drop__CheckActor"]) == "Item_Quiver_Small" &&
            nativeByml.Text(nativeDynamic["Drop__DropActor"]) == "TBox_Rupee_Blue" &&
            nativeByml.Text(nativeDynamic["Drop__DropActor_Substitute"]) == "TBox_ArrowBundle_Medium" &&
            nativeByml.Text(nativeDynamic["GetEventName"]) == edit.ExpectedEvent &&
            HAL_ChestScanner.Scan(nativeAmmo)[1].Reward == "TBox_Rupee_Blue",
            "native ammo chest uses the game's conditional fields and preserves the special event and sibling");
        foreach (var (reward, bag) in new[] { ("Item_BombBundle_Small", "Item_BombBag_Small"),
                     ("TBox_DekuSeedBundle", "Item_BulletBag_Small"), ("TBox_ArrowBundle_Large", "Item_Quiver_Small") })
        {
            var source = HAL_ChestPatcher.Patch(bytes, new[] { edit with { Reward = reward } });
            var result = new HAL_Byml(HAL_NativeAmmoChest.Patch(source, new HAL_AmmoChestRoute(edit.Placement, edit.Actor, reward)));
            var selected = result.Array(result.Fields(result.Root)["Actors"])
                .Select(x => result.Fields((int)result.Child(x, 0xc1)))
                .Single(x => x.TryGetValue("Hash", out var h) && result.UInt((int)result.Child(h, 0xd5), 8) == edit.Placement);
            var conditionalFields = result.Fields((int)result.Child(selected["Dynamic"], 0xc1));
            AssertTest.True(result.Text(conditionalFields["Drop__CheckActor"]) == bag &&
                result.Text(conditionalFields["Drop__DropActor"]) == "TBox_Rupee_Blue" &&
                result.Text(conditionalFields["Drop__DropActor_Substitute"]) == reward &&
                result.Text(conditionalFields["GetEventName"]) == edit.ExpectedEvent,
                $"native {reward} chest preserves its event and checks the correct bag");
        }
    }
    var eventBytes = Convert.FromBase64String("QkZFVkZMAAAABAAA//4AAAAAAAAAAAAAAAAAAAAAAAABAAAAAAAAAEAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABFVkZMAAAAAAAAAAAAAAAAAQAAAAAAAwAAAAAAAAAAAAAEAAAAAAAAAAEAAAAAAAAAAgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA8EAAAAAAAAAAAAAAAAAAAAAAAAAAAAACIEAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABQBAAAAAAAAAAAAQAAAAAAWQQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAADOBAAAAAAAAAAAAgAAAAAA1wQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABMBQAAAAAAAAAA//8AAAEAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAwAT3BlbmluZ0V2ZW50ABAARXZlbnRTeXN0ZW1BY3RvcgAyBAAAAAAAAEQEAAAAAAAADwBFdmVudFRyYW5zaXRpb24ACQBFdmVudFdhaXQABgBFdmVudDAAAQABAAAAAABxBAAAAAAAAKUEAAAAAAAARElDIAEAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACZBAAAAAAAAAkARXZlbnROYW1lAAUAAAAAAAAAAAAAAAAAAAC9BAAAAAAAAA4ARG1fS29raXJpXzAwMDAABgBFdmVudDEAAQABAAAAAADvBAAAAAAAACMFAAAAAAAARElDIAEAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAXBQAAAAAAAAkARXZlbnROYW1lAAUAAAAAAAAAAAAAAAAAAAA7BQAAAAAAAA4ARG1fS29raXJpXzAwMzAABgBFdmVudDIA");
    var eventFlow = new HAL_EventFlow(eventBytes, "OpeningEvent");
    int eventNameSlot = eventFlow.StringParameterPointerSlot(0, "EventName");
    AssertTest.True(eventFlow.UInt(eventNameSlot, 8) != 0, "event parameter pointer slot resolves a bounded string");
    var switchFixture = new byte[eventBytes.Length + 8]; eventBytes.CopyTo(switchFixture, 0);
    int switchAt = eventFlow.EventOffset(0); switchFixture[switchAt + 8] = 1;
    BinaryPrimitives.WriteUInt16LittleEndian(switchFixture.AsSpan(switchAt + 10), 1);
    BinaryPrimitives.WriteUInt64LittleEndian(switchFixture.AsSpan(switchAt + 24), (ulong)eventBytes.Length);
    BinaryPrimitives.WriteUInt32LittleEndian(switchFixture.AsSpan(eventBytes.Length), 7);
    BinaryPrimitives.WriteUInt16LittleEndian(switchFixture.AsSpan(eventBytes.Length + 4), 2);
    AssertTest.True(new HAL_EventFlow(switchFixture, "OpeningEvent").SwitchRoutes(0)[7] == 2,
        "event switch routes map case values to event targets");
    BinaryPrimitives.WriteUInt16LittleEndian(switchFixture.AsSpan(eventBytes.Length + 4), 3);
    bool badRoute = false;
    try { new HAL_EventFlow(switchFixture, "OpeningEvent").SwitchRoutes(0); } catch (InvalidDataException) { badRoute = true; }
    AssertTest.True(badRoute, "event switch routes reject targets outside the flow");
    EventRewardFixture.Check();
    SkulltulaFixture.Check();
    SariaRewardFixture.Check();
    WorldReachabilityFixture.Check();
    ProgressionFixture.Check();
    BossIntroFixture.Check(root);
    OpenForestOpeningFixture.Check();
    bool rejectedUnknownRewardFlow = false;
    try { HAL_EventRewardPatch.LostWoodsTargetGreenRupee(eventBytes); } catch (InvalidDataException) { rejectedUnknownRewardFlow = true; }
    AssertTest.True(rejectedUnknownRewardFlow, "event reward patch rejects an unreviewed flow");
    AssertTest.True(HAL_EventRewardScan.Inspect("fixture", "OpeningEvent", eventBytes).Count == 0,
        "event reward scanner excludes ordinary event actions");
    var timelineOnly = (byte[])eventBytes.Clone();
    System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(timelineOnly.AsSpan(32), 0);
    System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(timelineOnly.AsSpan(34), 1);
    AssertTest.True(HAL_EventRewardScan.IsTimelineOnly(timelineOnly) && !HAL_EventRewardScan.IsTimelineOnly(eventBytes) &&
        !HAL_EventRewardScan.IsTimelineOnly(timelineOnly.AsSpan(0, 35)),
        "event scanner distinguishes timeline-only headers from flows and truncated files");
    var timelineFixture = new byte[512];
    "BFEVFL\0\0"u8.CopyTo(timelineFixture);
    void T16(int at, ushort value) => System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(timelineFixture.AsSpan(at), value);
    void T32(int at, uint value) => System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(timelineFixture.AsSpan(at), value);
    void T64(int at, ulong value) => System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(timelineFixture.AsSpan(at), value);
    void TString(int at, string value) { var bytes = System.Text.Encoding.UTF8.GetBytes(value); T16(at, (ushort)bytes.Length); bytes.CopyTo(timelineFixture.AsSpan(at + 2)); }
    T16(8, 0x400); T16(12, 0xfeff); T16(34, 1); T64(56, 64); T64(64, 72);
    "TLIN"u8.CopyTo(timelineFixture.AsSpan(72)); T16(72 + 0x14, 1); T16(72 + 0x18, 1);
    T64(72 + 0x28, 168); T64(72 + 0x30, 224); T64(168, 400); T64(168 + 0x18, 248); T16(168 + 0x30, 1);
    T64(248, 416); T64(224 + 0x10, 256); timelineFixture[256] = 1; T16(258, 1); T64(264, 280); T64(272, 320);
    "DIC "u8.CopyTo(timelineFixture.AsSpan(280)); T32(284, 1); T64(312, 432); timelineFixture[320] = 5; T64(336, 448);
    TString(400, "Actor"); TString(416, "Give"); TString(432, "ActorName"); TString(448, "Item_Test");
    var timelineCandidates = HAL_EventTimelineScan.Inspect("fixture", timelineFixture);
    AssertTest.True(timelineCandidates.Count == 1 && timelineCandidates[0].Action == "Give" && timelineCandidates[0].Value == "Item_Test",
        "timeline scanner identifies item references with their action context");
    T64(264, 9999); bool badTimelineRejected = false;
    try { HAL_EventTimelineScan.Inspect("fixture", timelineFixture); } catch (InvalidDataException) { badTimelineRejected = true; }
    AssertTest.True(badTimelineRejected, "timeline scanner rejects an out-of-range dictionary");
    var eventRewardFixture = (byte[])eventBytes.Clone();
    eventRewardFixture[eventFlow.EventOffset(0) + 8] = 4;
    "ActorName"u8.CopyTo(eventRewardFixture.AsSpan(eventRewardFixture.AsSpan().IndexOf("EventName"u8)));
    "Item_Test_0000"u8.CopyTo(eventRewardFixture.AsSpan(eventRewardFixture.AsSpan().IndexOf("Dm_Kokiri_0000"u8)));
    var eventCandidates = HAL_EventRewardScan.Inspect("fixture", "OpeningEvent", eventRewardFixture);
    AssertTest.True(eventCandidates.Count == 1 && eventCandidates[0].EventIndex == 0 && eventCandidates[0].Actor == "Item_Test_0000",
        "event reward scanner identifies item-giving subflow candidates");
    AssertTest.True(HAL_EventRewardScan.Inspect("fixture.110", "OpeningEvent.110", eventRewardFixture).Count == 1,
        "event reward scanner recognizes a versioned filename whose internal flow name is unchanged");
    AssertTest.True(eventFlow.Count == 3 && eventFlow.Action(0) == ("EventSystemActor", "EventTransition") &&
        eventFlow.StringParameters(0)["EventName"] == "Dm_Kokiri_0000",
        "event flow reader matches the authored Python event fixture");
    var noParameters = (byte[])eventBytes.Clone();
    Array.Clear(noParameters, eventFlow.EventOffset(0) + 16, 8);
    AssertTest.True(new HAL_EventFlow(noParameters, "OpeningEvent").StringParameters(0).Count == 0,
        "event flow reader treats a null parameter pointer as an empty parameter list");
    AssertTest.True(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(HAL_OpeningCamera.PatchIntroExperiment(eventBytes))) ==
        "F701CC4FDF6F5978673C953D21208906CAC85F0C6A48CFD993EEFAC4EF62141A", "dormant opening experiment matches Python");
    bool badFlowRejected = false;
    try { _ = new HAL_EventFlow(eventBytes[..32], "OpeningEvent"); } catch (InvalidDataException) { badFlowRejected = true; }
    AssertTest.True(badFlowRejected, "event flow reader rejects truncated resources");
    // Authored fixture from tests/test_intro_skip.py, compressed to keep the test compact.
    var cameraCompressed = Convert.FromBase64String("eJztl8FKw0AQhketoCg0eimtevEsnjyLVVoF2xqaoBdBlnQIwTYbN5tAb76AL+IzePRJfAlvxphsNSYNSZDiZb9DNsz//ztzWBb2tNu57vYAoAYQfECGJbGeZKWIx5x6WUT3b1aj795PQUkNsvzHhinqyvx6K6c+G+M4R+/l1GdjP+foLwX93nL094Jcc2u+fpCpB0EyV0zsL89KNXt4IMvP8kUQVDsaVeeXfumX/ir+2oL3X6x/t+KN1Qjvq2o3lkQikUgkEsl/sAlXDtqWbXZ8tDmswcAx7gbEt2BfvCybEEk6s0wTmYb8gviok/E9jmAj1s7IBBmBW5F4EuthKmmPLhEdjROOfXRdYqJO1TGZIhMbtOJAn4YNqE6YiVylrhDrcB7+aFOX46RtcMrgVfTZ+dVniA8eurztcaqFk8I26MgYsexkNBDRhojGDtWyh+hSjxkISqwkQ0fizbweSzfE4vAJeKefEw==");
    using var cameraStream = new System.IO.Compression.ZLibStream(new MemoryStream(cameraCompressed), System.IO.Compression.CompressionMode.Decompress);
    using var cameraOutput = new MemoryStream(); cameraStream.CopyTo(cameraOutput);
    var cameraBytes = cameraOutput.ToArray();
    var cameraPatched = HAL_OpeningCamera.Patch(cameraBytes);
    AssertTest.True(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(cameraPatched)) ==
        "40D82EF5CF3DE2F486FEA98AE6BD2264828E71EDDEA8628F14DE04AC0D7047AA" &&
        BinaryPrimitives.ReadUInt16LittleEndian(cameraBytes.AsSpan(0x200 + 2 * 48 + 10)) == 3,
        "opening camera edit matches Python and preserves its input");
    bool cameraRejected = false;
    try { HAL_OpeningCamera.Patch(cameraPatched); } catch (InvalidDataException) { cameraRejected = true; }
    AssertTest.True(cameraRejected, "opening camera edit rejects an unexpected flow");
    ConversationFixtures.Check();
    TutorialFixtures.Check();
    NaviFixtures.Check();
    DekuExitFixture.Check();
    ChildStealthFixtures.Check();
    StealthMapFixtures.Check();
    {
        var texture = new byte[0x300]; "BNTX"u8.CopyTo(texture); "Ounc"u8.CopyTo(texture.AsSpan(0x20));
        BinaryPrimitives.WriteUInt32LittleEndian(texture.AsSpan(0x24), 1); BinaryPrimitives.WriteUInt64LittleEndian(texture.AsSpan(0x28), 0x40);
        BinaryPrimitives.WriteUInt64LittleEndian(texture.AsSpan(0x40), 0x80); "BRTI"u8.CopyTo(texture.AsSpan(0x80));
        BinaryPrimitives.WriteUInt16LittleEndian(texture.AsSpan(0x96), 1); BinaryPrimitives.WriteUInt32LittleEndian(texture.AsSpan(0x9c), 0x1a06);
        foreach (int at in new[] { 0xa4, 0xa8 }) BinaryPrimitives.WriteUInt32LittleEndian(texture.AsSpan(at), 4);
        foreach (int at in new[] { 0xac, 0xb0 }) BinaryPrimitives.WriteUInt32LittleEndian(texture.AsSpan(at), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(texture.AsSpan(0xd0), 8); BinaryPrimitives.WriteUInt64LittleEndian(texture.AsSpan(0xe0), 0x180);
        BinaryPrimitives.WriteUInt64LittleEndian(texture.AsSpan(0xf0), 0x1a0); BinaryPrimitives.WriteUInt16LittleEndian(texture.AsSpan(0x180), 4);
        "test"u8.CopyTo(texture.AsSpan(0x182)); BinaryPrimitives.WriteUInt64LittleEndian(texture.AsSpan(0x1a0), 0x200);
        "12345678"u8.CopyTo(texture.AsSpan(0x200)); var view = new HAL_TunicTexture(texture);
        AssertTest.True(view.Name == "test" && view.Linear(0).SequenceEqual("12345678"u8.ToArray()), "texture parser reads authored block-linear mip");
        bool invalidTexture = false; try { _ = new HAL_TunicTexture(texture[..0x100]); } catch (InvalidDataException) { invalidTexture = true; }
        AssertTest.True(invalidTexture, "texture parser rejects truncated input");
        new byte[] { 0x00, 0xf8, 0xe0, 0x07, 0, 0, 0, 0 }.CopyTo(texture, 0x200);
        var recolored = HAL_TexturePatch.Apply(texture, "sword_blade_color", "#0000FF", "unused", blade: (0,0,1,1));
        AssertTest.True(recolored.ChangedBlocks.SequenceEqual(new[] { 1 }) && recolored.AlphaSkipped.SequenceEqual(new[] { 0 }) &&
            !recolored.Data.SequenceEqual(texture), "texture patch changes selected opaque BC blocks");
        BinaryPrimitives.WriteUInt32LittleEndian(texture.AsSpan(0x9c), 0x1d01);
        texture[0x200] = 255; texture[0x201] = 0; Array.Clear(texture, 0x202, 6);
        var icon = HAL_IconTexture.Read(texture, "test");
        AssertTest.True(HAL_IconTexture.DecodeBc4(icon).All(x => x == 255), "icon parser untile and decodes BC4 blocks");
        string fixtureRoot = Path.Combine(root, "icon-builder"), game = Path.Combine(fixtureRoot, "game"), romfs = Path.Combine(game, "romfs");
        string assets = Path.Combine(fixtureRoot, "builder", "Adventure", "resources"), pack = Path.Combine(romfs, "Pack"), icons = Path.Combine(romfs, "UI");
        foreach (string path in new[] { assets, pack, icons }) Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(game, OperatingSystem.IsWindows() ? "hookshot.exe" : "hookshot"), "fixture");
        File.WriteAllText(Path.Combine(game, OperatingSystem.IsWindows() ? "hookshot_vk.dll" : "hookshot_vk.so"), "fixture");
        File.WriteAllText(Path.Combine(game, "image.bin"), "fixture");
        var archive = new byte[0x20]; BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(0x0c), 0x20);
        File.WriteAllBytes(Path.Combine(pack, "ZsDic.pack.zs"), HAL_Zstd.Compress(archive));
        File.WriteAllBytes(Path.Combine(icons, "fixture.bntx.zs"), HAL_Zstd.Compress(texture));
        File.WriteAllText(Path.Combine(assets, "icon_manifest.json"), "{\"icons\":{\"test_icon\":{\"verified\":true,\"archive\":\"UI/fixture.bntx.zs\",\"texture\":\"test\",\"alpha_mode\":\"mask\",\"crop\":null}}}");
        string cache = HAL_IconDatabase.Build(fixtureRoot, game, Path.Combine(fixtureRoot, "cache"));
        AssertTest.True(File.Exists(Path.Combine(cache, "test_icon.png")) && File.Exists(Path.Combine(cache, "index.json")),
            "C# icon builder publishes a synthetic BC4 icon cache");
    }
    {
        var model = new byte[0x300]; new byte[] { 0x46, 0x52, 0x45, 0x53, 32, 32, 32, 32, 0, 4, 10, 0, 255, 254 }.CopyTo(model, 0);
        BinaryPrimitives.WriteUInt64LittleEndian(model.AsSpan(40), 0x80); BinaryPrimitives.WriteUInt64LittleEndian(model.AsSpan(48), 0x70);
        BinaryPrimitives.WriteUInt32LittleEndian(model.AsSpan(0x74), 1); "FMDL"u8.CopyTo(model.AsSpan(0x80));
        BinaryPrimitives.WriteUInt64LittleEndian(model.AsSpan(0xb8), 0x100); BinaryPrimitives.WriteUInt16LittleEndian(model.AsSpan(0xec), 1);
        "FMAT"u8.CopyTo(model.AsSpan(0x100)); BinaryPrimitives.WriteUInt64LittleEndian(model.AsSpan(0x108), 0x200);
        BinaryPrimitives.WriteUInt64LittleEndian(model.AsSpan(0x120), 0x240); model[0x1a3] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(model.AsSpan(0x200), 5); "cloth"u8.CopyTo(model.AsSpan(0x202));
        BinaryPrimitives.WriteUInt64LittleEndian(model.AsSpan(0x240), 0x260); BinaryPrimitives.WriteUInt16LittleEndian(model.AsSpan(0x260), 4);
        "orig"u8.CopyTo(model.AsSpan(0x262));
        var aliases = new Dictionary<string, HAL_PlayerModel.Alias> { ["orig"] = new("repl", new HashSet<string> { "cloth" }) };
        var edited = HAL_PlayerModel.Patch(model, aliases);
        AssertTest.True(edited.Data.AsSpan(0x262, 4).SequenceEqual("repl"u8) && model.AsSpan(0x262, 4).SequenceEqual("orig"u8),
            "player model alias changes only the reviewed reference");
        bool rejected = false; try { HAL_PlayerModel.Patch(model, new Dictionary<string, HAL_PlayerModel.Alias> { ["orig"] = new("repl", new HashSet<string> { "other" }) }); }
        catch (InvalidDataException) { rejected = true; }
        AssertTest.True(rejected, "player model alias rejects unreviewed materials");
    }
    {
        byte[] image = [40,150,40,255,0,255,0,255,100,100,100,255,50,200,80,0];
        var fabric = HAL_ColorTransforms.Fabric(image, 2, 2, "kokiri_tunic_color", "#CC4422");
        var blade = HAL_ColorTransforms.Blade(image, 2, 2, "#99AADD", (0,0,1,1));
        AssertTest.True(fabric.Pixels.SequenceEqual(Convert.FromBase64String("lzEZ//9TK/9kZGT/MshQAA==")) &&
            blade.Pixels.SequenceEqual(Convert.FromBase64String("aHSW/7HF//9FTWT/MshQAA==")), "clothing and blade pixel transforms match Python fixture");
        byte[] varied = Convert.FromBase64String("8omz/0nDBf+/94z/63QA/0rhvP9TrWv/HmYm/6zPLP8JH3L/Ltjj/znYRf+gUxr/VyrN/9bw9P/EDyr/YoW2/7nFnv86gXj/q7q+/1sOw//cEA3/cdoW/8ZoNP9wWyf/jBLd/43/sP8a67z/aq2Q/+nz9v96Ve3/ul1g/3ADif+uW3v/BBR+/zu4T/8MpY//PHX0//q1hv/jU+X/lReO/6jjbf+Rgg7/AQ1D/4jXRv97Zt7/8R6E/xTV3P8vz2X/sbzF/1IIT/8PEyX/1GbC/zK0YP+fivv/P2zJ/30k0P+2pSr/qkgu/5+fXP/fu1r/D2v5/+P4W/8drun/eqcx/w==");
        byte[] expectedFabric = Convert.FromBase64String("8omz/6RG5v+2Tv//63QA/0rhvP+SPsz/ViV4/62Cof8JH3L/Ltjj/7ZO//+gUxr/VyrN/9bw9P/EDyr/YoW2/6pg2v86gXj/q7q+/1sOw//cEA3/tk7//8ZoNP9wWyf/jBLd/7ZO//8a67z/fnSv/+nz9v96Ve3/ul1g/3ADif+uW3v/BBR+/5tC2f8MpY//PHX0//q1hv/jU+X/lReO/7ZO//+Rgg7/AQ1D/7VO/v97Zt7/8R6E/xTV3P+uS/T/sbzF/1IIT/8PEyX/1GbC/5dB1P+fivv/P2zJ/30k0P+2pSr/qkgu/5+fXP/fu1r/D2v5/9nRgf8drun/jTzF/w==");
        AssertTest.True(HAL_ColorTransforms.Fabric(varied,8,8,"kokiri_tunic_color","#AB49EF").Pixels.SequenceEqual(expectedFabric), "varied fabric colors match Python fixture");
        var cosmeticRandom = new HAL_PythonRandom("0000000000001234");
        AssertTest.True(Enumerable.Range(0, 12).Select(_ => cosmeticRandom.ColorComponent()).SequenceEqual(new[] { 89,161,211,40,159,197,142,54,143,101,108,172 }),
            "seeded cosmetic colors match Python random sequence");
        var trailSettings = new HAL_SwordCosmetics { Kokiri = new HAL_SwordSetting { TrailColor = "#ab12ef", TrailMode = "locked" },
            Master = new HAL_SwordSetting { TrailColor = "#ffffff", TrailMode = "seed" },
            Biggoron = new HAL_SwordSetting { TrailColor = "#ffffff", TrailMode = "seed" } };
        var trailColors = HAL_SwordTrailColors.Resolve(trailSettings, "0000000000001234");
        AssertTest.True(trailColors["kokiri"] == "#AB12EF" && trailColors["master"] == "#59A1D3" &&
            trailColors.SequenceEqual(HAL_SwordTrailColors.Resolve(trailSettings, "0000000000001234")),
            "sword trail locked and seeded colors resolve consistently");
        byte[] trailFixture = new byte[512];
        "BNSH"u8.CopyTo(trailFixture); "grsc"u8.CopyTo(trailFixture.AsSpan(96));
        BinaryPrimitives.WriteUInt32LittleEndian(trailFixture.AsSpan(28), (uint)trailFixture.Length);
        for (int channel = 0; channel < 3; channel++)
        {
            var instruction = trailFixture.AsSpan(128 + channel * 16, 16);
            BinaryPrimitives.WriteUInt32LittleEndian(instruction, 0x12107a20u + ((uint)channel << 16));
            BinaryPrimitives.WriteUInt32LittleEndian(instruction[4..], 0x03805c00u + ((uint)channel << 8));
            BinaryPrimitives.WriteUInt32LittleEndian(instruction[8..], 0x00410000u);
            BinaryPrimitives.WriteUInt32LittleEndian(instruction[12..], 0x000fe200u);
        }
        byte[] redTrail = HAL_SwordTrailPatch.Apply(trailFixture, "#FF0000");
        AssertTest.True(BinaryPrimitives.ReadUInt32LittleEndian(redTrail.AsSpan(128)) == 0x12107820u &&
            BinaryPrimitives.ReadSingleLittleEndian(redTrail.AsSpan(132)) == 1f &&
            BinaryPrimitives.ReadSingleLittleEndian(redTrail.AsSpan(148)) == 0f &&
            BinaryPrimitives.ReadSingleLittleEndian(redTrail.AsSpan(164)) == 0f &&
            BinaryPrimitives.ReadUInt32LittleEndian(trailFixture.AsSpan(128)) == 0x12107a20u,
            "trail shader applies RGB without changing the source");
        byte[] mixedTrail = HAL_SwordTrailPatch.Apply(trailFixture, "#3280FF");
        AssertTest.True(Math.Abs(BinaryPrimitives.ReadSingleLittleEndian(mixedTrail.AsSpan(132)) - 50 / 255f) < 0.00001f &&
            Math.Abs(BinaryPrimitives.ReadSingleLittleEndian(mixedTrail.AsSpan(148)) - 128 / 255f) < 0.00001f &&
            BinaryPrimitives.ReadSingleLittleEndian(mixedTrail.AsSpan(164)) == 1f,
            "trail shader preserves independent RGB channels");
        byte[] duplicateTrail = (byte[])trailFixture.Clone();
        trailFixture.AsSpan(128, 48).CopyTo(duplicateTrail.AsSpan(256));
        bool trailRejected = false;
        try { HAL_SwordTrailPatch.Apply(duplicateTrail, "#FF0000"); } catch (InvalidDataException) { trailRejected = true; }
        AssertTest.True(trailRejected, "trail shader rejects ambiguous matches");
        byte[] bc1 = [0x00, 0xf8, 0xe0, 0x07, 0, 0, 0, 0]; var decoded = HAL_BcTextures.Decode(bc1, 4, 4, false);
        AssertTest.True(Enumerable.Range(0, 16).All(i => decoded[i * 4] == 255 && decoded[i * 4 + 1] == 0 && decoded[i * 4 + 3] == 255),
            "BC1 texture decoding expands authored red blocks");
        var encoded = HAL_BcTextures.Encode(decoded, 4, 4, bc1, false);
        AssertTest.True(HAL_BcTextures.Decode(encoded, 4, 4, false).SequenceEqual(decoded), "BC1 encoding preserves a solid block");
        string codecFixture = Path.Combine(root, "packaged-codec"); Directory.CreateDirectory(codecFixture);
        string bundledCodec = Path.Combine(codecFixture, OperatingSystem.IsWindows() ? "astcenc-sse2.exe" : "astcenc-sse2");
        string overrideCodec = Path.Combine(codecFixture, "override-codec");
        File.WriteAllText(bundledCodec, "authored codec"); File.WriteAllText(overrideCodec, "authored override");
        var oldCodecOverride = Environment.GetEnvironmentVariable("HOOKSHOT_ASTCENC");
        try
        {
            Environment.SetEnvironmentVariable("HOOKSHOT_ASTCENC", null);
            AssertTest.True(HAL_AstcCodec.Find(root, codecFixture) == bundledCodec, "packaged ASTC codec is discovered beside the launcher");
            Environment.SetEnvironmentVariable("HOOKSHOT_ASTCENC", overrideCodec);
            AssertTest.True(HAL_AstcCodec.Find(root, codecFixture) == overrideCodec, "explicit ASTC codec overrides the packaged codec");
        }
        finally { Environment.SetEnvironmentVariable("HOOKSHOT_ASTCENC", oldCodecOverride); }
        string codec = Environment.GetEnvironmentVariable("HAL_TEST_ASTCENC") ??
            Path.Combine(Environment.CurrentDirectory, "work", "tools", "astcenc", OperatingSystem.IsWindows() ? "astcenc-sse2.exe" : "astcenc-sse2");
        if (File.Exists(codec))
        {
            byte[] blocks = HAL_AstcCodec.Encode(codec, decoded, 4, 4, 4, 4);
            byte[] pixels = HAL_AstcCodec.Decode(codec, blocks, 4, 4, 4, 4);
            AssertTest.True(blocks.Length == 16 && pixels.Length == 64 && Enumerable.Range(0, 16).All(i => pixels[i * 4 + 3] == 255),
                "ASTC codec round-trips a synthetic opaque block");
            byte[] translucent = Enumerable.Range(0,16).SelectMany(_ => new byte[] { 255, 0, 0, 0 }).ToArray();
            var transparentPixels = HAL_AstcCodec.Decode(codec, HAL_AstcCodec.Encode(codec, translucent, 4, 4, 4, 4), 4, 4, 4, 4);
            AssertTest.True(Enumerable.Range(0,16).All(i => transparentPixels[i*4+3] == 0), "ASTC codec retains transparent pixels");
            byte[] semi = Enumerable.Range(0,16).SelectMany(_ => new byte[] { 200, 100, 50, 128 }).ToArray();
            var semiPixels = HAL_AstcCodec.Decode(codec, HAL_AstcCodec.Encode(codec, semi, 4, 4, 4, 4), 4, 4, 4, 4);
            AssertTest.True(Math.Abs(semiPixels[3] - 128) <= 1 && Math.Abs(semiPixels[0] - 200) <= 5, $"ASTC codec retains unpremultiplied color and alpha ({semiPixels[0]}, {semiPixels[3]})");
            CosmeticFixture.Check(root, codec);
        }
    }
    FieldFixtures.Check();
    OpeningDefaultsFixtures.Check();
    var sizeFixture = new byte[22 + 20 + 4];
    "RESTBL"u8.CopyTo(sizeFixture);
    BinaryPrimitives.WriteUInt32LittleEndian(sizeFixture.AsSpan(6), 1);
    BinaryPrimitives.WriteUInt32LittleEndian(sizeFixture.AsSpan(10), 20);
    BinaryPrimitives.WriteUInt32LittleEndian(sizeFixture.AsSpan(18), 1);
    "map.byml"u8.CopyTo(sizeFixture.AsSpan(22));
    BinaryPrimitives.WriteUInt32LittleEndian(sizeFixture.AsSpan(42), 120);
    var sizeTable = new HAL_ResourceSizeTable(sizeFixture);
    var resized = sizeTable.Patch(new Dictionary<string, (long, long)> { ["map.byml.zs"] = (100, 150) });
    AssertTest.True(BinaryPrimitives.ReadUInt32LittleEndian(resized.AsSpan(42)) == 170 &&
        BinaryPrimitives.ReadUInt32LittleEndian(sizeFixture.AsSpan(42)) == 120,
        "resource-size patch retains the original allocation allowance without mutating input");
    var cloned = sizeTable.CloneEntries(new Dictionary<string, string> { ["alias.byml.zs"] = "map.byml.zs" });
    var resizedAlias = new HAL_ResourceSizeTable(cloned).Patch(new Dictionary<string, (long, long)> { ["alias.byml.zs"] = (100, 150) });
    AssertTest.True(BinaryPrimitives.ReadUInt32LittleEndian(cloned.AsSpan(14)) == 1 &&
        BinaryPrimitives.ReadUInt32LittleEndian(resizedAlias.AsSpan(26)) == 170,
        "resource-size alias inherits and updates the source allocation allowance");
    bool rejectedSize = false;
    try { sizeTable.Patch(new Dictionary<string, (long, long)> { ["missing.zs"] = (1, 2) }); }
    catch (InvalidDataException) { rejectedSize = true; }
    AssertTest.True(rejectedSize, "resource-size patch rejects unknown resources");
    AssertTest.True(HAL_RandomizerSeedService.NormalizeSeed("00aF") == "00000000000000AF" &&
        HAL_RandomizerSeedService.NormalizeSeed("Hyrule") == "366D9D74E6E89CDC" &&
        HAL_RandomizerSeedService.NormalizeSeed(" café ") == "850F7DC43910FF89" &&
        HAL_RandomizerSeedService.NormalizeSeed("0123456789abcdef0") == "D4022B7A487C57C1" &&
        HAL_RandomizerSeedService.NormalizeSeed("星") == "83E9C51B3D7ADE97",
        "C# seed normalization matches Python for hex, text, Unicode and long hex input");
    foreach (var invalid in new[] { " ", "a\nb", new string('x', 65) })
    {
        bool rejected = false;
        try { HAL_RandomizerSeedService.NormalizeSeed(invalid); } catch (ArgumentException) { rejected = true; }
        AssertTest.True(rejected, "invalid seed input is rejected");
    }
    var randomizerCapabilities = HAL_RandomizerSeedService.GetCapabilities();
    AssertTest.True(randomizerCapabilities.Select(c => c.Id).SequenceEqual(
        new[] { "scope", "starting_maps", "heart_pieces", "event_rewards", "child_stealth" }) &&
        randomizerCapabilities.Single(c => c.Id == "heart_pieces").Coverage.Contains("childhood") &&
        randomizerCapabilities.Single(c => c.Id == "event_rewards").Coverage.Contains("Lost Woods"),
        "seed capability query describes only the implemented launcher contract and its limited coverage");
    System.Diagnostics.ProcessStartInfo VulkanFixture(string mode)
    {
        var info = new System.Diagnostics.ProcessStartInfo("dotnet");
        info.ArgumentList.Add(typeof(AssertTest).Assembly.Location);
        info.ArgumentList.Add(mode);
        return info;
    }
    AssertTest.True(await HAL_VulkanReadiness.ProbeAsync(VulkanFixture("--vk-ok"), TimeSpan.FromSeconds(10)) is null,
        "successful Vulkan driver probe permits launch");
    var failedVulkan = await HAL_VulkanReadiness.ProbeAsync(VulkanFixture("--vk-fail"), TimeSpan.FromSeconds(10));
    AssertTest.True(failedVulkan?.Contains("Vulkan cannot initialize a usable GPU") == true &&
        failedVulkan.Contains("ERROR_INITIALIZATION_FAILED"), "failed Vulkan probe reports driver failure with its diagnostic");
    AssertTest.True((await HAL_VulkanReadiness.ProbeAsync(VulkanFixture("--vk-hang"), TimeSpan.FromMilliseconds(200)))?.Contains("timed out") == true,
        "unresponsive Vulkan probe is stopped without hanging the launcher");
    var gpuMonitor = new HAL_GpuLaunchMonitor();
    AssertTest.True(HAL_GpuLaunchMonitor.TraceStatus("[hos host] [DEBUG] f9 Started")?.Contains("started") == true &&
        HAL_GpuLaunchMonitor.TraceStatus("[hos host] [DEBUG] f9 Stopped")?.Contains("stopped") == true &&
        HAL_GpuLaunchMonitor.TraceStatus("[hos host] gameplay trace: capture file logs/gameplay_trace_2026.log") == "F9 capture: logs/gameplay_trace_2026.log" &&
        HAL_GpuLaunchMonitor.TraceStatus("unrelated log line") is null,
        "F9 trace status identifies recording transitions and the capture file");
    var f9Info = VulkanFixture("--f9-log-fixture");
    f9Info.RedirectStandardError = true;
    var f9Messages = new List<string>();
    var previousConsole = Console.Out;
    using var capturedConsole = new StringWriter();
    try
    {
        Console.SetOut(capturedConsole);
        var f9Process = System.Diagnostics.Process.Start(f9Info) ?? throw new InvalidOperationException("Fixture process did not start.");
        await HAL_GpuLaunchMonitor.WatchAsync(f9Process, Path.Combine(root, "f9-fixture.log"), _ => { }, f9Messages.Add);
    }
    finally { Console.SetOut(previousConsole); }
    AssertTest.True(capturedConsole.ToString().Contains("gameplay trace: GameData Bool hash=12345678 value=1") &&
        f9Messages.Any(m => m.Contains("stopped")) && File.ReadAllText(Path.Combine(root, "f9-fixture.log")).Contains("hash=12345678"),
        "F9 trace lines reach the terminal, launcher status and launcher log");
    const string intelCard = "vk: device 0: Intel Graphics, Vulkan 1.3, driver 0x1, type 1, id 8086:1234";
    const string nvidiaCard = "vk: device 1: NVIDIA RTX, Vulkan 1.4, driver 0x2, type 2, id 10de:5678";
    const string intelSelected = "vk: Intel Graphics, Vulkan 1.3, driver 0x1; mesh shaders yes, host memory import yes";
    AssertTest.True(gpuMonitor.Observe(intelCard) is null && gpuMonitor.Observe(intelSelected) is null,
        "Intel alone does not warn");
    AssertTest.True(gpuMonitor.Observe(nvidiaCard) is null, "enumeration alone does not warn");
    AssertTest.True(gpuMonitor.Observe("vk: NVIDIA RTX, Vulkan 1.4, driver 0x2; mesh shaders yes") is null,
        "selected NVIDIA does not warn");
    AssertTest.True(gpuMonitor.Observe(intelSelected)?.Contains("NVIDIA GPU is available") == true,
        "selected Intel with NVIDIA available warns");
    AssertTest.True(gpuMonitor.Observe(intelSelected) is null, "GPU warning appears once per launch");
    var initialOutput = Path.Combine(root, "first-install");
    var initialSource = Path.Combine(root, "local-source");
    Directory.CreateDirectory(Path.Combine(initialSource, "romfs"));
    Directory.CreateDirectory(Path.Combine(initialSource, "exefs"));
    AssertTest.True(!HookshotGame.IsInstalled(initialOutput), "fresh setup has no installed game");
    var initialInstall = HAL_DevelopmentBuild.Create(root, initialSource, initialOutput, false, false, false);
    AssertTest.True(!initialInstall.UpdateExisting && initialInstall.StartInfo.ArgumentList.Contains("--game") &&
        initialInstall.StartInfo.ArgumentList.Contains(initialSource) && initialInstall.StartInfo.ArgumentList.Contains("--out"),
        "our build pipeline handles first-time installation directly from local source");
    Directory.CreateDirectory(Path.Combine(initialOutput, "romfs"));
    File.WriteAllText(HookshotGame.ResolveExecutable(initialOutput), "authored test fixture");
    AssertTest.True(!HookshotGame.IsInstalled(initialOutput), "partial installation is not accepted");
    File.WriteAllText(Path.Combine(initialOutput, "image.bin"), "authored test fixture");
    Directory.CreateDirectory(Path.Combine(initialOutput, ".hookshot_exefs"));
    File.WriteAllText(Path.Combine(initialOutput, ".hookshot_exefs", "main"), "authored test fixture");
    AssertTest.True(!HookshotGame.IsInstalled(initialOutput), "installation is incomplete without its built Vulkan renderer");
    File.WriteAllText(HookshotGame.ResolveRenderer(initialOutput), "authored renderer fixture");
    AssertTest.True(HookshotGame.IsInstalled(initialOutput), "completed installation is accepted");
    var copiedOutput = Path.Combine(root, "separate-adventure-build");
    var fromExisting = HAL_DevelopmentBuild.Create(root, initialOutput, copiedOutput, false, false, true);
    AssertTest.True(!fromExisting.UpdateExisting && fromExisting.StartInfo.ArgumentList.Contains("--copy-romfs") &&
        fromExisting.StartInfo.ArgumentList.Contains(Path.Combine(initialOutput, "romfs")) &&
        fromExisting.StartInfo.ArgumentList.Contains(Path.Combine(initialOutput, ".hookshot_exefs")) &&
        !fromExisting.StartInfo.ArgumentList.Contains("--update") && !fromExisting.StartInfo.Environment.ContainsKey("HOOKSHOT_ROMFS_IN_PLACE"),
        "existing installation supplies retained files to a separate independent build");
    AssertTest.True(HAL_DevelopmentBuild.SuggestedExistingSource(initialOutput, copiedOutput) == initialOutput,
        "a selected existing installation is suggested for first setup");
    if (OperatingSystem.IsLinux())
    {
        string config = Path.Combine(root, "builder-config"); Directory.CreateDirectory(Path.Combine(config, "hookshot"));
        File.WriteAllText(Path.Combine(config, "hookshot", "builder.ini"), "out=/missing\nout=" + initialOutput + "\n");
        AssertTest.True(HAL_DevelopmentBuild.SuggestedExistingSource(null, copiedOutput, config) == initialOutput,
            "first setup discovers the last custom installation saved by Hookshot Builder");
        File.WriteAllText(Path.Combine(config, "hookshot", "builder.ini"), "out=relative-path\n");
        AssertTest.True(HAL_DevelopmentBuild.SuggestedExistingSource(null, copiedOutput, config) != "relative-path",
            "relative paths in Builder configuration are not used for automatic discovery");
    }
    foreach (string overlapping in new[] { initialOutput, Path.Combine(initialOutput, "nested"), Path.GetDirectoryName(initialOutput)! })
    {
        bool overlapRejected = false;
        try { HAL_DevelopmentBuild.Create(root, initialOutput, overlapping, true, false, false); }
        catch (InvalidOperationException) { overlapRejected = true; }
        AssertTest.True(overlapRejected, "building from an installation cannot overwrite or contain its source");
    }
    AssertTest.True(File.ReadAllText(Path.Combine(initialOutput, ".hookshot_exefs", "main")) == "authored test fixture",
        "reviewing an existing installation does not modify its retained files");
    AssertTest.True(!HAL_EnhancementPreferences.CanUse(initialOutput),
        "custom stock installation cannot receive development skip preferences");
    var enhancementMarker = Path.Combine(initialOutput, "hookshot_randomizer_capability.txt");
    File.WriteAllText(enhancementMarker, "HOOKSHOT_DEV_RANDOMIZER=0\n");
    AssertTest.True(!HAL_EnhancementPreferences.CanUse(initialOutput),
        "disabled development marker cannot enable skip preferences");
    File.WriteAllText(enhancementMarker, "HOOKSHOT_DEV_RANDOMIZER=1\n");
    AssertTest.True(HAL_EnhancementPreferences.CanUse(initialOutput + Path.DirectorySeparatorChar),
        "custom development installation supports skip preferences with a trailing separator");
    var customEnhancements = new HAL_EnhancementPreferences(SkipOpening: false, SkipBossIntroductions: true, FastText: true);
    HAL_EnhancementPreferences.Save(initialOutput, customEnhancements);
    AssertTest.True(HAL_EnhancementPreferences.Load(initialOutput) == customEnhancements,
        "custom development skip preferences save and load for launch");
    var enhancementPath = Path.Combine(initialOutput, "profile-manager", "enhancements.json");
    const string legacyEnhancements = "{\"skip_opening\":false,\"skip_child_stealth\":true}";
    File.WriteAllText(enhancementPath, legacyEnhancements);
    var legacyPreferences = HAL_EnhancementPreferences.Load(initialOutput);
    AssertTest.True(!legacyPreferences.SkipOpening && legacyPreferences.SkipChildStealth && legacyPreferences.SchemaVersion == 1 &&
        File.ReadAllText(enhancementPath) == legacyEnhancements, "legacy enhancement preferences load without changing the file");
    foreach (string invalid in new[] { "{\"skip_opening\":1}", "{\"fast_chests\":true}", "{\"schema_version\":2}" })
    {
        File.WriteAllText(enhancementPath, invalid);
        bool rejected = false;
        try { HAL_EnhancementPreferences.Load(initialOutput); } catch (InvalidDataException) { rejected = true; }
        AssertTest.True(rejected, "invalid enhancement preference is rejected");
    }
    HAL_EnhancementPreferences.Save(initialOutput, customEnhancements);
    AssertTest.True(!HAL_EnhancementPreferences.CanUse(HookshotGame.StableGameDirectory()),
        "stable installation stays excluded from development skip preferences");
    AssertTest.True(HAL_DevelopmentBuild.ValidateOutputDirectory(initialOutput + Path.DirectorySeparatorChar) == initialOutput,
        "custom development output accepts a trailing separator");
    foreach (var protectedOutput in new[] { HookshotGame.StableGameDirectory(),
        Path.Combine(HookshotGame.StableGameDirectory(), "nested-dev"), Path.GetDirectoryName(HookshotGame.StableGameDirectory())! })
    {
        bool rejected = false;
        try { HAL_DevelopmentBuild.ValidateOutputDirectory(protectedOutput); }
        catch (InvalidOperationException) { rejected = true; }
        AssertTest.True(rejected, "stable install and overlapping directories stay protected");
    }
    var customPreferencesRoot = Path.Combine(root, "custom-output-preferences");
    Directory.CreateDirectory(customPreferencesRoot);
    var customPreferences = new PreferenceStore(customPreferencesRoot);
    customPreferences.SetDevGameDirectory(initialOutput);
    customPreferences.SetGameDirectory(initialOutput);
    AssertTest.True(customPreferences.Value.LaunchBuild == "dev", "custom development build is classified as dev");
    customPreferences.SetLaunchBuild("stable");
    var restoredCustomPreferences = new PreferenceStore(customPreferencesRoot);
    restoredCustomPreferences.SetLaunchBuild("dev");
    AssertTest.True(restoredCustomPreferences.Value.GameDirectory == initialOutput,
        "custom development output survives restart and switching between stable and dev");
    var managedUpdate = HAL_DevelopmentBuild.Create(root, "", initialOutput, false, false, false);
    AssertTest.True(managedUpdate.UpdateExisting && managedUpdate.StartInfo.ArgumentList.Contains("--update") &&
        managedUpdate.StartInfo.Environment["HOOKSHOT_DEV_RANDOMIZER"] == "1",
        "subsequent development updates remain managed by our tool");
    AssertTest.True(HAL_DevelopmentBuild.FailureDetails(1,
        "@@error Missing Linux build prerequisites: Python module unicorn", "")
        .Contains("Prepare Linux build tools", StringComparison.Ordinal),
        "missing Linux prerequisites point to the in-app setup action");
    AssertTest.True(HAL_DevelopmentBuild.FailureDetails(1,
        "large compiler command\ncp: cannot stat '/usr/lib/x86_64-linux-gnu/libXft.so.2': No such file or directory", "")
        .Contains("libXft.so.2", StringComparison.Ordinal),
        "build failures surface the concise actionable diagnostic rather than the full compiler command");
    if (OperatingSystem.IsLinux())
    {
        var setupRoot = Path.Combine(root, "launcher-source");
        var setupScript = Path.Combine(setupRoot, "scripts", "linux", "deps.sh");
        Directory.CreateDirectory(Path.GetDirectoryName(setupScript)!);
        File.WriteAllText(setupScript, "#!/bin/sh\n");
        var setup = HAL_LinuxBuildTools.HAL_CreateSetupStartInfo(setupRoot);
        AssertTest.True(setup.FileName == "bash" && setup.ArgumentList.Single() == setupScript &&
                        setup.Environment["DEPS"] == HAL_LinuxBuildTools.HAL_CacheDirectory,
            "Linux tool setup uses the pinned project installer in a user-cache directory");
        var prepared = Path.Combine(setupRoot, "build-linux", "deps");
        foreach (var name in new[] { "python/bin/python3", "emsdk/upstream/bin/clang", "emsdk/upstream/bin/clang++",
            "ffmpeg/lib/libavcodec.a", "sdl2/libSDL2-2.0.so.0", ".python", ".llvm", ".meson", ".ffmpeg", ".cpm" })
        {
            var file = Path.Combine(prepared, name);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, "authored fixture");
        }
        foreach (var name in new[] { "vulkanheaders", "spirv-headers", "vulkanmemoryallocator" })
            Directory.CreateDirectory(Path.Combine(prepared, "cpm", name));
        var configuredBuild = new System.Diagnostics.ProcessStartInfo("/usr/bin/python3");
        HAL_LinuxBuildTools.HAL_ConfigureBuild(configuredBuild, setupRoot);
        AssertTest.True(configuredBuild.FileName != "/usr/bin/python3" &&
            File.Exists(configuredBuild.FileName) && Directory.Exists(configuredBuild.Environment["EMSDK"]) &&
            Directory.Exists(configuredBuild.Environment["RX_CPM"]),
            "prepared tools replace system Python and configure the bundled compiler and headers");

        var bundle = Path.Combine(root, "appimage");
        var bundleRoot = Path.Combine(bundle, "usr", "share", "hookshot");
        var bundleLib = Path.Combine(bundle, "usr", "lib", "hookshot");
        var bundlePython = Path.Combine(bundle, "usr", "python", "bin", "python3");
        var bundleEnv = new Dictionary<string, string>
        {
            ["HOOKSHOT_TOOLS"] = bundleRoot, ["EMSDK"] = Path.Combine(bundleLib, "emsdk"),
            ["HOOKSHOT_SYSROOT"] = Path.Combine(bundleLib, "sysroot"), ["HOOKSHOT_FFMPEG"] = Path.Combine(bundleLib, "ffmpeg"),
            ["HOOKSHOT_SDL2"] = Path.Combine(bundleLib, "libSDL2-2.0.so.0")
        };
        var previousEnv = bundleEnv.Keys.ToDictionary(k => k, Environment.GetEnvironmentVariable);
        try
        {
            foreach (var pair in bundleEnv) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            foreach (var file in new[] { bundlePython, Path.Combine(bundleEnv["EMSDK"], "upstream", "bin", "clang"),
                Path.Combine(bundleEnv["EMSDK"], "upstream", "bin", "clang++"),
                Path.Combine(bundleEnv["HOOKSHOT_FFMPEG"], "lib", "libavcodec.a"), bundleEnv["HOOKSHOT_SDL2"] })
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, "authored fixture");
            }
            Directory.CreateDirectory(bundleEnv["HOOKSHOT_SYSROOT"]);
            foreach (var name in new[] { "vulkanheaders", "spirv-headers", "vulkanmemoryallocator" })
                Directory.CreateDirectory(Path.Combine(bundleRoot, "cpm", name));
            var bundledBuild = new System.Diagnostics.ProcessStartInfo("python3");
            HAL_LinuxBuildTools.HAL_ConfigureBuild(bundledBuild, bundleRoot);
            AssertTest.True(HAL_LinuxBuildTools.HAL_Available(bundleRoot) && bundledBuild.FileName == bundlePython &&
                bundledBuild.Environment["RX_CPM"] == Path.Combine(bundleRoot, "cpm"),
                "AppImage tools use the bundled Python, compiler and headers without preparing a second toolchain");
            File.Delete(bundleEnv["HOOKSHOT_SDL2"]);
            var incompleteBundle = new System.Diagnostics.ProcessStartInfo("python3");
            HAL_LinuxBuildTools.HAL_ConfigureBuild(incompleteBundle, bundleRoot);
            AssertTest.True(incompleteBundle.FileName != bundlePython, "an incomplete AppImage toolchain is not selected");
        }
        finally
        {
            foreach (var pair in previousEnv) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
    }
    var legacy = Path.Combine(root, "hookshot", "profiles.json");
    Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
    var gameSaves = Path.Combine(root, "hookshot-user");
    var slotFolder = Path.Combine(gameSaves, "save", "abc", "slot_00");
    Directory.CreateDirectory(slotFolder);
    var originalBytes = new byte[] { 3, 8, 15, 2 };
    File.WriteAllBytes(Path.Combine(slotFolder, "progress.sav"), originalBytes);
    var legacyJson = System.Text.Json.JsonSerializer.Serialize(new ProfileIndex
    {
        Version = 1, GameDir = "/old/hookshot",
        LauncherSlots = new string?[] { "abc001", null, null },
        Profiles = new List<ProfileRecord> { new ProfileRecord
        {
            Id = "abc001", Name = "Link", Kind = "vanilla", UserDir = gameSaves,
            External = true, CreatedAt = "2026-11-01T00:00:00+00:00"
        } }
    });
    File.WriteAllText(legacy, legacyJson);
    var repo = new ProfileRepository(Path.Combine(root, "new-store"), legacy, () => false);
    AssertTest.True(repo.ImportedOnFirstRun, "legacy metadata imported on first run");
    AssertTest.True(repo.Index.GameDir == "/old/hookshot" && repo.Index.LegacyHookshotGameDir == "/old/hookshot", "legacy Hookshot game path is preserved");
    AssertTest.True(repo.GetSlots().Length == 3 && repo.GetSlots()[0]!.Name == "Link", "exactly three shortcuts and existing save first");
    var isolatedCopy = repo.Duplicate("abc001", "Link Copy");
    AssertTest.True(!isolatedCopy.External && isolatedCopy.UserDir != gameSaves &&
                    File.ReadAllBytes(Path.Combine(isolatedCopy.UserDir, "save", "abc", "slot_00", "progress.sav")).SequenceEqual(originalBytes) &&
                    File.ReadAllBytes(Path.Combine(slotFolder, "progress.sav")).SequenceEqual(originalBytes),
                    "external save can be copied to an isolated launchable profile without altering source");
    var created = repo.Create("Another Save");
    AssertTest.True(Directory.Exists(created.UserDir) && created.UserDir != gameSaves, "new adventures are isolated");
    var backupZip = Path.Combine(root, "manual-backup.zip");
    ZipFile.CreateFromDirectory(gameSaves, backupZip, CompressionLevel.Optimal, includeBaseDirectory: false);
    var restoredBackup = repo.RestoreSaveBackup(backupZip, legacyFolder: false);
    AssertTest.True(!restoredBackup.External && restoredBackup.UserDir != gameSaves &&
                    File.ReadAllBytes(Path.Combine(restoredBackup.UserDir, "save", "abc", "slot_00", "progress.sav")).SequenceEqual(originalBytes) &&
                    File.ReadAllBytes(Path.Combine(slotFolder, "progress.sav")).SequenceEqual(originalBytes),
                    "compressed backup restores to a new isolated profile without modifying source saves");
    var legacyBackup = Path.Combine(root, "legacy-folder");
    Directory.CreateDirectory(Path.Combine(legacyBackup, "save", "abc", "slot_00"));
    File.WriteAllBytes(Path.Combine(legacyBackup, "save", "abc", "slot_00", "progress.sav"), originalBytes);
    var restoredLegacy = repo.RestoreSaveBackup(legacyBackup, legacyFolder: true);
    AssertTest.True(restoredLegacy.UserDir != legacyBackup && File.ReadAllBytes(Path.Combine(legacyBackup, "save", "abc", "slot_00", "progress.sav")).SequenceEqual(originalBytes),
                    "legacy folder restore imports to a new profile and leaves the folder intact");
    var orphanId = Guid.NewGuid().ToString("N");
    var orphanSlot = Path.Combine(repo.Root, "profiles", orphanId, "user", "save", "orphan", "slot_00");
    Directory.CreateDirectory(orphanSlot);
    File.WriteAllBytes(Path.Combine(orphanSlot, "progress.sav"), originalBytes);
    var stale = repo.Create("Missing profile directory");
    repo.Pin(stale.Id, 2);
    Directory.Delete(Path.Combine(repo.Root, "profiles", stale.Id), true);
    var startupScan = repo.ScanOnStartup();
    AssertTest.True(startupScan.Recovered == 1 && startupScan.RemovedStale == 1 && startupScan.RepairedPaths == 0 &&
                    repo.All().Any(p => p.Id == orphanId) && repo.All().All(p => p.Id != stale.Id) && repo.Index.LauncherSlots[2] is null &&
                    File.Exists(Path.Combine(orphanSlot, "progress.sav")),
                    "startup scan recovers unlisted save folders, drops missing registry entries, clears stale pins and leaves save data untouched");
    var seed = "0123456789ABCDEF";
    AssertTest.True(new HAL_EnhancementPreferences().RuntimeEnvironment()["HOS_SKIP_OPENING"] == "1" &&
                    new HAL_EnhancementPreferences(SkipOpening: false).RuntimeEnvironment()["HOS_SKIP_OPENING"] == "0" &&
                    new HAL_EnhancementPreferences().RuntimeEnvironment()["HOS_SKIP_BOSS_INTRODUCTIONS"] == "0" &&
                    new HAL_EnhancementPreferences(SkipBossIntroductions: true).RuntimeEnvironment()["HOS_SKIP_BOSS_INTRODUCTIONS"] == "1",
                    "saved intro-skip preference maps to the game's launch-time skip option");
    AssertTest.True(!new HAL_EnhancementPreferences().RuntimeEnvironment().ContainsKey("HOS_TEXT_SPEED") &&
                    new HAL_EnhancementPreferences(FastText: true).RuntimeEnvironment()["HOS_TEXT_SPEED"] == "3",
                    "fast text uses Hookshot's instant setting and leaves the game's text speed alone when disabled");
    var randomized = repo.CreateRandomized("Test Seed", seed, (profileDirectory, profileId) =>
    {
        var randomizerRoot = Path.Combine(profileDirectory, "native-randomizer");
        Directory.CreateDirectory(randomizerRoot);
        File.WriteAllText(Path.Combine(randomizerRoot, "seed.json"),
            System.Text.Json.JsonSerializer.Serialize(new { seed, binding = new { profile_id = profileId } }));
    });
    AssertTest.True(randomized.IsRandomized && randomized.Seed == seed && Directory.Exists(randomized.UserDir), "randomized profile is published only after its bound seed exists");
    bool failedSeed = false;
    try { repo.CreateRandomized("Broken Seed", seed, (_, _) => throw new InvalidOperationException("generation failed")); }
    catch (InvalidOperationException) { failedSeed = true; }
    AssertTest.True(failedSeed && repo.All().All(p => p.Name != "Broken Seed"), "failed seed generation leaves no profile record");
    repo.Pin(created.Id, 0);
    AssertTest.True(repo.GetSlots()[0]!.Id == created.Id && repo.GetSlots()[1]!.Id == "abc001", "explicit pins supersede autofill");
    repo.Pin(created.Id, 2);
    AssertTest.True(repo.GetSlots()[2]!.Id == created.Id && repo.Index.LauncherSlots[0] is null, "re-pinning cannot duplicate a profile in pins");
    repo.Rename(created.Id, "Renamed");
    AssertTest.True(new ProfileRepository(Path.Combine(root, "new-store"), legacy).Get(created.Id).Name == "Renamed", "renaming is persistent");
    var removable = repo.Create("Temporary adventure");
    File.WriteAllText(Path.Combine(removable.UserDir, "save.dat"), "owned by isolated profile");
    var runningGameRepo = new ProfileRepository(repo.Root, legacy, () => true);
    bool runningDeleteBlocked = false;
    try { runningGameRepo.Delete(removable.Id); } catch (InvalidOperationException) { runningDeleteBlocked = true; }
    AssertTest.True(runningDeleteBlocked && Directory.Exists(removable.UserDir), "a running game still blocks adventure deletion");
    repo.Pin(removable.Id, 1);
    repo.Delete(removable.Id);
    AssertTest.True(!Directory.Exists(Path.Combine(repo.Root, "profiles", removable.Id)) &&
                    repo.All().All(p => p.Id != removable.Id) && repo.Index.LauncherSlots[1] is null,
                    "deleting an isolated profile removes its folder and clears its shortcut without backup");
    var externalDeleteBlocked = false;
    try { repo.Delete("abc001"); } catch (InvalidOperationException) { externalDeleteBlocked = true; }
    AssertTest.True(externalDeleteBlocked && File.Exists(Path.Combine(slotFolder, "progress.sav")),
                    "external save profiles cannot be deleted");
    var externalRepo = new ProfileRepository(Path.Combine(root, "external-removal-test"), Path.Combine(root, "missing-index.json"));
    var externalProfile = externalRepo.RegisterExternal(gameSaves);
    externalRepo.Pin(externalProfile.Id, 0);
    externalRepo.RemoveExternal(externalProfile.Id);
    var reloadedExternal = new ProfileRepository(externalRepo.Root, Path.Combine(root, "missing-index.json"));
    AssertTest.True(reloadedExternal.IsExternalHidden(gameSaves) && reloadedExternal.All().Count == 0 &&
                    reloadedExternal.Index.LauncherSlots[0] is null && File.Exists(Path.Combine(slotFolder, "progress.sav")),
                    "removing an external adventure persists without deleting its saves");
    reloadedExternal.RegisterExternal(gameSaves);
    AssertTest.True(!reloadedExternal.IsExternalHidden(gameSaves), "explicitly importing an external adventure restores discovery");
    var previousXdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
    Environment.SetEnvironmentVariable("XDG_DATA_HOME", root);
    try
    {
        var legacyId = Guid.NewGuid().ToString("N");
        var legacyBase = Path.Combine(root, "hookshot-profile-manager", "profiles", legacyId);
        var legacyUser = Path.Combine(legacyBase, "user");
        Directory.CreateDirectory(legacyUser);
        File.WriteAllText(Path.Combine(legacyUser, "profile-marker.txt"), "isolated legacy profile");
        repo.Index.Profiles.Add(new ProfileRecord
        {
            Id = legacyId, Name = "Legacy isolated", Kind = "vanilla", UserDir = legacyUser,
            External = false, CreatedAt = "2026-11-01T00:00:00+00:00"
        });
        File.WriteAllText(repo.IndexPath, System.Text.Json.JsonSerializer.Serialize(repo.Index));
        repo.Delete(legacyId);
        AssertTest.True(!Directory.Exists(legacyBase) && repo.All().All(p => p.Id != legacyId),
                        "legacy isolated profiles under the recognized profile-manager root can be deleted safely");
    }
    finally { Environment.SetEnvironmentVariable("XDG_DATA_HOME", previousXdg); }
    AssertTest.True(File.ReadAllBytes(Path.Combine(slotFolder, "progress.sav")).SequenceEqual(originalBytes), "original save bytes untouched");
    AssertTest.True(File.ReadAllText(legacy).Contains("\"name\":\"Link\""), "original Hookshot registry untouched");
    AssertTest.True(ThemeCatalog.All.Count == 8 && ThemeCatalog.Resolve("not-a-theme").Id == "zelda", "all eight Qt themes and fallback");

    var fieldBytes = new byte[0x175D8 + 256];
    static void U32(byte[] data, int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at, 4), value);
    U32(fieldBytes, 0, 0x01020304); U32(fieldBytes, 4, 0x002D80EB); U32(fieldBytes, 8, 0x175D8);
    var fields = new (uint hash, int value)[]
    {
        (0xA77921D7, 123), (0x31AB5580, 35), (0xFBE01DA1, 40), (0x04735978, 12),
        (0x80D34CE9, 1), (0x938D56BF, 0), (0x24596E8E, 1), (0xFEB4D56B, 1),
        (0x5CE254A6, 1), (0xCBD1E58C, 0),
        (29491202, 1), (725212495, 0), (3682874359, 0), (0xB72CD3FC, 0x175D8),
        (0x457616B0, 30), (0x735B00F7, 25),
        (0xD53CE3A7, 1), (0xF39CE410, 1), (0x7D48DF76, unchecked((int)0x73356827)),
        (0xFCA29A04, 1), (0x000FF6EA, 0),
        (0x1AAFDB66, 1), (0x49D1C374, 0), (0x2BAD189A, 1), (0xC9220366, 1),
        (0x5E529AB9, 1), (0x40735A8D, 1), (0xBFB285C8, 1), (0xB796C16D, 1),
        (0x47F2CA87, 1), (0xF0828CD0, 1), (0xB7DF60F1, 0), (0x54E2A3D1, 1),
        (0x8573D2CD, 0), (0x8499EA34, 1), (0x1240044B, 0), (0x4AEB140A, 1)
    };
    for (int i = 0; i < fields.Length; i++)
    {
        U32(fieldBytes, 40 + i * 8, fields[i].hash);
        U32(fieldBytes, 44 + i * 8, unchecked((uint)fields[i].value));
    }
    var built = Path.Combine(root, "Game Install");
    var gameExe = Path.Combine(built, "bin", OperatingSystem.IsWindows() ? "hookshot.exe" : "hookshot");
    Directory.CreateDirectory(Path.GetDirectoryName(gameExe)!);
    Directory.CreateDirectory(Path.Combine(built, "romfs"));
    File.WriteAllText(gameExe, "dummy");
    File.WriteAllBytes(Path.Combine(built, "image.bin"), new byte[] { 1 });
    if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(gameExe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    bool rendererRequired = false;
    try { HookshotGame.CreatePlan(built, created.UserDir); }
    catch (InvalidOperationException ex) { rendererRequired = ex.Message.Contains("built Vulkan renderer is missing"); }
    AssertTest.True(rendererRequired, "launch rejects a missing renderer beside the executable");
    File.WriteAllText(HookshotGame.ResolveRenderer(built), "authored nested renderer fixture");
    var plan = HookshotGame.CreatePlan(built, created.UserDir);
    bool capabilityRequired = false;
    try { HAL_RandomizerSeedService.EnsureDevRandomizerBuild(built); }
    catch (InvalidOperationException) { capabilityRequired = true; }
    AssertTest.True(capabilityRequired, "randomizer refuses a vanilla Hookshot build");
    File.WriteAllText(Path.Combine(built, "hookshot_randomizer_capability.txt"), "HOOKSHOT_DEV_RANDOMIZER=1\n");
    HAL_RandomizerSeedService.EnsureDevRandomizerBuild(built);
    AssertTest.True(true, "development randomizer capability marker enables seed support");
    var processInfo = plan.ToStartInfo();
    var otherUser = Path.Combine(root, "other-adventure-user");
    Directory.CreateDirectory(otherUser);
    var otherPlan = HookshotGame.CreatePlan(built, otherUser);
    AssertTest.True(processInfo.Environment["HOS_SAVE_BACKUPS"] != otherPlan.ToStartInfo().Environment["HOS_SAVE_BACKUPS"] &&
        processInfo.Environment["HOS_SAVE_BACKUPS"] == plan.ToStartInfo().Environment["HOS_SAVE_BACKUPS"],
        "each adventure has a stable, isolated backup history");
    AssertTest.True(processInfo.ArgumentList.Count == 2 && processInfo.ArgumentList[0] == "--user" && processInfo.ArgumentList[1] == created.UserDir, "Hookshot launch uses discrete --user argument (no shell interpolation)");
    AssertTest.True(processInfo.WorkingDirectory == built && processInfo.FileName == gameExe, "Hookshot launches built bin executable from game root");
    AssertTest.True(!processInfo.ArgumentList.Contains("--Video-Debug"), "Video Debug is absent from normal launches");
    AssertTest.True(plan.ToStartInfo(videoDebug: true).ArgumentList.Contains("--Video-Debug"), "Video Debug setting adds the explicit game argument");
    AssertTest.True(!processInfo.ArgumentList.Contains("--HOS-Debug"), "HOS Debug is absent from normal launches");
    AssertTest.True(plan.ToStartInfo(hosDebug: true).ArgumentList.Contains("--HOS-Debug"), "HOS Debug setting adds the explicit game argument");
    AssertTest.True(!processInfo.ArgumentList.Contains("--A64-Debug"), "A64 Debug is absent from normal launches");
    AssertTest.True(plan.ToStartInfo(a64Debug: true).ArgumentList.Contains("--A64-Debug"), "A64 Debug setting adds the explicit game argument");
    AssertTest.True(!processInfo.Environment.ContainsKey("HOS_DEBUG_KEY"), "Debug Key is absent from normal launches");
    AssertTest.True(plan.ToStartInfo(debugKey: true).Environment["HOS_DEBUG_KEY"] == "1", "Debug Key setting enables F9 gameplay state tracing");
    AssertTest.True(Path.GetFileName(HookshotGame.DefaultGameDirectory()) == "Hookshot-Dev", "launcher defaults to the development install, never stable");
    var randoLaunch = plan.ToStartInfo(new Dictionary<string, string>
    {
        ["HOS_FS_OVERLAY"] = Path.Combine(root, "seed-overlay"),
        ["HOS_RANDO_STARTING_DUNGEON_FLAGS"] = "89787395",
        ["HOS_SKIP_OPENING"] = "1"
    });
    AssertTest.True(randoLaunch.Environment["HOS_FS_OVERLAY"] == Path.Combine(root, "seed-overlay") &&
                    randoLaunch.Environment["HOS_RANDO_STARTING_DUNGEON_FLAGS"] == "89787395" &&
                    randoLaunch.Environment["HOS_SKIP_OPENING"] == "1", "randomizer launch receives validated overlay switches and the saved intro-skip preference");
    AssertTest.True(plan.ToStartInfo(new HAL_EnhancementPreferences(FastText: true).RuntimeEnvironment()).Environment["HOS_TEXT_SPEED"] == "3",
        "fast text reaches the game process through the validated launch environment");
    bool blockedEnv = false;
    try { plan.ToStartInfo(new Dictionary<string, string> { ["LD_PRELOAD"] = "unsafe" }); }
    catch (InvalidDataException) { blockedEnv = true; }
    AssertTest.True(blockedEnv, "randomizer launch rejects unapproved environment overrides");
    var preference = new PreferenceStore(Path.Combine(root, "new-store"));
    string stealthBuild=Path.Combine(root,"stealth-build"); Directory.CreateDirectory(stealthBuild);
    bool staleStealthBuild=false;
    try { HAL_RandomizerSeedService.EnsureChildStealthBuild(stealthBuild); } catch(InvalidOperationException) { staleStealthBuild=true; }
    File.WriteAllText(Path.Combine(stealthBuild,"hookshot_child_stealth_capability.txt"),"HOOKSHOT_CHILD_STEALTH=1\n");
    HAL_RandomizerSeedService.EnsureChildStealthBuild(stealthBuild);
    AssertTest.True(staleStealthBuild,"child stealth rejects builds without the letter-grant capability");
    bool staleStoneBuild=false;
    try { HAL_RandomizerSeedService.EnsureStoneRewardBuild(stealthBuild); } catch(InvalidOperationException) { staleStoneBuild=true; }
    File.WriteAllText(Path.Combine(stealthBuild,"hookshot_stone_reward_capability.txt"),"HOOKSHOT_STONE_REWARD=1\n");
    HAL_RandomizerSeedService.EnsureStoneRewardBuild(stealthBuild);
    AssertTest.True(staleStoneBuild,"Deku reward recovery rejects builds without the stone-grant capability");
    preference.SetRandomizer(new HAL_RandomizerSettings("full_game", false, false, false, EventRewards: true, MaximumSkulltulas: 30));
    var savedRandomizer = new PreferenceStore(Path.Combine(root, "new-store")).Value;
    AssertTest.True(savedRandomizer.RandomizerScope == "full_game" && savedRandomizer.RandomizerEventRewards && savedRandomizer.RandomizerMaximumSkulltulas == 30,
        "full-game scope, experimental event rewards and the requested Skulltula maximum persist");
    bool invalidSkullMaximum=false;
    try { preference.SetRandomizer(new HAL_RandomizerSettings("full_game",false,false,false,MaximumSkulltulas:101)); }
    catch(ArgumentOutOfRangeException) { invalidSkullMaximum=true; }
    AssertTest.True(invalidSkullMaximum && preference.Value.RandomizerMaximumSkulltulas==30,
        "invalid Skulltula maximum is rejected without changing the saved preference");
    AssertTest.True(preference.Value.DebugKey, "new launcher preferences enable the F9 gameplay trace by default");
    var legacyPreferenceRoot = Path.Combine(root, "legacy-preferences");
    Directory.CreateDirectory(legacyPreferenceRoot);
    File.WriteAllText(Path.Combine(legacyPreferenceRoot, "preferences.json"), "{\"debug_key\":false}\n");
    var migratedPreferences = new PreferenceStore(legacyPreferenceRoot);
    AssertTest.True(migratedPreferences.Value.DebugKey, "legacy launcher preferences migrate to the enabled F9 debugging default");
    migratedPreferences.SetDebugKey(false);
    AssertTest.True(!new PreferenceStore(legacyPreferenceRoot).Value.DebugKey, "explicitly turning off the F9 trace persists after the default migration");
    preference.SetGameDirectory(built);
    AssertTest.True(new PreferenceStore(Path.Combine(root, "new-store")).Value.GameDirectory == built, "verified game directory persists");
    preference.SetLaunchBuild("stable");
    AssertTest.True(preference.Value.LaunchBuild == "stable" && preference.Value.GameDirectory == HookshotGame.StableGameDirectory(), "stable Hookshot can be selected explicitly for launch");
    preference.SetVideoDebug(true);
    preference.SetHosDebug(true);
    preference.SetA64Debug(true);
    preference.SetDebugKey(true);
    var reloadedPreferences = new PreferenceStore(Path.Combine(root, "new-store"));
    AssertTest.True(reloadedPreferences.Value.VideoDebug && reloadedPreferences.Value.HosDebug && reloadedPreferences.Value.A64Debug && reloadedPreferences.Value.DebugKey && reloadedPreferences.Value.LaunchBuild == "stable", "debug and selected build preferences persist");
    System.Text.Encoding.UTF8.GetBytes("Item_Ocarina_Time").CopyTo(fieldBytes, 0x175D8);
    var parser = new SaveReaderService();
    var summary = parser.Decode(fieldBytes, null);
    AssertTest.True(summary.Rupees == 123 && summary.Hearts == 10 && summary.CurrentHearts == 8.75, "byte-level rupees and quarter hearts decoding");
    AssertTest.True(summary.Items.Single(x => x.Role == "slingshot").State == OwnershipState.Owned &&
                    summary.Items.Single(x => x.Role == "bow").State == OwnershipState.NotCarried, "ranged ownership uses stock rather than ammunition");
    AssertTest.True(summary.Items.Single(x => x.Role == "boomerang").State == OwnershipState.Unknown, "unmapped Boomerang stays unknown");
    AssertTest.True(summary.Items.Single(x => x.Role == "scale_silver").State == OwnershipState.NotCarried &&
        summary.Items.Single(x => x.Role == "scale_gold").State == OwnershipState.Owned,
        "silver and golden scale ownership follows their saved stocks");
    AssertTest.True(new[] { "hammer", "boots_iron", "boots_hover", "fire_arrow", "ice_arrow", "light_arrow", "dins_fire", "farores_wind", "nayrus_love", "ocarina_time", "longshot" }
        .All(role => summary.Items.Single(x => x.Role == role).State == OwnershipState.Owned) &&
        new[] { "lens_of_truth", "ocarina_fairy", "hookshot" }.All(role => summary.Items.Single(x => x.Role == role).State == OwnershipState.NotCarried),
        "equipment, magic and progressive item icons use their own saved stock fields");
    AssertTest.True(summary.Items.Single(x => x.Role == "trade_weird_egg").State == OwnershipState.Owned, "trade stock uses current-held semantics");
    AssertTest.True(summary.Items.Single(x => x.Role == "gerudo_pass").State == OwnershipState.Owned, "Gerudo Pass read-only stock");
    AssertTest.True(summary.Items.Single(x => x.Role == "medallion_fire").State == OwnershipState.Owned, "medallion uses direct stock");
    AssertTest.True(summary.Items.Single(x => x.Role == "stone_kokiri_emerald").State == OwnershipState.NotCarried, "stone direct stock can be zero");
    AssertTest.True(summary.Items.Single(x => x.Role == "stone_goron_ruby").State == OwnershipState.Unknown &&
                    summary.Items.Single(x => x.Role == "stone_zora_sapphire").State == OwnershipState.Unknown,
                    "unavailable stone stocks remain unknown independently");
    AssertTest.True(summary.PreviewJpeg == null && summary.Location == null, "missing optional fields remain unknown");
    AssertTest.True(summary.Items.Single(x => x.Role == "song_zelda_lullaby").State == OwnershipState.Owned &&
                    summary.Items.Single(x => x.Role == "song_storms").State == OwnershipState.NotCarried,
                    "song acquisition flags distinguish learned from unlearned");
    AssertTest.True(summary.Items.Single(x => x.Role == "song_epona").State == OwnershipState.Unknown &&
                    summary.Items.Single(x => x.Role == "song_scarecrow").State == OwnershipState.Unknown,
                    "missing song flags and unmapped songs remain unknown");
    AssertTest.True(summary.Items.Single(x => x.Role == "song_time").State == OwnershipState.Unknown &&
                    summary.Items.Single(x => x.Role == "ocarina_time").State == OwnershipState.Owned,
                    "selected Ocarina of Time does not imply Song of Time is learned");
    AssertTest.True(summary.BottleSlots[0].Owned == true && summary.BottleSlots[0].Role == "bottle_milk" &&
                    summary.BottleSlots[1].Owned == null && summary.BottleSlots[2].Owned == null,
                    "bottle content is decoded while inconsistent and absent slots remain unknown");
    AssertTest.True(summary.BagCapacities["sticks"] == 30 && summary.BagCapacities["nuts"] == null,
                    "bag upgrades use valid capacity tiers rather than ammunition");
    fieldBytes[0] = 0;
    bool unsupported = false;
    try { parser.Decode(fieldBytes, null); } catch (InvalidDataException) { unsupported = true; }
    AssertTest.True(unsupported, "malformed save is rejected");
    AssertTest.True(File.ReadAllBytes(Path.Combine(slotFolder, "progress.sav")).SequenceEqual(originalBytes), "parser did not modify original saves");
    Console.WriteLine($"All {AssertTest.Count} core smoke checks passed.");
}
finally
{
    if (Directory.Exists(root)) Directory.Delete(root, true);
}

static class AssertTest
{
    public static int Count;
    public static void True(bool good, string message)
    {
        Count++;
        if (!good) throw new Exception("Test failure: " + message);
        Console.WriteLine("PASS " + message);
    }
}
