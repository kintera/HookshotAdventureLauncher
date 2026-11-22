using System.Diagnostics;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using HookshotAdventure.Core;

namespace HookshotAdventure.App;

// Shared stock/dev progress display, following the local Avalonia installer's stage-and-log flow.
public sealed class HAL_BuildProgressView : StackPanel
{
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 100, Height = 10 };
    private readonly TextBox _log = new() { IsReadOnly = true, AcceptsReturn = true, Height = 160, TextWrapping = TextWrapping.Wrap, FontSize = 11 };
    private readonly Dictionary<string, TextBlock> _stages = new();
    public string ResultText => _status.Text ?? "";

    public HAL_BuildProgressView(bool compact = false, bool expandLog = false)
    {
        Spacing = compact ? 6 : 8;
        if (compact) { _log.Height = 100; _status.FontSize = 13; }
        Children.Add(_status); Children.Add(_progress);
        var indicators = new Avalonia.Controls.Primitives.UniformGrid { Columns = compact ? 5 : 1 };
        foreach (var (id, label) in new[] { ("read", "Reading game files"), ("lift", "Translating game code"),
                     ("compile", "Compiling"), ("finish", "Installing game files"), ("shaders", "Preparing shaders") })
        {
            var title = compact ? id switch { "read" => "Reading", "lift" => "Translating", "compile" => "Compiling", "finish" => "Installing", _ => "Shaders" } : label;
            var row = new TextBlock { Text = "○ " + title, Tag = title, FontSize = compact ? 12 : 14 };
            _stages[id] = row; indicators.Children.Add(row);
        }
        Children.Add(indicators);
        Children.Add(new Expander { Header = "Build log", IsExpanded = expandLog, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, Content = _log });
    }

    public void ShowStatus(string value) => _status.Text = value;

    public async Task<bool> RunAsync(ProcessStartInfo info, string logPath, Action? install, CancellationToken cancellation,
        string? initialStatus = null)
    {
        string stdout = "", stderr = "";
        var logLock = new object();
        _status.Text = initialStatus ?? "Starting build…"; _progress.IsIndeterminate = true; _log.Text = "";
        foreach (var row in _stages.Values) row.Text = "○ " + row.Tag;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            using var log = new StreamWriter(logPath, append: false) { AutoFlush = true };
            using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start the builder.");
            using var registration = cancellation.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            });
            async Task Read(StreamReader reader, bool error)
            {
                while (await reader.ReadLineAsync() is { } line)
                {
                    lock (logLock)
                    {
                        log.WriteLine(line);
                        if (error) stderr = Tail(stderr + line + "\n"); else stdout = Tail(stdout + line + "\n");
                    }
                    Dispatcher.UIThread.Post(() =>
                    {
                        _log.Text = Tail((_log.Text ?? "") + line + "\n", 24000);
                        _log.CaretIndex = _log.Text.Length;
                    });
                    if (line.StartsWith("@@stage ", StringComparison.Ordinal))
                    {
                        var parts = line.Split(' ', 3);
                        if (parts.Length == 3)
                        {
                            Dispatcher.UIThread.Post(() =>
                            {
                                _status.Text = parts[2]; _progress.IsIndeterminate = true;
                                foreach (var row in _stages)
                                    if (row.Key == parts[1]) { row.Value.Text = "▶ " + row.Value.Tag; break; }
                                    else row.Value.Text = "✓ " + row.Value.Tag;
                            });
                        }
                    }
                    else if (line.StartsWith("@@progress ", StringComparison.Ordinal) &&
                             double.TryParse(line[11..], NumberStyles.Float, CultureInfo.InvariantCulture, out var progress))
                    {
                        Dispatcher.UIThread.Post(() => { _progress.IsIndeterminate = false; _progress.Value = Math.Clamp(progress * 100, 0, 100); });
                    }
                    else if (line.StartsWith("@@error ", StringComparison.Ordinal)) Dispatcher.UIThread.Post(() => _status.Text = line[8..]);
                }
            }
            var readOut = Read(process.StandardOutput, false);
            var readErr = Read(process.StandardError, true);
            await process.WaitForExitAsync();
            await Task.WhenAll(readOut, readErr);
            cancellation.ThrowIfCancellationRequested();
            if (process.ExitCode != 0)
            {
                _status.Text = HAL_DevelopmentBuild.FailureDetails(process.ExitCode, stdout, stderr) + "\nLog: " + logPath;
                return false;
            }
            if (install is not null) { _status.Text = "Installing completed build…"; await Task.Run(install, cancellation); }
            foreach (var row in _stages.Values)
                if (row.Text?.StartsWith("▶", StringComparison.Ordinal) == true) row.Text = "✓ " + row.Tag;
            _progress.Value = 100;
            _status.Text = "Installation completed.\nLog: " + logPath;
            return true;
        }
        catch (OperationCanceledException) { _status.Text = "Build stopped.\nLog: " + logPath; return false; }
        catch (Exception ex) { _status.Text = "Build failed: " + ex.Message + "\nLog: " + logPath; return false; }
        finally { _progress.IsIndeterminate = false; }
    }

    private static string Tail(string value, int limit = 64000) => value.Length > limit ? value[^limit..] : value;
}

public sealed class HAL_DevelopmentBuildDialog : Window
{
    public HAL_InstallView Screen { get; }
    public bool Completed => Screen.Completed;
    public string ResultText => Screen.ResultText;
    public string OutputDirectory => Screen.OutputDirectory;

    public HAL_DevelopmentBuildDialog(string root, PreferenceStore preferences, string source, string output,
        bool force, bool prepare, bool retain, int? shaderWorkers)
    {
        Title = "Hookshot — Development installation";
        Width = 980; Height = 660; MinWidth = 750; MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Screen = new HAL_InstallView(this, root, true, preferences, source, output, force, prepare, retain, shaderWorkers);
        Content = Screen;
    }
}

public sealed class HAL_LinuxBuildToolsDialog : Window
{
    public bool Completed { get; private set; }
    public string ResultText { get; private set; } = "Build-tool setup was cancelled.";

    public static async Task<bool> EnsureReadyAsync(Window owner, string root)
    {
        if (!OperatingSystem.IsLinux() || HAL_LinuxBuildTools.HAL_Available(root)) return true;
        var setup = new HAL_LinuxBuildToolsDialog(root) { Icon = owner.Icon };
        await setup.ShowDialog(owner);
        return setup.Completed && HAL_LinuxBuildTools.HAL_Available(root);
    }

    public HAL_LinuxBuildToolsDialog(string root)
    {
        Title = "Prepare Linux build tools"; Width = 680; Height = 560; MinWidth = 520; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var progress = new HAL_BuildProgressView(expandLog: true) { IsVisible = false };
        var start = new Button { Content = "Prepare build tools", Classes = { "raised", "primary" } };
        var close = new Button { Content = "Cancel", Classes = { "raised" } };
        var status = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Text = "Downloads and prepares the project's pinned Linux compiler, Python packages, FFmpeg libraries and Vulkan headers. This uses several GB in your user cache and does not read or upload game files." };
        bool running = false;
        CancellationTokenSource? cancellation = null;
        close.Click += (_, _) => { if (running) cancellation?.Cancel(); else Close(); };
        Closing += (_, e) => { if (running) { e.Cancel = true; cancellation?.Cancel(); } };
        start.Click += async (_, _) =>
        {
            if (running) return;
            try
            {
                var info = HAL_LinuxBuildTools.HAL_CreateSetupStartInfo(root);
                running = true; start.IsEnabled = false; close.Content = "Stop setup"; progress.IsVisible = true;
                status.Text = "Preparing pinned Linux build tools in " + HAL_LinuxBuildTools.HAL_CacheDirectory;
                cancellation = new CancellationTokenSource();
                var logPath = Path.Combine(HAL_LinuxBuildTools.HAL_CacheDirectory, "HAL-setup-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".log");
                Completed = await progress.RunAsync(info, logPath, null, cancellation.Token,
                    "Downloading and preparing the pinned Linux build tools…");
                if (Completed && !HAL_LinuxBuildTools.HAL_Available(root))
                {
                    Completed = false;
                    progress.ShowStatus("Setup exited successfully, but the required build tools are still missing. Check the setup log before retrying.");
                }
                ResultText = Completed ? "Pinned Linux build tools are ready." : progress.ResultText;
                status.Text = Completed ? ResultText : "Build-tool setup failed. Expand Build log below for details.";
            }
            catch (Exception ex) { ResultText = "Could not prepare Linux build tools: " + ex.Message; status.Text = ResultText; }
            finally { running = false; start.IsEnabled = !Completed; close.Content = "Close"; cancellation?.Dispose(); cancellation = null; }
        };
        var body = new StackPanel { Spacing = 12, Children =
        {
            new TextBlock { Text = "LINUX BUILD TOOL SETUP", FontWeight = FontWeight.SemiBold }, status, progress
        } };
        var actions = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Spacing = 8, Children = { close, start } };
        var layout = new DockPanel { Margin = new Avalonia.Thickness(18), LastChildFill = true };
        DockPanel.SetDock(actions, Dock.Bottom); layout.Children.Add(actions); layout.Children.Add(new ScrollViewer { Content = body });
        Content = layout;
    }
}
