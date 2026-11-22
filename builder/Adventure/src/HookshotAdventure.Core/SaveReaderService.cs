using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace HookshotAdventure.Core;

public enum OwnershipState { Unknown, NotCarried, Owned }

public sealed record ItemState(string Role, string Label, string Group, OwnershipState State, bool ArtMapped, string Evidence)
{
    public string Status => State switch { OwnershipState.Owned => "Owned", OwnershipState.NotCarried => Group.StartsWith("Trading", StringComparison.Ordinal) ? "Not held" : "Not in pouch", _ => "Unknown" };
    public string ArtStatus => ArtMapped ? "Mapped" : "Art pending";
}

public sealed record BottleSlotInfo(int Number, bool? Owned, string? Role, string Caption);

public sealed class SaveSummary
{
    public string Account { get; init; } = "";
    public int Slot { get; init; }
    public DateTime Modified { get; init; }
    public string? SavedInGame { get; init; }
    public byte[]? PreviewJpeg { get; init; }
    public string? Warning { get; init; }
    public int? Rupees { get; init; }
    public double? Hearts { get; init; }
    public double? CurrentHearts { get; init; }
    public int? Skulltulas { get; init; }
    public double? Magic { get; init; }
    public double? MaxMagic { get; init; }
    public string? Location { get; init; }
    public string? Wallet { get; init; }
    public int? WalletCapacity { get; init; }
    public int? Bottles { get; init; }
    public IReadOnlyList<BottleSlotInfo> BottleSlots { get; init; } = Array.Empty<BottleSlotInfo>();
    public IReadOnlyDictionary<string, int?> BagCapacities { get; init; } = new Dictionary<string, int?>();
    public IReadOnlyDictionary<string, int?> Supplies { get; init; } = new Dictionary<string, int?>();
    public IReadOnlyDictionary<string, int?> DungeonKeys { get; init; } = new Dictionary<string, int?>();
    public string? EquippedSword { get; init; }
    public string? EquippedShield { get; init; }
    public IReadOnlyList<ItemState> Items { get; init; } = Array.Empty<ItemState>();
    public string SlotName => $"{Account} · slot_{Slot:00}";
    public string HAL_SlotLabel => $"Save {Slot + 1} ({Account[..Math.Min(8, Account.Length)]})" + (Warning is null ? "" : " · unreadable");
    public string HeartsText => Hearts is null ? "Unknown" : $"{CurrentHearts?.ToString("0.##", CultureInfo.InvariantCulture) ?? "?"} / {Hearts.Value.ToString("0.##", CultureInfo.InvariantCulture)}";
    public string RupeesText => Rupees?.ToString(CultureInfo.InvariantCulture) ?? "Unknown";
    public string SkulltulasText => Skulltulas?.ToString(CultureInfo.InvariantCulture) ?? "Unknown";
    public string LocationText => Location ?? "Unknown location";
    public string SavedText => SavedInGame is not null ? "Saved in-game: " + SavedInGame : "File modified: " + Modified.ToString("g", CultureInfo.CurrentCulture);
}

internal sealed class SaveFieldTable
{
    public const int MaxSaveSize = 24 * 1024 * 1024;
    private readonly byte[] _bytes;
    private readonly int _header;
    private readonly Dictionary<uint, int> _entries = new();
    public SaveFieldTable(byte[] bytes, int expectedHeader)
    {
        if (bytes.Length < expectedHeader || bytes.Length > MaxSaveSize)
            throw new InvalidDataException("Unexpected save size.");
        _bytes = bytes;
        _header = expectedHeader;
        if (U32(0) != 0x01020304 || U32(4) != 0x002D80EB || U32(8) != (uint)expectedHeader)
            throw new InvalidDataException("Unsupported save format/version.");
        for (int offset = 40; offset < expectedHeader; offset += 8)
        {
            var key = U32(offset);
            if (key != 0 && !_entries.TryAdd(key, offset + 4))
                throw new InvalidDataException("Duplicate field identifier.");
        }
    }
    private uint U32(int offset)
    {
        if (offset < 0 || offset > _bytes.Length - 4) throw new InvalidDataException("Field outside save.");
        return BinaryPrimitives.ReadUInt32LittleEndian(_bytes.AsSpan(offset, 4));
    }
    public int? I32(uint hash) => _entries.TryGetValue(hash, out int offset) ? unchecked((int)U32(offset)) : null;
    private int? Block(uint hash, int length)
    {
        if (!_entries.TryGetValue(hash, out int offset)) return null;
        var position = U32(offset);
        if (position < _header || position > _bytes.Length - length)
            throw new InvalidDataException("Data block outside save.");
        return (int)position;
    }
    public string? String64(uint hash)
    {
        var start = Block(hash, 64);
        if (start is null) return null;
        var span = _bytes.AsSpan(start.Value, 64);
        var nullIndex = span.IndexOf((byte)0);
        if (nullIndex >= 0) span = span[..nullIndex];
        var value = Encoding.UTF8.GetString(span).Trim();
        return new string(value.Where(c => !char.IsControl(c)).Take(64).ToArray()) is var s && s.Length > 0 ? s : null;
    }
    public byte[]? Jpeg(uint hash)
    {
        var start = Block(hash, 8);
        if (start is null) return null;
        var size = U32(start.Value);
        if (size < 4 || size > 3 * 1024 * 1024 || start.Value + 4L + size > _bytes.Length) return null;
        var pos = start.Value + 4;
        if (_bytes[pos] != 0xFF || _bytes[pos + 1] != 0xD8 || _bytes[pos + (int)size - 2] != 0xFF || _bytes[pos + (int)size - 1] != 0xD9)
            return null;
        return _bytes.AsSpan(pos, (int)size).ToArray();
    }
}

public sealed class SaveReaderService
{
    private readonly IconManifest? _manifest;
    public SaveReaderService(IconManifest? manifest = null) => _manifest = manifest;
    private const int ProgressHeader = 0x175D8;
    private const int CaptionHeader = 0x2A0;

    public IReadOnlyList<SaveSummary> ReadSlots(string userDir)
    {
        var root = Path.Combine(userDir, "save");
        if (!Directory.Exists(root) || ProfileRepository.IsLink(root)) return Array.Empty<SaveSummary>();
        var found = new List<SaveSummary>();
        try
        {
            foreach (var account in Directory.EnumerateDirectories(root).OrderBy(x => x, StringComparer.Ordinal))
            {
                if (ProfileRepository.IsLink(account)) continue;
                foreach (var folder in Directory.EnumerateDirectories(account, "slot_*").OrderBy(x => x, StringComparer.Ordinal))
                {
                    if (ProfileRepository.IsLink(folder)) continue;
                    var name = Path.GetFileName(folder);
                    if (name.Length is not (7 or 8) || !name.StartsWith("slot_", StringComparison.Ordinal) ||
                        !name[5..].All(char.IsAsciiDigit) || !int.TryParse(name[5..], out int slot) || slot is < 0 or > 99) continue;
                    var progress = Path.Combine(folder, "progress.sav");
                    var caption = Path.Combine(folder, "caption.sav");
                    if (!File.Exists(progress) || ProfileRepository.IsLink(progress)) continue;
                    var modified = DateTime.MinValue;
                    try
                    {
                        modified = File.GetLastWriteTime(progress);
                        var p = ReadFile(progress);
                        byte[]? c = null;
                        if (File.Exists(caption) && !ProfileRepository.IsLink(caption))
                        {
                            modified = new[] { modified, File.GetLastWriteTime(caption) }.Max();
                            c = ReadFile(caption);
                        }
                        found.Add(Decode(p, c, Path.GetFileName(account), slot, modified));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
                    {
                        found.Add(new SaveSummary { Account = Path.GetFileName(account), Slot = slot, Modified = modified, Warning = ex.Message });
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Safe partial results: one unreadable directory must not make a profile inoperable.
            found.Add(new SaveSummary { Account = "Save scan", Warning = ex.Message });
        }
        return found.OrderByDescending(x => x.Modified).ToList();
    }

    private static byte[] ReadFile(string file)
    {
        if (ProfileRepository.IsLink(file)) throw new InvalidDataException("Linked saves are not read.");
        var length = new FileInfo(file).Length;
        if (length > SaveFieldTable.MaxSaveSize) throw new InvalidDataException("Save exceeds 24 MiB bound.");
        return File.ReadAllBytes(file);
    }

    private static int? Plausible(int? value, int low, int high) => value >= low && value <= high ? value : null;
    private static double? Hearts(int? value) => Plausible(value, 0, 80) is int number ? number / 4d : null;

    public SaveSummary Decode(byte[] progress, byte[]? caption, string account = "account", int slot = 0, DateTime modified = default)
    {
        var p = new SaveFieldTable(progress, ProgressHeader);
        var c = caption is null ? null : new SaveFieldTable(caption, CaptionHeader);
        var items = new List<ItemState>();
        foreach (var item in StockCatalog.All)
        {
            var raw = p.I32(item.Hash);
            var state = raw is null ? OwnershipState.Unknown : item.AllowCounts
                ? raw.Value is >= 0 and <= 99 ? raw.Value > 0 ? OwnershipState.Owned : OwnershipState.NotCarried : OwnershipState.Unknown
                : raw.Value switch { 0 => OwnershipState.NotCarried, 1 => OwnershipState.Owned, _ => OwnershipState.Unknown };
            items.Add(new ItemState(item.Role, item.Label, item.Group, state,
                _manifest?.HasVerifiedMapping(item.Role) == true, raw is null ? "Unmapped field" : $"StockNum:0x{item.Hash:X8}"));
        }
        // Equip string is positive ownership evidence and overrides a zero pouch count.
        var equipped = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Weapon_Sword_Kokiri"] = "sword_kokiri", ["Weapon_Sword_Master"] = "sword_master",
            ["Weapon_Sword_Biggoron"] = "sword_biggoron", ["Weapon_Shield_Deku"] = "shield_deku",
            ["Weapon_Shield_Hylian"] = "shield_hylian", ["Weapon_Shield_Hylia"] = "shield_hylian",
            ["Armor_Shield_Hylian"] = "shield_hylian", ["Armor_Shield_Hylia"] = "shield_hylian",
            ["Weapon_Shield_Mirror"] = "shield_mirror"
        };
        foreach (var (hash, sword) in new[] { (0xA95A3521u, true), (0xEE463235u, false), (0xA1F7EE2Bu, true), (0x2342B619u, false) })
        {
            var name = p.String64(hash);
            if (name is null || !equipped.TryGetValue(name, out var role) || role.StartsWith("sword_", StringComparison.Ordinal) != sword) continue;
            var at = items.FindIndex(x => x.Role == role);
            if (at >= 0) items[at] = items[at] with { State = OwnershipState.Owned, Evidence = $"Equipped:0x{hash:X8}" };
        }
        var maxMagicRaw = Plausible(p.I32(0x12D63A97), 0, 480);
        var magicRaw = Plausible(p.I32(0x76B18200), 0, 480);
        bool legacyMagic = maxMagicRaw is 24 or 48;
        int? walletCapacity = Plausible(p.I32(0xE0010A50), 0, 1000);
        string? wallet = (p.String64(0x3E42AFFA), walletCapacity) switch
        {
            ("Item_Wallet_Small", 200) => "Kokiri",
            ("Item_Wallet_Medium", 500) => "Adult",
            ("Item_Wallet_Large", 1000) => "Giant",
            _ => null
        };
        if (wallet is null) walletCapacity = null;
        // Direct translation of the approved read-only Qt bottle fields.
        // An unknown enum never turns into a fabricated Empty Bottle.
        var bottleContents = new Dictionary<uint, (string role, string caption)>
        {
            [0xBE69DE49] = ("bottle_empty", "Empty"), [0xEABA6067] = ("bottle_red_potion", "Red Potion"),
            [0x049D37C1] = ("bottle_green_potion", "Green Potion"), [0xF383D6E5] = ("bottle_blue_potion", "Blue Potion"),
            [0x1C293517] = ("bottle_blue_fire", "Blue Fire"), [0x1E834EF8] = ("bottle_fish", "Fish"),
            [0x0F24E808] = ("bottle_letter", "Ruto's Letter"), [0xCDC21471] = ("bottle_big_poe", "Big Poe"),
            [0x02084F9E] = ("bottle_poe", "Poe"), [0x74D62ECB] = ("bottle_bugs", "Bug"),
            [0x6F49E357] = ("bottle_fairy", "Fairy"), [0x73356827] = ("bottle_milk", "Milk"),
            [0xEEF6E4B0] = ("bottle_milk_half", "Half Milk")
        };
        var bottleSlots = new List<BottleSlotInfo>();
        int? bottles = 0;
        var bottleFields = new (uint stock, uint valid, uint content)[]
        {
            (0xD53CE3A7, 0xF39CE410, 0x7D48DF76), (0xFCA29A04, 0x000FF6EA, 0x1EDD164A),
            (0x893216A1, 0xF63B1194, 0xB80C70A8), (0x5324F9D3, 0xB7F052CE, 0xE4C1B1FC),
            (0xC92B16BE, 0xE27B136D, 0x365E1953)
        };
        for (int i = 0; i < bottleFields.Length; i++)
        {
            var (stock, valid, content) = bottleFields[i];
            var value = p.I32(stock);
            var validFlag = p.I32(valid);
            bool consistent = value is 0 or 1 && (validFlag is null || validFlag == value);
            bool? owned = consistent ? value == 1 : null;
            if (!consistent) bottles = null;
            else if (bottles.HasValue) bottles += value;
            string? role = null;
            string bottleCaption = owned is false ? "—" : "Unknown";
            if (owned is true && p.I32(content) is int rawContent &&
                bottleContents.TryGetValue(unchecked((uint)rawContent), out var decoded))
                (role, bottleCaption) = decoded;
            bottleSlots.Add(new BottleSlotInfo(i + 1, owned, role, bottleCaption));
        }
        var bagCapacities = new Dictionary<string, int?>(StringComparer.Ordinal)
        {
            ["sticks"] = p.I32(0x457616B0) is int sticks && sticks is 0 or 10 or 20 or 30 ? sticks : null,
            ["nuts"] = p.I32(0x735B00F7) is int nuts && nuts is 0 or 20 or 30 or 40 ? nuts : null,
            ["seeds"] = p.I32(0xAE8CC742) is int seeds && seeds is 0 or 30 or 40 or 50 ? seeds : null,
            ["quiver"] = p.I32(0x418DB791) is int quiver && quiver is 0 or 30 or 40 or 50 ? quiver : null,
            ["bombs"] = p.I32(0xE753508B) is int bombs && bombs is 0 or 20 or 30 or 40 ? bombs : null
        };
        var supplies = new Dictionary<string, int?>(StringComparer.Ordinal)
        {
            ["arrows"] = Plausible(p.I32(0x518EC74B), 0, 50), ["bombs"] = Plausible(p.I32(0xDB1BCD4F), 0, 40),
            ["bombchus"] = Plausible(p.I32(0xDF7C517A), 0, 99), ["seeds"] = Plausible(p.I32(0x5FADF7FC), 0, 50),
            ["nuts"] = Plausible(p.I32(0xF30D9F54), 0, 40), ["sticks"] = Plausible(p.I32(0xB9494411), 0, 30),
            ["beans"] = Plausible(p.I32(0xB003124D), 0, 10),
            ["business_seeds"] = Plausible(p.I32(0x600E29EB), 0, 30)
        };
        var dungeonKeys = new Dictionary<string, int?>(StringComparer.Ordinal)
        {
            ["Well"] = Plausible(p.I32(0x7DD79D10), 0, 2), ["Forest"] = Plausible(p.I32(0x90237092), 0, 5),
            ["Fire"] = Plausible(p.I32(0x1DF11487), 0, 7), ["Water"] = Plausible(p.I32(0x877BDCEE), 0, 4),
            ["Shadow"] = Plausible(p.I32(0x5EDDA9E3), 0, 5), ["Spirit"] = Plausible(p.I32(0x79EEEC8E), 0, 5),
            ["Fortress"] = Plausible(p.I32(0x3A28B6A4), 0, 4), ["Training"] = Plausible(p.I32(0xEA9786F5), 0, 8),
            ["Castle"] = Plausible(p.I32(0xFBFD2910), 0, 1)
        };
        string? Equipment(uint hash) => p.String64(hash) is string value ? value.Replace("Weapon_", "").Replace("Armor_", "").Replace('_', ' ') : null;
        string? saved = null;
        byte[]? preview = null;
        if (c is not null)
        {
            var year = c.I32(0x9811A3F7);
            var month = c.I32(0xDFD840D3);
            var day = c.I32(0xBD46F485);
            var hour = c.I32(0x23F3D75E);
            var minute = c.I32(0x27853BF7);
            if (year is >= 1 and <= 9999 && month is >= 1 and <= 12 && day is >= 1 and <= 31 && hour is >= 0 and <= 23 && minute is >= 0 and <= 59)
            {
                try { saved = new DateTime(year.Value, month.Value, day.Value, hour.Value, minute.Value, 0).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture); }
                catch (ArgumentOutOfRangeException) { /* keep unavailable */ }
            }
            preview = c.Jpeg(0x63696A32);
        }
        HAL_SaveDisplayFields.AddSongs(items, p.I32, p.String64);
        return new SaveSummary
        {
            Account = account, Slot = slot, Modified = modified, SavedInGame = saved, PreviewJpeg = preview,
            Rupees = Plausible(p.I32(0xA77921D7), 0, 9999),
            Hearts = Hearts(p.I32(0xFBE01DA1)), CurrentHearts = Hearts(p.I32(0x31AB5580)),
            Skulltulas = Plausible(p.I32(0x04735978), 0, 100),
            Magic = magicRaw is null ? null : legacyMagic ? magicRaw.Value : magicRaw.Value / 10d,
            MaxMagic = maxMagicRaw is null ? null : legacyMagic ? maxMagicRaw.Value : maxMagicRaw.Value / 10d,
            Location = p.String64(0x1D6189DA), Wallet = wallet, WalletCapacity = walletCapacity, Bottles = bottles,
            BottleSlots = bottleSlots, BagCapacities = bagCapacities, Supplies = supplies, DungeonKeys = dungeonKeys,
            EquippedSword = Equipment(0xA95A3521), EquippedShield = Equipment(0xEE463235), Items = items
        };
    }
}
