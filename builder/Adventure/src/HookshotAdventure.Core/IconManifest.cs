using System.Text.Json;
namespace HookshotAdventure.Core;

public sealed record IconMapping(bool Verified, string? Archive, string? Texture, string? Output);

public sealed class IconManifest
{
    public IReadOnlyDictionary<string, IconMapping> Icons { get; }
    public int VerifiedCount => Icons.Values.Count(x => x.Verified);
    public int PendingCount => Icons.Count - VerifiedCount;
    public IconManifest(string filename)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(filename));
        if (!document.RootElement.TryGetProperty("icons", out var values) || values.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Expected an icon manifest containing an icons object.");
        var found = new Dictionary<string, IconMapping>(StringComparer.Ordinal);
        foreach (var element in values.EnumerateObject())
        {
            var v = element.Value;
            static string? Text(JsonElement e, string name) => e.TryGetProperty(name, out var child) && child.ValueKind == JsonValueKind.String ? child.GetString() : null;
            var verified = v.TryGetProperty("verified", out var ver) && ver.ValueKind == JsonValueKind.True;
            found[element.Name] = new IconMapping(verified, Text(v, "archive"), Text(v, "texture"), Text(v, "output"));
        }
        Icons = found;
    }
    public bool HasVerifiedMapping(string role) => Icons.TryGetValue(role, out var mapping) && mapping.Verified &&
        !string.IsNullOrWhiteSpace(mapping.Archive) && !string.IsNullOrWhiteSpace(mapping.Texture);
    // Decoded images remain in the user's private local cache and are never distributed.
}
