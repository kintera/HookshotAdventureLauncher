using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HookshotAdventure.Core;

/// <summary>Legacy configuration-only manifest; never authorizes gameplay.</summary>
public static class HAL_RandomizerPreview
{
    private static readonly Dictionary<string, bool> Defaults = new(StringComparer.Ordinal)
    {
        ["native_chest_randomization"] = false, ["skip_new_game_intro"] = false, ["shuffle_chests"] = true,
        ["shuffle_npc_rewards"] = true, ["shuffle_heart_pieces"] = true, ["shuffle_songs"] = true,
        ["shuffle_shops"] = false, ["shuffle_skulltulas"] = false, ["start_with_dungeon_maps_compasses"] = false
    };
    public static JsonObject Create(string seed, JsonElement? options = null)
    {
        string normalized = HAL_RandomizerSeedService.NormalizeSeed(seed), scope = "kokiri_deku";
        var values = new Dictionary<string, bool>(Defaults, StringComparer.Ordinal);
        if (options is JsonElement input)
        {
            if (input.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid preview settings.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in input.EnumerateObject())
            {
                if (!seen.Add(property.Name)) throw new InvalidDataException("Duplicate preview setting.");
                if (property.Name == "chest_scope")
                {
                    scope = property.Value.GetString() ?? "";
                    if (scope is not ("kokiri_deku" or "childhood" or "full_game")) throw new InvalidDataException("Unsupported chest scope.");
                }
                else
                {
                    if (!values.ContainsKey(property.Name) || property.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                        throw new InvalidDataException("Unsupported preview setting.");
                    values[property.Name] = property.Value.GetBoolean();
                }
            }
        }
        var settings = new JsonObject { ["chest_scope"] = scope };
        foreach (var (name, value) in values) settings[name] = value;
        // The preview format uses compact sorted JSON, unlike full seed documents.
        var sorted = new SortedDictionary<string, object>(StringComparer.Ordinal) { ["chest_scope"] = scope };
        foreach (var (name, value) in values) sorted[name] = value;
        string text = JsonSerializer.Serialize(new SortedDictionary<string, object>(StringComparer.Ordinal) { ["seed"] = normalized, ["settings"] = sorted });
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..12];
        return new JsonObject { ["schema_version"] = 1, ["engine_status"] = "configuration_only", ["seed"] = normalized,
            ["settings"] = settings, ["settings_hash"] = hash, ["gameplay_enabled"] = false,
            ["notes"] = "Configuration preview only. No game item placements or hooks installed." };
    }
    public static JsonObject Validate(JsonElement manifest)
    {
        if (manifest.GetProperty("schema_version").GetInt32() != 1 || manifest.GetProperty("gameplay_enabled").ValueKind != JsonValueKind.False)
            throw new InvalidDataException("Unsupported preview manifest.");
        return Create(manifest.GetProperty("seed").GetString() ?? "", manifest.TryGetProperty("settings", out var settings) ? settings : null);
    }
}
