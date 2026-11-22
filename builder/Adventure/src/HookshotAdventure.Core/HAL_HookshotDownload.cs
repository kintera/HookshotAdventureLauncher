using System.Text.Json;
using System.Text.RegularExpressions;

namespace HookshotAdventure.Core;

/// <summary>Locates and downloads the official Hookshot builder; game data is never downloaded.</summary>
public static class HAL_HookshotDownload
{
    public const string ReleaseApi = "https://git.notrexed.dev/api/v1/repos/Hookshot/Hookshot/releases/latest";
    public sealed record Asset(string Name, Uri Url, long Size);

    public static bool ShouldOffer(string? stable, string? development, string? selected) =>
        !new[] { stable, development, selected }.Any(path => !string.IsNullOrWhiteSpace(path) && HookshotGame.IsInstalled(path));

    public static Asset SelectAsset(string json, bool windows)
    {
        using var release = JsonDocument.Parse(json);
        if (!release.RootElement.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("The Hookshot release did not contain a download list.");
        foreach (var item in assets.EnumerateArray())
        {
            if (!item.TryGetProperty("name", out var n) || n.ValueKind != JsonValueKind.String) continue;
            var name = n.GetString() ?? "";
            bool match = windows ? name == "HookshotBuilder.exe" :
                Regex.IsMatch(name, @"\AHookshot-v[0-9A-Za-z][0-9A-Za-z.\-]*-x86_64\.AppImage\z", RegexOptions.CultureInvariant);
            if (!match || !item.TryGetString("browser_download_url", out var link) ||
                !Uri.TryCreate(link, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
                !string.Equals(uri.Host, "git.notrexed.dev", StringComparison.OrdinalIgnoreCase) ||
                !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
                !uri.AbsolutePath.StartsWith("/Hookshot/Hookshot/releases/download/", StringComparison.Ordinal) ||
                !uri.AbsolutePath.EndsWith("/" + Uri.EscapeDataString(name), StringComparison.Ordinal) ||
                !item.TryGetProperty("size", out var sizeValue) || !sizeValue.TryGetInt64(out var size) || size <= 0)
                continue;
            return new Asset(name, uri, size);
        }
        throw new InvalidDataException("The latest official Hookshot release does not offer a builder for this platform.");
    }

    private static bool TryGetString(this JsonElement item, string name, out string value)
    {
        value = "";
        if (!item.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String) return false;
        value = property.GetString() ?? "";
        return value.Length > 0;
    }

    public static async Task<Asset> LatestAsync(HttpClient client, bool windows, CancellationToken cancellationToken = default)
    {
        using var response = await client.GetAsync(ReleaseApi, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 1024 * 1024) throw new InvalidDataException("The release response is too large.");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var limited = new MemoryStream();
        byte[] buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer, cancellationToken)) != 0)
        {
            if (limited.Length + count > 1024 * 1024) throw new InvalidDataException("The release response is too large.");
            limited.Write(buffer, 0, count);
        }
        return SelectAsset(System.Text.Encoding.UTF8.GetString(limited.ToArray()), windows);
    }

    public static async Task DownloadAsync(HttpClient client, Asset asset, string destination, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        string target = Path.GetFullPath(destination);
        if (File.Exists(target) || Directory.Exists(target)) throw new IOException("The selected file already exists. Choose a different name.");
        string temporary = target + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            using var response = await client.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length && length != asset.Size)
                throw new InvalidDataException("The download size does not match the official release.");
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                byte[] buffer = new byte[81920]; long total = 0; int count;
                while ((count = await input.ReadAsync(buffer, cancellationToken)) != 0)
                {
                    total += count;
                    if (total > asset.Size) throw new InvalidDataException("The download exceeded its declared size.");
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                    progress?.Report((double)total / asset.Size);
                }
                if (total != asset.Size) throw new InvalidDataException("The download ended before all bytes arrived.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.Move(temporary, target);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
