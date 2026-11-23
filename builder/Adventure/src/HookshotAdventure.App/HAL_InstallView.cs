using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using HookshotAdventure.Core;

namespace HookshotAdventure.App;

// The PyQt installer's three steps, sharing the current stock/development build services.
public sealed class HAL_InstallView : UserControl
{
    private readonly Window _owner;
    private readonly string _root;
    private readonly bool _development;
    private readonly PreferenceStore? _preferences;
    private readonly ComboBox _format = new() { SelectedIndex = 0 };
    private readonly TextBox _source = new() { Watermark = "Choose your local .nup file" };
    private readonly TextBox _output = new();
    private readonly ComboBox _language = new() { ItemsSource = new[] { "English (US)", "English (UK)", "Japanese" }, SelectedIndex = 0 };
    private readonly TextBox _jobs = new() { Watermark = "Automatic", Width = 100 };
    private readonly TextBox _shaders = new() { Watermark = "Automatic", Width = 100 };
    private readonly CheckBox _prepare = new() { Content = "Prepare shaders and pipelines" };
    private readonly CheckBox _retain = new() { Content = "Retain compiler objects for faster updates" };
    private readonly CheckBox _reinstall = new() { Content = "Reinstall from game source" };
    private readonly TextBlock _sourceStatus = new() { TextWrapping = TextWrapping.Wrap };
    private readonly HAL_BuildProgressView _progress = new(expandLog: true);
    private readonly Button _start = Button("Start installation");
    private readonly Button _close = Button("Back to launcher");
    private readonly Button _review = Button("Review setup");
    private readonly Button _next = Button("Next");
    private readonly Button _back = Button("Back");
    private readonly TextBlock _setupStatus = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _stepTitle = new() { FontSize = 13 };
    private Border[] _pages = Array.Empty<Border>();
    private int _step;
    private readonly Button _tools = Button("Prepare Linux build tools…");
    private readonly StackPanel _sourceFields = new() { Spacing = 6 };
    private readonly StackPanel _configuration = new() { Spacing = 6 };
    private CancellationTokenSource? _cancellation;
    private CancellationTokenSource? _downloadCancellation;
    private bool _running;

    public bool Completed { get; private set; }
    public string ResultText { get; private set; } = "Installation not started.";
    public string OutputDirectory => _output.Text ?? "";
    public int? ShaderWorkers => ParseWorkers(_shaders.Text, "Shader workers", 32);
    public string SourcePath => _source.Text ?? "";

    public HAL_InstallView(Window owner, string root, bool development, PreferenceStore? preferences,
        string source, string output, bool force, bool prepare, bool retain, int? shaderWorkers)
    {
        _owner = owner; _root = root; _development = development; _preferences = preferences;
        Styles.Add(new Style(x => x.OfType<TextBox>()) { Setters = { new Setter(TextBox.MinHeightProperty, 30d), new Setter(TextBox.PaddingProperty, new Thickness(8, 4)) } });
        Styles.Add(new Style(x => x.OfType<ComboBox>()) { Setters = { new Setter(ComboBox.MinHeightProperty, 30d), new Setter(ComboBox.PaddingProperty, new Thickness(8, 4)) } });
        Styles.Add(new Style(x => x.OfType<CheckBox>()) { Setters = { new Setter(CheckBox.MinHeightProperty, 24d), new Setter(CheckBox.PaddingProperty, new Thickness(5, 0, 0, 0)) } });
        var scope = new NameScope(); NameScope.SetNameScope(this, scope);
        void Name(string name, Control control) { control.Name = name; scope.Register(name, control); }
        Name("InstallSourceBox", _source); Name("InstallOutputBox", _output);
        Name("InstallSourceFormat", _format); Name("InstallLanguage", _language);
        Name("InstallJobsBox", _jobs); Name("InstallShaderWorkersBox", _shaders);
        Name("InstallReinstallCheck", _reinstall); Name("InstallReviewButton", _review);
        Name("InstallStartButton", _start); Name("InstallProgress", _progress);
        Name("InstallSourceStatus", _sourceStatus);
        Name("InstallNextButton", _next); Name("InstallBackButton", _back);
        Name("InstallSetupStatus", _setupStatus);
        _format.ItemsSource = development ? new[] { "NUP package", "Extracted game folder", "Existing Hookshot installation" }
            : new[] { "NUP package", "Extracted game folder" };
        if (development && string.IsNullOrWhiteSpace(source))
            source = HAL_DevelopmentBuild.SuggestedExistingSource(preferences?.Value.GameDirectory, output) ?? "";
        _source.Text = source; _output.Text = output; _output.IsReadOnly = !development;
        // An update keeps its existing language unless the user selects another.
        if (HAL_DevelopmentBuild.CanUpdate(output)) _language.SelectedIndex = -1;
        _language.PlaceholderText = "Keep installed language";
        _format.SelectedIndex = development && HAL_DevelopmentBuild.HasRetainedGameFiles(source) ? 2 : Directory.Exists(source) ? 1 : 0;
        _reinstall.IsChecked = force; _prepare.IsChecked = prepare; _retain.IsChecked = retain;
        _shaders.Text = shaderWorkers?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
        var browse = Button("Browse…");
        browse.Click += async (_, _) =>
        {
            try
            {
                if (_format.SelectedIndex == 0)
                {
                    var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                    {
                        Title = "Choose your local NUP package", AllowMultiple = false,
                        FileTypeFilter = new[] { new FilePickerFileType("NUP package") { Patterns = new[] { "*.nup" } } }
                    });
                    if (files.Count > 0) _source.Text = files[0].TryGetLocalPath();
                }
                else
                {
                    var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                        { Title = _format.SelectedIndex == 2 ? "Choose your existing Hookshot installation" : "Choose the folder containing romfs and exefs", AllowMultiple = false });
                    if (folders.Count > 0) _source.Text = folders[0].TryGetLocalPath();
                }
            }
            catch (Exception ex) { ShowSetupStatus("Could not browse: " + ex.Message); }
        };
        _sourceFields.Children.Add(Row(new TextBlock { Text = "Source format", VerticalAlignment = VerticalAlignment.Center }, _format));
        _sourceFields.Children.Add(Row(_source, browse));
        var sourceBody = new StackPanel { Spacing = 6, Children = { _sourceFields, _sourceStatus } };
        var outputBrowse = Button("Browse…"); outputBrowse.IsVisible = development;
        outputBrowse.Click += async (_, _) =>
        {
            try
            {
                var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                    { Title = "Choose development installation folder", AllowMultiple = false });
                if (folders.Count > 0) _output.Text = HAL_DevelopmentBuild.ValidateOutputDirectory(folders[0].TryGetLocalPath() ?? "");
            }
            catch (Exception ex) { ShowSetupStatus("Could not select output: " + ex.Message); }
        };
        _configuration.Children.Add(Row(new TextBlock { Text = "Install directory", VerticalAlignment = VerticalAlignment.Center }, Row(_output, outputBrowse), "130,*"));
        var options = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto,Auto"), ColumnSpacing = 10 };
        void Option(Control control, int column) { Grid.SetColumn(control, column); options.Children.Add(control); }
        Option(new TextBlock { Text = "Language", VerticalAlignment = VerticalAlignment.Center }, 0); Option(_language, 1);
        Option(new TextBlock { Text = "Build jobs", VerticalAlignment = VerticalAlignment.Center }, 2); Option(_jobs, 3);
        Option(new TextBlock { Text = "Shader workers", VerticalAlignment = VerticalAlignment.Center }, 4); Option(_shaders, 5);
        _configuration.Children.Add(options);
        _configuration.Children.Add(new WrapPanel { Orientation = Orientation.Horizontal, Children = { _prepare, _retain, _reinstall } });
        _retain.Margin = _reinstall.Margin = new Thickness(18, 0, 0, 0);
        var introduction = new StackPanel { Spacing = 18, Children =
        {
            new TextBlock { Text = development ? "Welcome to Hookshot-Dev setup" : "Welcome to Hookshot setup", FontSize = 23, FontWeight = FontWeight.SemiBold },
            new TextBlock { Text = development ? "Build a separate Hookshot installation with HAL's randomizer integration from your own local game files. Next, choose your game source, installation location and build options." : "Install Hookshot from your own local game files. Next, choose your game source, installation location and build options.", TextWrapping = TextWrapping.Wrap },
            new TextBlock { Text = "Your source files stay unchanged. Installation can take a while; the final screen shows progress and the live build log.", TextWrapping = TextWrapping.Wrap }
        } };
        var missingTools = new TextBlock { Name = "InstallMissingToolsNotice", TextWrapping = TextWrapping.Wrap,
            IsVisible = !File.Exists(Path.Combine(_root, "nx2", "make_game.py")),
            Text = "HAL's Hookshot-Dev build tools were not found. You can download the official Builder below; to build Hookshot-Dev, follow the HAL setup instructions for a compatible Hookshot source checkout." };
        Name("InstallMissingToolsNotice", missingTools);
        introduction.Children.Add(missingTools);
        if (development && OperatingSystem.IsWindows())
            introduction.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap,
                Text = "Windows support is experimental. Hookshot's Python packages, compiler and other build prerequisites must be installed before building; see READ-ME-FIRST.txt in the package." });
        var downloadStatus = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var download = Button("Download Hookshot Builder…");
        var cancelDownload = Button("Cancel download");
        Name("InstallDownloadHookshotButton", download);
        Name("InstallDownloadStatus", downloadStatus);
        Name("InstallCancelDownloadButton", cancelDownload);
        cancelDownload.IsVisible = false;
        bool offerDownload = HAL_HookshotDownload.ShouldOffer(HookshotGame.StableGameDirectory(),
            preferences?.Value.DevGameDirectory, preferences?.Value.GameDirectory);
        var downloadOffer = new StackPanel { Spacing = 8, IsVisible = offerDownload && (OperatingSystem.IsLinux() || OperatingSystem.IsWindows()), Children =
        {
            new TextBlock { Text = "No Hookshot installation was found. Download the official Hookshot Builder, or continue if you already have your own game files.", TextWrapping = TextWrapping.Wrap },
            new TextBlock { Text = "The Builder installs Hookshot from your own game files. Return here afterward to build Hookshot-Dev for HAL's randomizer.", TextWrapping = TextWrapping.Wrap },
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { download, cancelDownload } },
            downloadStatus
        } };
        Name("InstallDownloadOffer", downloadOffer);
        introduction.Children.Add(downloadOffer);
        cancelDownload.Click += (_, _) => _downloadCancellation?.Cancel();
        download.Click += async (_, _) =>
        {
            if (_downloadCancellation is not null) return;
            using var cancellation = new CancellationTokenSource();
            _downloadCancellation = cancellation;
            download.IsEnabled = false; cancelDownload.IsVisible = true;
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
                client.DefaultRequestHeaders.UserAgent.ParseAdd("HookshotAdventureLauncher/0.9.02");
                downloadStatus.Text = "Checking the official Hookshot release…";
                var asset = await HAL_HookshotDownload.LatestAsync(client, OperatingSystem.IsWindows(), cancellation.Token);
                var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Save the official Hookshot Builder", SuggestedFileName = asset.Name
                });
                if (file is null) { downloadStatus.Text = "Download cancelled."; return; }
                var destination = file.TryGetLocalPath();
                if (string.IsNullOrWhiteSpace(destination)) throw new InvalidOperationException("Choose a local file location.");
                int lastPercent = -1;
                var progress = new Progress<double>(value =>
                {
                    int percent = (int)(value * 100);
                    if (percent != lastPercent) { lastPercent = percent; downloadStatus.Text = $"Downloading official Hookshot Builder… {percent}%"; }
                });
                downloadStatus.Text = "Downloading official Hookshot Builder…";
                await HAL_HookshotDownload.DownloadAsync(client, asset, destination, progress, cancellation.Token);
                downloadStatus.Text = "Downloaded. Open the saved Hookshot Builder, install from your own game files, then return here and choose the existing installation.";
            }
            catch (OperationCanceledException) { downloadStatus.Text = "Download cancelled."; }
            catch (Exception ex) { downloadStatus.Text = "Could not download Hookshot Builder: " + ex.Message; }
            finally { _downloadCancellation = null; download.IsEnabled = true; cancelDownload.IsVisible = false; }
        };
        var introCard = Card("01", "Introduction", "WELCOME", introduction);
        var setupBody = new StackPanel { Spacing = 18, Children = { sourceBody, _configuration, _setupStatus } };
        var configurationCard = Card("02", "Locations and build setup", "CONFIGURATION", setupBody);
        var statusCard = Card("03", "Installation", "LIVE OUTPUT", _progress);
        Name("InstallIntroCard", introCard); Name("InstallConfigurationCard", configurationCard); Name("InstallStatusCard", statusCard);
        _pages = new[] { introCard, configurationCard, statusCard };
        _progress.ShowStatus("Ready to review your setup. No build is running.");
        _review.Click += (_, _) =>
        {
            try { CreateBuild(); ShowSetupStatus("Setup is ready. Start the installation when you are ready."); }
            catch (Exception ex) { ShowSetupStatus("Setup needs attention: " + ex.Message); }
        };
        _next.Click += (_, _) => ShowStep(1);
        _back.Click += (_, _) => { if (!_running) ShowStep(_step - 1); };
        _tools.IsVisible = false;
        _tools.Click += async (_, _) =>
        {
            var setup = new HAL_LinuxBuildToolsDialog(_root) { Icon = _owner.Icon };
            await setup.ShowDialog(_owner);
            ResultText = setup.Completed ? "Linux build tools are ready. Retry the installation to use them." : setup.ResultText;
            _progress.ShowStatus(ResultText);
            _tools.IsVisible = !setup.Completed;
        };
        _start.Classes.Add("primary");
        _start.Click += async (_, _) => await RunBuildAsync();
        _close.Click += (_, _) => { if (_running) _cancellation?.Cancel(); else owner.Close(); };
        owner.Closing += (_, e) => { _downloadCancellation?.Cancel(); if (_running) { e.Cancel = true; _cancellation?.Cancel(); } };
        _format.SelectionChanged += (_, _) =>
        {
            _source.Watermark = _format.SelectedIndex switch { 0 => "Choose your local .nup file", 2 => "Choose your existing Hookshot installation", _ => "Folder containing romfs and exefs" };
            RefreshMode();
        };
        _source.TextChanged += (_, _) => RefreshMode();
        _output.TextChanged += (_, _) => RefreshMode();
        _reinstall.IsCheckedChanged += (_, _) => RefreshMode();
        RefreshMode();
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8, Margin = new Thickness(0, 10, 0, 0), Children = { _tools, _close, _back, _review, _next, _start } };
        var cards = new StackPanel { Spacing = 10, Children = { _stepTitle, introCard, configurationCard, statusCard } };
        var scroll = new ScrollViewer { Content = cards, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Name("InstallCardsScroll", scroll);
        var layout = new DockPanel { Margin = new Thickness(18) };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock { Text = development ? "Install Hookshot-Dev" : "Install Hookshot", FontSize = 24, FontWeight = FontWeight.Bold });
        var subtitle = new TextBlock { Text = development ? "DEVELOPMENT BUILD" : "STANDARD BUILD", VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
        subtitle.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("SubtleBrush")); Grid.SetColumn(subtitle, 1); header.Children.Add(subtitle);
        DockPanel.SetDock(header, Dock.Top); DockPanel.SetDock(actions, Dock.Bottom);
        layout.Children.Add(header); layout.Children.Add(actions); layout.Children.Add(scroll); Content = layout;
        ShowStep(0);
    }

    private void ShowSetupStatus(string message)
    {
        _setupStatus.Text = message;
        _progress.ShowStatus(message);
    }

    private void ShowStep(int step)
    {
        _step = Math.Clamp(step, 0, 2);
        for (var i = 0; i < _pages.Length; i++) _pages[i].IsVisible = i == _step;
        _stepTitle.Text = $"Step {_step + 1} of 3";
        _next.IsVisible = _step == 0;
        _back.IsVisible = _step > 0 && !Completed;
        _back.IsEnabled = !_running;
        _review.IsVisible = _step == 1;
        _start.IsVisible = _step == 1 || (_step == 2 && !_running && !Completed);
    }

    private void RefreshMode()
    {
        var update = !_reinstall.IsChecked.GetValueOrDefault() && HAL_DevelopmentBuild.CanUpdate(_output.Text ?? "");
        _sourceFields.IsEnabled = !update;
        _sourceStatus.Text = update ? "Existing game found. Update from retained game files, or select Reinstall to choose another source."
            : _development && HAL_DevelopmentBuild.HasRetainedGameFiles(_source.Text ?? "")
                ? "Existing Hookshot game files found. A separate copy will be built; your installation and saves stay unchanged."
                : _development ? "Choose an existing Hookshot installation, a NUP package, or a folder containing romfs and exefs."
                : "Choose a local NUP package or a folder containing romfs and exefs. Your source files stay unchanged.";
        _start.Content = update ? "Update installation" : "Start installation";
    }

    private (ProcessStartInfo info, string log, Action? install) CreateBuild()
    {
        if (!File.Exists(Path.Combine(_root, "nx2", "make_game.py")))
            throw new InvalidOperationException("HAL's Hookshot build tools are missing. Follow the HAL setup instructions to integrate a compatible Hookshot source checkout.");
        if (_development && !File.ReadAllText(Path.Combine(_root, "nx2", "make_game.py")).Contains("hookshot_randomizer_capability.txt", StringComparison.Ordinal))
            throw new InvalidOperationException("These Hookshot build tools do not include HAL's randomizer integration. Use the matching HAL package or integrate a compatible Hookshot source checkout.");
        var workers = ShaderWorkers;
        var jobs = ParseWorkers(_jobs.Text, "Build jobs", 256) ?? 0;
        ProcessStartInfo info; string log; Action? install = null;
        if (_development)
        {
            var plan = HAL_DevelopmentBuild.Create(_root, SourcePath, OutputDirectory, _reinstall.IsChecked == true,
                _prepare.IsChecked == true, _retain.IsChecked == true, workers);
            info = plan.StartInfo; log = plan.LogPath; install = plan.InstallCompletedBuild;
            _preferences?.SetDevGameDirectory(plan.Output);
            _preferences?.SetDevShaderWorkers(workers);
        }
        else
        {
            if (!HAL_DevelopmentBuild.CanUpdate(OutputDirectory) || _reinstall.IsChecked == true)
            {
                var source = Path.GetFullPath(SourcePath);
                if (!(File.Exists(source) && Path.GetExtension(source).Equals(".nup", StringComparison.OrdinalIgnoreCase)) &&
                    !(Directory.Exists(Path.Combine(source, "romfs")) && Directory.Exists(Path.Combine(source, "exefs"))))
                    throw new InvalidOperationException("Choose a local .nup or a folder containing romfs and exefs.");
            }
            info = HAL_StockInstallDialog.HAL_CreateStartInfo(_root, SourcePath,
                HAL_DevelopmentBuild.CanUpdate(OutputDirectory) && _reinstall.IsChecked != true, _prepare.IsChecked == true);
            info.Environment["HOOKSHOT_KEEP_BUILD_FILES"] = _retain.IsChecked == true ? "1" : "0";
            if (workers is int count) info.Environment["RX_VK_PREPARE_CPUS"] = count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "hookshot-builder-stock",
                "HAL-build-" + Guid.NewGuid().ToString("N") + ".log");
        }
        var jobsIndex = info.ArgumentList.IndexOf("--jobs");
        info.ArgumentList[jobsIndex + 1] = jobs.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (_language.SelectedIndex >= 0)
        {
            info.ArgumentList.Add("--language"); info.ArgumentList.Add(new[] { "en-US", "en-GB", "ja" }[_language.SelectedIndex]);
        }
        return (info, log, install);
    }

    private async Task RunBuildAsync()
    {
        if (_running || Completed || _step == 0) return;
        try
        {
            if (HookshotGame.IsRunning()) throw new InvalidOperationException("Close Hookshot before changing its installation.");
            var build = CreateBuild();
            _running = true; _start.IsEnabled = _review.IsEnabled = _tools.IsEnabled = _configuration.IsEnabled = _sourceFields.IsEnabled = false;
            ShowStep(2);
            _progress.ShowStatus("Checking required build tools before installation…");
            _close.Content = "Stop build"; _cancellation = new CancellationTokenSource();
            if (!await HAL_LinuxBuildToolsDialog.EnsureReadyAsync(_owner, _root))
            {
                ResultText = "Build-tool setup was cancelled or did not complete. No game build was started.";
                _progress.ShowStatus(ResultText); return;
            }
            _cancellation.Token.ThrowIfCancellationRequested();
            HAL_LinuxBuildTools.HAL_ConfigureBuild(build.info, _root);
            Completed = await _progress.RunAsync(build.info, build.log, build.install, _cancellation.Token);
            ResultText = _progress.ResultText;
            _tools.IsVisible = !Completed && OperatingSystem.IsLinux() &&
                ResultText.Contains("Missing Linux build prerequisites", StringComparison.OrdinalIgnoreCase);
            if (Completed && _development && _preferences is not null)
            {
                _preferences.SetDevGameDirectory(OutputDirectory);
                _preferences.SetDevShaderWorkers(ShaderWorkers);
                _preferences.SetGameDirectory(OutputDirectory);
            }
        }
        catch (OperationCanceledException) { ResultText = "Installation stopped."; _progress.ShowStatus(ResultText); }
        catch (Exception ex) { ResultText = "Could not install: " + ex.Message; ShowSetupStatus(ResultText); }
        finally
        {
            _running = false; _start.IsEnabled = _review.IsEnabled = _tools.IsEnabled = _configuration.IsEnabled = !Completed;
            RefreshMode(); _sourceFields.IsEnabled &= !Completed;
            _close.Content = Completed ? "Return to launcher" : "Back to launcher";
            _cancellation?.Dispose(); _cancellation = null;
            ShowStep(_step);
        }
    }

    private static int? ParseWorkers(string? value, string label, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (int.TryParse(value, out var count) && count >= 1 && count <= maximum) return count;
        throw new InvalidOperationException($"{label} must be 1–{maximum}, or blank for Automatic.");
    }

    private static Button Button(string text) => new() { Content = text, Classes = { "raised" }, Padding = new Thickness(12, 6), MinHeight = 32 };
    private static Grid Row(Control first, Control second, string columns = "*,Auto")
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions(columns), ColumnSpacing = 8 };
        row.Children.Add(first); Grid.SetColumn(second, 1); row.Children.Add(second); return row;
    }

    private static Border Card(string number, string title, string caption, Control body)
    {
        var index = new TextBlock { Text = number, FontWeight = FontWeight.Bold, FontSize = 16,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        index.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("AccentBrush"));
        var badge = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(7), Child = index };
        badge.Bind(Border.BackgroundProperty, new DynamicResourceExtension("RaisedBrush"));
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("32,*,Auto"), ColumnSpacing = 10 };
        header.Children.Add(badge);
        var heading = new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(heading, 1); header.Children.Add(heading);
        var tag = new TextBlock { Text = caption, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        tag.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("SubtleBrush")); Grid.SetColumn(tag, 2); header.Children.Add(tag);
        var divider = new Border { Height = 1 }; divider.Bind(Border.BackgroundProperty, new DynamicResourceExtension("RimBrush"));
        var card = new Border { Classes = { "panel" }, Padding = new Thickness(12, 9),
            Child = new StackPanel { Spacing = 7, Children = { header, divider, body } } };
        return card;
    }
}
