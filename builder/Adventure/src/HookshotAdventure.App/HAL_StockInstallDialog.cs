using System.Diagnostics;
using Avalonia.Controls;
using HookshotAdventure.Core;

namespace HookshotAdventure.App;

public sealed class HAL_StockInstallDialog : Window
{
    public static ProcessStartInfo HAL_CreateBuiltinStartInfo(string sourceRoot)
    {
        if (OperatingSystem.IsLinux())
        {
            var script = Path.Combine(sourceRoot, "builder", "linux", "hookshot_builder.py");
            if (!File.Exists(script)) throw new FileNotFoundException("The built-in Linux Hookshot Builder was not found.", script);
            var info = new ProcessStartInfo(HAL_DevelopmentBuild.PythonExecutable(sourceRoot))
            {
                WorkingDirectory = sourceRoot, UseShellExecute = false
            };
            info.ArgumentList.Add("-I");
            info.ArgumentList.Add(script);
            HAL_LinuxBuildTools.HAL_ConfigureBuild(info, sourceRoot);
            info.Environment.Remove("HOOKSHOT_DEV_RANDOMIZER");
            return info;
        }
        if (OperatingSystem.IsWindows())
        {
            var path = Environment.GetEnvironmentVariable("HOOKSHOT_BUILDER_EXE");
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new FileNotFoundException("Set HOOKSHOT_BUILDER_EXE to your installed Hookshot Builder executable, then reopen HAL.");
            return new ProcessStartInfo(path) { UseShellExecute = false };
        }
        throw new PlatformNotSupportedException("The built-in Hookshot Builder is available on Windows and Linux.");
    }

    public string OutputDirectory => HookshotGame.StableGameDirectory();

    public HAL_InstallView Screen { get; }
    public bool Completed => Screen.Completed;

    public HAL_StockInstallDialog(string sourceRoot, bool force, bool preparePipelines, PreferenceStore? preferences = null)
    {
        Title = force ? "Hookshot — Reinstall" : "Hookshot — Review / update installation";
        Width = 980; Height = 660; MinWidth = 750; MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Screen = new HAL_InstallView(this, sourceRoot, false, preferences, "", OutputDirectory, force, preparePipelines, true,
            preferences?.Value.DevShaderWorkers);
        Content = Screen;
    }

    public static ProcessStartInfo HAL_CreateStartInfo(string sourceRoot, string source, bool update, bool preparePipelines)
    {
        var info = new ProcessStartInfo(HAL_DevelopmentBuild.PythonExecutable(sourceRoot))
        {
            WorkingDirectory = sourceRoot, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var arg in new[] { "-I", "-u", Path.Combine(sourceRoot, "nx2", "make_game.py") }) info.ArgumentList.Add(arg);
        var output = HookshotGame.StableGameDirectory();
        foreach (var arg in update ? new[] { "--update", output } : new[] { "--game", source, "--out", output }) info.ArgumentList.Add(arg);
        foreach (var arg in new[] { "--work", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "hookshot-builder-stock"),
                     "--jobs", Math.Max(1, Environment.ProcessorCount).ToString(System.Globalization.CultureInfo.InvariantCulture) }) info.ArgumentList.Add(arg);
        if (!preparePipelines) info.ArgumentList.Add("--no-prepare-shaders");
        info.Environment["HOOKSHOT_DEV_RANDOMIZER"] = "0";
        HAL_LinuxBuildTools.HAL_ConfigureBuild(info, sourceRoot);
        return info;
    }
}
