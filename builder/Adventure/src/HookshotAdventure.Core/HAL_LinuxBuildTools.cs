using System.Diagnostics;

namespace HookshotAdventure.Core;

public static class HAL_LinuxBuildTools
{
    public static string HAL_CacheDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".cache", "hookshot-builder-dev", "deps");

    public static string? HAL_FindPreparedDirectory(string root)
    {
        if (!OperatingSystem.IsLinux()) return null;
        foreach (var directory in new[] { HAL_CacheDirectory, Path.Combine(root, "build-linux", "deps") })
        {
            var python = Path.Combine(directory, "python", "bin", "python3");
            var clang = Path.Combine(directory, "emsdk", "upstream", "bin", "clang");
            var clangxx = Path.Combine(directory, "emsdk", "upstream", "bin", "clang++");
            var ffmpeg = Path.Combine(directory, "ffmpeg", "lib", "libavcodec.a");
            var cpm = Path.Combine(directory, "cpm");
            if (File.Exists(python) && File.Exists(clang) && File.Exists(clangxx) && File.Exists(ffmpeg) &&
                File.Exists(Path.Combine(directory, ".python")) && File.Exists(Path.Combine(directory, ".llvm")) &&
                File.Exists(Path.Combine(directory, ".meson")) && File.Exists(Path.Combine(directory, ".ffmpeg")) &&
                File.Exists(Path.Combine(directory, ".cpm")) &&
                Directory.Exists(Path.Combine(cpm, "vulkanheaders")) && Directory.Exists(Path.Combine(cpm, "spirv-headers")) &&
                Directory.Exists(Path.Combine(cpm, "vulkanmemoryallocator")) &&
                File.Exists(Path.Combine(directory, "sdl2", "libSDL2-2.0.so.0"))) return directory;
        }
        return null;
    }

    // AppRun supplies these paths for the stock builder. They are laid out differently from the
    // developer's deps cache, so validate the bundle as a whole before reusing its Python.
    private static string? HAL_BundledPython(string root)
    {
        if (!OperatingSystem.IsLinux()) return null;
        string? tools = Environment.GetEnvironmentVariable("HOOKSHOT_TOOLS");
        string? emsdk = Environment.GetEnvironmentVariable("EMSDK");
        string? sysroot = Environment.GetEnvironmentVariable("HOOKSHOT_SYSROOT");
        string? ffmpeg = Environment.GetEnvironmentVariable("HOOKSHOT_FFMPEG");
        string? sdl = Environment.GetEnvironmentVariable("HOOKSHOT_SDL2");
        if (string.IsNullOrWhiteSpace(tools) || string.IsNullOrWhiteSpace(emsdk) ||
            string.IsNullOrWhiteSpace(sysroot) || string.IsNullOrWhiteSpace(ffmpeg) || string.IsNullOrWhiteSpace(sdl) ||
            !string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(tools)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)), StringComparison.Ordinal)) return null;
        string python = Path.GetFullPath(Path.Combine(root, "..", "..", "python", "bin", "python3"));
        return File.Exists(python) && File.Exists(Path.Combine(emsdk, "upstream", "bin", "clang")) &&
            File.Exists(Path.Combine(emsdk, "upstream", "bin", "clang++")) && Directory.Exists(sysroot) &&
            File.Exists(Path.Combine(ffmpeg, "lib", "libavcodec.a")) && File.Exists(sdl) &&
            Directory.Exists(Path.Combine(root, "cpm", "vulkanheaders")) &&
            Directory.Exists(Path.Combine(root, "cpm", "spirv-headers")) &&
            Directory.Exists(Path.Combine(root, "cpm", "vulkanmemoryallocator")) ? python : null;
    }

    public static bool HAL_Available(string root) => HAL_BundledPython(root) is not null || HAL_FindPreparedDirectory(root) is not null;

    public static ProcessStartInfo HAL_CreateSetupStartInfo(string root)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Bundled build-tool preparation currently targets Linux.");
        var script = Path.Combine(root, "scripts", "linux", "deps.sh");
        if (!File.Exists(script)) throw new FileNotFoundException("The pinned Linux build-tool setup script was not found.", script);
        var info = new ProcessStartInfo("bash")
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        info.ArgumentList.Add(script);
        info.Environment["DEPS"] = HAL_CacheDirectory;
        return info;
    }

    public static void HAL_ConfigureBuild(ProcessStartInfo info, string root)
    {
        var bundledPython = HAL_BundledPython(root);
        if (bundledPython is not null)
        {
            info.FileName = bundledPython;
            info.Environment["RX_CPM"] = Path.Combine(root, "cpm");
            return;
        }
        var directory = HAL_FindPreparedDirectory(root);
        if (directory is null) return;
        info.FileName = Path.Combine(directory, "python", "bin", "python3");
        info.Environment["EMSDK"] = Path.Combine(directory, "emsdk");
        info.Environment["RX_CPM"] = Path.Combine(directory, "cpm");
        info.Environment["HOOKSHOT_FFMPEG"] = Path.Combine(directory, "ffmpeg");
        info.Environment["HOOKSHOT_SDL2"] = Path.Combine(directory, "sdl2", "libSDL2-2.0.so.0");
    }
}
