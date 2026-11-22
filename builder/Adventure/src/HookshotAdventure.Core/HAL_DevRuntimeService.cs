using System.Text.Json;

namespace HookshotAdventure.Core;

/// <summary>Prepares profile-local gameplay skips when launching a non-randomized adventure.</summary>
public static class HAL_DevRuntimeService
{
    public static IReadOnlyDictionary<string, string> PrepareLaunchEnvironment(
        string gameDirectory, string profileId, IReadOnlyDictionary<string, string>? existing)
    {
        if (profileId.Length != 32 || profileId.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new InvalidDataException("Invalid adventure profile ID.");
        string game = Path.GetFullPath(gameDirectory);
        if (!Directory.Exists(game)) throw new DirectoryNotFoundException("Game directory does not exist.");
        string manager = Path.Combine(game, "profile-manager"), metadata = Path.Combine(manager, "metadata");
        string profile = Path.Combine(metadata, profileId), baseDir = Path.Combine(profile, "runtime-base");
        foreach (string path in new[] { manager, metadata, profile, baseDir })
            if (ProfileRepository.IsLink(path)) throw new InvalidDataException("Linked adventure runtime metadata is not supported.");
        Directory.CreateDirectory(baseDir);
        string baseManifest = Path.Combine(baseDir, "manifest.json");
        if (ProfileRepository.IsLink(baseManifest)) throw new InvalidDataException("Linked runtime base manifest.");
        const string expected = "{\"format\":\"vanilla-runtime-base-v1\",\"output_hashes\":{},\"plan\":{}}";
        if (File.Exists(baseManifest))
        {
            using var actual = JsonDocument.Parse(File.ReadAllBytes(baseManifest)); using var baseline = JsonDocument.Parse(expected);
            if (HAL_CanonicalJson.Serialize(actual.RootElement) != HAL_CanonicalJson.Serialize(baseline.RootElement))
                throw new InvalidDataException("Runtime base manifest differs from the expected empty base.");
        }
        else File.WriteAllText(baseManifest, "{\"format\": \"vanilla-runtime-base-v1\", \"output_hashes\": {}, \"plan\": {}}\n");
        var preferences = HAL_EnhancementPreferences.Load(game);
        if (preferences.SkipDekuTreeClear) HAL_RandomizerSeedService.EnsureStoneRewardBuild(gameDirectory);
        var overlay = HAL_RuntimeOverlay.Prepare(baseDir, game, preferences);
        if (!Directory.Exists(overlay) || ProfileRepository.IsLink(overlay))
            throw new InvalidDataException("Gameplay runtime overlay is missing or linked.");
        var runtimePath = Path.Combine(overlay, "runtime.json");
        if (!File.Exists(runtimePath) || ProfileRepository.IsLink(runtimePath))
            throw new InvalidDataException("Gameplay runtime metadata is missing or linked.");
        using var runtime = JsonDocument.Parse(File.ReadAllText(runtimePath));
        var result = existing is null ? new Dictionary<string, string>(StringComparer.Ordinal) :
            new Dictionary<string, string>(existing, StringComparer.Ordinal);
        foreach (var pair in HAL_EnhancementPreferences.Load(gameDirectory).RuntimeEnvironment())
            result[pair.Key] = pair.Value;
        result["HOS_FS_OVERLAY"] = Path.GetFullPath(overlay);
        var rootElement = runtime.RootElement;
        if (rootElement.TryGetProperty("stone_reward_fallback", out var stone) && stone.ValueKind == JsonValueKind.True)
            result["HOS_RANDO_STONE_REWARD_FALLBACK"] = "1";
        if (rootElement.TryGetProperty("skip_boss_introductions", out var bossSkip) && bossSkip.ValueKind == JsonValueKind.True)
            result["HOS_SKIP_BOSS_INTRODUCTIONS"] = "1";
        if (rootElement.TryGetProperty("child_stealth_start", out var stealth) && stealth.ValueKind == JsonValueKind.True)
        {
            HAL_RandomizerSeedService.EnsureChildStealthBuild(gameDirectory);
            if (!rootElement.TryGetProperty("child_stealth_song", out var song) ||
                song.GetString() != "Item_Melody_ZeldaLullaby")
                throw new InvalidDataException("Gameplay runtime metadata has an unsupported Child Stealth song.");
            result["HOS_RANDO_CHILD_STEALTH"] = "1";
            result["HOS_RANDO_CHILD_STEALTH_SONG"] = song.GetString()!;
        }
        return result;
    }

}
