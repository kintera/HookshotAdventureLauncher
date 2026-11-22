using System.Text.Json;
using System.Text.Json.Serialization;

namespace HookshotAdventure.Core;

public sealed record HAL_EnhancementPreferences(
    [property: JsonPropertyName("skip_opening")] bool SkipOpening = true,
    [property: JsonPropertyName("skip_tutorial_conversations")] bool SkipTutorialConversations = true,
    [property: JsonPropertyName("skip_deku_first_entrance")] bool SkipDekuFirstEntrance = true,
    [property: JsonPropertyName("skip_deku_tree_meeting")] bool SkipDekuTreeMeeting = true,
    [property: JsonPropertyName("skip_kokiri_sword_cutscene")] bool SkipKokiriSwordCutscene = true,
    [property: JsonPropertyName("skip_deku_tree_clear")] bool SkipDekuTreeClear = true,
    [property: JsonPropertyName("skip_child_stealth")] bool SkipChildStealth = false,
    [property: JsonPropertyName("open_deku_tree")] bool OpenDekuTree = false,
    [property: JsonPropertyName("open_kokiri_forest")] bool OpenKokiriForest = false,
    [property: JsonPropertyName("skip_hyrule_field_entrance")] bool SkipHyruleFieldEntrance = false,
    [property: JsonPropertyName("skip_first_owl")] bool SkipFirstOwl = false,
    [property: JsonPropertyName("skip_castle_town_arrival")] bool SkipCastleTownArrival = false,
    [property: JsonPropertyName("skip_hyrule_castle_intro")] bool SkipHyruleCastleIntro = false,
    [property: JsonPropertyName("skip_kakariko_intro")] bool SkipKakarikoIntro = false,
    [property: JsonPropertyName("skip_entrance_cutscenes")] bool SkipEntranceCutscenes = false,
    [property: JsonPropertyName("skip_boss_introductions")] bool SkipBossIntroductions = false,
    [property: JsonPropertyName("open_door_of_time")] bool OpenDoorOfTime = false,
    [property: JsonPropertyName("open_jabu_jabu")] bool OpenJabuJabu = false,
    [property: JsonPropertyName("skip_trials")] bool SkipTrials = false,
    [property: JsonPropertyName("fast_text")] bool FastText = false)
{
    [JsonPropertyName("schema_version")] public int SchemaVersion { get; init; } = 1;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public IReadOnlyDictionary<string, string> RuntimeEnvironment()
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["HOS_SKIP_OPENING"] = SkipOpening ? "1" : "0",
            ["HOS_SKIP_BOSS_INTRODUCTIONS"] = SkipBossIntroductions ? "1" : "0",
            ["HOS_TEST_OPEN_TIME_DOOR"] = OpenDoorOfTime ? "1" : "0",
            ["HOS_TEST_OPEN_JABU"] = OpenJabuJabu ? "1" : "0",
            ["HOS_TEST_SKIP_TRIALS"] = SkipTrials ? "1" : "0"
        };
        if (FastText) environment["HOS_TEXT_SPEED"] = "3";
        return environment;
    }

    public static bool CanUse(string gameDirectory)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory) || !Directory.Exists(gameDirectory)) return false;
        try
        {
            // Development installs can live outside the default directory. Keep stable/linked
            // paths protected, and recognize custom builds by their compiled capability marker.
            var directory = HAL_DevelopmentBuild.ValidateOutputDirectory(gameDirectory);
            if (string.Equals(directory, Path.TrimEndingDirectorySeparator(Path.GetFullPath(HookshotGame.DefaultGameDirectory())),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return true;
            HAL_RandomizerSeedService.EnsureDevRandomizerBuild(directory);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return false;
        }
    }

    public static HAL_EnhancementPreferences Load(string gameDirectory)
    {
        var path = PreferencePath(gameDirectory);
        if (!File.Exists(path)) return new();
        if (ProfileRepository.IsLink(path)) throw new InvalidDataException("Enhancement settings file is a symlink.");
        string text = File.ReadAllText(path);
        using var document = JsonDocument.Parse(text);
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Enhancement settings must be an object.");
        var names = typeof(HAL_EnhancementPreferences).GetProperties().Select(p => p.GetCustomAttributes(typeof(JsonPropertyNameAttribute), false)
            .Cast<JsonPropertyNameAttribute>().SingleOrDefault()?.Name).Where(n => n is not null).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (!names.Contains(property.Name) || !seen.Add(property.Name)) throw new InvalidDataException("Unsupported enhancement settings.");
            if (property.Name == "schema_version")
            {
                if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetInt32(out int version) || version != 1)
                    throw new InvalidDataException("Unsupported enhancement settings version.");
            }
            else if (property.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new InvalidDataException("Enhancement settings must be boolean.");
        }
        return JsonSerializer.Deserialize<HAL_EnhancementPreferences>(text, Options)
            ?? throw new InvalidDataException("Enhancement settings file is invalid.");
    }

    public static void Save(string gameDirectory, HAL_EnhancementPreferences preferences)
    {
        var path = PreferencePath(gameDirectory);
        var directory = Path.GetDirectoryName(path)!;
        if (ProfileRepository.IsLink(directory)) throw new InvalidDataException("Hookshot-Dev profile-manager folder is a symlink.");
        Directory.CreateDirectory(directory);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(preferences, Options) + "\n");
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static string PreferencePath(string gameDirectory)
    {
        if (!CanUse(gameDirectory)) throw new InvalidOperationException("Skip preferences are restricted to the Hookshot-Dev installation.");
        return Path.Combine(Path.GetFullPath(gameDirectory), "profile-manager", "enhancements.json");
    }
}
