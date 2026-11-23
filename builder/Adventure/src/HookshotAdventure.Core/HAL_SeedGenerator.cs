using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HookshotAdventure.Core;

/// <summary>Scans local resources and creates the existing profile-bound seed format.</summary>
public static class HAL_SeedGenerator
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    private static JsonElement Element(JsonNode node) => JsonSerializer.SerializeToElement(node);
    private static string Digest(JsonNode node) => HAL_CanonicalJson.Digest(Element(node));
    private static void Write(string path, JsonNode node) => File.WriteAllText(path, node.ToJsonString(Indented) + "\n");

    public static void Generate(string gameDirectory, string profileDirectory, string profileId, string seed, HAL_RandomizerSettings settings)
    {
        if (profileId.Length != 32 || profileId.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')) ||
            seed.Length != 16 || seed.Any(c => !Uri.IsHexDigit(c))) throw new InvalidDataException("Invalid profile or seed identifier.");
        if (settings.Scope is not ("kokiri_deku" or "childhood" or "full_game")) throw new ArgumentException("Unsupported randomizer scope.");
        HAL_SkulltulaRewards.ValidateMaximum(settings.MaximumSkulltulas);
        seed = seed.ToUpperInvariant(); string game = Path.GetFullPath(gameDirectory), profile = Path.GetFullPath(profileDirectory);
        if (ProfileRepository.IsLink(profile) || !Directory.Exists(profile) || ProfileRepository.IsLink(Path.Combine(profile, "user")))
            throw new InvalidDataException("Invalid isolated profile directory.");
        string romfs = Path.Combine(game, "romfs"); if (!Directory.Exists(romfs)) throw new InvalidDataException("Game ROMFS is missing.");
        string root = Path.Combine(profile, "native-randomizer");
        if (ProfileRepository.IsLink(root)) throw new InvalidDataException("Linked randomizer directory.");
        Directory.CreateDirectory(root);
        if (File.Exists(Path.Combine(root, "seed.json")) || File.Exists(Path.Combine(root, "binding.json")))
            throw new InvalidDataException("This profile already has a seed.");
        var dictionaries = HAL_RuntimeOverlay.Dictionaries(romfs);
        var paths = HAL_ChestScope.Files(romfs, settings.Scope);
        var maps = new SortedDictionary<string, byte[]>(StringComparer.Ordinal); var hashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var variants = new List<HAL_ChestVariant>();
        var reviewedTokens = new List<HAL_WorldReachability.Location>();
        foreach (string path in paths.OrderBy(x => x, StringComparer.Ordinal))
        {
            string name = Path.GetRelativePath(romfs, path).Replace('\\', '/'); byte[] raw = HAL_Zstd.Unpack(File.ReadAllBytes(path), dictionaries);
            maps.Add(name, raw); hashes.Add(name, Hash(raw)); variants.AddRange(HAL_ChestScanner.Scan(raw).Select(row => new HAL_ChestVariant(name, row)));
            reviewedTokens.AddRange(HAL_ReviewedSkulltulas.Scan(name, raw));
        }
        if (variants.Count == 0) throw new InvalidDataException("No chest checks found in the selected scope.");
        string gameData = Path.Combine(romfs, "GameData", "GameDataList.Product.110.byml.zs");
        var flags = HAL_ChestCatalog.FlagCandidates(HAL_Zstd.Unpack(File.ReadAllBytes(gameData), dictionaries));
        var catalog = HAL_ChestCatalogDocument.Build(variants, hashes, flags, true, settings.Scope == "childhood", settings.HeartPieces, settings.Scope == "full_game");
        var locations = HAL_LocationDocument.FromCatalog(Element(catalog));
        var selected = HAL_LocationDocument.SelectCatalog(Element(catalog), Element(locations));
        HAL_LocationDocument.ApplyProgressionExclusions(selected, locations, settings.ExcludedChecks);
        var equipment = new HashSet<string>(StringComparer.Ordinal);
        if (settings.StartingSword) equipment.Add("Item_Sword_Kokiri");
        if (settings.StartingSlingshot) equipment.Add("Item_Range_SlingShot");
        if (settings.StartingBombBag) equipment.Add("Item_BombBag_Small");
        if (settings.StartingBoomerang) equipment.Add("Item_Boomerang");
        if (settings.StartingLens) equipment.Add("Item_GlassTruth");
        var plan = HAL_ChestPlanDocument.Build(Element(selected), seed, settings.StartingMaps, true, equipment);
        string? targetReward = settings.EventRewards ? HAL_EventRewardPlan.AddTargetExchange(plan, seed) : null;
        string? sariaReward = settings.SariaOcarina ? HAL_EventRewardPlan.AddSariaExchange(plan, seed, settings.StartingOcarina) : null;
        var skullExchanges = settings.SkulltulaRewards ? HAL_EventRewardPlan.AddSkulltulaExchanges(plan, seed) : null;
        var planElement = Element(plan);
        HAL_ChestPlanDocument.ValidateProgressionExclusions(planElement);
        HAL_SeedOpeningSolver.Validate(planElement, settings.StartingSword, settings.StartingSlingshot, Element(selected), reviewedTokens);
        HAL_StartingDungeonFlags.Resolve(planElement.GetProperty("starting_inventory").EnumerateArray()
            .Where(row => row.GetProperty("item").GetString() is "Item_Dungeon_Map" or "Item_Dungeon_Compass")
            .Select(row => (row.GetProperty("dungeon").GetString()!, row.GetProperty("item").GetString()!)), settings.StartingMaps);
        var assignments = planElement.GetProperty("assignments").EnumerateArray().Select(row => new HAL_ChestAssignment(
            row.GetProperty("placement").GetUInt64(), row.GetProperty("actor").GetString()!, row.GetProperty("expected_reward").GetString()!,
            row.GetProperty("expected_event").GetString(), row.GetProperty("reward").GetString()!, row.GetProperty("event").GetString(),
            row.GetProperty("resources").EnumerateArray().Select(x => x.GetString()!).ToArray(), row.GetProperty("runtime_blockers").GetArrayLength() != 0)).ToArray();
        if (assignments.Any(x => x.RuntimeBlocked)) throw new InvalidDataException("Seed requires an unimplemented runtime reward.");
        var outputs = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        if (settings.EventRewards)
        {
            string eventPath = HAL_EventRewardPlan.Resource;
            string eventFile = Path.Combine(romfs, eventPath.Replace('/', Path.DirectorySeparatorChar));
            if (ProfileRepository.IsLink(eventFile)) throw new InvalidDataException("Linked event reward resource.");
            byte[] raw = HAL_Zstd.Unpack(File.ReadAllBytes(eventFile), dictionaries);
            byte[] patched = HAL_EventRewardPatch.LostWoodsTargetRupee(raw, targetReward!);
            hashes.Add(eventPath, Hash(raw));
            outputs.Add(eventPath, HAL_Zstd.Compress(patched));
        }
        if (settings.SariaOcarina)
        {
            string eventPath = HAL_EventRewardPlan.SariaResource;
            string eventFile = Path.Combine(romfs, eventPath.Replace('/', Path.DirectorySeparatorChar));
            if (ProfileRepository.IsLink(eventFile)) throw new InvalidDataException("Linked Saria reward resource.");
            byte[] raw = HAL_Zstd.Unpack(File.ReadAllBytes(eventFile), dictionaries);
            byte[] patched = HAL_EventRewardPatch.SariaFairyOcarina(raw, sariaReward!);
            hashes.Add(eventPath, Hash(raw)); outputs.Add(eventPath, HAL_Zstd.Compress(patched));
            string bridgePath = HAL_EventRewardPatch.SariaBridgeResource;
            string bridgeFile = Path.Combine(romfs, bridgePath.Replace('/', Path.DirectorySeparatorChar));
            if (ProfileRepository.IsLink(bridgeFile)) throw new InvalidDataException("Linked Saria bridge resource.");
            byte[] bridge = HAL_Zstd.Unpack(File.ReadAllBytes(bridgeFile), dictionaries);
            hashes.Add(bridgePath, Hash(bridge));
            outputs.Add(bridgePath, HAL_Zstd.Compress(HAL_EventRewardPatch.SariaBridgeTutorial(bridge)));
        }
        if (skullExchanges is not null)
        {
            string eventPath = HAL_SkulltulaRewards.Resource;
            string eventFile = Path.Combine(romfs, eventPath.Replace('/', Path.DirectorySeparatorChar));
            if (ProfileRepository.IsLink(eventFile)) throw new InvalidDataException("Linked Skulltula reward resource.");
            byte[] raw = HAL_Zstd.Unpack(File.ReadAllBytes(eventFile), dictionaries);
            byte[] patched = HAL_SkulltulaRewards.PatchRupeeExchanges(raw, skullExchanges);
            hashes.Add(eventPath, Hash(raw)); outputs.Add(eventPath, HAL_Zstd.Compress(patched));
        }
        foreach (var (name, raw) in maps)
        {
            var edits = assignments.Where(row => row.Resources.Contains(name, StringComparer.Ordinal)).ToArray();
            if (edits.Length == 0) continue;
            byte[] patched = HAL_ChestPatcher.Patch(raw, edits);
            if (!patched.SequenceEqual(raw)) outputs.Add(name, HAL_Zstd.Compress(patched));
        }
        var checksums = new JsonObject(); foreach (var (name, bytes) in outputs) checksums[name] = Hash(bytes);
        var binding = new JsonObject { ["adapter"] = settings.Scope == "full_game" ? "full-game-chests-v1" : settings.Scope == "childhood" ? "childhood-chests-v1" : "combined-chests-v1",
            ["profile_id"] = profileId, ["seed"] = seed, ["plan_hash"] = plan["plan_hash"]!.GetValue<string>() };
        string overlayId = Digest(new JsonObject { ["binding"] = binding.DeepClone(), ["output_hashes"] = checksums.DeepClone() });
        var metadata = new JsonObject { ["binding"] = binding.DeepClone(), ["scope"] = settings.Scope, ["output_hashes"] = checksums.DeepClone(),
            ["intro_skip"] = false, ["locations"] = locations.DeepClone(), ["plan"] = plan.DeepClone() };
        string overlay = Path.Combine(root, overlayId);
        if (Directory.Exists(overlay) || ProfileRepository.IsLink(overlay)) throw new InvalidDataException("Seed overlay already exists.");
        Directory.CreateDirectory(overlay);
        foreach (var (name, bytes) in outputs)
        {
            string destination = Path.Combine(overlay, name.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.WriteAllBytes(destination, bytes);
        }
        Write(Path.Combine(overlay, "manifest.json"), metadata); Write(Path.Combine(root, "locations.json"), locations); Write(Path.Combine(root, "binding.json"), binding);
        var byId = planElement.GetProperty("assignments").EnumerateArray().ToDictionary(x => x.GetProperty("id").GetString()!, StringComparer.Ordinal);
        var checks = new JsonObject();
        foreach (var node in locations["locations"]!.AsArray())
        {
            var location = node!.AsObject(); string id = location["check_id"]!.GetValue<string>();
            bool assigned = byId.TryGetValue(id, out var assignment);
            string? itemId = assigned ? HAL_ItemIds.ForActor(assignment.GetProperty("reward").GetString()!) : location["vanilla_item_id"]?.GetValue<string>();
            var item = itemId is null ? ("Unresolved reward", (string?)null) : HAL_ItemIds.Describe(itemId);
            checks[location["key"]!.GetValue<string>()] = new JsonObject { ["check_id"] = id, ["name"] = location["name"]!.GetValue<string>(),
                ["region"] = location["region"]!.GetValue<string>(), ["item_id"] = itemId, ["item"] = item.Item1, ["pickup_actor"] = item.Item2,
                ["shuffled"] = assigned, ["progression_excluded"] = location["progression_excluded"]?.GetValue<bool>() == true,
                ["source_check"] = assigned ? assignment.GetProperty("donor_id").GetString() : id };
        }
        if (settings.EventRewards)
        {
            var reward = planElement.GetProperty("event_assignments").EnumerateArray().Single(row => row.GetProperty("id").GetString() == HAL_EventRewardPlan.CheckId);
            var item = HAL_ItemIds.Describe(HAL_ItemIds.ForActor(targetReward!));
            checks[HAL_EventRewardPlan.CheckId] = new JsonObject { ["check_id"] = HAL_EventRewardPlan.CheckId,
                ["name"] = "Lost Woods target challenge (first clear)", ["region"] = "Lost Woods",
                ["item_id"] = HAL_ItemIds.ForActor(targetReward!), ["item"] = item.Name,
                ["pickup_actor"] = targetReward, ["shuffled"] = true,
                ["source_check"] = reward.GetProperty("donor_id").GetString() };
        }
        if (settings.SariaOcarina)
        {
            var reward = planElement.GetProperty("event_assignments").EnumerateArray().Single(row => row.GetProperty("id").GetString() == HAL_EventRewardPlan.SariaCheckId);
            var item = HAL_ItemIds.Describe(HAL_ItemIds.ForActor(sariaReward!));
            checks[HAL_EventRewardPlan.SariaCheckId] = new JsonObject { ["check_id"] = HAL_EventRewardPlan.SariaCheckId,
                ["name"] = "Saria's Fairy Ocarina", ["region"] = "Kokiri Forest exit", ["item_id"] = HAL_ItemIds.ForActor(sariaReward!),
                ["item"] = item.Name, ["pickup_actor"] = sariaReward, ["shuffled"] = true,
                ["source_check"] = reward.GetProperty("donor_id").GetString() };
        }
        if (skullExchanges is not null)
        {
            foreach (var check in HAL_SkulltulaRewards.Checks)
            {
                string id = $"event:skulltula_house_{check.Tokens}", actor = skullExchanges[check.Tokens];
                var reward = planElement.GetProperty("event_assignments").EnumerateArray().Single(row => row.GetProperty("id").GetString() == id);
                var item = HAL_ItemIds.Describe(HAL_ItemIds.ForActor(actor));
                checks[id] = new JsonObject { ["check_id"] = id, ["name"] = $"{check.Tokens} Gold Skulltulas", ["region"] = "Skulltula House",
                    ["item_id"] = HAL_ItemIds.ForActor(actor), ["item"] = item.Name, ["pickup_actor"] = actor, ["shuffled"] = true,
                    ["source_check"] = reward.GetProperty("donor_id").GetString() };
            }
        }
        var seedSettings = new JsonObject { ["native_chest_randomization"] = true, ["skip_new_game_intro"] = false,
            ["shuffle_chests"] = true, ["shuffle_npc_rewards"] = settings.EventRewards, ["shuffle_heart_pieces"] = settings.HeartPieces,
            ["shuffle_songs"] = false, ["shuffle_shops"] = false, ["shuffle_skulltulas"] = settings.SkulltulaRewards,
            ["requested_maximum_skulltulas"] = settings.MaximumSkulltulas, ["maximum_skulltulas_enforced"] = false,
            ["start_with_dungeon_maps_compasses"] = settings.StartingMaps, ["child_stealth"] = settings.ChildStealth };
        if (settings.SariaOcarina) seedSettings["shuffle_saria_ocarina"] = true;
        if (settings.StartingSword) seedSettings["starting_sword"] = true;
        if (settings.StartingSlingshot) seedSettings["starting_slingshot"] = true;
        if (settings.StartingBombBag) seedSettings["starting_bomb_bag"] = true;
        if (settings.StartingBoomerang) seedSettings["starting_boomerang"] = true;
        if (settings.StartingLens) seedSettings["starting_lens"] = true;
        if (settings.StartingShield) seedSettings["starting_deku_shield"] = true;
        if (settings.StartingOcarina) seedSettings["starting_fairy_ocarina"] = true;
        if (settings.StartingSongs is { Count: > 0 }) {
            _ = HAL_StartingSongs.Mask(settings.StartingSongs);
            seedSettings["starting_songs"] = JsonSerializer.SerializeToNode(settings.StartingSongs.OrderBy(name => Array.IndexOf(HAL_StartingSongs.Names, name)));
        }
        var consumables = HAL_StartingConsumables.Validate(settings.StartingConsumables, settings.StartingSlingshot, settings.StartingBombBag);
        if (consumables.Any(x => x != 0)) seedSettings["starting_consumables"] = JsonSerializer.SerializeToNode(consumables);
        if (settings.Scope != "kokiri_deku") seedSettings["chest_scope"] = settings.Scope;
        var record = new JsonObject { ["schema_version"] = 1, ["file_type"] = "Hookshot generated chest seed", ["seed"] = seed,
            ["settings"] = seedSettings, ["overlay"] = overlayId, ["binding"] = binding.DeepClone(),
            ["source_hashes"] = JsonSerializer.SerializeToNode(hashes), ["metadata"] = metadata.DeepClone(), ["locations"] = checks,
            ["logic"] = plan["logic"]!.GetValue<string>() };
        record["content_hash"] = Digest(record); Write(Path.Combine(root, "seed.json"), record);
        _ = HAL_RuntimeOverlay.Prepare(overlay, game, HAL_EnhancementPreferences.Load(game), independentBombchus: true, nativeAmmoGuard: true);
    }
}
