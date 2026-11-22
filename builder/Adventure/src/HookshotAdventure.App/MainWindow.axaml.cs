using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using HookshotAdventure.Core;
namespace HookshotAdventure.App;

public partial class MainWindow : Window
{
    private readonly ProfileRepository _profiles;
    private readonly PreferenceStore _preferences;
    private readonly IconManifest? _manifest;
    private readonly SaveReaderService _saveReader;
    private readonly HAL_IconCacheService _iconCache;
    private readonly HAL_AdventureSummaryView[] _compactViews =
        { new HAL_AdventureSummaryView { Compact = true }, new HAL_AdventureSummaryView { Compact = true }, new HAL_AdventureSummaryView { Compact = true } };
    private readonly SaveSummary?[] _shortcutSummaries = new SaveSummary?[3];
    private ProfileRecord? _current;
    private IReadOnlyList<SaveSummary> _HAL_managedSlots = Array.Empty<SaveSummary>();
    private readonly Dictionary<string, (string account, int slot)> _HAL_selectedSaveSlots = new();
    private bool _HAL_updatingSlotPicker;
    private IReadOnlyList<SaveSummary> _currentSlots = Array.Empty<SaveSummary>();
    private bool _HAL_updatingDetailsSlotPicker;
    private HAL_SaveEditorSession? _HAL_editor;
    private bool _syncingShaderWorkerControls;
    private Bitmap? _previewBitmap;
    private Bitmap? _managedPreviewBitmap;
    private readonly Bitmap?[] _shortcutBitmaps = new Bitmap?[3];
    private int? _newSlot;
    private bool _startup;
    private readonly HashSet<string> _excludedChecks = new(StringComparer.Ordinal);
    private ProfileRecord?[] _shortcuts = new ProfileRecord?[3];
    private readonly string[] _pages = { "AdventurePage", "RandomizerPage", "EnhancementsPage", "CreatePage", "DetailsPage", "ManagePage", "ToolsPage", "SettingsPage" };

    public MainWindow() : this(null) { }

    public MainWindow(ProfileRepository? profiles)
    {
        AvaloniaXamlLoader.Load(this);
        Closed += (_, _) => { _HAL_editor?.Dispose(); _HAL_editor = null; };
        Opened += CenterOnPrimaryScreen;
        if (profiles is null)
        {
            Opened += HAL_CheckInitialInstallation;
            Opened += async (_, _) => await HAL_BuildIcons(onlyIfMissing: true);
        }
        _startup = true;
        _profiles = profiles ?? new ProfileRepository();
        var profileScan = _profiles.ScanOnStartup();
        _preferences = new PreferenceStore(_profiles.Root);
        Console.WriteLine(_preferences.Value.DebugKey
            ? "F9 gameplay tracing is enabled for the next game launch. Press F9 in game to start recording."
            : "F9 gameplay tracing is disabled. Enable Debug Key in Settings before launching the game.");
        foreach (var id in HAL_LocationDocument.ValidateExclusions(_preferences.Value.RandomizerExcludedChecks)) _excludedChecks.Add(id);
        var configured = _preferences.Value.GameDirectory;
        var suggested = _preferences.Value.DevGameDirectory;
        if (string.IsNullOrWhiteSpace(configured))
        {
            try
            {
                _preferences.SetLaunchBuild(_preferences.Value.LaunchBuild == "stable" ? "stable" : "dev");
                configured = _preferences.Value.GameDirectory;
            }
            catch (Exception)
            {
                configured = suggested;
            }
        }
        var manifestPath = Path.Combine(AppContext.BaseDirectory, "resources", "icon_manifest.json");
        try { _manifest = File.Exists(manifestPath) ? new IconManifest(manifestPath) : null; }
        catch (Exception ex) { Status("Manifest could not be parsed: " + ex.Message); }
        _saveReader = new SaveReaderService(_manifest);
        _iconCache = new HAL_IconCacheService(_manifest);
        for (int i = 0; i < 3; i++)
            this.FindControl<ContentControl>($"SlotCompact{i}")!.Content = _compactViews[i];
        this.FindControl<TextBox>("IconCacheBox")!.Text = _iconCache.ActiveDirectory ?? "";
        RefreshIconCacheText();
        ThemeApplicator.Apply(_preferences.Value.Theme);
        var picker = this.FindControl<ComboBox>("ThemePicker")!;
        picker.ItemsSource = ThemeCatalog.All;
        picker.SelectedItem = ThemeCatalog.Resolve(_preferences.Value.Theme);
        this.FindControl<TextBlock>("ManifestStats")!.Text = _manifest is null ? "Manifest missing" :
            $"{_manifest.Icons.Count} roles: {_manifest.VerifiedCount} verified, {_manifest.PendingCount} pending.";
        this.FindControl<TextBlock>("RegistryPathText")!.Text = "Adventure profile registry: " + _profiles.IndexPath;
        this.FindControl<TextBlock>("ImportedPathText")!.Text = "Imported from: " + (_profiles.Index.ImportedFrom ?? "No legacy import");
        if (profiles is null) RegisterDefaultSaves();
        this.FindControl<TextBox>("GameDirectoryBox")!.Text = !string.IsNullOrWhiteSpace(configured) ? configured : suggested;
        this.FindControl<RadioButton>("LaunchDevRadio")!.IsChecked = _preferences.Value.LaunchBuild == "dev";
        this.FindControl<RadioButton>("LaunchStableRadio")!.IsChecked = _preferences.Value.LaunchBuild == "stable";
        this.FindControl<CheckBox>("VideoDebugCheck")!.IsChecked = _preferences.Value.VideoDebug;
        this.FindControl<CheckBox>("HosDebugCheck")!.IsChecked = _preferences.Value.HosDebug;
        this.FindControl<CheckBox>("A64DebugCheck")!.IsChecked = _preferences.Value.A64Debug;
        this.FindControl<CheckBox>("DebugKeyCheck")!.IsChecked = _preferences.Value.DebugKey;
        UpdateLaunchBuildStatus();
        this.FindControl<TextBox>("DevOutputBox")!.Text = suggested;
        this.FindControl<TextBox>("DevSourceBox")!.Text = HAL_DevelopmentBuild.SuggestedExistingSource(configured, suggested) ?? "";
        this.FindControl<TextBox>("DevShaderWorkersBox")!.Text = _preferences.Value.DevShaderWorkers?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
        this.FindControl<TextBlock>("InstallReviewStatus")!.Text = "No installation review has been run yet.";
        RefreshWarnings();
        LoadRandomizerSettings();
        LoadEnhancementSettings();
        LoadTunicCosmetics();
        RefreshProfiles();
        RefreshReadiness();
        Show("AdventurePage");
        if (_profiles.ImportedOnFirstRun) Status("Imported Hookshot profile metadata into a separate Adventure registry. Existing save files remain untouched.");
        else if (profileScan.Recovered + profileScan.RemovedStale + profileScan.RepairedPaths > 0)
            Status($"Startup save scan: recovered {profileScan.Recovered}, removed {profileScan.RemovedStale} missing entries, repaired {profileScan.RepairedPaths} profile paths. Save files were not changed.");
        _startup = false;
    }

    private async void HAL_CheckInitialInstallation(object? sender, EventArgs e)
    {
        Opened -= HAL_CheckInitialInstallation;
        var configured = _preferences.Value.GameDirectory;
        if (HookshotGame.IsInstalled(_preferences.Value.DevGameDirectory) ||
            (!string.IsNullOrWhiteSpace(configured) && HookshotGame.IsInstalled(configured))) return;
        await HAL_OpenDevInstaller();
    }

    private void CenterOnPrimaryScreen(object? sender, EventArgs e)
    {
        var primary = Screens?.Primary;
        if (primary is null) return;
        var area = primary.WorkingArea;
        var scale = primary.Scaling > 0 ? primary.Scaling : 1;
        var width = (int)Math.Ceiling((Bounds.Width > 0 ? Bounds.Width : Width) * scale);
        var height = (int)Math.Ceiling((Bounds.Height > 0 ? Bounds.Height : Height) * scale);
        Position = new PixelPoint(area.X + Math.Max(0, (area.Width - width) / 2),
            area.Y + Math.Max(0, (area.Height - height) / 2));
    }

    private static bool PathsEqual(string left, string right)
    {
        try { return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal); }
        catch (Exception) { return false; }
    }

    private void RefreshIconCacheText()
    {
        this.FindControl<TextBlock>("IconCacheStatus")!.Text = _iconCache.Status;
        this.FindControl<TextBlock>("IconCacheStats")!.Text = _iconCache.Status;
    }

    private void ReloadIcons_Click(object? sender, RoutedEventArgs e)
    {
        var preferred = this.FindControl<TextBox>(sender is Button { Name: "ToolsReloadIcons" } ? "ToolsIconCacheBox" : "IconCacheBox")!.Text;
        _iconCache.Reload(preferred);
        RefreshIconCacheText();
        RefreshProfiles();
        if (_current is not null) DisplaySlot(this.FindControl<ComboBox>("HAL_DetailsSlotPicker")!.SelectedItem as SaveSummary);
        Status(_iconCache.Status);
    }

    private bool _buildingIcons;
    private async void BuildIcons_Click(object? sender, RoutedEventArgs e) => await HAL_BuildIcons();

    private async Task HAL_BuildIcons(bool onlyIfMissing = false)
    {
        if (_buildingIcons || (onlyIfMissing && _iconCache.AvailableCount > 0)) return;
        if (onlyIfMissing && !HookshotGame.IsInstalled(_preferences.Value.GameDirectory)) return;
        _buildingIcons = true;
        try
        {
            Status("Building the local icon database…");
            var directory = await HAL_IconDatabase.BuildAsync(FindSourceRoot(), _preferences.Value.GameDirectory);
            this.FindControl<TextBox>("IconCacheBox")!.Text = directory;
            this.FindControl<TextBox>("ToolsIconCacheBox")!.Text = directory;
            _iconCache.Reload(directory);
            RefreshIconCacheText();
            RefreshProfiles();
            if (_current is not null) DisplaySlot(this.FindControl<ComboBox>("HAL_DetailsSlotPicker")!.SelectedItem as SaveSummary);
            Status(_iconCache.Status);
        }
        catch (Exception ex) { Status("Could not build the local icon database: " + ex.Message); }
        finally { _buildingIcons = false; }
    }

    private void Status(string message) => this.FindControl<TextBlock>("StatusText")!.Text = message;
    private void Show(string name)
    {
        this.FindControl<Control>("LaunchNavigation")!.IsVisible = name is "AdventurePage" or "RandomizerPage" or "EnhancementsPage";
        foreach (var page in _pages) this.FindControl<Control>(page)!.IsVisible = page == name;
    }

    private void RegisterDefaultSaves()
    {
        // First-use discovery is metadata-only: no save movement or copying.
        // Existing Hookshot registry entries remain authoritative if present.
        var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        var legacy = OperatingSystem.IsWindows()
            ? Path.Combine(_profiles.Index.LegacyHookshotGameDir ?? "", "user")
            : Path.Combine(!string.IsNullOrWhiteSpace(dataHome) ? dataHome : Path.Combine(userHome, ".local", "share"), "hookshot");
        var roots = new[] { HookshotGame.DefaultHookshotUserDirectory(), legacy };
        foreach (var path in roots.Distinct(StringComparer.Ordinal))
        {
            try { if (!_profiles.IsExternalHidden(path) && ProfileRepository.HasProgressSave(path)) _profiles.RegisterExternal(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            { Status("Could not inspect default saves: " + ex.Message); }
        }
    }

    // Keep the Qt character/scene header rather than exposing raw IDs such as Market_Child.
    // An age is displayed only if it was explicitly encoded in the location field.
    private static (string Scene, string Age) ReadableLocation(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return ("Unknown Location", "Unknown");
        string scene = raw.Trim();
        string age = "Unknown";
        foreach (var era in new[] { "Child", "Adult" })
        {
            if (scene.EndsWith("_" + era, StringComparison.OrdinalIgnoreCase))
            {
                age = era;
                scene = scene[..^(era.Length + 1)];
                break;
            }
            if (scene.StartsWith(era + "_", StringComparison.OrdinalIgnoreCase))
            {
                age = era;
                scene = scene[(era.Length + 1)..];
                break;
            }
        }
        scene = Regex.Replace(scene, "([a-z0-9])([A-Z])", "$1 $2").Replace('_', ' ').Trim();
        return (scene.Length > 0 ? scene : "Unknown Location", age);
    }

    private void RefreshReadiness()
    {
        var indicator = this.FindControl<TextBlock>("ReadinessText")!;
        try
        {
            this.FindControl<TextBlock>("LaunchDirectoryStatus")!.Text = "Selected game folder: " + _preferences.Value.GameDirectory;
            var candidate = _profiles.GetSlots().FirstOrDefault(p => p is not null);
            if (candidate is null)
            {
                indicator.Text = "Create an Adventure";
                indicator.Foreground = Avalonia.Media.Brushes.Goldenrod;
            }
            else
            {
                HookshotGame.CreatePlan(_preferences.Value.GameDirectory, candidate.UserDir);
                var marker = Path.Combine(_preferences.Value.GameDirectory, "hookshot_randomizer_capability.txt");
                var randomizerReady = File.Exists(marker) && !ProfileRepository.IsLink(marker) &&
                    File.ReadAllText(marker).Trim() == "HOOKSHOT_DEV_RANDOMIZER=1";
                indicator.Text = randomizerReady ? "Ready to Play" : "Ready · Rando off";
                indicator.Foreground = randomizerReady ? Avalonia.Media.Brushes.LightGreen : Avalonia.Media.Brushes.Goldenrod;
                ToolTip.SetTip(indicator, randomizerReady
                    ? "The selected game supports vanilla and native randomized adventures."
                    : "Vanilla adventures can launch. Randomized adventures need a native-randomizer Hookshot-Dev build.");
            }
        }
        catch (Exception)
        {
            indicator.Text = "Install / Select Hookshot";
            indicator.Foreground = Avalonia.Media.Brushes.Goldenrod;
            this.FindControl<TextBlock>("LaunchDirectoryStatus")!.Text = "The selected folder is not a complete Hookshot installation. Verify hookshot, image.bin and romfs.";
        }
    }

    private static void HAL_SetSaveBadge(Border badge, TextBlock label, ProfileRecord? profile)
    {
        bool randomized = profile?.IsRandomized == true || profile?.IsRandomizerConfigured == true;
        badge.Classes.Set("rando", randomized);
        badge.Classes.Set("vanilla", !randomized);
        label.Text = profile?.IsRandomizerConfigured == true ? "Rando · unseeded" : randomized ? "Rando Save" : "Vanilla Save";
    }

    private void RefreshProfiles()
    {
        _shortcuts = _profiles.GetSlots();
        for (int i = 0; i < 3; i++)
        {
            var p = _shortcuts[i];
            this.FindControl<TextBlock>($"SlotName{i}")!.Text = p?.Name ?? "New Adventure";
            this.FindControl<TextBlock>($"SlotDesc{i}")!.Text = p is null ? "Create an isolated save" :
                (ProfileRepository.HasProgressSave(p.UserDir) ? "Existing saved progress" : "New / no saved progress");
            RefreshShortcutPreview(i, p);
        }
        var list = this.FindControl<ListBox>("ProfileList")!;
        var selected = (list.SelectedItem as ProfileRecord)?.Id;
        list.ItemsSource = _profiles.All();
        list.SelectedItem = selected is null
            ? _profiles.All().FirstOrDefault()
            : _profiles.All().FirstOrDefault(p => p.Id == selected) ?? _profiles.All().FirstOrDefault();
        ManagedProfile_Changed(null, null!);
        RefreshReadiness();
    }

    // Preserve the Qt 156x88 read-only caption for each pinned Adventure.
    private void RefreshShortcutPreview(int index, ProfileRecord? profile)
    {
        var picture = this.FindControl<Image>($"SlotPreview{index}")!;
        picture.Source = null;
        _shortcutBitmaps[index]?.Dispose();
        _shortcutBitmaps[index] = null;
        var placeholder = this.FindControl<TextBlock>($"SlotNoPreview{index}")!;
        var description = this.FindControl<TextBlock>($"SlotDesc{index}")!;
        var summary = this.FindControl<TextBlock>($"SlotSummary{index}")!;
        var badge = this.FindControl<Border>($"SlotBadgeFrame{index}")!;
        var action = this.FindControl<Button>($"SlotAction{index}")!;
        action.Content = profile is null ? "Create" : "Select";
        badge.IsVisible = profile is not null;
        HAL_SetSaveBadge(badge, this.FindControl<TextBlock>($"SlotKind{index}")!, profile);
        summary.Text = profile is null ? "No save data" : "Start the adventure to create a new save";
        placeholder.IsVisible = true;
        _shortcutSummaries[index] = null;
        _compactViews[index].UpdateVisual(null, _iconCache, _preferences.Value.Theme);
        if (profile is null) return;
        try
        {
            var save = _saveReader.ReadSlots(profile.UserDir).FirstOrDefault(x => x.Warning is null);
            if (save is null) return;
            _shortcutSummaries[index] = save;
            _compactViews[index].UpdateVisual(save, _iconCache, _preferences.Value.Theme);
            var (scene, age) = ReadableLocation(save.Location);
            description.Text = age == "Unknown" ? scene : $"{age} · {scene}";
            summary.Text = $"♥ {save.HeartsText}     {save.RupeesText} Rupees     {save.SkulltulasText} Skulltulas";
            if (save.PreviewJpeg is not { Length: > 0 } jpeg) return;
            _shortcutBitmaps[index] = new Bitmap(new MemoryStream(jpeg, writable: false));
            picture.Source = _shortcutBitmaps[index];
            placeholder.IsVisible = false;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException)
        {
            summary.Text = "Save preview unavailable";
            Status($"Could not read Adventure {index + 1} preview: {ex.Message}");
        }
    }

    private void Choose(int i)
    {
        var p = _shortcuts[i];
        if (p is null)
        {
            _newSlot = i;
            this.FindControl<TextBox>("CreateNameBox")!.Text = $"Adventure {i + 1}";
            this.FindControl<CheckBox>("CreateRandomizedCheck")!.IsChecked = false;
            Show("CreatePage");
            return;
        }
        OpenProfile(p);
    }
    private void Slot0_Click(object? s, RoutedEventArgs e) => Choose(0);
    private void Slot1_Click(object? s, RoutedEventArgs e) => Choose(1);
    private void Slot2_Click(object? s, RoutedEventArgs e) => Choose(2);
    private void Adventure_Click(object? s, RoutedEventArgs e) { RefreshProfiles(); Show("AdventurePage"); }
    private void Manage_Click(object? s, RoutedEventArgs e) { RefreshProfiles(); Show("ManagePage"); }
    private void Randomizer_Click(object? s, RoutedEventArgs e) => Show("RandomizerPage");
    private void Enhancements_Click(object? s, RoutedEventArgs e) => Show("EnhancementsPage");
    private void Tools_Click(object? s, RoutedEventArgs e) => Show("ToolsPage");
    private void Settings_Click(object? s, RoutedEventArgs e) => Show(this.FindControl<Control>("SettingsPage")!.IsVisible ? "AdventurePage" : "SettingsPage");
    private void NewFromManage_Click(object? s, RoutedEventArgs e)
    {
        _newSlot = null;
        this.FindControl<TextBox>("CreateNameBox")!.Text = "New Adventure";
        this.FindControl<CheckBox>("CreateRandomizedCheck")!.IsChecked = false;
        Show("CreatePage");
    }

    private HAL_RandomizerSettings ReadRandomizerSettings()
    {
        var maps = (this.FindControl<ComboBox>("StartingMapsModePicker")!.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "start_with";
        return new HAL_RandomizerSettings("full_game",
            maps,
            this.FindControl<CheckBox>("HeartPiecesCheck")!.IsChecked == true,
            this.FindControl<CheckBox>("EnhancementChildStealthCheck")!.IsChecked == true,
            this.FindControl<CheckBox>("StartingSwordCheck")!.IsChecked == true,
            this.FindControl<CheckBox>("StartingSlingshotCheck")!.IsChecked == true,
            this.FindControl<CheckBox>("StartingBombBagCheck")!.IsChecked == true,
            this.FindControl<CheckBox>("StartingBoomerangCheck")!.IsChecked == true,
            this.FindControl<CheckBox>("StartingLensCheck")!.IsChecked == true,
            this.FindControl<CheckBox>("StartingShieldCheck")!.IsChecked == true,
            this.FindControl<CheckBox>("StartingOcarinaCheck")!.IsChecked == true,
            this.FindControl<WrapPanel>("StartingSongsPanel")!.Children.OfType<CheckBox>().Where(x => x.IsChecked == true).Select(x => x.Tag!.ToString()!).ToArray(),
            new[] { "StartingSticksCount", "StartingNutsCount", "StartingSeedsCount", "StartingBombsCount" }.Select(name => (int)(this.FindControl<NumericUpDown>(name)!.Value ?? 0)).ToArray(),
            this.FindControl<CheckBox>("NpcQuestRewardsCheck")!.IsChecked == true,
            int.TryParse((this.FindControl<ComboBox>("MaximumSkulltulasCount")!.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var skullMaximum) ? skullMaximum : HAL_SkulltulaRewards.DefaultMaximum,
            this.FindControl<CheckBox>("SariaFairyOcarinaShuffleCheck")!.IsChecked == true,
            this.FindControl<CheckBox>("SkulltulaRewardsCheck")!.IsChecked == true, _excludedChecks.OrderBy(x => x, StringComparer.Ordinal).ToArray());
    }

    private void LoadRandomizerSettings()
    {
        var mapsPicker = this.FindControl<ComboBox>("StartingMapsModePicker")!;
        mapsPicker.SelectedItem = mapsPicker.Items.Cast<ComboBoxItem>().FirstOrDefault(item => item.Tag?.ToString() == (_preferences.Value.RandomizerStartingMaps ? "start_with" : "vanilla")) ?? mapsPicker.Items.Cast<ComboBoxItem>().First();
        this.FindControl<CheckBox>("HeartPiecesCheck")!.IsChecked = _preferences.Value.RandomizerHeartPieces;
        this.FindControl<CheckBox>("NpcQuestRewardsCheck")!.IsChecked = _preferences.Value.RandomizerEventRewards;
        this.FindControl<CheckBox>("SariaFairyOcarinaShuffleCheck")!.IsChecked = _preferences.Value.RandomizerSariaOcarina;
        this.FindControl<CheckBox>("SkulltulaRewardsCheck")!.IsChecked = _preferences.Value.RandomizerSkulltulaRewards;
        var skullPicker = this.FindControl<ComboBox>("MaximumSkulltulasCount")!;
        var savedMaximum = _preferences.Value.RandomizerMaximumSkulltulas;
        var skullChoice = skullPicker.Items.Cast<ComboBoxItem>().FirstOrDefault(item => item.Tag?.ToString() == savedMaximum.ToString());
        if (skullChoice is null)
        {
            // Preserve custom values saved by older versions until the player chooses a milestone.
            skullChoice = new ComboBoxItem { Content = $"{savedMaximum} (saved)", Tag = savedMaximum.ToString() };
            skullPicker.Items.Add(skullChoice);
        }
        skullPicker.SelectedItem = skullChoice;
        this.FindControl<CheckBox>("StartingSwordCheck")!.IsChecked = _preferences.Value.RandomizerStartingSword;
        this.FindControl<CheckBox>("StartingSlingshotCheck")!.IsChecked = _preferences.Value.RandomizerStartingSlingshot;
        this.FindControl<CheckBox>("StartingBombBagCheck")!.IsChecked = _preferences.Value.RandomizerStartingBombBag;
        this.FindControl<CheckBox>("StartingBoomerangCheck")!.IsChecked = _preferences.Value.RandomizerStartingBoomerang;
        this.FindControl<CheckBox>("StartingLensCheck")!.IsChecked = _preferences.Value.RandomizerStartingLens;
        this.FindControl<CheckBox>("StartingShieldCheck")!.IsChecked = _preferences.Value.RandomizerStartingShield;
        this.FindControl<CheckBox>("StartingOcarinaCheck")!.IsChecked = _preferences.Value.RandomizerStartingOcarina;
        var songsPanel = this.FindControl<WrapPanel>("StartingSongsPanel")!;
        if (songsPanel.Children.Count == 0)
        {
            string[] labels = { "Zelda's Lullaby", "Epona's Song", "Saria's Song", "Sun's Song", "Song of Time", "Song of Storms",
                "Minuet of Forest", "Bolero of Fire", "Serenade of Water", "Nocturne of Shadow", "Requiem of Spirit", "Prelude of Light" };
            for (int i = 0; i < HAL_StartingSongs.Names.Length; i++) songsPanel.Children.Add(new CheckBox { Content = labels[i], Tag = HAL_StartingSongs.Names[i] });
        }
        foreach (var song in songsPanel.Children.OfType<CheckBox>()) song.IsChecked = (_preferences.Value.RandomizerStartingSongs ?? new()).Contains(song.Tag!.ToString()!);
        var amounts = _preferences.Value.RandomizerStartingConsumables ?? new();
        var amountControls = new[] { "StartingSticksCount", "StartingNutsCount", "StartingSeedsCount", "StartingBombsCount" };
        for (int i = 0; i < amountControls.Length; i++) this.FindControl<NumericUpDown>(amountControls[i])!.Value = i < amounts.Count ? amounts[i] : 0;
        this.FindControl<CheckBox>("EnhancementChildStealthCheck")!.IsChecked = _preferences.Value.RandomizerChildStealth;
        RefreshRandomizerLocations();
    }

    private void LoadEnhancementSettings()
    {
        var enabled = HAL_EnhancementPreferences.CanUse(_preferences.Value.GameDirectory);
        var panel = this.FindControl<Control>("EnhancementEditor")!;
        panel.IsEnabled = enabled;
        this.FindControl<CheckBox>("RandomizerSkipOpeningCheck")!.IsEnabled = enabled;
        this.FindControl<CheckBox>("OpenTimeDoorCheck")!.IsEnabled = enabled;
        this.FindControl<CheckBox>("OpenJabuCheck")!.IsEnabled = enabled;
        this.FindControl<CheckBox>("SkipTrialsCheck")!.IsEnabled = enabled;
        this.FindControl<CheckBox>("RandomizerSkipFirstOwlCheck")!.IsEnabled = enabled;
        this.FindControl<CheckBox>("RandomizerFastTextCheck")!.IsEnabled = enabled;
        this.FindControl<TextBlock>("EnhancementInstallHint")!.IsVisible = !enabled;
        if (!enabled) return;
        HAL_EnhancementPreferences prefs;
        try { prefs = HAL_EnhancementPreferences.Load(_preferences.Value.GameDirectory); }
        catch (Exception ex) { prefs = new(); Status("Using default skip settings: " + ex.Message); }
        this.FindControl<CheckBox>("EnhancementChildStealthCheck")!.IsChecked = _preferences.Value.RandomizerChildStealth;
        this.FindControl<CheckBox>("SkipOpeningCheck")!.IsChecked = prefs.SkipOpening;
        this.FindControl<CheckBox>("SkipTutorialCheck")!.IsChecked = prefs.SkipTutorialConversations;
        this.FindControl<CheckBox>("SkipDekuEntranceCheck")!.IsChecked = prefs.SkipDekuFirstEntrance;
        this.FindControl<CheckBox>("SkipGreatDekuCheck")!.IsChecked = prefs.SkipDekuTreeMeeting || prefs.SkipDekuTreeClear;
        this.FindControl<CheckBox>("SkipSwordCutsceneCheck")!.IsChecked = prefs.SkipKokiriSwordCutscene;
        this.FindControl<CheckBox>("OpenDekuTreeCheck")!.IsChecked = prefs.OpenDekuTree;
        this.FindControl<CheckBox>("OpenKokiriForestCheck")!.IsChecked = prefs.OpenKokiriForest;
        this.FindControl<CheckBox>("OpenTimeDoorCheck")!.IsChecked = prefs.OpenDoorOfTime;
        this.FindControl<CheckBox>("OpenJabuCheck")!.IsChecked = prefs.OpenJabuJabu;
        this.FindControl<CheckBox>("SkipTrialsCheck")!.IsChecked = prefs.SkipTrials;
        this.FindControl<CheckBox>("SkipEntranceCutscenesCheck")!.IsChecked = prefs.SkipEntranceCutscenes ||
            prefs.SkipHyruleFieldEntrance || prefs.SkipCastleTownArrival || prefs.SkipHyruleCastleIntro || prefs.SkipKakarikoIntro;
        this.FindControl<CheckBox>("SkipFirstOwlCheck")!.IsChecked = prefs.SkipFirstOwl;
        this.FindControl<CheckBox>("RandomizerFastTextCheck")!.IsChecked = prefs.FastText;
        this.FindControl<CheckBox>("SkipBossIntroductionsCheck")!.IsChecked = prefs.SkipBossIntroductions;
        SyncTimeSavers("SkipOpeningCheck", "RandomizerSkipOpeningCheck");
        SyncTimeSavers("SkipFirstOwlCheck", "RandomizerSkipFirstOwlCheck");
    }

    private void SyncTimeSavers(string source, string target)
    {
        var from = this.FindControl<CheckBox>(source);
        var to = this.FindControl<CheckBox>(target);
        if (from is not null && to is not null && to.IsChecked != from.IsChecked)
            to.IsChecked = from.IsChecked;
    }

    private void RandomizerTimeSaver_Changed(object? sender, RoutedEventArgs e)
    {
        if (sender is CheckBox box)
            SyncTimeSavers(box.Name!, box.Name == "RandomizerSkipOpeningCheck" ? "SkipOpeningCheck" : "SkipFirstOwlCheck");
    }

    private void EnhancementTimeSaver_Changed(object? sender, RoutedEventArgs e)
    {
        if (sender is CheckBox box)
            SyncTimeSavers(box.Name!, box.Name == "SkipOpeningCheck" ? "RandomizerSkipOpeningCheck" : "RandomizerSkipFirstOwlCheck");
    }

    private void SetSupportedEnhancements(bool value)
    {
        foreach (var name in new[] { "SkipOpeningCheck", "SkipTutorialCheck", "SkipDekuEntranceCheck",
                     "SkipGreatDekuCheck", "SkipSwordCutsceneCheck", "SkipEntranceCutscenesCheck", "SkipFirstOwlCheck", "SkipBossIntroductionsCheck",
                     "EnhancementChildStealthCheck" })
            this.FindControl<CheckBox>(name)!.IsChecked = value;
    }
    private void EnhancementsAll_Click(object? s, RoutedEventArgs e) => SetSupportedEnhancements(true);
    private void EnhancementsNone_Click(object? s, RoutedEventArgs e) => SetSupportedEnhancements(false);

    private void SaveEnhancementSettings_Click(object? s, RoutedEventArgs e)
    {
        try
        {
            var prefs = new HAL_EnhancementPreferences(
                this.FindControl<CheckBox>("SkipOpeningCheck")!.IsChecked == true,
                this.FindControl<CheckBox>("SkipTutorialCheck")!.IsChecked == true,
                this.FindControl<CheckBox>("SkipDekuEntranceCheck")!.IsChecked == true,
                this.FindControl<CheckBox>("SkipGreatDekuCheck")!.IsChecked == true,
                this.FindControl<CheckBox>("SkipSwordCutsceneCheck")!.IsChecked == true,
                this.FindControl<CheckBox>("SkipGreatDekuCheck")!.IsChecked == true,
                this.FindControl<CheckBox>("EnhancementChildStealthCheck")!.IsChecked == true,
                this.FindControl<CheckBox>("OpenDekuTreeCheck")!.IsChecked == true,
                this.FindControl<CheckBox>("OpenKokiriForestCheck")!.IsChecked == true,
                this.FindControl<CheckBox>("SkipEntranceCutscenesCheck")!.IsChecked == true,
                this.FindControl<CheckBox>("SkipFirstOwlCheck")!.IsChecked == true,
                this.FindControl<CheckBox>("SkipEntranceCutscenesCheck")!.IsChecked == true,
                this.FindControl<CheckBox>("SkipEntranceCutscenesCheck")!.IsChecked == true,
                this.FindControl<CheckBox>("SkipEntranceCutscenesCheck")!.IsChecked == true,
                this.FindControl<CheckBox>("SkipEntranceCutscenesCheck")!.IsChecked == true,
                this.FindControl<CheckBox>("SkipBossIntroductionsCheck")!.IsChecked == true,
                this.FindControl<CheckBox>("OpenTimeDoorCheck")!.IsChecked == true,
                this.FindControl<CheckBox>("OpenJabuCheck")!.IsChecked == true,
                this.FindControl<CheckBox>("SkipTrialsCheck")!.IsChecked == true,
                FastText: this.FindControl<CheckBox>("RandomizerFastTextCheck")!.IsChecked == true);
            HAL_EnhancementPreferences.Save(_preferences.Value.GameDirectory, prefs);
            _preferences.SetRandomizer(new HAL_RandomizerSettings("full_game",
                _preferences.Value.RandomizerStartingMaps, _preferences.Value.RandomizerHeartPieces,
                this.FindControl<CheckBox>("EnhancementChildStealthCheck")!.IsChecked == true,
                _preferences.Value.RandomizerStartingSword, _preferences.Value.RandomizerStartingSlingshot,
                _preferences.Value.RandomizerStartingBombBag, _preferences.Value.RandomizerStartingBoomerang, _preferences.Value.RandomizerStartingLens, _preferences.Value.RandomizerStartingShield,
                _preferences.Value.RandomizerStartingOcarina, _preferences.Value.RandomizerStartingSongs, _preferences.Value.RandomizerStartingConsumables, _preferences.Value.RandomizerEventRewards, _preferences.Value.RandomizerMaximumSkulltulas, _preferences.Value.RandomizerSariaOcarina, _preferences.Value.RandomizerSkulltulaRewards, _preferences.Value.RandomizerExcludedChecks));
            Status("Supported skips saved to the Hookshot-Dev profile-manager folder. They apply to randomized profiles on their next launch.");
        }
        catch (Exception ex) { Status("Could not save skip settings: " + ex.Message); }
    }

    private void LoadTunicCosmetics()
    {
        var settings = _preferences.Value.TunicCosmetics ?? new HAL_TunicCosmetics();
        this.FindControl<CheckBox>("CustomCosmeticsCheck")!.IsChecked = settings.Enabled;
        SetTunicControls("Kokiri", settings.Kokiri);
        SetTunicControls("Goron", settings.Goron);
        SetTunicControls("Zora", settings.Zora);
        SetSwordColor("KokiriSwordBlade", settings.Swords.Kokiri.BladeColor);
        SetSwordColor("KokiriSwordTrail", settings.Swords.Kokiri.TrailColor);
        SetSwordMode("KokiriSwordBlade", settings.Swords.Kokiri.BladeMode);
        SetSwordMode("KokiriSwordTrail", settings.Swords.Kokiri.TrailMode);
        SetSwordColor("MasterSwordBlade", settings.Swords.Master.BladeColor);
        SetSwordColor("MasterSwordTrail", settings.Swords.Master.TrailColor);
        SetSwordMode("MasterSwordBlade", settings.Swords.Master.BladeMode);
        SetSwordMode("MasterSwordTrail", settings.Swords.Master.TrailMode);
        SetSwordColor("BiggoronSwordBlade", settings.Swords.Biggoron.BladeColor);
        SetSwordColor("BiggoronSwordTrail", settings.Swords.Biggoron.TrailColor);
        SetSwordMode("BiggoronSwordBlade", settings.Swords.Biggoron.BladeMode);
        SetSwordMode("BiggoronSwordTrail", settings.Swords.Biggoron.TrailMode);
        this.FindControl<CheckBox>("HammerColorEnabled")!.IsChecked = settings.HammerEnabled;
        SetSwordColor("Hammer", settings.Hammer.Color);
        SetSwordMode("Hammer", settings.Hammer.Mode);
    }

    private void SetTunicControls(string name, HAL_TunicSetting setting)
    {
        SetTunicColor(name, setting.Color);
        this.FindControl<RadioButton>($"{name}TunicLocked")!.IsChecked = setting.Mode == "locked";
        this.FindControl<RadioButton>($"{name}TunicRandomLaunch")!.IsChecked = setting.Mode == "random_launch";
        this.FindControl<RadioButton>($"{name}TunicSeedRandom")!.IsChecked = setting.Mode == "seed";
    }

    private HAL_TunicSetting ReadTunicControls(string name)
    {
        var mode = this.FindControl<RadioButton>($"{name}TunicRandomLaunch")!.IsChecked == true ? "random_launch" :
            this.FindControl<RadioButton>($"{name}TunicSeedRandom")!.IsChecked == true ? "seed" : "locked";
        return new HAL_TunicSetting(this.FindControl<Button>($"{name}TunicColor")!.Content?.ToString() ?? "", mode);
    }

    private void SetTunicColor(string name, string hex)
    {
        var button = this.FindControl<Button>($"{name}TunicColor")!;
        var color = Color.Parse(hex);
        button.Content = hex.ToUpperInvariant();
        button.Background = new SolidColorBrush(color);
        button.Foreground = color.R + color.G + color.B > 390 ? Brushes.Black : Brushes.White;
    }

    private async void PickTunicColor_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button source || source.Tag?.ToString() is not { } name) return;
        var chosen = await ShowTunicColorPickerAsync(name, ReadTunicControls(name).Color);
        if (chosen is not null) SetTunicColor(name, chosen);
    }

    private void SetSwordColor(string name, string hex)
    {
        var button = this.FindControl<Button>($"{name}Color")!;
        var color = Color.Parse(hex);
        button.Content = hex.ToUpperInvariant();
        button.Background = new SolidColorBrush(color);
        button.Foreground = color.R + color.G + color.B > 390 ? Brushes.Black : Brushes.White;
    }

    private void SetSwordMode(string name, string mode)
    {
        var picker = this.FindControl<ComboBox>($"{name}Mode")!;
        picker.SelectedItem = picker.Items.Cast<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag?.ToString() == mode) ?? picker.Items.Cast<ComboBoxItem>().First();
    }

    private async void PickSwordColor_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button source || source.Tag?.ToString() is not { } name) return;
        var initial = source.Content?.ToString() ?? "#FFFFFF";
        var chosen = await ShowTunicColorPickerAsync(name, initial);
        if (chosen is not null) SetSwordColor(name, chosen);
    }

    private async Task<string?> ShowTunicColorPickerAsync(string name, string initial)
    {
        var initialColor = Color.Parse(initial);
        RgbToHsv(initialColor, out var hue, out var saturation, out var value);
        var custom = new string?[16];
        string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        Color Selected() => HsvToRgb(hue, saturation, value);

        var preview = new Border { Height = 58, CornerRadius = new Avalonia.CornerRadius(4), BorderThickness = new Avalonia.Thickness(1), BorderBrush = Brushes.Gray };
        var previewText = new TextBlock { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, FontWeight = Avalonia.Media.FontWeight.Bold };
        preview.Child = previewText;
        var hsvFields = new TextBox[3];
        var rgbFields = new TextBox[3];
        var htmlField = new TextBox { MaxLength = 7 };
        var sv = new Grid { Height = 218, MinWidth = 250, ClipToBounds = true, Background = new SolidColorBrush(HsvToRgb(hue, 1, 1)) };
        sv.Children.Add(new Border { Background = new LinearGradientBrush { StartPoint = new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Relative), EndPoint = new Avalonia.RelativePoint(1, 0, Avalonia.RelativeUnit.Relative), GradientStops = new GradientStops { new(Colors.White, 0), new(Color.FromArgb(0, 255, 255, 255), 1) } } });
        sv.Children.Add(new Border { Background = new LinearGradientBrush { StartPoint = new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Relative), EndPoint = new Avalonia.RelativePoint(0, 1, Avalonia.RelativeUnit.Relative), GradientStops = new GradientStops { new(Color.FromArgb(0, 0, 0, 0), 0), new(Colors.Black, 1) } } });
        var hueBar = new Border { Width = 24, Height = 218, BorderBrush = Brushes.Gray, BorderThickness = new Avalonia.Thickness(1), Background = new LinearGradientBrush { StartPoint = new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Relative), EndPoint = new Avalonia.RelativePoint(0, 1, Avalonia.RelativeUnit.Relative), GradientStops = new GradientStops { new(Colors.Red, 0), new(Colors.Yellow, .17), new(Colors.Lime, .33), new(Colors.Cyan, .5), new(Colors.Blue, .67), new(Colors.Magenta, .83), new(Colors.Red, 1) } } };

        void Refresh()
        {
            var color = Selected();
            sv.Background = new SolidColorBrush(HsvToRgb(hue, 1, 1));
            preview.Background = new SolidColorBrush(color);
            previewText.Text = Hex(color);
            previewText.Foreground = color.R + color.G + color.B > 390 ? Brushes.Black : Brushes.White;
            var values = new[] { (int)Math.Round(hue), (int)Math.Round(saturation * 100), (int)Math.Round(value * 100) };
            var channels = new[] { (int)color.R, (int)color.G, (int)color.B };
            for (var i = 0; i < 3; i++) { hsvFields[i].Text = values[i].ToString(); rgbFields[i].Text = channels[i].ToString(); }
            htmlField.Text = Hex(color);
        }
        void Select(Color color) { RgbToHsv(color, out hue, out saturation, out value); Refresh(); }
        void PickSv(PointerEventArgs args)
        {
            if (sv.Bounds.Width <= 0 || sv.Bounds.Height <= 0) return;
            var point = args.GetPosition(sv);
            saturation = Math.Clamp(point.X / sv.Bounds.Width, 0, 1);
            value = 1 - Math.Clamp(point.Y / sv.Bounds.Height, 0, 1);
            Refresh();
        }
        void PickHue(PointerEventArgs args)
        {
            if (hueBar.Bounds.Height <= 0) return;
            hue = Math.Clamp(args.GetPosition(hueBar).Y / hueBar.Bounds.Height, 0, 1) * 360;
            Refresh();
        }
        sv.PointerPressed += (_, args) => { PickSv(args); args.Pointer.Capture(sv); };
        sv.PointerMoved += (_, args) => { if (args.GetCurrentPoint(sv).Properties.IsLeftButtonPressed) PickSv(args); };
        sv.PointerReleased += (_, args) => args.Pointer.Capture(null);
        hueBar.PointerPressed += (_, args) => { PickHue(args); args.Pointer.Capture(hueBar); };
        hueBar.PointerMoved += (_, args) => { if (args.GetCurrentPoint(hueBar).Properties.IsLeftButtonPressed) PickHue(args); };
        hueBar.PointerReleased += (_, args) => args.Pointer.Capture(null);

        var paletteColors = new[]
        {
            "#800000", "#FF0000", "#808000", "#FFFF00", "#008000", "#00FF00", "#008080", "#00FFFF",
            "#000080", "#0000FF", "#800080", "#FF00FF", "#C0C0C0", "#808080", "#FFFFFF", "#000000",
            "#F4A460", "#D2691E", "#8B4513", "#A52A2A", "#FF69B4", "#DA70D6", "#7B68EE", "#4682B4",
            "#2E8B57", "#9ACD32", "#FFD700", "#F0E68C", "#708090", "#B0C4DE", "#E6E6FA", "#D3D3D3"
        };
        Grid MakeSwatches(int rows, int columns, Func<int, string?> getColor, Action<int, string?> selected)
        {
            var grid = new Grid { RowDefinitions = new RowDefinitions(string.Join(',', Enumerable.Repeat("Auto", rows))), ColumnDefinitions = new ColumnDefinitions(string.Join(',', Enumerable.Repeat("Auto", columns))), RowSpacing = 3, ColumnSpacing = 3 };
            for (var i = 0; i < rows * columns; i++)
            {
                var index = i;
                var color = getColor(index);
                var button = new Button { Width = 21, Height = 19, Padding = new Avalonia.Thickness(0), Background = color is null ? Brushes.White : new SolidColorBrush(Color.Parse(color)), BorderBrush = Brushes.Gray, BorderThickness = new Avalonia.Thickness(1) };
                button.Click += (_, _) => { var valueColor = getColor(index); if (valueColor is not null) selected(index, valueColor); };
                grid.Children.Add(button); Grid.SetRow(button, i / columns); Grid.SetColumn(button, i % columns);
            }
            return grid;
        }
        var basic = MakeSwatches(4, 8, i => paletteColors[i], (_, hex) => Select(Color.Parse(hex!)));
        var customGrid = MakeSwatches(2, 8, i => custom[i], (i, hex) => { if (hex is not null) Select(Color.Parse(hex)); });
        var currentCustomGrid = customGrid;

        var left = new StackPanel { Spacing = 8, Children =
        {
            new TextBlock { Text = "Basic colors", FontSize = 12 }, basic,
            new Button { Content = "Add to Custom Colors", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, MinHeight = 30 },
            new TextBlock { Text = "Custom colors", FontSize = 12, Margin = new Avalonia.Thickness(0, 8, 0, 0) }, customGrid
        }};
        var addCustom = (Button)left.Children[2];
        addCustom.Click += (_, _) =>
        {
            var slot = Array.FindIndex(custom, c => c is null);
            if (slot < 0) { Array.Copy(custom, 1, custom, 0, custom.Length - 1); slot = custom.Length - 1; }
            custom[slot] = Hex(Selected());
            var replacement = MakeSwatches(2, 8, i => custom[i], (i, hex) => { if (hex is not null) Select(Color.Parse(hex)); });
            var index = left.Children.IndexOf(currentCustomGrid); left.Children[index] = replacement; currentCustomGrid = replacement;
        };

        var colorSpace = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8, Children = { sv, hueBar } };
        Grid.SetColumn(hueBar, 1);
        var center = new StackPanel { Spacing = 8, Children = { colorSpace } };
        var details = new StackPanel { Spacing = 5, Children = { preview } };
        void AddValueRow(string label, TextBox field, int row)
        {
            var rowPanel = new Grid { ColumnDefinitions = new ColumnDefinitions("44,*"), ColumnSpacing = 5, Children = { new TextBlock { Text = label, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }, field } };
            Grid.SetColumn(field, 1); details.Children.Add(rowPanel);
        }
        for (var i = 0; i < 3; i++) { hsvFields[i] = new TextBox { IsReadOnly = true, MinWidth = 74 }; AddValueRow(new[] { "Hue", "Sat", "Val" }[i], hsvFields[i], i); }
        for (var i = 0; i < 3; i++) { rgbFields[i] = new TextBox { IsReadOnly = true, MinWidth = 74 }; AddValueRow(new[] { "Red", "Green", "Blue" }[i], rgbFields[i], i + 3); }
        AddValueRow("HTML", htmlField, 6);
        htmlField.TextChanged += (_, _) =>
        {
            var hex = htmlField.Text?.Trim() ?? "";
            if (Regex.IsMatch(hex, "^#[0-9a-fA-F]{6}$"))
            {
                try { Select(Color.Parse(hex)); } catch (FormatException) { }
            }
        };
        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("210,*,160"), RowDefinitions = new RowDefinitions("*,Auto"), ColumnSpacing = 12, RowSpacing = 10, Margin = new Avalonia.Thickness(14) };
        content.Children.Add(left); content.Children.Add(center); content.Children.Add(details);
        Grid.SetColumn(center, 1); Grid.SetColumn(details, 2);
        var actions = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Spacing = 8, Children =
        {
            new Button { Content = "OK", MinWidth = 92, IsDefault = true }, new Button { Content = "Cancel", MinWidth = 92, IsCancel = true }
        }};
        content.Children.Add(actions); Grid.SetRow(actions, 1); Grid.SetColumn(actions, 2);
        var dialog = new Window { Title = $"{name} Tunic Colour", Width = 730, Height = 480, MinWidth = 680, MinHeight = 440, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = content };
        var ok = (Button)actions.Children[0]; var cancel = (Button)actions.Children[1];
        ok.Click += (_, _) => dialog.Close(Hex(Selected())); cancel.Click += (_, _) => dialog.Close(null);
        Refresh();
        return await dialog.ShowDialog<string?>(this);
    }

    private static void RgbToHsv(Color color, out double hue, out double saturation, out double value)
    {
        var r = color.R / 255d; var g = color.G / 255d; var b = color.B / 255d;
        var max = Math.Max(r, Math.Max(g, b)); var min = Math.Min(r, Math.Min(g, b)); var delta = max - min;
        hue = delta == 0 ? 0 : max == r ? 60 * (((g - b) / delta) % 6) : max == g ? 60 * (((b - r) / delta) + 2) : 60 * (((r - g) / delta) + 4);
        if (hue < 0) hue += 360;
        saturation = max == 0 ? 0 : delta / max; value = max;
    }

    private static Color HsvToRgb(double hue, double saturation, double value)
    {
        var chroma = value * saturation; var x = chroma * (1 - Math.Abs((hue / 60 % 2) - 1)); var m = value - chroma;
        var (r, g, b) = hue switch
        {
            < 60 => (chroma, x, 0d), < 120 => (x, chroma, 0d), < 180 => (0d, chroma, x),
            < 240 => (0d, x, chroma), < 300 => (x, 0d, chroma), _ => (chroma, 0d, x)
        };
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    private void SaveTunicCosmetics_Click(object? s, RoutedEventArgs e)
    {
        try
        {
            _preferences.SetTunicCosmetics(new HAL_TunicCosmetics
            {
                Enabled = this.FindControl<CheckBox>("CustomCosmeticsCheck")!.IsChecked == true,
                Kokiri = ReadTunicControls("Kokiri"),
                Goron = ReadTunicControls("Goron"),
                Zora = ReadTunicControls("Zora"),
                HammerEnabled = this.FindControl<CheckBox>("HammerColorEnabled")!.IsChecked == true,
                Hammer = new HAL_TunicSetting(this.FindControl<Button>("HammerColor")!.Content?.ToString() ?? "",
                    (this.FindControl<ComboBox>("HammerMode")!.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "locked"),
                Swords = new HAL_SwordCosmetics
                {
                    Kokiri = ReadSwordControls("Kokiri"),
                    Master = ReadSwordControls("Master"),
                    Biggoron = ReadSwordControls("Biggoron")
                }
            });
            Status("Cosmetic preferences saved. Enabled tunic, sword and hammer colours apply on the next game launch.");
        }
        catch (Exception ex) { Status("Could not save tunic preferences: " + ex.Message); }
    }

    private HAL_SwordSetting ReadSwordControls(string sword) => new()
    {
        BladeColor = this.FindControl<Button>($"{sword}SwordBladeColor")!.Content?.ToString() ?? "",
        BladeMode = this.FindControl<ComboBox>($"{sword}SwordBladeMode")!.SelectedItem is ComboBoxItem bladeMode
            ? bladeMode.Tag?.ToString() ?? "locked" : "locked",
        TrailColor = this.FindControl<Button>($"{sword}SwordTrailColor")!.Content?.ToString() ?? "",
        TrailMode = this.FindControl<ComboBox>($"{sword}SwordTrailMode")!.SelectedItem is ComboBoxItem trailMode
            ? trailMode.Tag?.ToString() ?? "locked" : "locked"
    };

    private void RandomizerCreate_Click(object? s, RoutedEventArgs e)
    {
        _newSlot = null;
        this.FindControl<TextBox>("CreateNameBox")!.Text = "Randomized Adventure";
        this.FindControl<TextBox>("CreateSeedBox")!.Text = HAL_RandomizerSeedService.NewSeed();
        this.FindControl<CheckBox>("CreateRandomizedCheck")!.IsChecked = true;
        Show("CreatePage");
    }

    private void CreateMode_Changed(object? s, RoutedEventArgs e)
    {
        var randomized = this.FindControl<CheckBox>("CreateRandomizedCheck")!.IsChecked == true;
        var seed = this.FindControl<TextBox>("CreateSeedBox")!;
        if (randomized && string.IsNullOrWhiteSpace(seed.Text)) seed.Text = HAL_RandomizerSeedService.NewSeed();
        seed.IsEnabled = randomized;
        this.FindControl<Button>("CreateSeedButton")!.IsEnabled = randomized;
        var canCreateRandomized = HasDevRandomizerBuild();
        var create = this.FindControl<Button>("CreateActionButton")!;
        create.Content = randomized ? "Create profile and generate seed" : "Create profile";
        create.IsEnabled = !randomized || canCreateRandomized;
        this.FindControl<TextBlock>("CreateNotice")!.Text = randomized
            ? canCreateRandomized
                ? "Full-game chest placement is experimental: full-game reachability is not verified. Creating this profile generates its fixed seed once."
                : "Randomized profile creation needs a Hookshot-Dev build compiled with native randomizer support. Select that build in Settings; vanilla profiles can still be created."
            : "A new isolated save directory will be created. Other profiles are untouched.";
        var name = this.FindControl<TextBox>("CreateNameBox")!;
        if (name.Text is "Link" or "Randomized") name.Text = randomized ? "Randomized" : "Link";
    }

    private bool HasDevRandomizerBuild()
    {
        try { HAL_RandomizerSeedService.EnsureDevRandomizerBuild(_preferences.Value.GameDirectory); return true; }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException)
        { return false; }
    }

    private void CreateSeed_Click(object? s, RoutedEventArgs e) =>
        this.FindControl<TextBox>("CreateSeedBox")!.Text = HAL_RandomizerSeedService.NewSeed();

    private void RandomizerLocations_Refresh(object? s, RoutedEventArgs e) => RefreshRandomizerLocations();

    private void StartingMapsMode_Changed(object? s, SelectionChangedEventArgs e)
    {
        if (_startup) return;
        try
        {
            _preferences.SetRandomizer(ReadRandomizerSettings());
            Status("Maps/compasses preference saved. It applies to newly created randomized adventures.");
        }
        catch (Exception ex) { Status("Could not save maps/compasses preference: " + ex.Message); }
    }


    private int _HAL_locationScanRevision;
    private void AddLocationToggle(Grid row, string id, TextBlock status, bool eligible, bool edit)
    {
        if (!eligible) return;
        bool excluded = _excludedChecks.Contains(id);
        status.Text = excluded ? "Filler only" : "In pool";
        if (!edit) return;
        var toggle = new CheckBox { IsChecked = !excluded, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new Thickness(4, 0) };
        ToolTip.SetTip(toggle, "Allow progression at this check. Uncheck it to limit the shuffled reward to filler.");
        toggle.IsCheckedChanged += (_, _) =>
        {
            if (toggle.IsChecked == true) _excludedChecks.Remove(id); else _excludedChecks.Add(id);
            status.Text = toggle.IsChecked == true ? "In pool" : "Filler only";
            var tree = this.FindControl<TreeView>("RandomizerLocationsTree");
            var summary = this.FindControl<TextBlock>("RandomizerLocationCoverage");
            if (tree is not null && summary?.Text is { } text)
            {
                int count = tree.Items.OfType<TreeViewItem>().SelectMany(group => group.Items.OfType<TreeViewItem>())
                    .Select(item => item.Header).OfType<Grid>().Count(grid => grid.Children.OfType<TextBlock>().LastOrDefault()?.Text is "In pool" or "Filler only");
                summary.Text = System.Text.RegularExpressions.Regex.Replace(text, @"^\d+ checks in pool", $"{count} checks in pool");
            }
        };
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        Grid.SetColumn(toggle, 3); row.Children.Add(toggle);
    }
    private static void AddSkulltulaLocations(TreeView tree, bool shuffle)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "resources", "HALr_skulltula_rewards.json");
        var checks = HAL_SkulltulaRewards.LoadPreview(path);
        var group = new TreeViewItem { Header = $"Skulltula House ({checks.Count})", IsExpanded = false };
        foreach (var check in checks)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("3*,1.5*,Auto"), MinWidth = 590 };
            row.Children.Add(new TextBlock { Text = check.Name, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
            var reward = new TextBlock { Text = check.Reward, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            Grid.SetColumn(reward, 1); row.Children.Add(reward);
            var selected = shuffle && check.Tokens != 100;
            var status = new TextBlock { Text = selected ? "Selected" : "Vanilla", Margin = new Thickness(8,0), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            Grid.SetColumn(status, 2); row.Children.Add(status);
            var item = new TreeViewItem { Header = row, Opacity = selected ? 1 : 0.62 };
            ToolTip.SetTip(item, check.Tokens == 100
                ? "The repeatable 100-token reward remains vanilla."
                : selected ? "Selected for an experimental exchange with a rupee chest; seed creation validates availability."
                : "Enable Gold Skulltula rewards to select this reward for an experimental exchange.");
            group.Items.Add(item);
        }
        tree.Items.Add(group);
    }
    private async void RefreshRandomizerLocations()
    {
        var tree = this.FindControl<TreeView>("RandomizerLocationsTree");
        var summary = this.FindControl<TextBlock>("RandomizerLocationCoverage");
        if (tree is null || summary is null) return;
        const string scope = "full_game";
        bool editChecks = this.FindControl<CheckBox>("EditLocationExclusionsCheck")?.IsChecked == true;
        var skullShuffle = this.FindControl<CheckBox>("SkulltulaRewardsCheck")?.IsChecked == true;
        var skullSummary = skullShuffle ? "Five one-time Skulltula House rewards are selected for experimental exchanges; the repeatable 100-token reward remains vanilla." : "Skulltula House rewards are listed separately and remain vanilla.";
        string romfs = Path.Combine(_preferences.Value.GameDirectory ?? "", "romfs");
        var revision = ++_HAL_locationScanRevision;
        if (Directory.Exists(romfs))
        {
            tree.Items.Clear();
            summary.Text = "Scanning chest locations from the selected game…";
            try
            {
                var heartsEnabled = this.FindControl<CheckBox>("HeartPiecesCheck")?.IsChecked == true;
                var result = await Task.Run(() => HAL_LocationPreview.Scan(romfs, scope, heartsEnabled));
                if (revision != _HAL_locationScanRevision) return;
                summary.Text = $"{result.InPool} checks in pool / {result.Locations.Count} discovered loose chest checks. {skullSummary}";
                foreach (var region in result.Locations.GroupBy(x => x.Region).OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
                {
                    var group = new TreeViewItem { Header = $"{region.Key} ({region.Count()})", IsExpanded = false };
                    foreach (var location in region)
                    {
                        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("3*,1.5*,Auto"), MinWidth = 590 };
                        row.Children.Add(new TextBlock { Text = location.Name, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                        var reward = new TextBlock { Text = location.Reward, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                        Grid.SetColumn(reward, 1); row.Children.Add(reward);
                        var status = new TextBlock { Text = location.Status, Margin = new Thickness(8, 0), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
                        Grid.SetColumn(status, 2); row.Children.Add(status);
                        AddLocationToggle(row, location.Id, status, location.Status == "In pool", editChecks);
                        var item = new TreeViewItem { Header = row, Opacity = location.Status == "In pool" ? 1 : 0.62 };
                        ToolTip.SetTip(item, location.Note); group.Items.Add(item);
                    }
                    tree.Items.Add(group);
                }
                AddSkulltulaLocations(tree, skullShuffle);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
            {
                if (revision != _HAL_locationScanRevision) return;
                tree.Items.Clear(); summary.Text = "Could not scan locations from the selected game: " + ex.Message;
            }
            return;
        }
        tree.Items.Clear();
        summary.Text = "Select an installed game to scan its locations. " + skullSummary;
        AddSkulltulaLocations(tree, skullShuffle);
    }

    private void SaveRandomizer_Click(object? s, RoutedEventArgs e)
    {
        try
        {
            var settings = ReadRandomizerSettings();
            if (!HAL_EnhancementPreferences.CanUse(_preferences.Value.GameDirectory))
                throw new InvalidOperationException("Select Hookshot-Dev to save open-area access settings.");
            this.FindControl<CheckBox>("EnhancementChildStealthCheck")!.IsChecked = settings.ChildStealth;
            _preferences.SetRandomizer(settings);
            var enhancements = HAL_EnhancementPreferences.Load(_preferences.Value.GameDirectory) with
            {
                OpenDekuTree = this.FindControl<CheckBox>("OpenDekuTreeCheck")!.IsChecked == true,
                OpenKokiriForest = this.FindControl<CheckBox>("OpenKokiriForestCheck")!.IsChecked == true,
                OpenDoorOfTime = this.FindControl<CheckBox>("OpenTimeDoorCheck")!.IsChecked == true,
                OpenJabuJabu = this.FindControl<CheckBox>("OpenJabuCheck")!.IsChecked == true,
                SkipTrials = this.FindControl<CheckBox>("SkipTrialsCheck")!.IsChecked == true,
                SkipOpening = this.FindControl<CheckBox>("RandomizerSkipOpeningCheck")!.IsChecked == true,
                SkipFirstOwl = this.FindControl<CheckBox>("RandomizerSkipFirstOwlCheck")!.IsChecked == true,
                FastText = this.FindControl<CheckBox>("RandomizerFastTextCheck")!.IsChecked == true
            };
            HAL_EnhancementPreferences.Save(_preferences.Value.GameDirectory, enhancements);
            Status("Saved randomizer defaults, access settings and time savers. Existing seeds are unchanged.");
        }
        catch (Exception ex) { Status("Could not save randomizer defaults: " + ex.Message); }
    }

    private void Create_Click(object? s, RoutedEventArgs e)
    {
        try
        {
            var name = this.FindControl<TextBox>("CreateNameBox")!.Text ?? "";
            ProfileRecord p;
            if (this.FindControl<CheckBox>("CreateRandomizedCheck")!.IsChecked == true)
            {
                var gameDirectory = _preferences.Value.GameDirectory;
                if (string.IsNullOrWhiteSpace(gameDirectory)) throw new InvalidOperationException("Set and verify the Hookshot game directory before creating a randomized adventure.");
                var seed = HAL_RandomizerSeedService.NormalizeSeed(this.FindControl<TextBox>("CreateSeedBox")!.Text ?? "");
                var settings = _preferences.Value;
                var rando = new HAL_RandomizerSettings("full_game", settings.RandomizerStartingMaps,
                    settings.RandomizerHeartPieces, settings.RandomizerChildStealth, settings.RandomizerStartingSword, settings.RandomizerStartingSlingshot,
                    settings.RandomizerStartingBombBag, settings.RandomizerStartingBoomerang, settings.RandomizerStartingLens, settings.RandomizerStartingShield,
                    settings.RandomizerStartingOcarina, settings.RandomizerStartingSongs, settings.RandomizerStartingConsumables, settings.RandomizerEventRewards, settings.RandomizerMaximumSkulltulas, settings.RandomizerSariaOcarina, settings.RandomizerSkulltulaRewards, settings.RandomizerExcludedChecks);
                p = _profiles.CreateRandomized(name, seed,
                    (profileDirectory, profileId) => HAL_RandomizerSeedService.Generate(gameDirectory, profileDirectory, profileId, seed, rando));
            }
            else p = _profiles.Create(name);
            if (_newSlot.HasValue) _profiles.Pin(p.Id, _newSlot.Value);
            _newSlot = null;
            RefreshProfiles();
            OpenProfile(p);
            Status(p.IsRandomized
                ? $"Created randomized adventure with seed {p.Seed}. The seed is fixed to this profile."
                : $"Created isolated user directory: {p.UserDir}. No existing saves changed.");
        }
        catch (Exception ex) { Status("Could not create adventure: " + ex.Message); }
    }
    private ProfileRecord? SelectedManaged() => this.FindControl<ListBox>("ProfileList")!.SelectedItem as ProfileRecord;
    private async void Rename_Click(object? s, RoutedEventArgs e)
    {
        try
        {
            var p = SelectedManaged() ?? throw new InvalidOperationException("Select a profile first.");
            var name = await new HAL_RenameAdventureDialog(p.Name) { Icon = Icon }.ShowDialog<string?>(this);
            if (name is null) return;
            _profiles.Rename(p.Id, name);
            RefreshProfiles();
            this.FindControl<ListBox>("ProfileList")!.SelectedItem = _profiles.Get(p.Id);
            Status($"Renamed adventure to '{_profiles.Get(p.Id).Name}'.");
        }
        catch (Exception ex) { Status(ex.Message); }
    }
    private void ManagedProfile_Changed(object? s, SelectionChangedEventArgs e)
    {
        var p = SelectedManaged();
        this.FindControl<Button>("DeleteManagedButton")!.IsEnabled = p is { External: false };
        var managedDelete = this.FindControl<Button>("ManagedDeleteButton")!;
        var managedLaunch = this.FindControl<Button>("ManagedLaunchButton")!;
        managedDelete.IsVisible = p is { External: false };
        managedLaunch.IsVisible = p is not null;
        managedLaunch.IsEnabled = p is not null && CanLaunch(p);
        ToolTip.SetTip(managedLaunch, p is null ? "Select an adventure." : CanLaunchReason(p) ?? "Launch this adventure using its isolated save directory.");
        foreach (var name in new[] { "Pin0Button", "Pin1Button", "Pin2Button" })
            this.FindControl<Button>(name)!.IsEnabled = p is not null;
        var title = this.FindControl<TextBlock>("ManagedProfileTitle");
        if (title is null) return;
        title.Text = p?.Name ?? "Adventure Select";
        this.FindControl<TextBlock>("ManagedProfileMode")!.Text = p is null ? "Choose a profile to inspect its save and settings."
            : $"{(p.IsRandomized ? "RANDOMIZED" : "VANILLA")}  ·  {(ProfileRepository.HasProgressSave(p.UserDir) ? "Saved progress" : "No saved progress")}";
        this.FindControl<TextBlock>("ManagedSeed")!.Text = p?.IsRandomized == true ? "Seed: " + p.Seed : "";
        this.FindControl<TextBlock>("ManagedSavePath")!.Text = p is null ? "" : "User directory: " + p.UserDir;
        this.FindControl<TextBlock>("ManagedRandomizerSummary")!.Text = ManagedRandomizerSettings(p);
        _HAL_managedSlots = p is null ? Array.Empty<SaveSummary>() : _saveReader.ReadSlots(p.UserDir);
        var picker = this.FindControl<ComboBox>("HAL_ManagedSlotPicker")!;
        _HAL_updatingSlotPicker = true;
        try
        {
            picker.ItemsSource = _HAL_managedSlots;
            picker.IsEnabled = _HAL_managedSlots.Count > 0;
            SaveSummary? selected = null;
            if (p is not null && _HAL_selectedSaveSlots.TryGetValue(p.Id, out var remembered))
                selected = _HAL_managedSlots.FirstOrDefault(slot => slot.Account == remembered.account && slot.Slot == remembered.slot);
            picker.SelectedItem = selected ?? _HAL_managedSlots.FirstOrDefault(slot => slot.Warning is null) ?? _HAL_managedSlots.FirstOrDefault();
        }
        finally { _HAL_updatingSlotPicker = false; }
        this.FindControl<TextBlock>("ManagedDiskSummary")!.Text = p is null ? "No profile selected." : BuildDiskSummary(p.UserDir);
        HAL_ManagedSlot_Changed(null, null!);
    }
    private void HAL_ManagedSlot_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_HAL_updatingSlotPicker) return;
        var p = SelectedManaged();
        var slot = this.FindControl<ComboBox>("HAL_ManagedSlotPicker")!.SelectedItem as SaveSummary;
        if (p is not null && slot is not null) _HAL_selectedSaveSlots[p.Id] = (slot.Account, slot.Slot);
        this.FindControl<TextBlock>("ManagedSlotInfo")!.Text = ManagedSlotSummary(_HAL_managedSlots, slot) +
            "\nLast launch: " + (string.IsNullOrWhiteSpace(p?.LastPlayed) ? "Never played from this launcher" : p.LastPlayed);
        this.FindControl<TextBlock>("ManagedInventoryInfo")!.Text = ManagedInventorySummary(slot);
        this.FindControl<TextBlock>("ManagedStoneSummary")!.Text = BuildManagedStoneSummary(slot);
        var (scene, age) = ReadableLocation(slot?.Location);
        this.FindControl<TextBlock>("ManagedLocation")!.Text = "Location: " + scene;
        this.FindControl<TextBlock>("ManagedAge")!.Text = "Age: " + age;
        this.FindControl<TextBlock>("ManagedSaveDate")!.Text = slot?.SavedInGame is string inGame ? "Save Date: " + inGame :
            slot is null ? "Save Date: Not yet saved" : "Save Date: Unknown";
        var image = this.FindControl<Image>("ManagedSavePreview")!;
        image.Source = null;
        _managedPreviewBitmap?.Dispose();
        _managedPreviewBitmap = null;
        if (slot?.PreviewJpeg is { Length: > 0 } jpeg)
        {
            try { _managedPreviewBitmap = new Bitmap(new MemoryStream(jpeg, writable: false)); image.Source = _managedPreviewBitmap; }
            catch (Exception ex) when (ex is IOException or ArgumentException or InvalidDataException) { }
        }
        this.FindControl<TextBlock>("ManagedNoPreview")!.IsVisible = image.Source is null;
        this.FindControl<HAL_AdventureSummaryView>("ManagedSummaryView")!.UpdateVisual(slot, _iconCache, _preferences.Value.Theme);
    }
    private static string BuildDiskSummary(string userDir)
    {
        try
        {
            if (!Directory.Exists(userDir) || ProfileRepository.IsLink(userDir)) return "Save directory is unavailable.";
            long bytes = 0; int files = 0, saveFiles = 0; var latest = Directory.GetLastWriteTime(userDir);
            var pending = new Stack<string>(); pending.Push(userDir);
            while (pending.Count > 0)
            {
                var dir = pending.Pop();
                foreach (var entry in Directory.EnumerateFileSystemEntries(dir))
                {
                    if (ProfileRepository.IsLink(entry)) continue;
                    if (Directory.Exists(entry)) pending.Push(entry);
                    else if (File.Exists(entry))
                    {
                        var info = new FileInfo(entry); files++; bytes += info.Length;
                        if (info.LastWriteTime > latest) latest = info.LastWriteTime;
                        var relative = Path.GetRelativePath(userDir, entry);
                        if (relative.Split(Path.DirectorySeparatorChar).Any(part => part.Equals("save", StringComparison.OrdinalIgnoreCase))) saveFiles++;
                    }
                }
            }
            var changed = latest.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            return $"{files} files · {bytes / 1024d:0.#} KiB · {saveFiles} save-associated files\nLast change: {changed}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return "Save data could not be scanned: " + ex.Message; }
    }
    private static string ManagedSlotSummary(IReadOnlyList<SaveSummary> slots, SaveSummary? slot)
    {
        if (slot?.Warning is string warning) return $"{slots.Count} game slot(s) detected · {slot.HAL_SlotLabel}\nCould not read this slot: {warning}";
        var recognized = slots.Count(x => x.Warning is null);
        if (recognized == 0) return slots.Count == 0 ? "No game save slots detected." : "Save slots found, but none could be decoded: " + string.Join("; ", slots.Select(x => x.Warning).Where(x => !string.IsNullOrWhiteSpace(x)));
        return $"{recognized} game slot(s) detected · Save {slot!.Slot + 1} · {slot.Account} · {slot.SavedText}";
    }
    private static string ManagedInventorySummary(SaveSummary? slot)
    {
        if (slot is null) return "No readable save slot.";
        static string Segment(IReadOnlyDictionary<string, int?> values, params (string key, string label)[] names) =>
            string.Join("  ·  ", names.Where(x => values.TryGetValue(x.key, out var v) && v is not null).Select(x => $"{x.label}: {values[x.key]!.Value}"));
        var supplies = Segment(slot.Supplies, ("arrows","Arrows"),("bombs","Bombs"),("bombchus","Bombchus"),("seeds","Seeds"),("nuts","Nuts"),("sticks","Sticks"),("beans","Beans"));
        var capacities = Segment(slot.BagCapacities, ("quiver","Quiver"),("bombs","Bomb bag"),("seeds","Seed bag"),("nuts","Nut bag"),("sticks","Stick bag"));
        var equipment = string.Join("  ·  ", new[] { slot.EquippedSword is null ? null : "Sword: " + slot.EquippedSword, slot.EquippedShield is null ? null : "Shield: " + slot.EquippedShield, slot.Bottles is int bottles ? $"Bottles: {bottles}" : null }.Where(x => x is not null));
        var keys = string.Join("  ·  ", slot.DungeonKeys.Where(x => x.Value is > 0).Select(x => $"{x.Key}: {x.Value} keys"));
        var stones = BuildManagedStoneSummary(slot);
        var rows = new[] { supplies, capacities, equipment, keys, stones }.Where(x => !string.IsNullOrWhiteSpace(x));
        return string.Join("\n", rows.DefaultIfEmpty("Inventory fields are unavailable in this save."));
    }
    private static string BuildManagedStoneSummary(SaveSummary? slot)
    {
        if (slot is null) return "No readable save slot.";
        string State(string role) => slot.Items.FirstOrDefault(x => x.Role == role)?.State switch
        {
            OwnershipState.Owned => "Owned",
            OwnershipState.NotCarried => "Not collected",
            _ => "Unknown"
        };
        return $"Kokiri Emerald: {State("stone_kokiri_emerald")}  ·  Goron Ruby: {State("stone_goron_ruby")}  ·  Zora Sapphire: {State("stone_zora_sapphire")}";
    }
    private static string ManagedRandomizerSettings(ProfileRecord? profile)
    {
        if (profile is null) return "Select an adventure to view its randomizer settings.";
        if (!profile.IsRandomized)
            return profile.IsRandomizerConfigured
                ? "This adventure has randomizer configuration metadata, but no generated seed settings."
                : "Vanilla adventure · no randomizer settings.";

        var profileDirectory = Path.GetDirectoryName(Path.GetFullPath(profile.UserDir))!;
        var seedPath = Path.Combine(profileDirectory, "native-randomizer", "seed.json");
        if (ProfileRepository.IsLink(profile.UserDir) || ProfileRepository.IsLink(profileDirectory) ||
            ProfileRepository.IsLink(Path.GetDirectoryName(seedPath)!) ||
            ProfileRepository.IsLink(seedPath) || !File.Exists(seedPath))
            return $"Seed: {profile.Seed}\nThe saved settings snapshot is missing or unavailable.";

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(seedPath));
            var root = document.RootElement;
            var storedSeed = root.TryGetProperty("seed", out var seedElement) ? seedElement.GetString() : null;
            if (!string.Equals(storedSeed, profile.Seed, StringComparison.OrdinalIgnoreCase))
                return $"Seed: {profile.Seed}\nThe settings snapshot does not match this adventure's seed.";

            var settings = root.GetProperty("settings");
            var scope = settings.TryGetProperty("chest_scope", out var scopeElement)
                ? scopeElement.GetString() switch
                {
                    "childhood" => "Childhood",
                    "full_game" => "Full Game Chests - Experimental",
                    "kokiri_deku" => "Kokiri Forest + Deku Tree",
                    _ => "Unknown"
                }
                : "Kokiri Forest + Deku Tree";
            static string OnOff(JsonElement value, string key) =>
                value.TryGetProperty(key, out var option) && option.ValueKind == JsonValueKind.True ? "On" : "Off";

            var lines = new List<string>
            {
                $"Seed: {profile.Seed}",
                $"Location scope: {scope}",
                $"Chest rewards: {OnOff(settings, "shuffle_chests")}",
                $"Heart Pieces: {OnOff(settings, "shuffle_heart_pieces")}",
                $"Start with dungeon maps and compasses: {OnOff(settings, "start_with_dungeon_maps_compasses")}",
                $"Child Stealth: {OnOff(settings, "child_stealth")}",
                $"NPC and quest rewards: {OnOff(settings, "shuffle_npc_rewards")}",
                "Other item pools: Vanilla (songs, shops and Skulltula rewards)"
            };
            if (settings.TryGetProperty("requested_maximum_skulltulas", out var skullCap) && skullCap.TryGetInt32(out int maximum))
                lines.Add($"Requested maximum Skulltulas: {maximum} (not enforced yet)");
            if (root.TryGetProperty("metadata", out var metadata) && metadata.TryGetProperty("plan", out var plan))
            {
                if (plan.TryGetProperty("assignments", out var assignments) && assignments.ValueKind == JsonValueKind.Array)
                    lines.Add($"Shuffled checks: {assignments.GetArrayLength()}");
                if (plan.TryGetProperty("logic", out var logic) && logic.ValueKind == JsonValueKind.String)
                    lines.Add("Logic status: " + logic.GetString());
            }
            lines.Add("Settings and placements are fixed for this seed.");
            return string.Join('\n', lines);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return $"Seed: {profile.Seed}\nCould not read this seed's settings snapshot: {ex.Message}";
        }
    }
    private async void ManagedImport_Click(object? s, RoutedEventArgs e)
    {
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Import existing save directory", AllowMultiple = false
            });
            if (folders.Count == 0) return;
            var path = folders[0].TryGetLocalPath() ?? throw new InvalidOperationException("Select a local save directory.");
            var profile = _profiles.RegisterExternal(path);
            RefreshProfiles();
            this.FindControl<ListBox>("ProfileList")!.SelectedItem = _profiles.Get(profile.Id);
            Status($"Registered '{profile.Name}' in place. No save files copied or modified.");
        }
        catch (Exception ex) { Status("Could not import saves: " + ex.Message); }
    }
    private void Pin0_Click(object? s, RoutedEventArgs e) => PinSelectedTo(0);
    private void Pin1_Click(object? s, RoutedEventArgs e) => PinSelectedTo(1);
    private void Pin2_Click(object? s, RoutedEventArgs e) => PinSelectedTo(2);
    private void PinSelectedTo(int index)
    {
        try
        {
            var p = SelectedManaged() ?? throw new InvalidOperationException("Select a profile first.");
            _profiles.Pin(p.Id, index);
            RefreshProfiles();
            this.FindControl<ListBox>("ProfileList")!.SelectedItem = _profiles.Get(p.Id);
            Status($"Pinned '{p.Name}' to shortcut {index + 1}. Save directories not moved.");
        }
        catch (Exception ex) { Status(ex.Message); }
    }
    private void OpenSelected_Click(object? s, RoutedEventArgs e)
    {
        if (SelectedManaged() is { } p) OpenProfile(p); else Status("Select an adventure first.");
    }
    private void ManagedLaunch_Click(object? s, RoutedEventArgs e)
    {
        if (SelectedManaged() is not { } p) { Status("Select an adventure first."); return; }
        if (!CanLaunch(p)) { Status(CanLaunchReason(p) ?? "This adventure cannot be launched yet."); return; }
        _current = p;
        Launch_Click(s, e);
    }
    private void DuplicateSelected_Click(object? s, RoutedEventArgs e)
    {
        if (SelectedManaged() is { } p) DuplicateProfile(p); else Status("Select an adventure first.");
    }
    private async void DeleteSelectedSave_Click(object? s, RoutedEventArgs e)
    {
        if (_current is null) return;
        var current = _current;
        var selected = this.FindControl<ComboBox>("HAL_DetailsSlotPicker")!.SelectedItem as SaveSummary;
        var choose = new Window { Title = "Delete", Width = 610, Height = 310, CanResize = false, Icon = Icon, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var choices = new StackPanel { Spacing = 14 };
        var title = new TextBlock { Text = "What would you like to delete?", FontSize = 19, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap };
        choices.Children.Add(title);
        var details = new Grid { ColumnDefinitions = new ColumnDefinitions("170,*"), ColumnSpacing = 14 };
        var previewImage = new Image { Name = "HAL_DeletePreview", Source = this.FindControl<Image>("SavePreview")!.Source, Stretch = Stretch.Uniform };
        var previewContent = new Grid();
        previewContent.Children.Add(previewImage);
        if (previewImage.Source is null) previewContent.Children.Add(new TextBlock { Text = "No save image", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
        var preview = new Border { Width = 170, Height = 105, Padding = new Thickness(6), Child = previewContent };
        preview.Classes.Add("previewShell");
        details.Children.Add(preview);
        var info = new StackPanel { Spacing = 5, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        info.Children.Add(new TextBlock { Text = current.Name, FontWeight = FontWeight.Bold, FontSize = 16, TextTrimming = TextTrimming.CharacterEllipsis });
        var location = new TextBlock { Text = this.FindControl<TextBlock>("DetailsLocation")!.Text, TextWrapping = TextWrapping.Wrap }; location.Classes.Add("subtitle"); info.Children.Add(location);
        var slotLabel = new TextBlock { Text = selected?.HAL_SlotLabel ?? "No save slot selected", TextWrapping = TextWrapping.Wrap }; slotLabel.Classes.Add("subtitle"); info.Children.Add(slotLabel);
        Grid.SetColumn(info, 1); details.Children.Add(info); choices.Children.Add(details);
        var buttons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Spacing = 8 };
        var cancelChoice = new Button { Content = "Cancel" };
        var slotChoice = new Button { Content = selected is null ? "Delete slot" : $"Delete {selected.HAL_SlotLabel}", IsEnabled = selected is not null };
        var adventureChoice = new Button { Content = "Delete adventure" };
        foreach (var button in new[] { cancelChoice, slotChoice, adventureChoice }) { button.Classes.Add("raised"); button.Padding = new Thickness(12, 7); }
        cancelChoice.Click += (_, _) => choose.Close(0); slotChoice.Click += (_, _) => choose.Close(1); adventureChoice.Click += (_, _) => choose.Close(2);
        buttons.Children.Add(cancelChoice); buttons.Children.Add(slotChoice); buttons.Children.Add(adventureChoice); choices.Children.Add(buttons);
        var shell = new Border { Margin = new Thickness(12), Padding = new Thickness(16), Child = choices }; shell.Classes.Add("panel"); choose.Content = shell;
        var selection = await choose.ShowDialog<int>(this);
        if (selection == 0) return;
        if (selection == 1)
        {
            var profile = current;
            var slot = selected;
            if (slot is null) return;
            if (!await HAL_ConfirmSaveAction("Delete save slot", $"Permanently delete {slot.HAL_SlotLabel} from '{profile.Name}'? No backup will be created.", "Delete slot", profile, slot)) return;
            try {
                if (HookshotGame.IsRunning()) throw new InvalidOperationException("Close Hookshot before deleting a save slot.");
                if (Path.GetFileName(slot.Account) != slot.Account || slot.Account is "." or ".." || slot.Slot is < 0 or > 99)
                    throw new InvalidDataException("Invalid save slot.");
                var user = Path.GetFullPath(profile.UserDir);
                var save = Path.Combine(user, "save"); var account = Path.Combine(save, slot.Account); var folder = Path.Combine(account, $"slot_{slot.Slot:00}");
                if (!Directory.Exists(folder) || new[] { user, save, account, folder }.Any(ProfileRepository.IsLink))
                    throw new InvalidDataException("Save slot is missing or linked.");
                foreach (var entry in Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.AllDirectories))
                    if (ProfileRepository.IsLink(entry)) throw new InvalidDataException("Save slot contains a symbolic link.");
                Directory.Delete(folder, true);
                OpenProfile(profile);
                Status("Deleted the selected save slot. No backup was created.");
            } catch (Exception ex) { Status("Could not delete save slot: " + ex.Message); }
            return;
        }
        if (current.External) {
            if (!await HAL_ConfirmSaveAction("Remove external adventure", $"Remove '{current.Name}' from the launcher? Its external save files will remain on disk.", "Remove adventure", current, selected)) return;
            try { _profiles.RemoveExternal(current.Id); _current = null; RefreshProfiles(); Show("AdventurePage"); Status("Removed the external adventure from the launcher. Its save files remain on disk."); }
            catch (Exception ex) { Status("Could not remove adventure: " + ex.Message); }
            return;
        }
        if (!await HAL_ConfirmSaveAction("Delete adventure", $"Permanently delete '{current.Name}' and all its save files? No backup will be created.", "Delete adventure", current, selected)) return;
        try
        {
            _profiles.Delete(current.Id);
            _current = null;
            RefreshProfiles();
            Show("AdventurePage");
            Status("Deleted the selected adventure and its profile folder. No backup was created.");
        }
        catch (Exception ex) { Status("Could not delete adventure: " + ex.Message); }
    }
    private async Task<bool> HAL_ConfirmSaveAction(string title, string message, string action, ProfileRecord profile, SaveSummary? slot)
    {
        var dialog = new Window { Title = title, Width = 610, Height = 320, CanResize = false, Icon = Icon, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var content = new StackPanel { Spacing = 13 };
        content.Children.Add(new TextBlock { Text = title, FontSize = 19, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap });
        var details = new Grid { ColumnDefinitions = new ColumnDefinitions("170,*"), ColumnSpacing = 14 };
        var image = new Image { Name = "HAL_ConfirmSavePreview", Source = this.FindControl<Image>("SavePreview")!.Source, Stretch = Stretch.Uniform };
        var imageArea = new Grid(); imageArea.Children.Add(image);
        if (image.Source is null) imageArea.Children.Add(new TextBlock { Text = "No save image", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
        var preview = new Border { Width = 170, Height = 105, Padding = new Thickness(6), Child = imageArea }; preview.Classes.Add("previewShell"); details.Children.Add(preview);
        var info = new StackPanel { Spacing = 5, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        info.Children.Add(new TextBlock { Text = profile.Name, FontSize = 16, FontWeight = FontWeight.Bold, TextTrimming = TextTrimming.CharacterEllipsis });
        var location = new TextBlock { Text = this.FindControl<TextBlock>("DetailsLocation")!.Text, TextWrapping = TextWrapping.Wrap }; location.Classes.Add("subtitle"); info.Children.Add(location);
        var slotLabel = new TextBlock { Text = slot?.HAL_SlotLabel ?? "No save slot selected", TextWrapping = TextWrapping.Wrap }; slotLabel.Classes.Add("subtitle"); info.Children.Add(slotLabel);
        Grid.SetColumn(info, 1); details.Children.Add(info); content.Children.Add(details);
        content.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        var buttons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Spacing = 8 };
        var cancel = new Button { Content = "Cancel", Padding = new Thickness(12, 7) }; var confirm = new Button { Content = action, Padding = new Thickness(12, 7) };
        cancel.Classes.Add("raised"); confirm.Classes.Add("raised");
        cancel.Click += (_, _) => dialog.Close(false); confirm.Click += (_, _) => dialog.Close(true);
        buttons.Children.Add(cancel); buttons.Children.Add(confirm); content.Children.Add(buttons);
        var shell = new Border { Margin = new Thickness(12), Padding = new Thickness(16), Child = content }; shell.Classes.Add("panel"); dialog.Content = shell;
        return await dialog.ShowDialog<bool>(this);
    }
    private async void DeleteManaged_Click(object? s, RoutedEventArgs e)
    {
        var p = SelectedManaged();
        if (p is null || p.External) { Status("Only isolated managed profiles can be deleted."); return; }
        if (!await ConfirmDelete(p.Name)) return;
        try
        {
            _profiles.Delete(p.Id);
            RefreshProfiles();
            Status("Deleted the selected adventure and its profile folder. No backup was created.");
        }
        catch (Exception ex) { Status("Could not delete adventure: " + ex.Message); }
    }
    private async Task<bool> ConfirmDelete(string name)
    {
        var dialog = new Window { Title = "Delete adventure", Width = 440, Height = 185,
            CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var content = new StackPanel { Margin = new Avalonia.Thickness(18), Spacing = 14 };
        content.Children.Add(new TextBlock { Text = $"Permanently delete '{name}' and its save files? No backup will be created.", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        var actions = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Spacing = 8 };
        var cancel = new Button { Content = "Cancel" };
        var remove = new Button { Content = "Delete Save" };
        cancel.Click += (_, _) => dialog.Close(false);
        remove.Click += (_, _) => dialog.Close(true);
        actions.Children.Add(cancel); actions.Children.Add(remove); content.Children.Add(actions); dialog.Content = content;
        return await dialog.ShowDialog<bool>(this);
    }
    private void DuplicateCurrent_Click(object? s, RoutedEventArgs e)
    {
        if (_current is not null) DuplicateProfile(_current);
    }
    private void DuplicateProfile(ProfileRecord source)
    {
        try
        {
            var suffix = " Copy";
            var name = source.Name.Length + suffix.Length <= 80 ? source.Name + suffix : source.Name[..(80 - suffix.Length)] + suffix;
            var copy = _profiles.Duplicate(source.Id, name);
            RefreshProfiles();
            OpenProfile(copy);
            Status($"Created isolated copy '{copy.Name}'. The original save directory was left unchanged.");
        }
        catch (Exception ex) { Status("Could not copy adventure: " + ex.Message); }
    }

    private void BackupSelected_Click(object? s, RoutedEventArgs e)
    {
        var profile = SelectedManaged() ?? _current;
        if (profile is null) { Status("Select an adventure before requesting a backup."); return; }
        try
        {
            var source = Path.GetFullPath(profile.UserDir);
            if (!Directory.Exists(source) || ProfileRepository.IsLink(source))
                throw new InvalidOperationException("The selected adventure save directory is missing or linked.");
            foreach (var entry in Directory.EnumerateFileSystemEntries(source, "*", SearchOption.AllDirectories))
                if (ProfileRepository.IsLink(entry)) throw new InvalidDataException("Backup stopped because the save directory contains a symbolic link.");
            var backupRoot = Path.Combine(_profiles.Root, "backups");
            if (ProfileRepository.IsLink(_profiles.Root) || ProfileRepository.IsLink(backupRoot))
                throw new InvalidDataException("Backup folder cannot be a symbolic link.");
            Directory.CreateDirectory(backupRoot);
            var archive = Path.Combine(backupRoot, $"{profile.Id}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.zip");
            ZipFile.CreateFromDirectory(source, archive, CompressionLevel.Optimal, includeBaseDirectory: false);
            Status("Created requested compressed save backup: " + archive);
        }
        catch (Exception ex) { Status("Could not create save backup: " + ex.Message); }
    }

    private void RecoverUnlisted_Click(object? s, RoutedEventArgs e)
    {
        try
        {
            var recovered = _profiles.RecoverUnlisted();
            RefreshProfiles();
            Status(recovered.Count == 0
                ? "No unlisted isolated save profiles were found."
                : $"Recovered {recovered.Count} profile record(s). Save files were left untouched.");
        }
        catch (Exception ex) { Status("Could not recover profile records: " + ex.Message); }
    }

    private async void RestoreBackup_Click(object? s, RoutedEventArgs e)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Restore compressed save backup", AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("Compressed backups") { Patterns = new[] { "*.zip" } } }
            });
            if (files.Count == 0) return;
            var restored = _profiles.RestoreSaveBackup(files[0].Path.LocalPath, legacyFolder: false);
            RefreshProfiles();
            Status($"Restored '{restored.Name}' as a separate isolated adventure. Existing saves were left unchanged.");
        }
        catch (Exception ex) { Status("Could not restore compressed backup: " + ex.Message); }
    }

    private async void RestoreLegacy_Click(object? s, RoutedEventArgs e)
    {
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Restore legacy save folder", AllowMultiple = false });
            if (folders.Count == 0) return;
            var restored = _profiles.RestoreSaveBackup(folders[0].Path.LocalPath, legacyFolder: true);
            RefreshProfiles();
            Status($"Imported '{restored.Name}' as a separate isolated adventure. The legacy folder was left unchanged.");
        }
        catch (Exception ex) { Status("Could not restore legacy folder: " + ex.Message); }
    }

    private void RefreshWarnings()
    {
        var gameDir = this.FindControl<TextBox>("GameDirectoryBox")?.Text ?? "";
        var marker = Path.Combine(gameDir, "hookshot_randomizer_capability.txt");
        var capable = false;
        try { capable = File.Exists(marker) && !ProfileRepository.IsLink(marker) && File.ReadAllText(marker).Trim() == "HOOKSHOT_DEV_RANDOMIZER=1"; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        var title = this.FindControl<TextBlock>("WarningTitle");
        var details = this.FindControl<TextBlock>("WarningDetails");
        var resolution = this.FindControl<TextBlock>("WarningResolution");
        if (title is null || details is null || resolution is null) return;
        if (!capable)
        {
            title.Text = "Warning · Native randomizer build required";
            details.Text = "The selected Hookshot-Dev installation does not have the native randomizer capability marker.";
            resolution.Text = "Open Settings → Tools → Dev Build Setup, select your local game source, and build/update Hookshot-Dev with native randomizer support.";
        }
        else
        {
            title.Text = "Ready · Native randomizer build detected";
            details.Text = "The selected development installation reports HOOKSHOT_DEV_RANDOMIZER=1.";
            resolution.Text = "No action is needed.";
        }
    }

    private void RetryChecks_Click(object? s, RoutedEventArgs e)
    {
        RefreshWarnings();
        RefreshReadiness();
        Status("Installation and native randomizer checks completed.");
    }

    private async void ReviewInstall_Click(object? s, RoutedEventArgs e)
    {
        await HAL_OpenStockInstall(force: false);
    }

    private async void BeginReinstall_Click(object? s, RoutedEventArgs e)
    {
        await HAL_OpenStockInstall(force: true);
    }

    private async Task HAL_OpenStockInstall(bool force)
    {
        try
        {
            var info = HAL_StockInstallDialog.HAL_CreateBuiltinStartInfo(FindSourceRoot());
            using var process = System.Diagnostics.Process.Start(info) ?? throw new InvalidOperationException("The Hookshot Builder did not start.");
            this.FindControl<TextBlock>("InstallReviewStatus")!.Text = "Hookshot Builder is open. Choose the installation and build options there.";
            await process.WaitForExitAsync();
            UpdateLaunchBuildStatus();
            RefreshWarnings();
            RefreshReadiness();
            this.FindControl<TextBlock>("InstallReviewStatus")!.Text = "Hookshot Builder closed. Check its result and selected installation folder.";
        }
        catch (Exception ex) { this.FindControl<TextBlock>("InstallReviewStatus")!.Text = "Could not open stock installation setup: " + ex.Message; }
    }

    private void ReviewDevBuild_Click(object? s, RoutedEventArgs e)
    {
        try
        {
            var plan = HAL_CreateDevBuildPlan();
            this.FindControl<TextBlock>("DevBuildStatus")!.Text = plan.UpdateExisting
                ? "Ready to update Hookshot-Dev from its retained game files."
                : HAL_DevelopmentBuild.HasRetainedGameFiles(this.FindControl<TextBox>("DevSourceBox")!.Text ?? "")
                    ? "Ready to build a separate copy from your existing Hookshot installation."
                    : "Ready to build from the selected game source and install the completed development build.";
        }
        catch (Exception ex) { this.FindControl<TextBlock>("DevBuildStatus")!.Text = "Setup needs attention: " + ex.Message; }
    }

    private HAL_DevelopmentBuild HAL_CreateDevBuildPlan()
    {
        var output = HAL_DevelopmentBuild.ValidateOutputDirectory(this.FindControl<TextBox>("DevOutputBox")!.Text ?? "");
        var workersText = this.FindControl<TextBox>("DevShaderWorkersBox")!.Text?.Trim();
        int? shaderWorkers = null;
        if (!string.IsNullOrEmpty(workersText))
        {
            if (!int.TryParse(workersText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsed) || parsed is < 1 or > 32)
                throw new InvalidOperationException("Shader preparation workers must be a whole number from 1 to 32, or left blank for the game default.");
            shaderWorkers = parsed;
        }
        _preferences.SetDevShaderWorkers(shaderWorkers);
        var plan = HAL_DevelopmentBuild.Create(FindSourceRoot(), this.FindControl<TextBox>("DevSourceBox")!.Text ?? "", output,
            this.FindControl<CheckBox>("HAL_ForceDevReinstallCheck")!.IsChecked == true,
            this.FindControl<CheckBox>("PreparePipelinesCheck")!.IsChecked == true,
            this.FindControl<CheckBox>("RetainCompilerObjectsCheck")!.IsChecked == true, shaderWorkers);
        _preferences.SetDevGameDirectory(output);
        return plan;
    }

    private int DevShaderWorkersMax => Math.Clamp(Environment.ProcessorCount - 2, 1, 32);

    private void DevShaderWorkersMax_Changed(object? s, RoutedEventArgs e)
    {
        if (_syncingShaderWorkerControls) return;
        _syncingShaderWorkerControls = true;
        try
        {
            this.FindControl<TextBox>("DevShaderWorkersBox")!.Text =
                this.FindControl<CheckBox>("DevShaderWorkersMaxCheck")!.IsChecked == true
                    ? DevShaderWorkersMax.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
        }
        finally { _syncingShaderWorkerControls = false; }
    }

    private void DevShaderWorkers_TextChanged(object? s, TextChangedEventArgs e)
    {
        if (_syncingShaderWorkerControls) return;
        var text = this.FindControl<TextBox>("DevShaderWorkersBox")!.Text?.Trim();
        var isMax = int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture,
            out var workers) && workers == DevShaderWorkersMax;
        _syncingShaderWorkerControls = true;
        try { this.FindControl<CheckBox>("DevShaderWorkersMaxCheck")!.IsChecked = isMax; }
        finally { _syncingShaderWorkerControls = false; }
    }

    private async void BrowseDevSourceFile_Click(object? s, RoutedEventArgs e)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Choose local Hookshot game source", AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("Hookshot game source") { Patterns = new[] { "*.nup" } } }
            });
            if (files.Count > 0) this.FindControl<TextBox>("DevSourceBox")!.Text = files[0].Path.LocalPath;
        }
        catch (Exception ex) { this.FindControl<TextBlock>("DevBuildStatus")!.Text = "Could not browse for game source: " + ex.Message; }
    }

    private async void BrowseDevSourceFolder_Click(object? s, RoutedEventArgs e)
    {
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                { Title = "Choose an existing Hookshot installation or extracted game source", AllowMultiple = false });
            if (folders.Count > 0) this.FindControl<TextBox>("DevSourceBox")!.Text = folders[0].Path.LocalPath;
        }
        catch (Exception ex) { this.FindControl<TextBlock>("DevBuildStatus")!.Text = "Could not browse for game source folder: " + ex.Message; }
    }

    private async void BrowseDevOutput_Click(object? s, RoutedEventArgs e)
    {
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                { Title = "Choose Hookshot-Dev installation directory", AllowMultiple = false });
            if (folders.Count == 0) return;
            var selected = folders[0].Path.LocalPath;
            this.FindControl<TextBox>("DevOutputBox")!.Text = HAL_DevelopmentBuild.ValidateOutputDirectory(selected);
        }
        catch (Exception ex) { this.FindControl<TextBlock>("DevBuildStatus")!.Text = "Could not browse for development install folder: " + ex.Message; }
    }

    private async void BuildDev_Click(object? s, RoutedEventArgs e) => await HAL_OpenDevInstaller();

    private async Task HAL_OpenDevInstaller()
    {
        try
        {
            string sourceRoot;
            try { sourceRoot = FindSourceRoot(); }
            catch (InvalidOperationException) { sourceRoot = AppContext.BaseDirectory; }
            var dialog = new HAL_DevelopmentBuildDialog(sourceRoot, _preferences,
                this.FindControl<TextBox>("DevSourceBox")!.Text ?? "",
                this.FindControl<TextBox>("DevOutputBox")!.Text ?? _preferences.Value.DevGameDirectory,
                this.FindControl<CheckBox>("HAL_ForceDevReinstallCheck")!.IsChecked == true,
                this.FindControl<CheckBox>("PreparePipelinesCheck")!.IsChecked == true,
                this.FindControl<CheckBox>("RetainCompilerObjectsCheck")!.IsChecked == true,
                _preferences.Value.DevShaderWorkers) { Icon = Icon };
            dialog.Screen.FindControl<TextBox>("InstallShaderWorkersBox")!.Text = this.FindControl<TextBox>("DevShaderWorkersBox")!.Text;
            await dialog.ShowDialog(this);
            this.FindControl<TextBlock>("DevBuildStatus")!.Text = dialog.ResultText;
            this.FindControl<TextBox>("DevSourceBox")!.Text = dialog.Screen.SourcePath;
            this.FindControl<TextBox>("DevOutputBox")!.Text = dialog.OutputDirectory;
            this.FindControl<TextBox>("DevShaderWorkersBox")!.Text = dialog.Screen.FindControl<TextBox>("InstallShaderWorkersBox")!.Text;
            if (dialog.Completed)
            {
                this.FindControl<TextBox>("GameDirectoryBox")!.Text = _preferences.Value.GameDirectory;
                this.FindControl<RadioButton>("LaunchDevRadio")!.IsChecked = true;
                this.FindControl<RadioButton>("LaunchStableRadio")!.IsChecked = false;
                UpdateLaunchBuildStatus();
                await HAL_BuildIcons(onlyIfMissing: true);
                RefreshProfiles();
            }
            RefreshWarnings();
        }
        catch (Exception ex) { this.FindControl<TextBlock>("DevBuildStatus")!.Text = "Could not open Hookshot-Dev installation: " + ex.Message; }
    }

    private static string FindSourceRoot()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            var current = new DirectoryInfo(Path.GetFullPath(start));
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "nx2", "make_game.py"))) return current.FullName;
                current = current.Parent;
            }
        }
        throw new InvalidOperationException("The Hookshot source checkout containing nx2/make_game.py was not found.");
    }

    private void OpenBuildConfiguration_Click(object? s, RoutedEventArgs e)
    {
        this.FindControl<TabControl>("SettingsSections")!.SelectedIndex = 3;
        this.FindControl<TabControl>("SettingsToolsSections")!.SelectedItem = this.FindControl<TabItem>("HAL_DevBuildTab");
        this.FindControl<TextBlock>("DevBuildStatus")!.Text = "Choose a local game source and Hookshot-Dev output, then review before installing or updating.";
        this.FindControl<TextBox>("DevSourceBox")!.Focus();
    }
    private void OpenProfile(ProfileRecord p)
    {
        _current = p;
        var delete = this.FindControl<Button>("DeleteSelectedSaveButton")!;
        delete.IsVisible = true;
        delete.Content = "Delete…";
        ToolTip.SetTip(delete, "Choose whether to delete the selected save slot or the adventure.");
        this.FindControl<TextBlock>("DetailsTitle")!.Text = p.Name;
        HAL_SetSaveBadge(this.FindControl<Border>("DetailsBadge")!, this.FindControl<TextBlock>("DetailsType")!, p);
        this.FindControl<TextBlock>("DetailsSeed")!.Text = p.IsRandomized ? $"Seed: {p.Seed}" : p.IsRandomizerConfigured ? "This imported configuration has no generated seed." : "";
        this.FindControl<TextBlock>("DetailsPath")!.Text = "User directory: " + p.UserDir;
        _currentSlots = _saveReader.ReadSlots(p.UserDir);
        var picker = this.FindControl<ComboBox>("HAL_DetailsSlotPicker")!;
        _HAL_updatingDetailsSlotPicker = true;
        try
        {
            picker.ItemsSource = _currentSlots;
            picker.IsEnabled = _currentSlots.Count > 0;
            SaveSummary? selected = null;
            if (_HAL_selectedSaveSlots.TryGetValue(p.Id, out var remembered))
                selected = _currentSlots.FirstOrDefault(slot => slot.Account == remembered.account && slot.Slot == remembered.slot);
            picker.SelectedItem = selected ?? _currentSlots.FirstOrDefault(slot => slot.Warning is null) ?? _currentSlots.FirstOrDefault();
        }
        finally { _HAL_updatingDetailsSlotPicker = false; }
        HAL_DetailsSlot_Changed(null, null!);
        var launchButton = this.FindControl<Button>("LaunchButton")!;
        launchButton.IsEnabled = CanLaunch(p);
        ToolTip.SetTip(launchButton, CanLaunchReason(p) ?? "Launch this adventure using its isolated save directory.");
        Show("DetailsPage");
    }
    private void HAL_DetailsSlot_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_HAL_updatingDetailsSlotPicker) return;
        var slot = this.FindControl<ComboBox>("HAL_DetailsSlotPicker")!.SelectedItem as SaveSummary;
        this.FindControl<Button>("DeleteSelectedSaveButton")!.IsEnabled = _current is not null;
        if (_current is not null && slot is not null) _HAL_selectedSaveSlots[_current.Id] = (slot.Account, slot.Slot);
        this.FindControl<Button>("EditSaveButton")!.IsEnabled = slot is { Warning: null };
        DisplaySlot(slot);
    }
    private async void EditSave_Click(object? sender, RoutedEventArgs e)
    {
        if (_HAL_editor is not null) return;
        var slot = this.FindControl<ComboBox>("HAL_DetailsSlotPicker")!.SelectedItem as SaveSummary;
        if (_current is null || slot is null || slot.Warning is not null) return;
        try
        {
            var status = this.FindControl<TextBlock>("HAL_EditStatus")!;
            var selector = this.FindControl<ContentControl>("HAL_EditSelector")!;
            var save = this.FindControl<Button>("HAL_SaveEditsButton")!;
            _HAL_editor = new HAL_SaveEditorSession(FindSourceRoot(), _preferences.Value.GameDirectory, _current.UserDir, slot,
                _iconCache, _preferences.Value.Theme, this.FindControl<HAL_AdventureSummaryView>("SummaryView")!, status, save, selector,
                this.FindControl<ComboBox>("HAL_EditAge")!);
            HAL_SetEditing(true);
            await _HAL_editor.Load();
        }
        catch (Exception ex) { Status("Could not open save editor: " + ex.Message); }
    }
    private void HAL_SetEditing(bool editing)
    {
        this.FindControl<Button>("EditSaveButton")!.IsVisible = !editing;
        this.FindControl<Button>("HAL_SaveEditsButton")!.IsVisible = editing;
        this.FindControl<Button>("HAL_CancelEditsButton")!.IsVisible = editing;
        this.FindControl<TextBlock>("HAL_EditStatus")!.IsVisible = editing;
        this.FindControl<TextBlock>("DetailsAge")!.IsVisible = !editing;
        this.FindControl<StackPanel>("HAL_EditAgePanel")!.IsVisible = editing;
        this.FindControl<ContentControl>("HAL_EditSelector")!.IsVisible = editing;
        this.FindControl<ComboBox>("HAL_DetailsSlotPicker")!.IsEnabled = !editing && _currentSlots.Count > 0;
        this.FindControl<Button>("HAL_DetailsBackButton")!.IsEnabled = !editing;
        this.FindControl<Button>("SettingsButton")!.IsEnabled = !editing;
        this.FindControl<Button>("LaunchButton")!.IsEnabled = !editing && _current is not null && CanLaunch(_current);
        this.FindControl<Button>("DeleteSelectedSaveButton")!.IsEnabled = !editing && _current is not null;
    }
    private void HAL_EndEditing()
    {
        _HAL_editor?.Dispose(); _HAL_editor = null;
        HAL_SetEditing(false);
        if (_current is not null) OpenProfile(_current);
    }
    private void HAL_CancelEdits_Click(object? sender, RoutedEventArgs e)
    {
        if (_HAL_editor?.IsBusy == true) { this.FindControl<TextBlock>("HAL_EditStatus")!.Text = "Please wait for the current edit to finish."; return; }
        HAL_EndEditing();
    }
    private async void HAL_SaveEdits_Click(object? sender, RoutedEventArgs e)
    {
        if (_HAL_editor is not null && await _HAL_editor.Save())
        {
            var result = this.FindControl<TextBlock>("HAL_EditStatus")!.Text;
            HAL_EndEditing();
            Status(result ?? "Save changes completed.");
        }
    }
    private bool CanLaunch(ProfileRecord p)
    {
        return CanLaunchReason(p) is null;
    }
    private string? CanLaunchReason(ProfileRecord p)
    {
        if (p.IsRandomizerConfigured || (p.Kind == "randomizer" && !p.IsRandomized))
            return "Generate a seed when creating this randomized adventure before launching.";
        try
        {
            _ = HookshotGame.CreatePlan(_preferences.Value.GameDirectory, p.UserDir);
            if (p.IsRandomized) HAL_RandomizerSeedService.EnsureDevRandomizerBuild(_preferences.Value.GameDirectory);
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }
    private void DisplaySlot(SaveSummary? slot)
    {
        var img = this.FindControl<Image>("SavePreview")!;
        img.Source = null;
        _previewBitmap?.Dispose();
        _previewBitmap = null;
        if (slot?.PreviewJpeg is { Length: > 0 } jpeg)
        {
            try { _previewBitmap = new Bitmap(new MemoryStream(jpeg, writable: false)); img.Source = _previewBitmap; }
            catch (Exception ex) when (ex is IOException or ArgumentException or InvalidDataException)
            { Status("Could not display caption preview; save remains read-only."); }
        }
        this.FindControl<TextBlock>("NoPreviewText")!.IsVisible = img.Source is null;
        var (scene, age) = ReadableLocation(slot?.Location);
        this.FindControl<TextBlock>("DetailsLocation")!.Text = "Location: " + scene;
        this.FindControl<TextBlock>("DetailsAge")!.Text = "Age: " + age;
        this.FindControl<TextBlock>("DetailsTime")!.Text = slot?.SavedInGame is string inGame
            ? "Save Date: " + inGame
            : slot is null ? "Save Date: Not yet saved" : "Save Date: Unknown";
        if (slot is not null)
            ToolTip.SetTip(this.FindControl<TextBlock>("DetailsTime")!,
                "Filesystem last modified: " + slot.Modified.ToString("yyyy-MM-dd HH:mm"));
        this.FindControl<TextBlock>("SaveWarning")!.Text = slot?.Warning ?? "";
        this.FindControl<HAL_AdventureSummaryView>("SummaryView")!.UpdateVisual(slot, _iconCache, _preferences.Value.Theme);
    }
    private void GameDirectory_Click(object? s, RoutedEventArgs e)
    {
        try
        {
            _preferences.SetGameDirectory(this.FindControl<TextBox>("GameDirectoryBox")!.Text ?? "");
            this.FindControl<RadioButton>("LaunchDevRadio")!.IsChecked = _preferences.Value.LaunchBuild == "dev";
            this.FindControl<RadioButton>("LaunchStableRadio")!.IsChecked = _preferences.Value.LaunchBuild == "stable";
            UpdateLaunchBuildStatus();
            LoadEnhancementSettings();
            RefreshReadiness();
            if (_current is not null) this.FindControl<Button>("LaunchButton")!.IsEnabled = CanLaunch(_current);
            Status("Verified Hookshot game directory. Existing game files and saves unchanged.");
        }
        catch (Exception ex) { Status("Could not configure game directory: " + ex.Message); }
    }

    private void UseDevDirectory_Click(object? s, RoutedEventArgs e)
    {
        try
        {
            _preferences.SetLaunchBuild("dev");
            this.FindControl<TextBox>("GameDirectoryBox")!.Text = _preferences.Value.GameDirectory;
            this.FindControl<RadioButton>("LaunchDevRadio")!.IsChecked = true;
            this.FindControl<RadioButton>("LaunchStableRadio")!.IsChecked = false;
            UpdateLaunchBuildStatus();
            LoadEnhancementSettings();
            RefreshReadiness();
            if (_current is not null) this.FindControl<Button>("LaunchButton")!.IsEnabled = CanLaunch(_current);
            Status("Hookshot-Dev selected. The stable installation was not changed.");
        }
        catch (Exception ex) { Status("Could not select Hookshot-Dev: " + ex.Message); }
    }

    private void LaunchBuild_Changed(object? s, RoutedEventArgs e)
    {
        if (_startup || s is not RadioButton { IsChecked: true } radio || radio.Tag?.ToString() is not { } build) return;
        try
        {
            _preferences.SetLaunchBuild(build);
            this.FindControl<TextBox>("GameDirectoryBox")!.Text = _preferences.Value.GameDirectory;
            UpdateLaunchBuildStatus();
            LoadEnhancementSettings();
            RefreshReadiness();
            if (_current is not null) this.FindControl<Button>("LaunchButton")!.IsEnabled = CanLaunch(_current);
            Status($"{(build == "stable" ? "Hookshot" : "Hookshot-Dev")} selected for game launch.");
        }
        catch (Exception ex) { Status("Could not select launch build: " + ex.Message); }
    }

    private void UpdateLaunchBuildStatus()
    {
        var directory = _preferences.Value.GameDirectory;
        var executable = HookshotGame.ResolveExecutable(directory);
        var ready = Directory.Exists(directory) && File.Exists(executable) &&
            File.Exists(Path.Combine(directory, "image.bin")) && Directory.Exists(Path.Combine(directory, "romfs"));
        this.FindControl<TextBlock>("LaunchDirectoryStatus")!.Text =
            $"{(_preferences.Value.LaunchBuild == "stable" ? "Hookshot" : _preferences.Value.LaunchBuild == "dev" ? "Hookshot-Dev" : "Custom install")}: {directory} · {(ready ? "ready" : "not installed or incomplete")}";
    }

    private void VideoDebug_Changed(object? s, RoutedEventArgs e)
    {
        if (_startup) return;
        try
        {
            var enabled = this.FindControl<CheckBox>("VideoDebugCheck")!.IsChecked == true;
            _preferences.SetVideoDebug(enabled);
            Status(enabled ? "Video Debug enabled for the next game launch." : "Video Debug disabled for the next game launch.");
        }
        catch (Exception ex) { Status("Could not save Video Debug setting: " + ex.Message); }
    }

    private void HosDebug_Changed(object? s, RoutedEventArgs e)
    {
        if (_startup) return;
        try
        {
            var enabled = this.FindControl<CheckBox>("HosDebugCheck")!.IsChecked == true;
            _preferences.SetHosDebug(enabled);
            Status(enabled ? "HOS Debug enabled for the next game launch." : "HOS Debug disabled for the next game launch.");
        }
        catch (Exception ex) { Status("Could not save HOS Debug setting: " + ex.Message); }
    }

    private void A64Debug_Changed(object? s, RoutedEventArgs e)
    {
        if (_startup) return;
        try
        {
            var enabled = this.FindControl<CheckBox>("A64DebugCheck")!.IsChecked == true;
            _preferences.SetA64Debug(enabled);
            Status(enabled ? "A64 Debug enabled for the next game launch." : "A64 Debug disabled for the next game launch.");
        }
        catch (Exception ex) { Status("Could not save A64 Debug setting: " + ex.Message); }
    }

    private void DebugKey_Changed(object? s, RoutedEventArgs e)
    {
        if (_startup) return;
        try
        {
            var enabled = this.FindControl<CheckBox>("DebugKeyCheck")!.IsChecked == true;
            _preferences.SetDebugKey(enabled);
            Status(enabled ? "Debug Key enabled: F9 will toggle a gameplay state trace on the next game launch." : "Debug Key disabled.");
        }
        catch (Exception ex) { Status("Could not save Debug Key setting: " + ex.Message); }
    }

    private bool _launching;
    private async void Launch_Click(object? s, RoutedEventArgs e)
    {
        if (_current is null || _launching) return;
        _launching = true;
        try
        {
            var plan = HookshotGame.CreatePlan(_preferences.Value.GameDirectory, _current.UserDir);
            var profileDirectory = Path.GetDirectoryName(_current.UserDir)!;
            IReadOnlyDictionary<string, string>? randomizerEnvironment = _current.IsRandomized
                ? HAL_RandomizerSeedService.PrepareLaunchEnvironment(_preferences.Value.GameDirectory, profileDirectory, _current.Id)
                : _current.External ? null : HAL_DevRuntimeService.PrepareLaunchEnvironment(_preferences.Value.GameDirectory, _current.Id, null);
            if (_current.External && !_current.IsRandomized && HAL_EnhancementPreferences.CanUse(_preferences.Value.GameDirectory) &&
                HAL_EnhancementPreferences.Load(_preferences.Value.GameDirectory).FastText)
                randomizerEnvironment = new Dictionary<string, string>(StringComparer.Ordinal) { ["HOS_TEXT_SPEED"] = "3" };
            if (!_current.External)
                randomizerEnvironment = HAL_TunicCosmeticsService.PrepareLaunchEnvironment(
                    _preferences.Value.GameDirectory, profileDirectory, _preferences.Value.TunicCosmetics, randomizerEnvironment);
            var name = _current.Name;
            var flags = (_preferences.Value.VideoDebug, _preferences.Value.HosDebug, _preferences.Value.A64Debug, _preferences.Value.DebugKey);
            Status("Checking Vulkan driver readiness…");
            var driverError = await HAL_VulkanReadiness.CheckAsync();
            if (driverError is not null) throw new InvalidOperationException(driverError);
            var startInfo = plan.ToStartInfo(randomizerEnvironment, flags.VideoDebug, flags.HosDebug, flags.A64Debug, flags.DebugKey);
            startInfo.RedirectStandardError = true;
            var process = Process.Start(startInfo) ?? throw new InvalidOperationException("The operating system did not start Hookshot.");
            Console.WriteLine(flags.DebugKey ? "Hookshot F9 gameplay trace requested; press F9 in-game to start recording." : "Hookshot F9 gameplay trace is disabled.");
            Status($"Started Hookshot using adventure '{name}' (PID {process.Id}). " +
                (flags.DebugKey ? "Press F9 in the game to start a gameplay trace." : "F9 gameplay tracing is disabled in Settings."));
            _ = HAL_GpuLaunchMonitor.WatchAsync(process,
                Path.Combine(plan.WorkingDirectory, "logs", "hookshot-launcher.log"),
                warning => Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
                {
                    Status(warning);
                    var close = new Button { Content = "OK", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
                    var dialog = new Window { Title = "Hookshot GPU warning", Width = 540, SizeToContent = SizeToContent.Height,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner,
                        Content = new StackPanel { Margin = new Thickness(20), Spacing = 16,
                            Children = { new TextBlock { Text = warning, TextWrapping = TextWrapping.Wrap }, close } } };
                    close.Click += (_, _) => dialog.Close();
                    await dialog.ShowDialog(this);
                }),
                trace => Avalonia.Threading.Dispatcher.UIThread.Post(() => Status(trace)));
        }
        catch (Exception ex) { Status("Could not start Hookshot: " + ex.Message); }
        finally { _launching = false; }
    }

    private void ThemePicker_Changed(object? s, SelectionChangedEventArgs e)
    {
        if (_startup) return;
        if (this.FindControl<ComboBox>("ThemePicker")?.SelectedItem is not ThemePalette p) return;
        try
        {
            _preferences.SetTheme(p.Id);
            ThemeApplicator.Apply(p.Id);
            for (int i = 0; i < 3; i++) _compactViews[i].UpdateVisual(_shortcutSummaries[i], _iconCache, p.Id);
            if (_current is not null) DisplaySlot(this.FindControl<ComboBox>("HAL_DetailsSlotPicker")!.SelectedItem as SaveSummary);
            Status("Applied native Avalonia theme: " + p.Label);
        }
        catch (Exception ex) { Status("Could not apply theme: " + ex.Message); }
    }
    protected override void OnClosed(EventArgs e)
    {
        _previewBitmap?.Dispose();
        foreach (var bitmap in _shortcutBitmaps) bitmap?.Dispose();
        _iconCache.Dispose();
        base.OnClosed(e);
    }
}
