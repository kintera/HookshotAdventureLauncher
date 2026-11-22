using System.Text.Json;
using System.Text.Json.Serialization;

namespace HookshotAdventure.Core;

public sealed class ProfileRecord
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("kind")] public string Kind { get; set; } = "vanilla";
    [JsonPropertyName("seed")] public string Seed { get; set; } = "";
    [JsonPropertyName("user_dir")] public string UserDir { get; set; } = "";
    [JsonPropertyName("external")] public bool External { get; set; }
    [JsonPropertyName("created_at")] public string CreatedAt { get; set; } = "";
    [JsonPropertyName("last_played")] public string LastPlayed { get; set; } = "";
    [JsonIgnore] public string DisplayText => $"{Name}  ·  {(IsRandomized ? "Rando · " + Seed : Kind == "randomizer-planned" ? "Rando configuration only" : "Vanilla")}  ·  {(External ? "External saves" : "Isolated")}";
    [JsonIgnore] public bool IsRandomizerConfigured => Kind == "randomizer-planned";
    [JsonIgnore] public bool IsRandomized => Kind == "randomizer" && Seed.Length == 16;
}

public sealed class ProfileIndex
{
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("game_dir")] public string GameDir { get; set; } = "";
    [JsonPropertyName("profiles")] public List<ProfileRecord> Profiles { get; set; } = new();
    [JsonPropertyName("launcher_slots")] public string?[] LauncherSlots { get; set; } = new string?[3];
    [JsonPropertyName("imported_from")] public string? ImportedFrom { get; set; }
    [JsonPropertyName("legacy_hookshot_game_dir")] public string? LegacyHookshotGameDir { get; set; }
    [JsonPropertyName("hidden_external_dirs")] public List<string> HiddenExternalDirs { get; set; } = new();
}

public sealed class ProfileRepository
{
    private readonly Func<bool> _gameIsRunning;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public string Root { get; }
    public string IndexPath => Path.Combine(Root, "profiles.json");
    public ProfileIndex Index { get; private set; }
    public bool ImportedOnFirstRun { get; private set; }

    public static string DefaultRoot()
    {
        if (OperatingSystem.IsWindows())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HookshotAdventure");
        var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        return Path.Combine(!string.IsNullOrWhiteSpace(xdg) ? xdg : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share"), "hookshot-adventure");
    }

    public static string LegacyHookshotIndex()
    {
        if (OperatingSystem.IsWindows())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HookshotProfileManager", "profiles.json");
        var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        return Path.Combine(!string.IsNullOrWhiteSpace(xdg) ? xdg : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share"), "hookshot-profile-manager", "profiles.json");
    }

    public ProfileRepository(string? root = null, string? legacyPath = null) : this(root, legacyPath, HookshotGame.IsRunning) { }

    internal ProfileRepository(string? root, string? legacyPath, Func<bool> gameIsRunning)
    {
        _gameIsRunning = gameIsRunning ?? throw new ArgumentNullException(nameof(gameIsRunning));
        Root = Path.GetFullPath(root ?? DefaultRoot());
        Directory.CreateDirectory(Root);
        if (File.Exists(IndexPath))
        {
            Index = ReadIndex(IndexPath);
        }
        else
        {
            var old = legacyPath ?? LegacyHookshotIndex();
            if (File.Exists(old))
            {
                // Import ONLY JSON metadata. Preserve the original registry and every save directory.
                Index = ReadIndex(old);
                Index.LegacyHookshotGameDir = Index.GameDir; // Keep the imported Hookshot install path available.
                Index.ImportedFrom = Path.GetFullPath(old);
                ImportedOnFirstRun = true;
            }
            else Index = new ProfileIndex();
            Save();
        }
    }

    private static ProfileIndex ReadIndex(string path)
    {
        var index = JsonSerializer.Deserialize<ProfileIndex>(File.ReadAllText(path), JsonOptions)
                    ?? throw new InvalidDataException("Profile registry is empty or invalid.");
        if (index.Version != 1 || index.Profiles is null)
            throw new InvalidDataException("Unsupported profile registry schema. No save data was changed.");
        index.LauncherSlots = index.LauncherSlots is { Length: 3 } ? index.LauncherSlots : new string?[3];
        if (index.Profiles.Any(p => string.IsNullOrWhiteSpace(p.Id) || string.IsNullOrWhiteSpace(p.UserDir)) ||
            index.Profiles.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != index.Profiles.Count)
            throw new InvalidDataException("Invalid profile entries or duplicate identifiers.");
        return index;
    }

    private void Save()
    {
        Directory.CreateDirectory(Root);
        var tmp = Path.Combine(Root, "profiles-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(tmp, JsonSerializer.Serialize(Index, JsonOptions) + "\n");
            File.Move(tmp, IndexPath, true);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }

    public IReadOnlyList<ProfileRecord> All() => Index.Profiles.ToList();

    public IReadOnlyList<ProfileRecord> RecoverUnlisted()
    {
        var root = Path.Combine(Root, "profiles");
        if (!Directory.Exists(root) || IsLink(root)) return Array.Empty<ProfileRecord>();
        var known = new HashSet<string>(Index.Profiles.Select(p => Path.GetFullPath(p.UserDir)),
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var knownIds = new HashSet<string>(Index.Profiles.Select(p => p.Id), StringComparer.OrdinalIgnoreCase);
        var recovered = new List<ProfileRecord>();
        foreach (var directory in Directory.EnumerateDirectories(root).OrderBy(p => p, StringComparer.Ordinal))
        {
            if (IsLink(directory)) continue;
            var id = Path.GetFileName(directory);
            if (id.Length != 32 || id.Any(c => !Uri.IsHexDigit(c)) || knownIds.Contains(id)) continue;
            var user = Path.Combine(directory, "user");
            if (!Directory.Exists(user) || IsLink(user) || !HasProgressSave(user) || !known.Add(Path.GetFullPath(user))) continue;
            var record = new ProfileRecord
            {
                Id = id,
                Name = "Recovered Adventure " + (recovered.Count + 1),
                UserDir = user,
                Kind = "vanilla",
                CreatedAt = Directory.GetCreationTimeUtc(directory).ToString("yyyy-MM-ddTHH:mm:ss+00:00")
            };
            var seedFile = Path.Combine(directory, "native-randomizer", "seed.json");
            try
            {
                if (File.Exists(seedFile) && !IsLink(seedFile))
                {
                    using var json = JsonDocument.Parse(File.ReadAllText(seedFile));
                    var seed = json.RootElement.GetProperty("seed").GetString();
                    var boundId = json.RootElement.GetProperty("binding").GetProperty("profile_id").GetString();
                    if (seed is { Length: 16 } && boundId == id && seed.All(Uri.IsHexDigit))
                    {
                        record.Kind = "randomizer";
                        record.Seed = seed.ToUpperInvariant();
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException) { }
            recovered.Add(record);
        }
        if (recovered.Count == 0) return recovered;
        Index.Profiles.AddRange(recovered);
        try { Save(); }
        catch { Index.Profiles.RemoveAll(p => recovered.Contains(p)); throw; }
        return recovered;
    }

    public (int Recovered, int RemovedStale, int RepairedPaths) ScanOnStartup()
    {
        var profilesRoot = Path.Combine(Root, "profiles");
        if (IsLink(Root) || IsLink(profilesRoot)) return (0, 0, 0);

        var originalProfiles = Index.Profiles.ToList();
        var originalSlots = Index.LauncherSlots.ToArray();
        var removed = 0;
        var repaired = 0;
        foreach (var profile in Index.Profiles.ToList())
        {
            if (profile.External || profile.Id.Length != 32 || profile.Id.Any(c => !Uri.IsHexDigit(c))) continue;
            var profileBase = Path.GetFullPath(Path.Combine(profilesRoot, profile.Id));
            var expectedUser = Path.Combine(profileBase, "user");
            if (IsLink(profileBase) || IsLink(expectedUser)) continue;

            var recordedUser = Path.GetFullPath(profile.UserDir);
            if (Directory.Exists(profileBase) && Directory.Exists(expectedUser))
            {
                if (!string.Equals(recordedUser, expectedUser,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                {
                    profile.UserDir = expectedUser;
                    repaired++;
                }
                continue;
            }

            // Keep unusual/out-of-root paths for manual review. Drop only stale isolated records
            // whose recorded directory is gone; no save files are deleted by this scan.
            if (Directory.Exists(recordedUser) && !IsLink(recordedUser)) continue;
            Index.Profiles.Remove(profile);
            removed++;
        }

        var knownProfiles = new HashSet<string>(Index.Profiles.Select(p => p.Id), StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < Index.LauncherSlots.Length; i++)
            if (Index.LauncherSlots[i] is { } id && !knownProfiles.Contains(id)) Index.LauncherSlots[i] = null;

        if (removed > 0 || repaired > 0 || !originalSlots.SequenceEqual(Index.LauncherSlots))
        {
            try { Save(); }
            catch
            {
                Index.Profiles = originalProfiles;
                Index.LauncherSlots = originalSlots;
                throw;
            }
        }

        var recovered = RecoverUnlisted().Count;
        return (recovered, removed, repaired);
    }

    public ProfileRecord Get(string id) => Index.Profiles.FirstOrDefault(p => p.Id == id)
        ?? throw new InvalidOperationException("Unknown adventure profile.");

    public ProfileRecord Duplicate(string id, string name)
    {
        var source = Get(id);
        if (source.IsRandomized) throw new InvalidOperationException("Randomized seeds are bound to their original profile and cannot be copied.");
        if (!ValidName(name)) throw new ArgumentException("Adventure name must be 1–80 characters.");
        var sourceDir = Path.GetFullPath(source.UserDir);
        if (!Directory.Exists(sourceDir) || IsLink(sourceDir)) throw new InvalidOperationException("Source save directory is missing or linked.");
        foreach (var entry in Directory.EnumerateFileSystemEntries(sourceDir, "*", SearchOption.AllDirectories))
            if (IsLink(entry)) throw new InvalidDataException("Profile copy stopped because the source contains a symbolic link.");
        var newId = Guid.NewGuid().ToString("N");
        var profileBase = Path.Combine(Root, "profiles", newId);
        var userDir = Path.Combine(profileBase, "user");
        Directory.CreateDirectory(userDir);
        try
        {
            CopyDirectoryContents(sourceDir, userDir);
            var record = new ProfileRecord
            {
                Id = newId, Name = name.Trim(), Kind = "vanilla", UserDir = userDir,
                External = false, CreatedAt = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss+00:00")
            };
            Index.Profiles.Add(record);
            try { Save(); }
            catch { Index.Profiles.Remove(record); throw; }
            return record;
        }
        catch
        {
            if (Directory.Exists(profileBase) && !IsLink(profileBase)) Directory.Delete(profileBase, true);
            throw;
        }
    }

    public ProfileRecord RestoreSaveBackup(string source, bool legacyFolder)
    {
        var fullSource = Path.GetFullPath(source);
        if (IsLink(fullSource)) throw new InvalidDataException("Backup source cannot be a symbolic link.");
        if (legacyFolder && !Directory.Exists(fullSource)) throw new DirectoryNotFoundException("Legacy backup folder was not found.");
        if (!legacyFolder && (!File.Exists(fullSource) || !string.Equals(Path.GetExtension(fullSource), ".zip", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Choose a compressed ZIP backup.");

        var id = Guid.NewGuid().ToString("N");
        var profileBase = Path.Combine(Root, "profiles", id);
        var userDir = Path.Combine(profileBase, "user");
        Directory.CreateDirectory(userDir);
        try
        {
            if (legacyFolder) CopyDirectoryContents(fullSource, userDir);
            else
            {
                using var archive = System.IO.Compression.ZipFile.OpenRead(fullSource);
                long expandedBytes = 0;
                foreach (var entry in archive.Entries)
                {
                    var relative = entry.FullName.Replace('\\', '/');
                    if (string.IsNullOrWhiteSpace(relative) || relative.StartsWith('/') || relative.Split('/').Any(x => x is "." or ".."))
                        throw new InvalidDataException("Backup contains an unsafe path.");
                    var destination = Path.GetFullPath(Path.Combine(userDir, relative.Replace('/', Path.DirectorySeparatorChar)));
                    var prefix = Path.GetFullPath(userDir) + Path.DirectorySeparatorChar;
                    if (!destination.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                        throw new InvalidDataException("Backup contains a path outside the profile.");
                    if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(destination); continue; }
                    expandedBytes = checked(expandedBytes + entry.Length);
                    if (entry.Length > 1_073_741_824 || expandedBytes > 4_294_967_296)
                        throw new InvalidDataException("Backup is larger than the supported restore limit.");
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    using var input = entry.Open();
                    using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    input.CopyTo(output);
                }
            }
            if (!HasProgressSave(userDir)) throw new InvalidDataException("Backup does not contain a recognized saved game.");
            var name = Path.GetFileNameWithoutExtension(fullSource);
            if (string.IsNullOrWhiteSpace(name)) name = "Restored Adventure";
            if (name.Length > 80) name = name[..80];
            var profile = new ProfileRecord { Id = id, Name = name, Kind = "vanilla", UserDir = userDir,
                CreatedAt = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss+00:00") };
            Index.Profiles.Add(profile);
            try { Save(); }
            catch { Index.Profiles.Remove(profile); throw; }
            return profile;
        }
        catch
        {
            if (Directory.Exists(profileBase) && !IsLink(profileBase)) Directory.Delete(profileBase, true);
            throw;
        }
    }

    private static void CopyDirectoryContents(string source, string destination)
    {
        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            if (IsLink(directory)) throw new InvalidDataException("Symbolic links cannot be copied into an isolated adventure.");
            var target = Path.Combine(destination, Path.GetFileName(directory));
            Directory.CreateDirectory(target);
            CopyDirectoryContents(directory, target);
        }
        foreach (var file in Directory.EnumerateFiles(source))
        {
            if (IsLink(file)) throw new InvalidDataException("Symbolic links cannot be copied into an isolated adventure.");
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: false);
        }
    }

    private static bool ValidName(string name) => !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 80;

    public ProfileRecord Create(string name)
    {
        if (!ValidName(name)) throw new ArgumentException("Adventure name must be 1–80 characters.");
        var id = Guid.NewGuid().ToString("N");
        var profileBase = Path.Combine(Root, "profiles", id);
        var userDir = Path.Combine(profileBase, "user");
        Directory.CreateDirectory(userDir);
        var record = new ProfileRecord
        {
            Id = id, Name = name.Trim(),
            Kind = "vanilla",
            UserDir = userDir,
            External = false,
            CreatedAt = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss+00:00")
        };
        Index.Profiles.Add(record);
        try { Save(); }
        catch
        {
            Index.Profiles.Remove(record);
            if (Directory.Exists(profileBase)) Directory.Delete(profileBase, true);
            throw;
        }
        return record;
    }

    public ProfileRecord CreateRandomized(string name, string seed, Action<string, string> generate)
    {
        if (!ValidName(name)) throw new ArgumentException("Adventure name must be 1–80 characters.");
        if (seed.Length != 16 || seed.Any(c => !Uri.IsHexDigit(c))) throw new ArgumentException("Randomizer seed must be 16 hexadecimal digits.");
        ArgumentNullException.ThrowIfNull(generate);
        var id = Guid.NewGuid().ToString("N");
        var profileBase = Path.Combine(Root, "profiles", id);
        var userDir = Path.Combine(profileBase, "user");
        Directory.CreateDirectory(userDir);
        try
        {
            // Generate and validate the immutable seed before publishing the profile in the registry.
            generate(profileBase, id);
            var seedPath = Path.Combine(profileBase, "native-randomizer", "seed.json");
            if (!File.Exists(seedPath) || IsLink(seedPath))
                throw new InvalidDataException("Seed generator did not create a safe seed record.");
            using (var json = JsonDocument.Parse(File.ReadAllText(seedPath)))
            {
                if (json.RootElement.GetProperty("seed").GetString() != seed.ToUpperInvariant() ||
                    json.RootElement.GetProperty("binding").GetProperty("profile_id").GetString() != id)
                    throw new InvalidDataException("Generated seed is not bound to this new profile.");
            }
            var record = new ProfileRecord
            {
                Id = id, Name = name.Trim(), Kind = "randomizer", Seed = seed.ToUpperInvariant(),
                UserDir = userDir, External = false,
                CreatedAt = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss+00:00")
            };
            Index.Profiles.Add(record);
            try { Save(); }
            catch { Index.Profiles.Remove(record); throw; }
            return record;
        }
        catch
        {
            if (Directory.Exists(profileBase) && !IsLink(profileBase)) Directory.Delete(profileBase, true);
            throw;
        }
    }

    public void Rename(string id, string name)
    {
        if (!ValidName(name)) throw new ArgumentException("Adventure name must be 1–80 characters.");
        var p = Get(id);
        var previous = p.Name;
        p.Name = name.Trim();
        try { Save(); } catch { p.Name = previous; throw; }
    }

    public void Pin(string id, int index)
    {
        if (index is < 0 or > 2) throw new ArgumentOutOfRangeException(nameof(index));
        Get(id);
        var before = Index.LauncherSlots.ToArray();
        for (int i = 0; i < 3; i++)
            if (Index.LauncherSlots[i] == id) Index.LauncherSlots[i] = null;
        Index.LauncherSlots[index] = id;
        try { Save(); } catch { Index.LauncherSlots = before; throw; }
    }

    public void Delete(string id)
    {
        var profile = Get(id);
        if (_gameIsRunning()) throw new InvalidOperationException("Close Hookshot before deleting an adventure.");
        if (profile.External) throw new InvalidOperationException("External/original save folders are read-only and cannot be deleted.");
        if (id.Length != 32 || id.Any(c => !Uri.IsHexDigit(c))) throw new InvalidDataException("Invalid managed profile identifier.");
        if (!TryResolveManagedPaths(profile, out var root, out var profileBase))
            throw new InvalidDataException("Profile directory is missing, linked, or outside its isolated folder.");
        foreach (var entry in Directory.EnumerateFileSystemEntries(profileBase, "*", SearchOption.AllDirectories))
            if (IsLink(entry)) throw new InvalidDataException("Profile deletion stopped because the profile contains a symbolic link.");
        var slots = Index.LauncherSlots.ToArray();
        var position = Index.Profiles.IndexOf(profile);
        var deleting = profileBase + ".deleting-" + Guid.NewGuid().ToString("N");
        Directory.Move(profileBase, deleting);
        Index.Profiles.Remove(profile);
        for (var i = 0; i < Index.LauncherSlots.Length; i++)
            if (Index.LauncherSlots[i] == id) Index.LauncherSlots[i] = null;
        try
        {
            Save();
            Directory.Delete(deleting, true);
        }
        catch
        {
            if (!Index.Profiles.Contains(profile))
                Index.Profiles.Insert(Math.Clamp(position, 0, Index.Profiles.Count), profile);
            Index.LauncherSlots = slots;
            try { Save(); } catch { /* preserve the original deletion failure */ }
            if (Directory.Exists(deleting) && !Directory.Exists(profileBase)) Directory.Move(deleting, profileBase);
            throw;
        }
    }

    public bool IsExternalHidden(string userDir) => (Index.HiddenExternalDirs ?? new()).Contains(Path.GetFullPath(userDir),
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    public void RemoveExternal(string id)
    {
        var profile = Get(id);
        if (!profile.External) throw new InvalidOperationException("Only external adventures can be removed this way.");
        var oldProfiles = Index.Profiles.ToList(); var oldSlots = Index.LauncherSlots.ToArray();
        var oldHidden = (Index.HiddenExternalDirs ?? new()).ToList();
        Index.HiddenExternalDirs ??= new();
        if (!IsExternalHidden(profile.UserDir)) Index.HiddenExternalDirs.Add(Path.GetFullPath(profile.UserDir));
        Index.Profiles.Remove(profile);
        for (int i = 0; i < Index.LauncherSlots.Length; i++) if (Index.LauncherSlots[i] == id) Index.LauncherSlots[i] = null;
        try { Save(); } catch { Index.Profiles = oldProfiles; Index.LauncherSlots = oldSlots; Index.HiddenExternalDirs = oldHidden; throw; }
    }

    private bool TryResolveManagedPaths(ProfileRecord profile, out string profilesRoot, out string profileBase)
    {
        profilesRoot = "";
        profileBase = "";
        if (IsLink(Root)) return false;
        var comparer = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var candidates = new[]
        {
            Path.Combine(Root, "profiles"),
            Path.Combine(Path.GetDirectoryName(LegacyHookshotIndex()) ?? "", "profiles")
        }.Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            var basePath = Path.GetFullPath(Path.Combine(candidate, profile.Id));
            var userPath = Path.Combine(basePath, "user");
            if (IsLink(candidate) || IsLink(basePath) || IsLink(userPath) ||
                !Directory.Exists(basePath) || !Directory.Exists(userPath)) continue;
            if (!string.Equals(Path.GetFullPath(profile.UserDir), userPath, comparer)) continue;
            profilesRoot = candidate;
            profileBase = basePath;
            return true;
        }
        return false;
    }

    public void ClearPin(int index)
    {
        if (index is < 0 or > 2) throw new ArgumentOutOfRangeException(nameof(index));
        var previous = Index.LauncherSlots[index];
        Index.LauncherSlots[index] = null;
        try { Save(); } catch { Index.LauncherSlots[index] = previous; throw; }
    }

    // Exactly 3 shortcuts: explicit pins, then real save-bearing profiles, then new managed profiles.
    // Empty external default save roots are never displayed as occupied shortcut rows.
    public ProfileRecord?[] GetSlots()
    {
        var slots = new ProfileRecord?[3];
        var used = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < 3; i++)
        {
            var pin = Index.LauncherSlots[i];
            var p = pin is null ? null : Index.Profiles.FirstOrDefault(x => x.Id == pin);
            if (p is null || !used.Add(p.Id)) continue;
            slots[i] = p;
        }
        var available = Index.Profiles.Where(p => !used.Contains(p.Id) && (!p.External || HasProgressSave(p.UserDir)))
            .OrderByDescending(p => HasProgressSave(p.UserDir))
            .ThenByDescending(p => p.External)
            .ThenBy(p => p.CreatedAt, StringComparer.Ordinal).ToList();
        for (int i = 0; i < 3; i++)
            if (slots[i] is null && available.Count > 0)
            {
                slots[i] = available[0];
                available.RemoveAt(0);
            }
        return slots;
    }

    public static bool HasProgressSave(string userDir)
    {
        var saveDir = Path.Combine(userDir, "save");
        if (!Directory.Exists(saveDir) || IsLink(saveDir)) return false;
        try
        {
            foreach (var account in Directory.EnumerateDirectories(saveDir))
            {
                if (IsLink(account)) continue;
                foreach (var slot in Directory.EnumerateDirectories(account, "slot_*"))
                {
                    if (IsLink(slot)) continue;
                    var name = Path.GetFileName(slot);
                    if (name.Length is not (7 or 8) || !name.StartsWith("slot_", StringComparison.Ordinal) ||
                        !name[5..].All(char.IsAsciiDigit) || !int.TryParse(name[5..], out var slotNumber) || slotNumber is < 0 or > 99)
                        continue;
                    var progress = Path.Combine(slot, "progress.sav");
                    if (File.Exists(progress) && !IsLink(progress)) return true;
                }
            }
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        return false;
    }

    public static bool IsLink(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch (IOException) { return false; }
    }

    public ProfileRecord RegisterExternal(string userDir, string name = "Link")
    {
        var canonical = Path.GetFullPath(userDir);
        if (IsLink(canonical) || !HasProgressSave(canonical))
            throw new InvalidOperationException("Choose a real user-data directory containing saved progress.");
        var existing = Index.Profiles.FirstOrDefault(x => Path.GetFullPath(x.UserDir) == canonical);
        if (existing is not null) return existing;
        if (!ValidName(name)) throw new ArgumentException("Adventure name must be 1–80 characters.");
        var p = new ProfileRecord
        {
            Id = Guid.NewGuid().ToString("N"), Name = name.Trim(), Kind = "vanilla", UserDir = canonical,
            External = true, CreatedAt = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss+00:00")
        };
        Index.Profiles.Add(p);
        var hidden = (Index.HiddenExternalDirs ?? new()).ToList();
        Index.HiddenExternalDirs ??= new();
        Index.HiddenExternalDirs.RemoveAll(path => string.Equals(path, canonical, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
        try { Save(); } catch { Index.Profiles.Remove(p); Index.HiddenExternalDirs = hidden; throw; }
        return p;
    }
}
