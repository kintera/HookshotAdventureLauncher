using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace HookshotAdventure.Core;

/// <summary>Starts the installed Hookshot binary with one adventure's isolated user directory.</summary>
public static class HookshotGame
{
    private static readonly string[] RandomizerEnvironmentKeys =
    {
        "HOS_SKIP_OPENING", "HOS_SKIP_BOSS_INTRODUCTIONS", "HOS_RANDO_SKIP_SARIA", "HOS_RANDO_SKIP_OPENING", "HOS_RANDO_STARTING_CONSUMABLES", "HOS_RANDO_STARTING_DUNGEON_FLAGS", "HOS_RANDO_STARTING_EQUIPMENT", "HOS_RANDO_STARTING_SONGS",
        "HOS_RANDO_CHILD_STEALTH", "HOS_RANDO_CHILD_STEALTH_SONG", "HOS_FS_OVERLAY",
        "HOS_MIDO_SWORD_TEST", "HOS_MIDO_CHEST_SWAP", "HOS_RANDO_STONE_REWARD_FALLBACK",
        "HOS_MIDO_CHEST_EXPECT", "HOS_MIDO_CHEST_CALLER", "HOS_MIDO_CHEST_PROBE",
        "HOS_MIDO_CHEST_PROFILE_ID", "HOS_MIDO_CHEST_PROFILE_SEED"
        , "HOS_TEST_OPEN_TIME_DOOR", "HOS_TEST_OPEN_JABU", "HOS_TEST_SKIP_TRIALS"
    };

    public sealed record LaunchPlan(string Executable, string WorkingDirectory, string UserDirectory)
    {
        public ProcessStartInfo ToStartInfo(IReadOnlyDictionary<string, string>? randomizerEnvironment = null, bool videoDebug = false, bool hosDebug = false, bool a64Debug = false, bool debugKey = false)
        {
            var info = new ProcessStartInfo(Executable)
            {
                WorkingDirectory = WorkingDirectory,
                UseShellExecute = false
            };
            info.ArgumentList.Add("--user");
            info.ArgumentList.Add(UserDirectory);
            if (videoDebug) info.ArgumentList.Add("--Video-Debug");
            if (hosDebug) info.ArgumentList.Add("--HOS-Debug");
            if (a64Debug) info.ArgumentList.Add("--A64-Debug");
            info.Environment.Remove("HOS_DEBUG_KEY");
            if (debugKey) info.Environment["HOS_DEBUG_KEY"] = "1";
            info.Environment["HOOKSHOT_NO_UPDATE_CHECK"] = "1";
            // Hookshot otherwise restores the newest backup from every installation into an empty adventure.
            // Give each user directory a persistent, independent backup history outside its profile folder.
            var identity = Path.TrimEndingDirectorySeparator(Path.GetFullPath(UserDirectory));
            if (OperatingSystem.IsWindows()) identity = identity.ToUpperInvariant();
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
            var data = OperatingSystem.IsWindows() ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) :
                Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (string.IsNullOrWhiteSpace(data)) data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
            info.Environment["HOS_SAVE_BACKUPS"] = Path.Combine(data, "hookshot-adventure", "save-backups", hash);
            foreach (var key in RandomizerEnvironmentKeys) info.Environment.Remove(key);
            if (randomizerEnvironment is not null)
                foreach (var pair in randomizerEnvironment)
                {
                    if (!RandomizerEnvironmentKeys.Contains(pair.Key, StringComparer.Ordinal) && pair.Key != "HOS_TEXT_SPEED")
                        throw new InvalidDataException("Randomizer attempted to set an unapproved runtime variable.");
                    info.Environment[pair.Key] = pair.Value;
                }
            return info;
        }
    }

    // Adventure development and randomizer work must default to the isolated
    // dev install. Never select the stable installation implicitly.
    public static string DefaultGameDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Games", "Hookshot-Dev");

    public static string StableGameDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Games", "Hookshot");

    public static string DefaultHookshotUserDirectory()
    {
        if (OperatingSystem.IsWindows())
            return Path.Combine(DefaultGameDirectory(), "user");
        var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        var data = !string.IsNullOrWhiteSpace(xdg)
            ? xdg : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        return Path.Combine(data, "hookshot");
    }

    public static bool IsInstalled(string directory) => Directory.Exists(directory) &&
        !ProfileRepository.IsLink(directory) && File.Exists(ResolveExecutable(directory)) &&
        File.Exists(ResolveRenderer(directory)) && !ProfileRepository.IsLink(ResolveRenderer(directory)) &&
        File.Exists(Path.Combine(directory, "image.bin")) && Directory.Exists(Path.Combine(directory, "romfs"));

    public static string ResolveExecutable(string gameDirectory)
    {
        var game = Path.GetFullPath(gameDirectory);
        var name = OperatingSystem.IsWindows() ? "hookshot.exe" : "hookshot";
        var nested = Path.Combine(game, "bin", name);
        var flat = Path.Combine(game, name);
        return File.Exists(nested) ? nested : flat;
    }

    public static string ResolveRenderer(string gameDirectory) => Path.Combine(
        Path.GetDirectoryName(ResolveExecutable(gameDirectory))!, OperatingSystem.IsWindows() ? "hookshot_vk.dll" : "hookshot_vk.so");

    public static LaunchPlan CreatePlan(string gameDirectory, string userDirectory)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory))
            throw new InvalidOperationException("Choose the built Hookshot game directory in Settings.");
        if (string.IsNullOrWhiteSpace(userDirectory))
            throw new InvalidOperationException("Adventure save directory is missing.");
        var game = Path.GetFullPath(gameDirectory);
        var user = Path.GetFullPath(userDirectory);
        var executable = ResolveExecutable(game);
        if (!Directory.Exists(game) || ProfileRepository.IsLink(game))
            throw new InvalidOperationException("Hookshot game directory does not exist or is a symlink.");
        if (!File.Exists(executable) || ProfileRepository.IsLink(executable))
            throw new InvalidOperationException("Hookshot executable is missing (expected bin/hookshot or hookshot).");
        var renderer = ResolveRenderer(game);
        if (!File.Exists(renderer) || ProfileRepository.IsLink(renderer))
            throw new InvalidOperationException("The built Vulkan renderer is missing (" + Path.GetFileName(renderer) + "). Update or rebuild this installation from the launcher.");
        if (!File.Exists(Path.Combine(game, "image.bin")) || !Directory.Exists(Path.Combine(game, "romfs")))
            throw new InvalidOperationException("The selected folder must contain image.bin and romfs.");
        if (!Directory.Exists(user) || ProfileRepository.IsLink(user))
            throw new InvalidOperationException("Adventure user-data directory is missing or linked.");
        if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(executable) &
              (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) == 0)
            throw new InvalidOperationException("Hookshot executable is not marked executable.");
        return new LaunchPlan(executable, game, user);
    }

    public static bool IsRunning()
    {
        try
        {
            foreach (var process in Process.GetProcessesByName("hookshot"))
            {
                using (process)
                {
                    try { if (!process.HasExited) return true; }
                    catch (InvalidOperationException) { }
                }
            }
        }
        catch (System.ComponentModel.Win32Exception) { return true; }
        return false;
    }

    public static Process Start(LaunchPlan plan, IReadOnlyDictionary<string, string>? randomizerEnvironment = null, bool videoDebug = false, bool hosDebug = false, bool a64Debug = false, bool debugKey = false) => Process.Start(plan.ToStartInfo(randomizerEnvironment, videoDebug, hosDebug, a64Debug, debugKey))
        ?? throw new InvalidOperationException("The operating system did not start Hookshot.");
}
