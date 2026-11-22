using System.Text.Json;
using System.Security.Cryptography;

namespace HookshotAdventure.Core;

/// <summary>Checks seed document and plan identity without reading or changing game data.</summary>
public static class HAL_SeedIdentity
{
    private static string SafeResource(string root, string name)
    {
        if (string.IsNullOrEmpty(name) || Path.IsPathRooted(name) || name.Contains('\\') ||
            name.Split('/').Any(part => part is ".." or "" or "."))
            throw new InvalidDataException("Invalid seed resource path.");
        string path = root;
        foreach (string part in name.Split('/'))
        {
            path = Path.Combine(path, part);
            if (ProfileRepository.IsLink(path)) throw new InvalidDataException("Symlink in generated seed resource.");
        }
        return path;
    }

    public static string ValidateOverlay(JsonElement record, string profileId, string randomizerRoot)
    {
        Validate(record, profileId);
        if (ProfileRepository.IsLink(randomizerRoot)) throw new InvalidDataException("Symlinked randomizer directory.");
        string overlay = SafeResource(randomizerRoot, record.GetProperty("overlay").GetString()!);
        if (!Directory.Exists(overlay)) throw new InvalidDataException("Generated seed overlay is missing.");
        string bindingPath = SafeResource(randomizerRoot, "binding.json");
        using (var binding = JsonDocument.Parse(File.ReadAllBytes(bindingPath)))
            if (HAL_CanonicalJson.Serialize(binding.RootElement) != HAL_CanonicalJson.Serialize(record.GetProperty("binding")))
                throw new InvalidDataException("Generated seed binding file differs.");
        var metadata = record.GetProperty("metadata");
        using (var manifest = JsonDocument.Parse(File.ReadAllBytes(SafeResource(overlay, "manifest.json"))))
            if (HAL_CanonicalJson.Serialize(manifest.RootElement) != HAL_CanonicalJson.Serialize(metadata))
                throw new InvalidDataException("Generated seed overlay manifest differs.");
        var expected = new HashSet<string>(StringComparer.Ordinal) { "manifest.json" };
        foreach (var resource in metadata.GetProperty("output_hashes").EnumerateObject())
        {
            if (!expected.Add(resource.Name)) throw new InvalidDataException("Duplicate generated seed resource.");
            string path = SafeResource(overlay, resource.Name);
            using var stream = File.OpenRead(path);
            string actualHash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if (actualHash != resource.Value.GetString()) throw new InvalidDataException("Generated seed resource was modified.");
        }
        var actual = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>(); pending.Push(overlay);
        while (pending.Count != 0)
            foreach (string path in Directory.EnumerateFileSystemEntries(pending.Pop()))
            {
                if (ProfileRepository.IsLink(path)) throw new InvalidDataException("Symlink in generated seed overlay.");
                if (Directory.Exists(path)) pending.Push(path);
                else if (File.Exists(path)) actual.Add(Path.GetRelativePath(overlay, path).Replace('\\', '/'));
                else throw new InvalidDataException("Unsupported generated seed resource.");
            }
        if (!actual.SetEquals(expected)) throw new InvalidDataException("Unexpected or missing generated seed resource.");
        return overlay;
    }

    public static void ValidateSourceResources(JsonElement record, string romfs, IReadOnlyList<byte[]> dictionaries)
    {
        foreach (var resource in record.GetProperty("source_hashes").EnumerateObject())
        {
            // The selected ROMFS root itself may be a read-only asset link;
            // every component below it is checked by SafeResource.
            byte[] packed = File.ReadAllBytes(SafeResource(romfs, resource.Name));
            byte[] raw = HAL_Zstd.Unpack(packed, dictionaries);
            string actual = Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant();
            if (actual != resource.Value.GetString()) throw new InvalidDataException("Game resources changed since seed generation.");
        }
    }

    public static void Validate(JsonElement record, string profileId)
    {
        try
        {
            if (record.GetProperty("schema_version").GetInt32() != 1 ||
                record.GetProperty("file_type").GetString() != "Hookshot generated chest seed" ||
                record.GetProperty("content_hash").GetString() != HAL_CanonicalJson.DigestWithoutProperty(record, "content_hash"))
                throw new InvalidDataException("Generated seed JSON is invalid or modified.");
            var binding = record.GetProperty("binding"); var metadata = record.GetProperty("metadata");
            var plan = metadata.GetProperty("plan");
            string? overlay = record.GetProperty("overlay").GetString();
            if (overlay is null || overlay.Length != 64 || overlay.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
                throw new InvalidDataException("Invalid generated seed overlay ID.");
            if (binding.GetProperty("profile_id").GetString() != profileId ||
                HAL_CanonicalJson.Serialize(binding) != HAL_CanonicalJson.Serialize(metadata.GetProperty("binding")))
                throw new InvalidDataException("Generated seed does not match the profile binding.");
            if (plan.GetProperty("schema_version").GetInt32() != 4 ||
                plan.GetProperty("plan_hash").GetString() != HAL_CanonicalJson.DigestWithoutProperty(plan, "plan_hash") ||
                binding.GetProperty("plan_hash").GetString() != plan.GetProperty("plan_hash").GetString() ||
                record.GetProperty("seed").GetString() != plan.GetProperty("seed").GetString())
                throw new InvalidDataException("Generated seed plan identity mismatch.");
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException("Generated seed JSON is malformed.", ex);
        }
    }
}
