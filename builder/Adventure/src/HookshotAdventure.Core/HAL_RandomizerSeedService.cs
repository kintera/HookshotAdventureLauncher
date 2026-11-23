using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;

namespace HookshotAdventure.Core;

public sealed record HAL_RandomizerSettings(string Scope, bool StartingMaps, bool HeartPieces, bool ChildStealth, bool StartingSword = false, bool StartingSlingshot = false,
    bool StartingBombBag = false, bool StartingBoomerang = false, bool StartingLens = false, bool StartingShield = false,
    bool StartingOcarina = false, IReadOnlyList<string>? StartingSongs = null, IReadOnlyList<int>? StartingConsumables = null,
    bool EventRewards = false, int MaximumSkulltulas = HAL_SkulltulaRewards.DefaultMaximum, bool SariaOcarina = false,
    bool SkulltulaRewards = false, IReadOnlyList<string>? ExcludedChecks = null);

// Describes the launcher seed contract, not an in-game interop API. In
// particular, availability here does not assert full-game beatability.
public sealed record HAL_RandomizerCapability(string Id, string Phase, string Coverage);

public static class HAL_RandomizerSeedService
{
    public static IReadOnlyList<HAL_RandomizerCapability> GetCapabilities() => Array.AsReadOnly(new[]
    {
        new HAL_RandomizerCapability("scope", "seed", "Reviewed Kokiri/Deku, childhood or experimental full-game loose chest checks"),
        new HAL_RandomizerCapability("starting_maps", "seed", "Start with supported maps and compasses"),
        new HAL_RandomizerCapability("heart_pieces", "seed", "Experimental; childhood and full-game scopes"),
        new HAL_RandomizerCapability("event_rewards", "seed", "Experimental; Lost Woods target first-clear exchange"),
        new HAL_RandomizerCapability("child_stealth", "seed", "Child stealth starting state")
    });

    private sealed class SeedResult
    {
        [JsonPropertyName("seed")] public string Seed { get; set; } = "";
        [JsonPropertyName("overlay")] public string Overlay { get; set; } = "";
        [JsonPropertyName("starting_dungeon_flags")] public List<uint> StartingDungeonFlags { get; set; } = new();
        [JsonPropertyName("child_stealth")] public bool ChildStealth { get; set; }
        [JsonPropertyName("child_stealth_song")] public string ChildStealthSong { get; set; } = "";
        [JsonPropertyName("stone_reward_fallback")] public bool StoneRewardFallback { get; set; }
        public int StartingEquipment { get; set; }
        public int StartingSongs { get; set; }
        public int[] StartingConsumables { get; set; } = new int[4];
    }

    public static string NewSeed() => Convert.ToHexString(RandomNumberGenerator.GetBytes(8));

    /// <summary>Normalize seed input using the randomizer's existing Python seed rules.</summary>
    public static string NormalizeSeed(string seed)
    {
        // Python str.strip also treats U+001C–U+001F as whitespace.
        var text = seed ?? "";
        static bool IsPythonWhitespace(char c) => char.IsWhiteSpace(c) || c is >= '\u001c' and <= '\u001f';
        int first = 0, last = text.Length;
        while (first < last && IsPythonWhitespace(text[first])) first++;
        while (last > first && IsPythonWhitespace(text[last - 1])) last--;
        text = text[first..last];
        if (text.Length == 0) throw new ArgumentException("Enter a seed or generate one first.");
        if (text.Length <= 16 && text.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F'))
            return text.ToUpperInvariant().PadLeft(16, '0');
        var runes = text.EnumerateRunes().ToArray();
        if (runes.Length > 64 || runes.Any(c => c.Value < 32))
            throw new ArgumentException("Seed text must be 1–64 printable characters.");
        return Convert.ToHexString(SHA256.HashData(new UTF8Encoding(false, true).GetBytes(text)))[..16];
    }

    public static void EnsureDevRandomizerBuild(string gameDirectory)
    {
        var marker = Path.Combine(Path.GetFullPath(gameDirectory), "hookshot_randomizer_capability.txt");
        if (!File.Exists(marker) || ProfileRepository.IsLink(marker) ||
            File.ReadAllText(marker).Trim() != "HOOKSHOT_DEV_RANDOMIZER=1")
            throw new InvalidOperationException("Randomizer gameplay requires a Hookshot development build compiled with HOOKSHOT_DEV_RANDOMIZER=1.");
        var bombchu = Path.Combine(Path.GetFullPath(gameDirectory), "hookshot_bombchu_capacity_capability.txt");
        if (!File.Exists(bombchu) || ProfileRepository.IsLink(bombchu) || File.ReadAllText(bombchu).Trim() != "HOOKSHOT_BOMBCHU_CAPACITY=1")
            throw new InvalidOperationException("Randomizer gameplay needs an updated Hookshot-Dev build for bombchu capacity. Rebuild it in Dev Build Setup.");
    }

    public static void Generate(string gameDirectory, string profileDirectory, string profileId,
        string seed, HAL_RandomizerSettings settings)
    {
        EnsureDevRandomizerBuild(gameDirectory);
        if (settings.ChildStealth) EnsureChildStealthBuild(gameDirectory);
        int songMask = HAL_StartingSongs.Mask(settings.StartingSongs ?? Array.Empty<string>());
        var consumables = HAL_StartingConsumables.Validate(settings.StartingConsumables, settings.StartingSlingshot, settings.StartingBombBag);
        if (settings.StartingMaps || settings.StartingSword || settings.StartingSlingshot || settings.StartingBombBag || settings.StartingBoomerang || settings.StartingLens || settings.StartingShield || settings.StartingOcarina || songMask != 0 || consumables.Any(x => x != 0)) EnsureStartingInventoryBuild(gameDirectory, settings.StartingSlingshot);
        HAL_SeedGenerator.Generate(gameDirectory, profileDirectory, profileId, seed, settings);
    }
    private static void EnsureStartingInventoryBuild(string gameDirectory, bool startingSlingshot)
    {
        var marker = Path.Combine(Path.GetFullPath(gameDirectory), "hookshot_starting_inventory_capability.txt");
        var capability = !File.Exists(marker) || ProfileRepository.IsLink(marker) ? "" : File.ReadAllText(marker).Trim();
        if (startingSlingshot && capability == "HOOKSHOT_STARTING_INVENTORY=5")
            throw new InvalidOperationException("Starting Slingshot needs an updated Hookshot-Dev build to grant its seed bag. Rebuild or update it in Dev Build Setup.");
        if (capability is not ("HOOKSHOT_STARTING_INVENTORY=5" or "HOOKSHOT_STARTING_INVENTORY=6"))
            throw new InvalidOperationException("Starting inventory requires an updated Hookshot-Dev build. Rebuild or update it in Dev Build Setup.");
    }
    public static void EnsureChildStealthBuild(string gameDirectory)
    {
        var marker = Path.Combine(Path.GetFullPath(gameDirectory), "hookshot_child_stealth_capability.txt");
        if (!File.Exists(marker) || ProfileRepository.IsLink(marker) || File.ReadAllText(marker).Trim() != "HOOKSHOT_CHILD_STEALTH=1")
            throw new InvalidOperationException("Skip Child Stealth needs an updated Hookshot-Dev build to grant Zelda's Letter and Lullaby. Rebuild it in Dev Build Setup.");
    }
    public static void EnsureStoneRewardBuild(string gameDirectory)
    {
        var marker = Path.Combine(Path.GetFullPath(gameDirectory), "hookshot_stone_reward_capability.txt");
        if (!File.Exists(marker) || ProfileRepository.IsLink(marker) || File.ReadAllText(marker).Trim() != "HOOKSHOT_STONE_REWARD=1")
            throw new InvalidOperationException("Skipping the Deku Tree farewell needs an updated Hookshot-Dev build to recover a missed Kokiri Emerald. Rebuild in Dev Build Setup or turn off that skip.");
    }

    public static IReadOnlyDictionary<string, string> PrepareLaunchEnvironment(
        string gameDirectory, string profileDirectory, string profileId)
    {
        EnsureDevRandomizerBuild(gameDirectory);
        string profile = Path.GetFullPath(profileDirectory), game = Path.GetFullPath(gameDirectory);
        if (ProfileRepository.IsLink(profile) || !Directory.Exists(profile) || ProfileRepository.IsLink(Path.Combine(profile, "user")))
            throw new InvalidDataException("Invalid isolated adventure profile.");
        string root = Path.Combine(profile, "native-randomizer"), seedPath = Path.Combine(root, "seed.json");
        if (ProfileRepository.IsLink(root) || ProfileRepository.IsLink(seedPath) || !File.Exists(seedPath))
            throw new InvalidDataException("Randomizer seed is missing or linked.");
        using var seedDoc = JsonDocument.Parse(File.ReadAllBytes(seedPath)); var record = seedDoc.RootElement;
        string seedOverlay = HAL_SeedIdentity.ValidateOverlay(record, profileId, root);
        string romfs = Path.Combine(game, "romfs");
        HAL_SeedIdentity.ValidateSourceResources(record, romfs, HAL_RuntimeOverlay.Dictionaries(romfs));
        var settings = record.GetProperty("settings"); var plan = record.GetProperty("metadata").GetProperty("plan");
        var enhancements = HAL_EnhancementPreferences.Load(game);
        if (enhancements.SkipDekuTreeClear) EnsureStoneRewardBuild(gameDirectory);
        bool childStealth = (settings.TryGetProperty("child_stealth", out var child) && child.GetBoolean()) || enhancements.SkipChildStealth;
        if (childStealth) EnsureChildStealthBuild(gameDirectory);
        var startingItems = plan.GetProperty("starting_inventory").EnumerateArray()
            .Select(x => (Dungeon: x.GetProperty("dungeon").GetString()!, Item: x.GetProperty("item").GetString()!)).ToArray();
        bool startingMaps = settings.TryGetProperty("start_with_dungeon_maps_compasses", out var maps) && maps.GetBoolean();
        int equipment = (startingItems.Any(x => x.Item == "Item_Sword_Kokiri") ? 1 : 0) |
                        (startingItems.Any(x => x.Item == "Item_Range_SlingShot") ? 2 : 0) |
                        (startingItems.Any(x => x.Item == "Item_BombBag_Small") ? 4 : 0) |
                        (startingItems.Any(x => x.Item == "Item_Boomerang") ? 8 : 0) |
                        (startingItems.Any(x => x.Item == "Item_GlassTruth") ? 16 : 0) |
                        (settings.TryGetProperty("starting_deku_shield", out var shield) && shield.GetBoolean() ? 32 : 0) |
                        (settings.TryGetProperty("starting_fairy_ocarina", out var ocarina) && ocarina.GetBoolean() ? 64 : 0);
        int startingSongs = settings.TryGetProperty("starting_songs", out var songs)
            ? HAL_StartingSongs.Mask(songs.EnumerateArray().Select(x => x.GetString() ?? "")) : 0;
        var consumables = HAL_StartingConsumables.Validate(settings.TryGetProperty("starting_consumables", out var items)
            ? items.EnumerateArray().Select(x => x.GetInt32()).ToArray() : null, (equipment & 2) != 0, (equipment & 4) != 0);
        if (startingMaps || equipment != 0 || startingSongs != 0 || consumables.Any(x => x != 0)) EnsureStartingInventoryBuild(gameDirectory, (equipment & 2) != 0);
        var result = new SeedResult
        {
            Seed = record.GetProperty("seed").GetString()!,
            Overlay = HAL_RuntimeOverlay.Prepare(seedOverlay, game, enhancements with { SkipChildStealth = childStealth }, independentBombchus: true, nativeAmmoGuard: true),
            StartingDungeonFlags = HAL_StartingDungeonFlags.Resolve(startingItems.Where(x => x.Item is "Item_Dungeon_Map" or "Item_Dungeon_Compass"), startingMaps).ToList(),
            StartingEquipment = equipment,
            StartingSongs = startingSongs,
            StartingConsumables = consumables,
            ChildStealth = childStealth,
            ChildStealthSong = "Item_Melody_ZeldaLullaby",
            StoneRewardFallback = enhancements.SkipDekuTreeClear
        };
        return EnvironmentFromResult(gameDirectory, result);
    }

    private static IReadOnlyDictionary<string, string> EnvironmentFromResult(string gameDirectory, SeedResult result)
    {
        if (result.Seed.Length != 16 || !Directory.Exists(result.Overlay))
            throw new InvalidDataException("Randomizer seed or runtime overlay is invalid.");
        if (result.StartingDungeonFlags.Count > 10 || result.StartingDungeonFlags.Distinct().Count() != result.StartingDungeonFlags.Count)
            throw new InvalidDataException("Randomizer starting-item list is invalid.");
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["HOS_FS_OVERLAY"] = Path.GetFullPath(result.Overlay),
            ["HOS_RANDO_BOMBCHU_CAPACITY"] = "1"
        };
        // Gameplay skips live in the Hookshot-Dev enhancement preferences, not
        // in the seed manifest. Apply them to randomized launches explicitly.
        foreach (var pair in HAL_EnhancementPreferences.Load(gameDirectory).RuntimeEnvironment())
            environment[pair.Key] = pair.Value;
        if (result.StartingDungeonFlags.Count > 0)
            environment["HOS_RANDO_STARTING_DUNGEON_FLAGS"] = string.Join(',', result.StartingDungeonFlags.Select(x => x.ToString("X8")));
        if (result.StartingEquipment != 0) environment["HOS_RANDO_STARTING_EQUIPMENT"] = result.StartingEquipment.ToString();
        if (result.StartingSongs != 0) environment["HOS_RANDO_STARTING_SONGS"] = result.StartingSongs.ToString();
        if (result.StartingConsumables.Any(x => x != 0)) environment["HOS_RANDO_STARTING_CONSUMABLES"] = string.Join(',', result.StartingConsumables);
        if (result.StoneRewardFallback)
            environment["HOS_RANDO_STONE_REWARD_FALLBACK"] = "1";
        if (result.ChildStealth)
        {
            if (result.ChildStealthSong != "Item_Melody_ZeldaLullaby")
                throw new InvalidDataException("Unsupported Child Stealth starting song.");
            environment["HOS_RANDO_CHILD_STEALTH"] = "1";
            environment["HOS_RANDO_CHILD_STEALTH_SONG"] = result.ChildStealthSong;
        }
        return environment;
    }

}
