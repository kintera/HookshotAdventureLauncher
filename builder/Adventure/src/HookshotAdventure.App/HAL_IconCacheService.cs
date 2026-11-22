using System.Text.Json;
using Avalonia.Media.Imaging;
using HookshotAdventure.Core;

namespace HookshotAdventure.App;

/// <summary>
/// Read-only adapter for Hookshot v0.8's PRIVATE PNG cache. Game artwork is never
/// shipped with this project. The local builder decodes the installed game. Only indexed, verified local files load.
/// </summary>
public sealed class HAL_IconCacheService : IDisposable
{
    private readonly IconManifest? _manifest;
    private readonly Dictionary<string, Bitmap> _bitmaps = new(StringComparer.Ordinal);
    private HashSet<string> _roles = new(StringComparer.Ordinal);
    public string? ActiveDirectory { get; private set; }
    public int AvailableCount => _roles.Count;
    public int LoadedCount => _bitmaps.Count;
    public string Status { get; private set; } = "No private save-icon cache selected.";

    public HAL_IconCacheService(IconManifest? manifest)
    {
        _manifest = manifest;
        Reload(null);
    }

    private static bool SafeRole(string role) => role.Length is > 0 and <= 96 &&
        role.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');

    private static bool IsRegularFile(string path)
    {
        try { return File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static string ExpandPath(string path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (path == "~") return home;
        if (path.StartsWith("~/", StringComparison.Ordinal)) return Path.Combine(home, path[2..]);
        return Path.GetFullPath(path);
    }

    private static IEnumerable<string> CandidateRoots(string? preferred)
    {
        if (!string.IsNullOrWhiteSpace(preferred)) yield return ExpandPath(preferred);
        yield return HAL_IconDatabase.CacheDirectory;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var cacheHome = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        if (string.IsNullOrWhiteSpace(cacheHome)) cacheHome = Path.Combine(home, ".cache");
        yield return Path.Combine(cacheHome, "hookshot", "save-assets");
        yield return Path.Combine(home, ".cache", "hookshot", "save-assets");
        yield return Path.Combine(cacheHome, "longshot", "save-assets");
    }

    private (HashSet<string> roles, DateTime written)? ValidateFolder(string folder)
    {
        try
        {
            if (_manifest is null || (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) return null;
            var indexPath = Path.Combine(folder, "index.json");
            if (!IsRegularFile(indexPath) || new FileInfo(indexPath).Length > 1_000_000) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(indexPath));
            var root = doc.RootElement;
            if (!root.TryGetProperty("cache_schema", out var schema) || schema.GetInt32() != 2) return null;
            if (!root.TryGetProperty("roles", out var good) || good.ValueKind != JsonValueKind.Array ||
                !root.TryGetProperty("requested_roles", out var requested) || requested.ValueKind != JsonValueKind.Array ||
                !root.TryGetProperty("failures", out var failures) || failures.ValueKind != JsonValueKind.Object) return null;
            var goodNames = good.EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
            var requestedNames = requested.EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
            var failNames = failures.EnumerateObject().Select(x => x.Name).ToArray();
            if (goodNames.Length == 0 || goodNames.Distinct(StringComparer.Ordinal).Count() != goodNames.Length ||
                requestedNames.Distinct(StringComparer.Ordinal).Count() != requestedNames.Length ||
                goodNames.Concat(failNames).Distinct(StringComparer.Ordinal).Count() != requestedNames.Length ||
                !goodNames.Concat(failNames).ToHashSet(StringComparer.Ordinal).SetEquals(requestedNames) ||
                !requestedNames.All(SafeRole)) return null;
            // A cache generated against a different manifest may still provide
            // a subset of approved icons; never activate unverified roles.
            var roles = new HashSet<string>(StringComparer.Ordinal);
            foreach (var role in goodNames)
            {
                if (!_manifest.HasVerifiedMapping(role)) continue;
                var file = Path.Combine(folder, role + ".png");
                if (!IsRegularFile(file)) continue;
                var length = new FileInfo(file).Length;
                if (length is <= 0 or > 33_554_432) continue;
                roles.Add(role);
            }
            if (roles.Count == 0) return null;
            return (roles, File.GetLastWriteTimeUtc(indexPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                                   or JsonException or ArgumentException or InvalidOperationException) { return null; }
    }

    public void Reload(string? preferred)
    {
        foreach (var b in _bitmaps.Values) b.Dispose();
        _bitmaps.Clear();
        _roles.Clear();
        ActiveDirectory = null;
        if (_manifest is null) { Status = "No icon manifest found."; return; }
        var matches = new List<(string path, HashSet<string> roles, DateTime written)>();
        foreach (var root in CandidateRoots(preferred).Distinct(StringComparer.Ordinal))
        {
            try
            {
                if (!Directory.Exists(root)) continue;
                var options = new[] { root }.Concat(Directory.EnumerateDirectories(root));
                foreach (var path in options)
                {
                    var validated = ValidateFolder(path);
                    if (validated is { } x) matches.Add((path, x.roles, x.written));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { /* try other roots */ }
        }
        if (matches.Count == 0)
        {
            Status = "No usable icon cache found. Choose Rebuild icon database to generate icons from your installed game.";
            return;
        }
        var selected = matches.OrderByDescending(x => x.roles.Count).ThenByDescending(x => x.written).First();
        ActiveDirectory = selected.path;
        _roles = selected.roles;
        Status = $"Found {_roles.Count}/{_manifest.VerifiedCount} approved icon PNGs in {selected.path}";
    }

    public Bitmap? Get(string role)
    {
        role = role switch { "skulltula" => "gold_skulltula", "bottle" => "bottle_empty", "wallet_kokiri" => "wallet_child", _ => role };
        if (ActiveDirectory is null || !_roles.Contains(role) || !SafeRole(role)) return null;
        if (_bitmaps.TryGetValue(role, out var existing)) return existing;
        var path = Path.Combine(ActiveDirectory, role + ".png");
        if (!IsRegularFile(path)) return null;
        try
        {
            var image = new Bitmap(path);
            if (image.PixelSize.Width is < 1 or > 8192 || image.PixelSize.Height is < 1 or > 8192)
            {
                image.Dispose();
                return null;
            }
            _bitmaps[role] = image;
            return image;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidDataException)
        { return null; }
    }

    public void Dispose()
    {
        foreach (var b in _bitmaps.Values) b.Dispose();
        _bitmaps.Clear();
    }
}
