using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HookshotAdventure.Core;

/// <summary>Builds profile-local clothing and sword overlays from local game resources.</summary>
public static class HAL_CosmeticOverlay
{
    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    public static string Prepare(string game, string profile, HAL_TunicCosmetics settings, string? baseOverlay, string codec)
    {
        game = Path.GetFullPath(game); profile = Path.GetFullPath(profile);
        if (!Directory.Exists(profile) || ProfileRepository.IsLink(profile) || ProfileRepository.IsLink(Path.Combine(profile, "user")))
            throw new InvalidDataException("Invalid isolated profile.");
        if (!settings.Enabled) return baseOverlay ?? "";
        if (!File.Exists(codec)) throw new InvalidDataException("The ASTC codec is missing.");
        string seedPath = Path.Combine(profile, "native-randomizer", "seed.json");
        if (ProfileRepository.IsLink(seedPath)) throw new InvalidDataException("Linked seed metadata.");
        string seed = Path.GetFileName(profile);
        if (File.Exists(seedPath))
        {
            using var record = JsonDocument.Parse(File.ReadAllBytes(seedPath));
            if (record.RootElement.TryGetProperty("seed", out var s) && s.GetString() is string candidate && candidate.Length == 16 && candidate.All(Uri.IsHexDigit)) seed = candidate;
        }
        var random = new HAL_PythonRandom(seed);
        foreach (var sword in new[] { settings.Swords.Kokiri, settings.Swords.Master, settings.Swords.Biggoron })
            if (sword.TrailColor.Length != 7 || sword.TrailColor[0] != '#' || sword.TrailColor.Skip(1).Any(c => !Uri.IsHexDigit(c)) ||
                sword.TrailMode is not ("locked" or "random_launch" or "seed"))
                throw new InvalidDataException("Invalid sword trail preference.");
        string Choose(string value, string mode)
        {
            if (value.Length != 7 || value[0] != '#' || value.Skip(1).Any(c => !Uri.IsHexDigit(c))) throw new InvalidDataException("Invalid cosmetic color.");
            if (mode == "locked") return value.ToUpperInvariant();
            if (mode is not ("random_launch" or "seed")) throw new InvalidDataException("Invalid cosmetic color mode.");
            int Next() => mode == "seed" ? random.ColorComponent() : RandomNumberGenerator.GetInt32(24, 232);
            return $"#{Next():X2}{Next():X2}{Next():X2}";
        }
        var colors = new Dictionary<string,string>(StringComparer.Ordinal)
        {
            ["kokiri_tunic_color"] = Choose(settings.Kokiri.Color, settings.Kokiri.Mode),
            ["goron_tunic_color"] = Choose(settings.Goron.Color, settings.Goron.Mode),
            ["zora_tunic_color"] = Choose(settings.Zora.Color, settings.Zora.Mode)
        };
        var swords = new Dictionary<string,string>(StringComparer.Ordinal)
        {
            ["kokiri"] = Choose(settings.Swords.Kokiri.BladeColor, settings.Swords.Kokiri.BladeMode),
            ["master"] = Choose(settings.Swords.Master.BladeColor, settings.Swords.Master.BladeMode),
            ["biggoron"] = Choose(settings.Swords.Biggoron.BladeColor, settings.Swords.Biggoron.BladeMode)
        };
        var trails = HAL_SwordTrailColors.Resolve(settings.Swords, seed);
        string romfs = Path.Combine(game, "romfs"); var dictionaries = HAL_RuntimeOverlay.Dictionaries(romfs);
        byte[] Read(string name) => File.ReadAllBytes(Path.Combine(romfs, name.Replace('/', Path.DirectorySeparatorChar)));
        var additions = new SortedDictionary<string, byte[]>(StringComparer.Ordinal); var aliases = new Dictionary<string,string>(StringComparer.Ordinal);
        void Texture(string name, string? alias, string kind, string color, (double,double,double,double)? blade = null)
        {
            string source = "Tex/" + name + ".bntx.zs", destination = "Tex/" + (alias ?? name) + ".bntx.zs";
            byte[] raw = HAL_Zstd.Unpack(Read(source), dictionaries); var view = new HAL_TunicTexture(raw);
            if (view.Name != name) throw new InvalidDataException("Texture name differs from its resource.");
            additions.Add(destination, HAL_Zstd.Compress(HAL_TexturePatch.Apply(raw, kind, color, codec, alias, blade).Data));
            if (alias is not null) aliases.Add(destination, source);
        }
        foreach (var (name, alias) in new[] { ("Mt_Vest_Player_Child_Alb", (string?)null), ("Mt_Cloth_Parts_Player_Child_Alb", null),
            ("Mt_Vest_Player_Adult_Alb", null), ("Mt_Angin_Acs_Player_Adult_Alb", null), ("Angin_Tile_A_Alb", "HS_Kokiri_01_Alb"),
            ("Angin_Tile_A_Dark_Alb", "HS_Kokiri_01_Dark_Alb") }) Texture(name, alias, "kokiri_tunic_color", colors["kokiri_tunic_color"]);
        Texture("Mt_Angin_Armor_Adult_Goron_Alb", null, "goron_tunic_color", colors["goron_tunic_color"]);
        Texture("Mt_Angin_Armor_Adult_Zora_Alb", null, "zora_tunic_color", colors["zora_tunic_color"]);
        Texture("Mt_Weapon_Sword_Kokiri_Alb", null, "sword_blade_color", swords["kokiri"], (0,.18,.45,1));
        Texture("Mt_Weapon_Sword_Master_Alb", null, "sword_blade_color", swords["master"], (0,0,.30,.96));
        foreach (string name in new[] { "Mt_Weapon_Sword_Biggoron_Alb.00", "Mt_Weapon_Sword_Biggoron_Alb.01" })
            Texture(name, null, "sword_blade_color", swords["biggoron"], (0,0,.27,.92));
        string? hammerColor = settings.HammerEnabled ? Choose(settings.Hammer.Color, settings.Hammer.Mode) : null;
        if (hammerColor is not null)
            Texture("Mt_Item_Hammer_Alb", null, "hammer_color", hammerColor, (0,0,1,1));
        foreach (var (resource, modelAliases) in new[]
        {
            ("Model/Player_Child.Player_Child.bfres.zs", new Dictionary<string, HAL_PlayerModel.Alias> {
                ["Angin_Tile_A_Alb"] = new("HS_Kokiri_01_Alb", new HashSet<string> { "Mt_Cloth" }),
                ["Angin_Tile_A_Dark_Alb"] = new("HS_Kokiri_01_Dark_Alb", new HashSet<string> { "Mt_Cloth_Pants" }) }),
            ("Model/Player_Adult.Player_Adult.bfres.zs", new Dictionary<string, HAL_PlayerModel.Alias> {
                ["Angin_Tile_A_Alb"] = new("HS_Kokiri_01_Alb", new HashSet<string> { "Mt_Angin" }) })
        }) additions.Add(resource, HAL_Zstd.Compress(HAL_PlayerModel.Patch(HAL_Zstd.Unpack(Read(resource), dictionaries), modelAliases).Data));
        var files = new SortedDictionary<string,byte[]>(StringComparer.Ordinal);
        if (baseOverlay is not null)
        {
            if (!Directory.Exists(baseOverlay) || ProfileRepository.IsLink(baseOverlay)) throw new InvalidDataException("Invalid base overlay.");
            var pending = new Stack<string>(); pending.Push(baseOverlay);
            while (pending.Count > 0) foreach (string path in Directory.EnumerateFileSystemEntries(pending.Pop()))
            {
                if (ProfileRepository.IsLink(path)) throw new InvalidDataException("Linked base overlay resource.");
                if (Directory.Exists(path)) pending.Push(path); else files.Add(Path.GetRelativePath(baseOverlay,path).Replace('\\','/'), File.ReadAllBytes(path));
            }
        }
        foreach (var (name, bytes) in additions) if (!files.TryAdd(name, bytes)) throw new InvalidDataException("Conflicting cosmetic resource.");
        foreach (var (sword, name) in new[] { ("kokiri", "Kokiri"), ("master", "Master"), ("biggoron", "Biggoron") })
        {
            string path = $"Effect/Weapon_Sword_{name}.Nin_Ounce_NVN2.vsetb.byml.zs";
            byte[] original = HAL_Zstd.Unpack(files.TryGetValue(path, out var existingEffect) ? existingEffect : Read(path), dictionaries);
            files[path] = HAL_Zstd.Compress(HAL_SwordTrailPatch.Apply(original, trails[sword]));
        }
        string[] tables = Directory.GetFiles(Path.Combine(romfs,"System","Resource"), "ResourceSizeTable.*.rsizetable.zs");
        if (tables.Length != 1) throw new InvalidDataException("Expected one resource size table.");
        string table = Path.GetRelativePath(romfs,tables[0]).Replace('\\','/');
        bool hasPreviousTable = files.TryGetValue(table, out var previous);
        byte[] tableData = HAL_Zstd.Unpack(hasPreviousTable ? previous! : Read(table), dictionaries);
        string prompts = Path.Combine(game, "prompts", "rsizetable.zs");
        if (!hasPreviousTable && File.Exists(prompts))
        {
            byte[] promptTable = HAL_Zstd.Unpack(File.ReadAllBytes(prompts), dictionaries);
            tableData = new HAL_ResourceSizeTable(tableData).Patch(new Dictionary<string, (long, long)>(), promptTable);
        }
        files[table] = HAL_Zstd.Compress(new HAL_ResourceSizeTable(tableData).CloneEntries(aliases));
        if (files.TryGetValue("runtime.json", out var runtime))
        {
            var record = JsonNode.Parse(runtime)!.AsObject(); record["output_hashes"]![table] = Hash(files[table]);
            foreach (string name in new[] { "Kokiri", "Master", "Biggoron" })
            {
                string path = $"Effect/Weapon_Sword_{name}.Nin_Ounce_NVN2.vsetb.byml.zs";
                if (record["output_hashes"]!.AsObject().ContainsKey(path)) record["output_hashes"]![path] = Hash(files[path]);
            }
            files["runtime.json"] = System.Text.Encoding.UTF8.GetBytes(record.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        }
        var hashes = new JsonObject(); foreach (var (name, bytes) in files) hashes[name] = Hash(bytes);
        var payload = new JsonObject { ["enabled"] = settings.Enabled, ["tunics"] = new JsonObject {
            ["kokiri"] = JsonSerializer.SerializeToNode(settings.Kokiri), ["goron"] = JsonSerializer.SerializeToNode(settings.Goron), ["zora"] = JsonSerializer.SerializeToNode(settings.Zora) },
            ["swords"] = JsonSerializer.SerializeToNode(settings.Swords), ["hammer_enabled"] = settings.HammerEnabled,
            ["hammer"] = JsonSerializer.SerializeToNode(settings.Hammer) };
        var identity = new JsonObject { ["format"] = "player-and-sword-textures-v6", ["profile"] = Path.GetFileName(profile), ["settings"] = payload,
            ["colours"] = JsonSerializer.SerializeToNode(colors), ["sword_blade_colours"] = JsonSerializer.SerializeToNode(swords),
            ["sword_trail_colours"] = JsonSerializer.SerializeToNode(trails),
            ["hammer_colour"] = hammerColor,
            ["output_hashes"] = hashes, ["resource_count"] = additions.Count + 3, ["codec_sha256"] = Hash(File.ReadAllBytes(codec)) };
        using var json = JsonDocument.Parse(identity.ToJsonString()); string hash = HAL_CanonicalJson.Digest(json.RootElement);
        string root = Path.Combine(profile,"cosmetic-runtime"); if (ProfileRepository.IsLink(root)) throw new InvalidDataException("Linked cosmetic directory.");
        Directory.CreateDirectory(root); string target = Path.Combine(root, hash);
        if (Directory.Exists(target))
        {
            if (ProfileRepository.IsLink(target)) throw new InvalidDataException("Linked cosmetic overlay.");
            var actual = new HashSet<string>(StringComparer.Ordinal); var pending = new Stack<string>(); pending.Push(target);
            while (pending.Count > 0) foreach (string path in Directory.EnumerateFileSystemEntries(pending.Pop()))
            {
                if (ProfileRepository.IsLink(path)) throw new InvalidDataException("Linked cosmetic resource.");
                if (Directory.Exists(path)) pending.Push(path); else actual.Add(Path.GetRelativePath(target,path).Replace('\\','/'));
            }
            if (!actual.SetEquals(files.Keys.Append("cosmetics.json"))) throw new InvalidDataException("Cosmetic overlay contents differ.");
            using var existing = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(target,"cosmetics.json")));
            if (HAL_CanonicalJson.Serialize(existing.RootElement) != HAL_CanonicalJson.Serialize(json.RootElement) ||
                files.Any(x => Hash(File.ReadAllBytes(Path.Combine(target,x.Key.Replace('/',Path.DirectorySeparatorChar)))) != Hash(x.Value)))
                throw new InvalidDataException("Cosmetic overlay was modified.");
            return target;
        }
        string stage = Path.Combine(root,".tunic-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage);
        try
        {
            foreach (var (name, bytes) in files) { string path = Path.Combine(stage,name.Replace('/',Path.DirectorySeparatorChar)); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path,bytes); }
            File.WriteAllText(Path.Combine(stage,"cosmetics.json"),identity.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
            Directory.Move(stage,target);
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage,true); }
        return target;
    }
}
