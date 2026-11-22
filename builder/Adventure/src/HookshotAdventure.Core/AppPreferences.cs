using System.Text.Json;
using System.Text.Json.Serialization;
namespace HookshotAdventure.Core;

public sealed class AppPreferences
{
    [JsonPropertyName("theme")] public string Theme { get; set; } = ThemeCatalog.DefaultTheme;
    [JsonPropertyName("launch_build")] public string LaunchBuild { get; set; } = "dev";
    [JsonPropertyName("video_debug")] public bool VideoDebug { get; set; }
    [JsonPropertyName("hos_debug")] public bool HosDebug { get; set; }
    [JsonPropertyName("a64_debug")] public bool A64Debug { get; set; }
    [JsonPropertyName("debug_key")] public bool DebugKey { get; set; } = true;
    [JsonPropertyName("debug_key_default_version")] public int DebugKeyDefaultVersion { get; set; }
    // The game folder managed by Hookshot Builder.
    [JsonPropertyName("dev_game_directory")] public string DevGameDirectory { get; set; } = HookshotGame.DefaultGameDirectory();
    [JsonPropertyName("game_directory")] public string GameDirectory { get; set; } = "";
    [JsonPropertyName("upstream_launcher_source")] public string UpstreamLauncherSource { get; set; } = "";
    [JsonPropertyName("randomizer_scope")] public string RandomizerScope { get; set; } = "kokiri_deku";
    [JsonPropertyName("randomizer_starting_maps")] public bool RandomizerStartingMaps { get; set; }
    [JsonPropertyName("randomizer_starting_sword")] public bool RandomizerStartingSword { get; set; }
    [JsonPropertyName("randomizer_starting_slingshot")] public bool RandomizerStartingSlingshot { get; set; }
    [JsonPropertyName("randomizer_starting_bomb_bag")] public bool RandomizerStartingBombBag { get; set; }
    [JsonPropertyName("randomizer_starting_boomerang")] public bool RandomizerStartingBoomerang { get; set; }
    [JsonPropertyName("randomizer_starting_lens")] public bool RandomizerStartingLens { get; set; }
    [JsonPropertyName("randomizer_starting_deku_shield")] public bool RandomizerStartingShield { get; set; }
    [JsonPropertyName("randomizer_starting_fairy_ocarina")] public bool RandomizerStartingOcarina { get; set; }
    [JsonPropertyName("randomizer_starting_songs")] public List<string> RandomizerStartingSongs { get; set; } = new();
    [JsonPropertyName("randomizer_starting_consumables")] public List<int> RandomizerStartingConsumables { get; set; } = new();
    [JsonPropertyName("randomizer_heart_pieces")] public bool RandomizerHeartPieces { get; set; }
    [JsonPropertyName("randomizer_event_rewards")] public bool RandomizerEventRewards { get; set; }
    [JsonPropertyName("randomizer_saria_ocarina")] public bool RandomizerSariaOcarina { get; set; }
    [JsonPropertyName("randomizer_skulltula_rewards")] public bool RandomizerSkulltulaRewards { get; set; }
    [JsonPropertyName("randomizer_excluded_checks")] public List<string> RandomizerExcludedChecks { get; set; } = new();
    [JsonPropertyName("randomizer_maximum_skulltulas")] public int RandomizerMaximumSkulltulas { get; set; } = HAL_SkulltulaRewards.DefaultMaximum;
    [JsonPropertyName("randomizer_child_stealth")] public bool RandomizerChildStealth { get; set; }
    [JsonPropertyName("dev_shader_workers")] public int? DevShaderWorkers { get; set; }
    [JsonPropertyName("tunic_cosmetics")] public HAL_TunicCosmetics TunicCosmetics { get; set; } = new();
}

public sealed class HAL_TunicCosmetics
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    [JsonPropertyName("kokiri")] public HAL_TunicSetting Kokiri { get; set; } = new("#1E691B", "locked");
    [JsonPropertyName("goron")] public HAL_TunicSetting Goron { get; set; } = new("#641E00", "locked");
    [JsonPropertyName("zora")] public HAL_TunicSetting Zora { get; set; } = new("#143C96", "locked");
    [JsonPropertyName("swords")] public HAL_SwordCosmetics Swords { get; set; } = new();
    [JsonPropertyName("hammer_enabled")] public bool HammerEnabled { get; set; }
    [JsonPropertyName("hammer")] public HAL_TunicSetting Hammer { get; set; } = new("#A8A8A8", "locked");
}

public sealed class HAL_SwordCosmetics
{
    [JsonPropertyName("kokiri")] public HAL_SwordSetting Kokiri { get; set; } = new() { BladeColor = "#D8D8D8", TrailColor = "#FFFFFF" };
    [JsonPropertyName("master")] public HAL_SwordSetting Master { get; set; } = new() { BladeColor = "#B9E8FF", TrailColor = "#77D8FF" };
    [JsonPropertyName("biggoron")] public HAL_SwordSetting Biggoron { get; set; } = new() { BladeColor = "#D6D6D6", TrailColor = "#FFFFFF" };
}

public sealed record HAL_SwordSetting
{
    [JsonPropertyName("blade_color")] public string BladeColor { get; init; } = "#FFFFFF";
    [JsonPropertyName("blade_mode")] public string BladeMode { get; init; } = "locked";
    [JsonPropertyName("trail_color")] public string TrailColor { get; init; } = "#FFFFFF";
    [JsonPropertyName("trail_mode")] public string TrailMode { get; init; } = "locked";
}

public sealed record HAL_TunicSetting(
    [property: JsonPropertyName("color")] string Color,
    [property: JsonPropertyName("mode")] string Mode);

public sealed class PreferenceStore
{
    private readonly string _path;
    public AppPreferences Value { get; }
    public PreferenceStore(string root)
    {
        _path = Path.Combine(root, "preferences.json");
        Value = File.Exists(_path) ? JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(_path)) ?? new() : new();
        var enableDebugKeyDefault = Value.DebugKeyDefaultVersion < 1;
        if (enableDebugKeyDefault)
        {
            Value.DebugKey = true;
            Value.DebugKeyDefaultVersion = 1;
        }
        Value.Theme = ThemeCatalog.Resolve(Value.Theme).Id;
        if (Value.RandomizerScope is not ("kokiri_deku" or "childhood" or "full_game")) Value.RandomizerScope = "kokiri_deku";
        if (Value.RandomizerMaximumSkulltulas is < 0 or > 100) Value.RandomizerMaximumSkulltulas = HAL_SkulltulaRewards.DefaultMaximum;
        try { Value.DevGameDirectory = HAL_DevelopmentBuild.ValidateOutputDirectory(Value.DevGameDirectory); }
        catch (Exception) { Value.DevGameDirectory = HookshotGame.DefaultGameDirectory(); }
        if (PathsEqual(Value.GameDirectory, HookshotGame.StableGameDirectory())) Value.LaunchBuild = "stable";
        else if (!PathsEqual(Value.GameDirectory, Value.DevGameDirectory) && !string.IsNullOrWhiteSpace(Value.GameDirectory)) Value.LaunchBuild = "custom";
        else if (Value.LaunchBuild is not ("dev" or "stable")) Value.LaunchBuild = "dev";
        if (enableDebugKeyDefault) Save();
    }
    public void SetTheme(string id)
    {
        Value.Theme = ThemeCatalog.Resolve(id).Id;
        Save();
    }
    public void SetDevGameDirectory(string path)
    {
        Value.DevGameDirectory = HAL_DevelopmentBuild.ValidateOutputDirectory(path);
        Save();
    }

    public void SetGameDirectory(string path)
    {
        var canonical = Path.GetFullPath(path);
        // Validate existence and layout before persisting; save root remains separate.
        var executable = HookshotGame.ResolveExecutable(canonical);
        if (!Directory.Exists(canonical) || ProfileRepository.IsLink(canonical) || !File.Exists(executable) ||
            !File.Exists(Path.Combine(canonical, "image.bin")) || !Directory.Exists(Path.Combine(canonical, "romfs")))
            throw new InvalidOperationException("Choose a built Hookshot folder with hookshot, image.bin and romfs.");
        Value.GameDirectory = canonical;
        Value.LaunchBuild = PathsEqual(canonical, HookshotGame.StableGameDirectory()) ? "stable" :
            PathsEqual(canonical, Value.DevGameDirectory) ? "dev" : "custom";
        Save();
    }
    public void SetLaunchBuild(string build)
    {
        if (build is not ("dev" or "stable")) throw new ArgumentException("Choose Hookshot-Dev or Hookshot.");
        Value.LaunchBuild = build;
        Value.GameDirectory = build == "stable" ? HookshotGame.StableGameDirectory() : Value.DevGameDirectory;
        Save();
    }
    public void SetVideoDebug(bool enabled)
    {
        Value.VideoDebug = enabled;
        Save();
    }
    public void SetHosDebug(bool enabled)
    {
        Value.HosDebug = enabled;
        Save();
    }
    public void SetA64Debug(bool enabled)
    {
        Value.A64Debug = enabled;
        Save();
    }
    public void SetDebugKey(bool enabled)
    {
        Value.DebugKey = enabled;
        Save();
    }
    public void SetDevShaderWorkers(int? workers)
    {
        if (workers is < 1 or > 32) throw new ArgumentOutOfRangeException(nameof(workers), "Shader workers must be from 1 to 32, or automatic.");
        Value.DevShaderWorkers = workers;
        Save();
    }
    public void ClearGameDirectory()
    {
        Value.GameDirectory = "";
        Save();
    }
    public void SetRandomizer(HAL_RandomizerSettings settings)
    {
        if (settings.Scope is not ("kokiri_deku" or "childhood" or "full_game")) throw new ArgumentException("Unsupported randomizer scope.");
        HAL_SkulltulaRewards.ValidateMaximum(settings.MaximumSkulltulas);
        _ = HAL_StartingSongs.Mask(settings.StartingSongs ?? Array.Empty<string>());
        var consumables = HAL_StartingConsumables.Validate(settings.StartingConsumables, settings.StartingSlingshot, settings.StartingBombBag);
        Value.RandomizerScope = settings.Scope;
        Value.RandomizerStartingMaps = settings.StartingMaps;
        Value.RandomizerStartingSword = settings.StartingSword;
        Value.RandomizerStartingSlingshot = settings.StartingSlingshot;
        Value.RandomizerStartingBombBag = settings.StartingBombBag;
        Value.RandomizerStartingBoomerang = settings.StartingBoomerang;
        Value.RandomizerStartingLens = settings.StartingLens;
        Value.RandomizerStartingShield = settings.StartingShield;
        Value.RandomizerStartingOcarina = settings.StartingOcarina;
        Value.RandomizerStartingSongs = (settings.StartingSongs ?? Array.Empty<string>()).ToList();
        Value.RandomizerStartingConsumables = consumables.ToList();
        Value.RandomizerHeartPieces = settings.HeartPieces;
        Value.RandomizerEventRewards = settings.EventRewards;
        Value.RandomizerSariaOcarina = settings.SariaOcarina;
        Value.RandomizerSkulltulaRewards = settings.SkulltulaRewards;
        Value.RandomizerExcludedChecks = HAL_LocationDocument.ValidateExclusions(settings.ExcludedChecks).ToList();
        Value.RandomizerMaximumSkulltulas = settings.MaximumSkulltulas;
        Value.RandomizerChildStealth = settings.ChildStealth;
        Save();
    }
    public void SetTunicCosmetics(HAL_TunicCosmetics settings)
    {
        static HAL_TunicSetting Validate(HAL_TunicSetting setting)
        {
            var color = setting.Color.Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(color, "^#[0-9a-fA-F]{6}$"))
                throw new ArgumentException("Tunic colors must use #RRGGBB format.");
            if (setting.Mode is not ("locked" or "random_launch" or "seed"))
                throw new ArgumentException("Unsupported tunic randomization mode.");
            return setting with { Color = color.ToUpperInvariant() };
        }
        Value.TunicCosmetics = new HAL_TunicCosmetics
        {
            Enabled = settings.Enabled,
            Kokiri = Validate(settings.Kokiri), Goron = Validate(settings.Goron), Zora = Validate(settings.Zora),
            Swords = ValidateSwordCosmetics(settings.Swords), HammerEnabled = settings.HammerEnabled, Hammer = Validate(settings.Hammer)
        };
        Save();
    }
    private static HAL_SwordCosmetics ValidateSwordCosmetics(HAL_SwordCosmetics? swords)
    {
        static string ValidateColor(string color)
        {
            color = color.Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(color, "^#[0-9a-fA-F]{6}$"))
                throw new ArgumentException("Sword colors must use #RRGGBB format.");
            return color.ToUpperInvariant();
        }
        static string ValidateMode(string mode)
        {
            if (mode is not ("locked" or "random_launch" or "seed"))
                throw new ArgumentException("Unsupported sword color mode.");
            return mode;
        }
        swords ??= new HAL_SwordCosmetics();
        return new HAL_SwordCosmetics
        {
            Kokiri = ValidateSword(swords.Kokiri),
            Master = ValidateSword(swords.Master),
            Biggoron = ValidateSword(swords.Biggoron)
        };

        HAL_SwordSetting ValidateSword(HAL_SwordSetting setting) => new()
        {
            BladeColor = ValidateColor(setting.BladeColor), BladeMode = ValidateMode(setting.BladeMode),
            TrailColor = ValidateColor(setting.TrailColor), TrailMode = ValidateMode(setting.TrailMode)
        };
    }
    private void Save()
    {
        var tmp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(tmp, JsonSerializer.Serialize(Value, new JsonSerializerOptions { WriteIndented = true }) + "\n"); File.Move(tmp, _path, true); }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }
    private static bool PathsEqual(string left, string right)
    {
        try { return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal); }
        catch (Exception) { return false; }
    }
}
