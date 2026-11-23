using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace HookshotAdventure.Core;

/// <summary>Acquire the pinned Arm codec only when a local or bundled copy is unavailable.</summary>
public static class HAL_AstcDownload
{
    // Arm publishes the archive checksums with this release; no game resources are sent to the download server.
    private const string Version = "5.7.1";
    private const string LicenseHash = "494accc32e50eb523a0e384d0ae6d4b702db867a89d6971216760e92b240ee12";
    private static readonly SemaphoreSlim Gate = new(1, 1);
    public static string CacheDirectory => Path.Combine(ProfileRepository.DefaultRoot(), "tools", "astcenc", Version);

    private static async Task<byte[]> Fetch(HttpClient client, string url, int maximum)
    {
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > maximum) throw new InvalidDataException("ASTC download is too large.");
        await using var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[65536];
        int n;
        while ((n = await input.ReadAsync(buffer).ConfigureAwait(false)) != 0)
        {
            if (output.Length + n > maximum) throw new InvalidDataException("ASTC download is too large.");
            output.Write(buffer, 0, n);
        }
        return output.ToArray();
    }

    private static void CheckHash(byte[] bytes, string expected)
    {
        if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("ASTC download did not match the expected checksum.");
    }

    public static async Task<string> EnsureAsync(string root, HttpClient? client = null, string? cacheDirectory = null,
        string? applicationDirectory = null)
    {
        try { return HAL_AstcCodec.Find(root, applicationDirectory, cacheDirectory is null); }
        catch (InvalidOperationException) { }
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64 || !(OperatingSystem.IsLinux() || OperatingSystem.IsWindows()))
            throw new InvalidOperationException("Automatic ASTC setup supports 64-bit x86 Linux and Windows. Install astcenc or set HOOKSHOT_ASTCENC.");
        bool windows = OperatingSystem.IsWindows();
        string name = windows ? "astcenc-sse2.exe" : "astcenc-sse2";
        string hash = windows ? "117ed7cc2b0cc4f3d5774eaa1bde3a2ba75695c2f490e84e4a08d1726c963db8" :
            "f1d576f0801e7701548da78d782f4bf70dfde3673a238d1a2ac0ebd3511b2f37";
        string directory = Path.GetFullPath(cacheDirectory ?? CacheDirectory), output = Path.Combine(directory, name);
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (File.Exists(output)) return output;
            using var owned = client is null ? new HttpClient { Timeout = TimeSpan.FromSeconds(90) } : null;
            client ??= owned!;
            string platform = windows ? "windows-x64" : "linux-x64";
            byte[] zip = await Fetch(client, $"https://github.com/ARM-software/astc-encoder/releases/download/{Version}/astcenc-{Version}-{platform}.zip", 32 * 1024 * 1024).ConfigureAwait(false);
            CheckHash(zip, hash);
            byte[] license = await Fetch(client, $"https://raw.githubusercontent.com/ARM-software/astc-encoder/{Version}/LICENSE.txt", 128 * 1024).ConfigureAwait(false);
            CheckHash(license, LicenseHash);
            using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
            var entry = archive.Entries.SingleOrDefault(x => x.FullName == "bin/" + name)
                ?? throw new InvalidDataException("ASTC archive has no compatible codec.");
            if (entry.Length is <= 0 or > 16 * 1024 * 1024) throw new InvalidDataException("ASTC codec has an invalid size.");
            Directory.CreateDirectory(directory);
            string temp = Path.Combine(directory, ".astcenc-" + Guid.NewGuid().ToString("N"));
            try
            {
                await using (var source = entry.Open())
                await using (var target = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    await source.CopyToAsync(target).ConfigureAwait(false);
                if (!windows) File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                await File.WriteAllBytesAsync(Path.Combine(directory, "LICENSE.txt"), license).ConfigureAwait(false);
                try { File.Move(temp, output); }
                catch (IOException) when (File.Exists(output)) { }
                return output;
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        catch (HttpRequestException ex) { throw new InvalidOperationException("Could not download the ASTC codec. Check your connection and try again.", ex); }
        catch (TaskCanceledException ex) { throw new InvalidOperationException("Downloading the ASTC codec timed out. Try again.", ex); }
        finally { Gate.Release(); }
    }
}
