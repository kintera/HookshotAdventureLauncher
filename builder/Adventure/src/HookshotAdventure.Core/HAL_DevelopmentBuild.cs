using System.Diagnostics;

namespace HookshotAdventure.Core;

public sealed record HAL_DevelopmentBuild(string Root, string Output, string BuildDirectory, bool UpdateExisting,
    ProcessStartInfo StartInfo, string LogPath, int? ShaderWorkers)
{
    public static string PythonExecutable(string root)
    {
        var configured = Environment.GetEnvironmentVariable("HOOKSHOT_PYTHON");
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        var candidates = OperatingSystem.IsWindows()
            ? new[] { Path.Combine(root, "builder", "py", "python", "python.exe"), Path.Combine(root, ".venv", "Scripts", "python.exe") }
            : new[] { Path.Combine(root, ".venv", "bin", "python3"), Path.Combine(root, "builder", "py", "python", "bin", "python3") };
        return candidates.FirstOrDefault(File.Exists) ?? (OperatingSystem.IsWindows() ? "python" : "python3");
    }

    public static bool CanUpdate(string directory) => Directory.Exists(directory) && !ProfileRepository.IsLink(directory) &&
        File.Exists(Path.Combine(directory, ".hookshot_exefs", "main")) && Directory.Exists(Path.Combine(directory, "romfs")) &&
        !Directory.EnumerateFileSystemEntries(directory).Any(ProfileRepository.IsLink);

    public static bool HasRetainedGameFiles(string directory)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return false;
            for (var parent = new DirectoryInfo(Path.GetFullPath(directory)); parent is not null; parent = parent.Parent)
                if (ProfileRepository.IsLink(parent.FullName)) return false;
            return Directory.Exists(Path.Combine(directory, "romfs")) && !ProfileRepository.IsLink(Path.Combine(directory, "romfs")) &&
                Directory.Exists(Path.Combine(directory, ".hookshot_exefs")) && !ProfileRepository.IsLink(Path.Combine(directory, ".hookshot_exefs")) &&
                File.Exists(Path.Combine(directory, ".hookshot_exefs", "main")) && !ProfileRepository.IsLink(Path.Combine(directory, ".hookshot_exefs", "main"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return false; }
    }

    private static bool PathsOverlap(string first, string second)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        first = Path.TrimEndingDirectorySeparator(Path.GetFullPath(first));
        second = Path.TrimEndingDirectorySeparator(Path.GetFullPath(second));
        static string Prefix(string path) => Path.EndsInDirectorySeparator(path) ? path : path + Path.DirectorySeparatorChar;
        return string.Equals(first, second, comparison) || first.StartsWith(Prefix(second), comparison) || second.StartsWith(Prefix(first), comparison);
    }

    public static string? SuggestedExistingSource(string? selectedGame, string output, string? configDirectory = null)
    {
        string? builderGame = null;
        if (OperatingSystem.IsLinux())
        {
            try
            {
                var config = configDirectory ?? Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
                if (string.IsNullOrWhiteSpace(config)) config = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
                var path = Path.Combine(config, "hookshot", "builder.ini");
                if (File.Exists(path) && new FileInfo(path).Length <= 65536)
                    builderGame = File.ReadLines(path).Where(line => line.StartsWith("out=", StringComparison.Ordinal)).Select(line => line[4..]).LastOrDefault();
                if (!string.IsNullOrEmpty(builderGame) && !Path.IsPathFullyQualified(builderGame)) builderGame = null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
        }
        foreach (string? candidate in new[] { selectedGame, builderGame, HookshotGame.StableGameDirectory() })
            try
            {
                if (candidate is not null && HasRetainedGameFiles(candidate) && !PathsOverlap(candidate, output))
                    return Path.GetFullPath(candidate);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
        return null;
    }

    public static string ValidateOutputDirectory(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) throw new InvalidOperationException("Choose a development install directory.");
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(output));
        var stable = Path.TrimEndingDirectorySeparator(Path.GetFullPath(HookshotGame.StableGameDirectory()));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        static string Prefix(string value) => Path.EndsInDirectorySeparator(value) ? value : value + Path.DirectorySeparatorChar;
        if (string.Equals(path, stable, comparison) || path.StartsWith(Prefix(stable), comparison) ||
            stable.StartsWith(Prefix(path), comparison))
            throw new InvalidOperationException("The stable Hookshot installation is protected. Choose a separate development directory.");
        for (var parent = new DirectoryInfo(path); parent is not null; parent = parent.Parent)
            if (ProfileRepository.IsLink(parent.FullName))
                throw new InvalidOperationException("The development install directory cannot use symbolic links.");
        return path;
    }

    public static HAL_DevelopmentBuild Create(string root, string source, string output, bool force, bool prepare, bool retain,
        int? shaderWorkers = null)
    {
        if (shaderWorkers is < 1 or > 32) throw new ArgumentOutOfRangeException(nameof(shaderWorkers), "Shader workers must be from 1 to 32, or automatic.");
        output = ValidateOutputDirectory(output);
        var update = !force && CanUpdate(output);
        bool existingInstallation = false;
        if (!update)
        {
            if (string.IsNullOrWhiteSpace(source)) throw new InvalidOperationException("Select game source to rebuild this older development installation.");
            source = Path.GetFullPath(source);
            if (ProfileRepository.IsLink(source)) throw new InvalidOperationException("Choose a game source that is not a symbolic link.");
            for (var parent = new DirectoryInfo(Directory.Exists(source) ? source : Path.GetDirectoryName(source)!); parent is not null; parent = parent.Parent)
                if (ProfileRepository.IsLink(parent.FullName))
                    throw new InvalidOperationException("The game source path cannot use symbolic links.");
            existingInstallation = HasRetainedGameFiles(source);
            if (!(File.Exists(source) && Path.GetExtension(source).Equals(".nup", StringComparison.OrdinalIgnoreCase)) &&
                !(Directory.Exists(Path.Combine(source, "romfs")) && Directory.Exists(Path.Combine(source, "exefs"))) && !existingInstallation)
                throw new InvalidOperationException("Choose an existing Hookshot installation, a .nup file, or a folder containing romfs and exefs.");
            if (PathsOverlap(source, output) || PathsOverlap(source, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "hookshot-builder-dev")))
                throw new InvalidOperationException("Choose a development installation and build cache separate from the game source.");
        }
        var buildDirectory = update ? output : Path.Combine(Path.GetDirectoryName(output)!, ".HAL-dev-build-" + Guid.NewGuid().ToString("N"));
        var work = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "hookshot-builder-dev");
        var log = Path.Combine(work, "HAL-build-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".log");
        var info = new ProcessStartInfo(PythonExecutable(root))
        {
            WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var arg in new[] { "-I", "-u", Path.Combine(root, "nx2", "make_game.py") }) info.ArgumentList.Add(arg);
        foreach (var arg in update ? new[] { "--update", output } : existingInstallation
                     ? new[] { "--romfs", Path.Combine(source, "romfs"), "--exefs", Path.Combine(source, ".hookshot_exefs"), "--copy-romfs", "--out", buildDirectory }
                     : new[] { "--game", source, "--out", buildDirectory }) info.ArgumentList.Add(arg);
        foreach (var arg in new[] { "--work", work, "--jobs", "0" }) info.ArgumentList.Add(arg);
        if (!prepare) info.ArgumentList.Add("--no-prepare-shaders");
        info.Environment["HOOKSHOT_DEV_RANDOMIZER"] = "1";
        if (existingInstallation) info.Environment.Remove("HOOKSHOT_ROMFS_IN_PLACE");
        info.Environment["HOOKSHOT_KEEP_BUILD_FILES"] = retain ? "1" : "0";
        if (shaderWorkers is int workers) info.Environment["RX_VK_PREPARE_CPUS"] = workers.ToString(System.Globalization.CultureInfo.InvariantCulture);
        HAL_LinuxBuildTools.HAL_ConfigureBuild(info, root);
        return new(root, output, buildDirectory, update, info, log, shaderWorkers);
    }

    // Replace only files emitted by the successful build. Existing profile and save folders stay in place.
    // Moving old entries also moves symlinks themselves, never the stock files they reference.
    public void InstallCompletedBuild()
    {
        if (UpdateExisting) return;
        if (ProfileRepository.IsLink(Output) || ProfileRepository.IsLink(BuildDirectory))
            throw new IOException("Installation directory became a symbolic link.");
        if (!HookshotGame.IsInstalled(BuildDirectory))
            throw new InvalidDataException("The staged build is incomplete: the executable, Vulkan renderer, image.bin and romfs are required.");
        HAL_RandomizerSeedService.EnsureDevRandomizerBuild(BuildDirectory);
        var entries = Directory.GetFileSystemEntries(BuildDirectory);
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "user", "save", "profile-manager", "native-randomizer" };
        if (entries.Any(entry => reserved.Contains(Path.GetFileName(entry)) || ProfileRepository.IsLink(entry)))
            throw new InvalidDataException("The staged build contains an unexpected save folder or linked entry.");
        Directory.CreateDirectory(Output);
        var backup = BuildDirectory + "-previous";
        Directory.CreateDirectory(backup);
        var moved = new List<(string source, string destination, string old, bool installed)>();
        try
        {
            foreach (var entry in entries)
            {
                // A successful --prepare-shaders run creates an empty logs/ directory in the staging tree. Keep any
                // existing installation logs intact instead of replacing them with that generated empty directory.
                if (string.Equals(Path.GetFileName(entry), "logs", StringComparison.OrdinalIgnoreCase)) continue;
                var destination = Path.Combine(Output, Path.GetFileName(entry));
                var old = Path.Combine(backup, Path.GetFileName(entry));
                if (Exists(destination)) Move(destination, old);
                moved.Add((entry, destination, old, false));
                Move(entry, destination);
                moved[^1] = (entry, destination, old, true);
            }
        }
        catch
        {
            foreach (var entry in moved.AsEnumerable().Reverse())
            {
                if (entry.installed) Move(entry.destination, entry.source);
                if (Exists(entry.old)) Move(entry.old, entry.destination);
            }
            throw;
        }
    }

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path) || ProfileRepository.IsLink(path);
    private static void Move(string source, string destination)
    {
        if (Directory.Exists(source)) Directory.Move(source, destination);
        else File.Move(source, destination);
    }

    public static string FailureDetails(int exitCode, string stdout, string stderr)
    {
        var reported = stdout.Split('\n').LastOrDefault(line => line.StartsWith("@@error ", StringComparison.Ordinal));
        var details = reported is null ? (stderr + "\n" + stdout).Trim() : reported[8..].Trim();
        if (details.StartsWith("Missing Linux build prerequisites:", StringComparison.Ordinal))
            return "Missing Linux build prerequisites. Use Prepare Linux build tools, then retry. Full diagnostics are in the build log.";
        if (reported is null)
        {
            var useful = details.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(line => line.Length < 500 && (line.Contains("cannot stat", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("error:", StringComparison.OrdinalIgnoreCase) || line.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("No such file", StringComparison.OrdinalIgnoreCase) || line.Contains("not found", StringComparison.OrdinalIgnoreCase)))
                .TakeLast(3).ToArray();
            if (useful.Length > 0) details = string.Join("\n", useful);
            else details = "Expand Build log for the command output.";
        }
        if (details.Length > 1200) details = details[^1200..];
        return $"Build failed (exit {exitCode}): " + (details.Length == 0 ? "The builder returned no diagnostic output." : details);
    }
}
