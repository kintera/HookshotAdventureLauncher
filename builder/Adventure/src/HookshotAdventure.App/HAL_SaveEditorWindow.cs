using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using HookshotAdventure.Core;

namespace HookshotAdventure.App;

/// <summary>Edits one selected slot in memory using Hookshot's save editor backend.</summary>
public sealed class HAL_SaveEditorSession : IDisposable
{
    private readonly Process _process;
    private readonly SemaphoreSlim _requests = new(1, 1);
    private readonly TextBlock _status;
    private readonly Button _save;
    private readonly string _account;
    private readonly string _slot;
    private bool _loading = true;
    private int _edited;
    public bool IsBusy => _loading || _requests.CurrentCount == 0;
    private readonly HAL_AdventureSummaryView _view;
    private readonly ContentControl _selector;
    private readonly ComboBox? _agePicker;
    private int _ageIndex = -1;
    private readonly SaveSummary _summary;
    private readonly HAL_IconCacheService _icons;
    private readonly string _theme;
    private readonly Dictionary<string, bool> _owned = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _songs = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> SongIds = new(StringComparer.Ordinal) {
        ["song_zelda_lullaby"] = "ZeldaLullaby", ["song_epona"] = "EponaSong", ["song_saria"] = "SariaSong",
        ["song_sun"] = "SunSong", ["song_time"] = "TimeSong", ["song_storms"] = "StormSong",
        ["song_forest_minuet"] = "ForestMinuet", ["song_fire_bolero"] = "FireBolero", ["song_water_serenade"] = "WaterSerenade",
        ["song_shadow_nocturne"] = "ShadowNocturne", ["song_spirit_requiem"] = "SpiritRequiem", ["song_light_prelude"] = "LightPrelude"
    };
    private readonly Dictionary<string, int> _amounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _amountLimits = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ComboBox> _choices = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Label, string? Role)[]> _choiceIcons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _stagedChoices = new(StringComparer.Ordinal);
    private double? _heartCurrent, _heartMaximum;
    private int? _magicLevel;
    private static readonly (string Label, string Role)[] TradeItems = {
        ("Weird Egg", "trade_weird_egg"), ("Wake-up Cucco", "trade_wake_up_cucco"), ("Zelda's Letter", "trade_zelda_letter"),
        ("Pocket Egg", "trade_pocket_egg"), ("Pocket Cucco", "trade_pocket_cucco"), ("Cojiro", "trade_cojiro"),
        ("Odd Mushroom", "trade_odd_mushroom"), ("Odd Poultice", "trade_odd_poultice"), ("Poacher's Saw", "trade_poachers_saw"),
        ("Broken Goron's Sword", "trade_broken_goron_sword"), ("Prescription", "trade_prescription"),
        ("Eyeball Frog", "trade_eyeball_frog"), ("Eye Drops", "trade_eye_drops"), ("Claim Check", "trade_claim_check")
    };
    private static readonly Dictionary<string, string> ItemIds = new(StringComparer.Ordinal) {
        ["slingshot"] = "Weapon_Range_SlingShot", ["boomerang"] = "Item_Boomerang", ["bow"] = "Weapon_Range_Bow",
        ["hammer"] = "Item_Hammer", ["lens_of_truth"] = "Armor_GlassTruth", ["boots_iron"] = "Armor_Adult_HeavyBoot", ["boots_hover"] = "Armor_Adult_HoverBoot",
        ["fire_arrow"] = "Item_Arrow_Fire", ["ice_arrow"] = "Item_Arrow_Ice", ["light_arrow"] = "Item_Arrow_Light",
        ["dins_fire"] = "Item_DinFire", ["farores_wind"] = "Item_FaroreWind", ["nayrus_love"] = "Item_NayruLove",
        ["sword_kokiri"] = "Weapon_Sword_Kokiri", ["sword_master"] = "Weapon_Sword_Master", ["sword_biggoron"] = "Weapon_Sword_Biggoron",
        ["shield_deku"] = "Weapon_Shield_Deku", ["shield_hylian"] = "Weapon_Shield_Hylian", ["shield_mirror"] = "Weapon_Shield_Mirror",
        ["tunic_kokiri"] = "Obj_Armor_Child_Normal", ["tunic_goron"] = "Armor_Adult_Goron", ["tunic_zora"] = "Armor_Adult_Zora",
        ["mask_keaton"] = "Armor_Child_Mask_Keaton", ["mask_skull"] = "Armor_Child_Mask_Skull", ["mask_spooky"] = "Armor_Child_Mask_Spooky",
        ["mask_bunny"] = "Armor_Child_Mask_Bunny", ["mask_truth"] = "Armor_Child_Mask_Truth", ["mask_goron"] = "Armor_Child_Mask_Goron",
        ["mask_zora"] = "Armor_Child_Mask_Zora", ["mask_gerudo"] = "Armor_Child_Mask_Gerudo", ["mask_amiibo_0"] = "Armor_Child_Mask_Amiibo0", ["mask_amiibo_1"] = "Armor_Child_Mask_Amiibo1",
        ["stone_kokiri"] = "Item_Stone_Forest", ["stone_goron"] = "Item_Stone_Fire", ["stone_zora"] = "Item_Stone_Water",
        ["medallion_forest"] = "Item_Medal_Forest", ["medallion_fire"] = "Item_Medal_Fire", ["medallion_water"] = "Item_Medal_Water",
        ["medallion_spirit"] = "Item_Medal_Spirit", ["medallion_shadow"] = "Item_Medal_Shadow", ["medallion_light"] = "Item_Medal_Light",
        ["agony_stone"] = "Item_AgonyStone", ["gerudo_pass"] = "Item_Token_Gerudo",
        ["trade_weird_egg"] = "Item_WeirdEgg", ["trade_wake_up_cucco"] = "Item_AlarmCucco", ["trade_zelda_letter"] = "Item_ZeldaLetter",
        ["trade_pocket_egg"] = "Item_PocketEgg", ["trade_pocket_cucco"] = "Item_PocketCucco", ["trade_cojiro"] = "Item_CojiroCucco",
        ["trade_odd_mushroom"] = "Item_OddMushroom", ["trade_odd_poultice"] = "Item_OddPowder", ["trade_poachers_saw"] = "Item_PoacherSaw",
        ["trade_broken_goron_sword"] = "Item_BrokenSword", ["trade_prescription"] = "Item_Prescription", ["trade_eyeball_frog"] = "Item_EyeballFrog",
        ["trade_eye_drops"] = "Item_EyeDrop", ["trade_claim_check"] = "Item_ClaimCheck"
    };
    private static readonly Dictionary<string, (string Label, string Id)[]> Progressions = new(StringComparer.Ordinal) {
        ["ocarina"] = new[] { ("Fairy Ocarina", "Item_Ocarina_Fairy"), ("Ocarina of Time", "Item_Ocarina_Time") },
        ["hookshot"] = new[] { ("Hookshot", "Item_Hookshot"), ("Longshot", "Item_Longshot") },
        ["strength"] = new[] { ("Goron Bracelet", "Armor_Child_Gauntlet_Goron"), ("Silver Gauntlets", "Armor_Adult_Gauntlet_Silver"), ("Golden Gauntlets", "Armor_Adult_Gauntlet_Gold") },
        ["scale"] = new[] { ("Silver Scale", "Item_Scale_Silver"), ("Golden Scale", "Item_Scale_Gold") }
    };
    private static readonly Dictionary<string, string> ProgressionRoles = new(StringComparer.Ordinal) {
        ["ocarina_fairy"] = "Item_Ocarina_Fairy", ["ocarina_time"] = "Item_Ocarina_Time",
        ["hookshot"] = "Item_Hookshot", ["longshot"] = "Item_Longshot", ["bracelet_goron"] = "Armor_Child_Gauntlet_Goron",
        ["gauntlets_silver"] = "Armor_Adult_Gauntlet_Silver", ["gauntlets_gold"] = "Armor_Adult_Gauntlet_Gold",
        ["scale_silver"] = "Item_Scale_Silver", ["scale_gold"] = "Item_Scale_Gold"
    };
    private static readonly Dictionary<string, string> AmountIds = new(StringComparer.Ordinal) {
        ["deku_sticks"] = "Item_DekuStick", ["deku_nuts"] = "Item_DekuNut", ["magic_bean"] = "Item_MagicBean",
        ["bomb"] = "Item_Bomb", ["bombchu"] = "Item_Bombchu", ["business_scrub_seed"] = "Item_ScrubBusinessSeed"
    };
    private static readonly Dictionary<string, string> QuantityIds = new(AmountIds, StringComparer.Ordinal) {
        ["slingshot"] = "Item_DekuSeed", ["bow"] = "Item_Arrow_Normal"
    };

    public HAL_SaveEditorSession(string root, string game, string user, SaveSummary slot, HAL_IconCacheService icons, string theme,
        HAL_AdventureSummaryView view, TextBlock status, Button save, ContentControl selector, ComboBox? agePicker = null,
        bool independentBombchus = false)
    {
        _summary = slot; _icons = icons; _theme = theme; _view = view; _status = status; _save = save; _selector = selector;
        _agePicker = agePicker;
        if (_agePicker is not null) { _agePicker.IsEnabled = false; _agePicker.SelectionChanged += AgeChanged; }
        _status.Text = "Loading save…";
        if (!Directory.Exists(game)) throw new DirectoryNotFoundException("Select an installed Hookshot game first.");
        if (Path.GetFileName(slot.Account) != slot.Account || slot.Slot is < 0 or > 99)
            throw new InvalidDataException("Invalid save slot.");
        _account = Path.Combine(user, "save", slot.Account);
        _slot = $"slot_{slot.Slot:00}";
        foreach (var path in new[] { user, Path.Combine(user, "save"), _account, Path.Combine(_account, _slot),
                     Path.Combine(_account, _slot, "progress.sav"), Path.Combine(_account, _slot, "caption.sav"),
                     Path.Combine(_account, "account.sav"), Path.Combine(_account, "option.sav") })
            if (ProfileRepository.IsLink(path)) throw new InvalidDataException("Linked save paths cannot be edited.");
        var script = Path.Combine(root, "nx2", "saveedit", "server.py");
        if (!File.Exists(script)) throw new FileNotFoundException("Save editor backend not found.", script);
        var info = new ProcessStartInfo(HAL_DevelopmentBuild.PythonExecutable(root)) {
            WorkingDirectory = root, UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = new UTF8Encoding(false), CreateNoWindow = true
        };
        info.ArgumentList.Add("-I"); info.ArgumentList.Add(script); info.ArgumentList.Add("--game"); info.ArgumentList.Add(game);
        info.ArgumentList.Add("--user"); info.ArgumentList.Add(user);
        if (independentBombchus) info.ArgumentList.Add("--independent-bombchus");
        HAL_LinuxBuildTools.HAL_ConfigureBuild(info, root);
        _process = Process.Start(info) ?? throw new InvalidOperationException("Could not start the save editor.");
        _process.ErrorDataReceived += (_, _) => { }; _process.BeginErrorReadLine();
        _save.IsEnabled = false;
        _view.EditAction = EditIcon;
        _view.EditHearts = () => _heartCurrent is double current && _heartMaximum is double maximum ? (current, maximum) : null;
        _view.SetEditHearts = SetHeartContainers;
        _view.EditMagicLevel = () => _magicLevel;
        _view.CycleEditMagic = CycleMagic;
        _view.EditAmount = role => QuantityIds.TryGetValue(role, out var id) && _amounts.TryGetValue(id, out var amount) ? amount : null;
        _view.EditQuantity = role => QuantityIds.TryGetValue(role, out var id) && _amounts.TryGetValue(id, out var amount) &&
            _amountLimits.TryGetValue(id, out var limit) ? (amount, Math.Max(amount, limit)) : null;
        _view.SetEditQuantity = SetQuantity;
        _view.EditBag = key => _stagedChoices.TryGetValue("bag:" + (key == "quiver" ? "arrows" : key), out var value) ? value : null;
        _view.EditBottle = number => {
            if (!_stagedChoices.TryGetValue("bottle:" + (number - 1), out var value)) return null;
            string[] roles = { "bottle_empty", "bottle_empty", "bottle_red_potion", "bottle_green_potion", "bottle_blue_potion", "bottle_milk", "bottle_milk_half", "bottle_fairy", "bottle_fish", "bottle_bugs", "bottle_blue_fire", "bottle_poe", "bottle_big_poe", "bottle_letter" };
            string[] labels = { "None", "Empty", "Red Potion", "Green Potion", "Blue Potion", "Milk", "Half Milk", "Fairy", "Fish", "Bug", "Blue Fire", "Poe", "Big Poe", "Ruto's Letter" };
            return value >= 0 && value < roles.Length ? new BottleSlotInfo(number, value != 0, roles[value], labels[value]) : null;
        };
        _view.CanEdit = role => ItemIds.TryGetValue(role, out var id) && _owned.ContainsKey(id) ||
            SongIds.TryGetValue(role, out var song) && _songs.ContainsKey(song) ||
            role == "trade_quest" && TradeItems.Any(item => _owned.ContainsKey(ItemIds[item.Role])) ||
            AmountIds.TryGetValue(role, out var amountId) && _amounts.ContainsKey(amountId) ||
            QuantityIds.TryGetValue(role, out var quantityId) && _amounts.ContainsKey(quantityId) ||
            role.StartsWith("bottle_slot_", StringComparison.Ordinal) && _choices.ContainsKey("bottle:" + (int.Parse(role[12..]) - 1)) ||
            role.StartsWith("bag_", StringComparison.Ordinal) && _choices.ContainsKey("bag:" + (role[4..] == "quiver" ? "arrows" : role[4..])) ||
            role.StartsWith("progressive_", StringComparison.Ordinal) && _choices.ContainsKey(role);
        _view.EditOwnership = role => SongIds.TryGetValue(role, out var song) && _songs.TryGetValue(song, out var learned) ? learned :
            (ItemIds.TryGetValue(role, out var id) || ProgressionRoles.TryGetValue(role, out id)) && _owned.TryGetValue(id, out var yes) ? yes :
            AmountIds.TryGetValue(role, out var amountId) && _amounts.TryGetValue(amountId, out var count) ? count > 0 : null;
        _view.UpdateVisual(_summary, _icons, _theme);
    }

    public void Dispose()
    {
        if (_agePicker is not null) { _agePicker.SelectionChanged -= AgeChanged; _agePicker.IsEnabled = false; }
        _view.EditAction = null; _view.CanEdit = null; _view.EditOwnership = null; _view.EditBottle = null; _view.EditBag = null;
        _view.EditAmount = null; _view.EditQuantity = null; _view.SetEditQuantity = null;
        _view.EditHearts = null; _view.SetEditHearts = null;
        _view.EditMagicLevel = null; _view.CycleEditMagic = null;
        _selector.Content = null;
        try { _process.StandardInput.Close(); if (!_process.WaitForExit(1000)) _process.Kill(true); } catch { }
        _process.Dispose();
    }
    private async Task<JsonObject> Ask(JsonObject request)
    {
        await _requests.WaitAsync();
        try {
            await _process.StandardInput.WriteLineAsync(request.ToJsonString());
            await _process.StandardInput.FlushAsync();
            while (true) {
                var line = await _process.StandardOutput.ReadLineAsync() ?? throw new IOException("Save editor stopped.");
                var result = JsonNode.Parse(line)?.AsObject() ?? throw new InvalidDataException("Invalid editor response.");
                if (result["progress"] is not null) continue;
                if (result["ok"]?.GetValue<bool>() != true) throw new InvalidOperationException(result["error"]?.ToString() ?? "Editor request failed.");
                return result;
            }
        } finally { _requests.Release(); }
    }
    private void Changed(JsonObject r) { _edited = r["edited"]?.GetValue<int>() ?? _edited; _save.IsEnabled = _edited > 0; _status.Text = $"{_edited} staged changes."; }
    public async Task Load()
    {
        try {
            var opened = await Ask(new JsonObject { ["cmd"] = "open", ["account"] = _account, ["slot"] = _slot });
            if (opened["errors"] is JsonArray errors && errors.Count > 0) throw new InvalidDataException(string.Join("; ", errors.Select(x => x?.ToString())));
            var items = await Ask(new JsonObject { ["cmd"] = "items" });
            foreach (var item in items["items"]!.AsArray().Where(x => x is not null && x["givable"]?.GetValue<bool>() == true))
                _owned[item!["item"]!.ToString()] = item["n"]!.GetValue<int>() > 0;
            var songs = await Ask(new JsonObject { ["cmd"] = "songs" });
            foreach (var song in songs["songs"]!.AsArray())
                _songs[song!["song"]!.ToString()] = song["learned"]!.GetValue<bool>();
            _view.UpdateVisual(_summary, _icons, _theme);
            var stats = await Ask(new JsonObject { ["cmd"] = "stats" });
            if (_agePicker is not null)
            {
                _ageIndex = stats["age"]?.ToString() switch { "young" => 0, "adult" => 1, _ => -1 };
                _agePicker.SelectedIndex = _ageIndex;
                _agePicker.IsEnabled = _ageIndex >= 0 && stats["age_switchable"]?.GetValue<bool>() == true;
                ToolTip.SetTip(_agePicker, _agePicker.IsEnabled ? "Change Link's age in this save" :
                    "Change age from an outdoor save with a child and adult version of the area.");
            }
            ReadHearts(stats);
            ReadMagic(stats);
            ReadAmounts(stats);
            foreach (var (group, entries) in Progressions)
                if (entries.All(entry => _owned.ContainsKey(entry.Id))) AddProgression(group, entries);
            foreach (var bag in stats["bags"]!.AsArray()) {
                if (bag!["available"]?.GetValue<bool>() != true) continue;
                var choices = bag!["choices"]!.AsArray().Select(x => (Text: x!["text"]!.ToString(), Value: x["n"]!.GetValue<int>())).ToArray();
                AddChoice(bag["label"]!.ToString(), "bag:" + bag["key"], choices, bag["now"]!.GetValue<int>());
            }
            var bottles = stats["bottles"]!.AsArray();
            var bottleChoices = stats["bottle_choices"]!.AsArray().Select((x, i) => (Text: x!.ToString(), Value: i)).ToArray();
            for (int i = 0; i < bottles.Count; i++)
                if (stats["bottle_available"]![i]!.GetValue<bool>()) AddChoice($"Bottle {i + 1}", "bottle:" + i, bottleChoices, bottles[i]!.GetValue<int>());
            _loading = false; _view.UpdateVisual(_summary, _icons, _theme); _status.Text = "Outlined icons can be edited. Adding a bow or slingshot also adds its basic ammo bag if needed.";
        } catch (Exception ex) { _loading = false; _status.Text = "Could not read save: " + ex.Message; }
    }
    private async void AgeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loading || _agePicker is null || _agePicker.SelectedIndex < 0 || _agePicker.SelectedIndex == _ageIndex) return;
        _loading = true; _agePicker.IsEnabled = false;
        try
        {
            var result = await Ask(new JsonObject { ["cmd"] = "age" });
            _ageIndex = result["age"]?.ToString() switch { "young" => 0, "adult" => 1, _ => -1 };
            _agePicker.SelectedIndex = _ageIndex;
            Changed(result);
        }
        catch (Exception ex) { _agePicker.SelectedIndex = _ageIndex; _status.Text = ex.Message; }
        finally { _agePicker.IsEnabled = _ageIndex >= 0; _loading = false; }
    }
    private void AddChoice(string label, string key, (string Text, int Value)[] choices, int selected)
    {
        string[] bottleRoles = { "", "bottle_empty", "bottle_red_potion", "bottle_green_potion", "bottle_blue_potion", "bottle_milk", "bottle_milk_half", "bottle_fairy", "bottle_fish", "bottle_bugs", "bottle_blue_fire", "bottle_poe", "bottle_big_poe", "bottle_letter" };
        var bagKey = key.StartsWith("bag:", StringComparison.Ordinal) ? key[4..] : "";
        var bagRole = bagKey == "arrows" ? "quiver" : bagKey;
        int[] levels = bagKey switch { "sticks" => new[] { 10, 20, 30 }, "nuts" or "bombs" => new[] { 20, 30, 40 }, _ => new[] { 30, 40, 50 } };
        _choiceIcons[key] = choices.Select(choice => (choice.Text,
            key.StartsWith("bottle:", StringComparison.Ordinal) ? (choice.Value > 0 && choice.Value < bottleRoles.Length ? bottleRoles[choice.Value] : null) :
            choice.Value > 0 && Array.IndexOf(levels, choice.Value) >= 0 ? $"bag_{bagRole}_{Array.IndexOf(levels, choice.Value) + 1}" : null)).ToArray();
        var combo = new ComboBox { ItemsSource = choices.Select(x => x.Text).ToArray(), SelectedIndex = Array.FindIndex(choices, x => x.Value == selected), MinWidth = 200 };
        _choices[key] = combo;
        _stagedChoices[key] = selected;
        int previous = combo.SelectedIndex;
        combo.SelectionChanged += async (_, _) => {
            if (_loading) { previous = combo.SelectedIndex; return; }
            if (combo.SelectedIndex < 0) return;
            combo.IsEnabled = false;
            try {
                var value = choices[combo.SelectedIndex].Value;
                var result = await Ask(new JsonObject { ["cmd"] = "set_stat", ["key"] = key, ["value"] = value.ToString() });
                ReadAmounts(result);
                previous = combo.SelectedIndex; _stagedChoices[key] = value; _view.UpdateVisual(_summary, _icons, _theme); Changed(result);
            }
            catch (Exception ex) { _loading = true; combo.SelectedIndex = previous; _loading = false; _status.Text = ex.Message; }
            finally { combo.IsEnabled = true; }
        };
        ToolTip.SetTip(combo, label);
    }
    private void AddProgression(string group, (string Label, string Id)[] entries)
    {
        var combo = new ComboBox { ItemsSource = new[] { "None" }.Concat(entries.Select(x => x.Label)).ToArray(),
            SelectedIndex = Array.FindLastIndex(entries, x => _owned[x.Id]) + 1, MinWidth = 200 };
        _choices["progressive_" + group] = combo;
        string[] roles = group switch {
            "ocarina" => new[] { "ocarina_fairy", "ocarina_time" },
            "hookshot" => new[] { "hookshot", "longshot" },
            "strength" => new[] { "bracelet_goron", "gauntlets_silver", "gauntlets_gold" },
            "scale" => new[] { "scale_silver", "scale_gold" },
            _ => Array.Empty<string>()
        };
        _choiceIcons["progressive_" + group] = new[] { ("None", (string?)null) }
            .Concat(entries.Select((entry, index) => (entry.Label, (string?)roles[index]))).ToArray();
        combo.SelectionChanged += async (_, _) => {
            if (_loading || combo.SelectedIndex < 0) return;
            var old = Array.FindLastIndex(entries, x => _owned[x.Id]) + 1;
            int selected = combo.SelectedIndex;
            combo.IsEnabled = false;
            try {
                var result = await Ask(new JsonObject { ["cmd"] = "progressive", ["group"] = group,
                    ["item"] = selected == 0 ? null : entries[selected - 1].Id });
                for (int i = 0; i < entries.Length; i++) _owned[entries[i].Id] = i + 1 == selected;
                _view.UpdateVisual(_summary, _icons, _theme); Changed(result);
            } catch (Exception ex) { _loading = true; combo.SelectedIndex = old; _loading = false; _status.Text = ex.Message; }
            finally { combo.IsEnabled = true; }
        };
        ToolTip.SetTip(combo, group);
    }
    private async void EditIcon(string role)
    {
        if (_loading) return;
        if (SongIds.TryGetValue(role, out var song) && _songs.TryGetValue(song, out var learned)) {
            _loading = true;
            try {
                var result = await Ask(new JsonObject { ["cmd"] = "set_song", ["song"] = song, ["learned"] = !learned });
                _songs[song] = !learned;
                _view.UpdateVisual(_summary, _icons, _theme); Changed(result);
            } catch (Exception ex) { _status.Text = ex.Message; }
            finally { _loading = false; }
            return;
        }
        if (role == "trade_quest") {
            var items = TradeItems.Where(item => _owned.ContainsKey(ItemIds[item.Role])).ToArray();
            var icons = items.Select(item => (item.Label + (_owned[ItemIds[item.Role]] ? " · held" : " · not held"), (string?)item.Role)).ToArray();
            _selector.Content = null;
            _view.ShowIconChoices(role, icons, index => EditIcon(items[index].Role));
            return;
        }
        string? key = role.StartsWith("progressive_", StringComparison.Ordinal) ? role : role.StartsWith("bottle_slot_", StringComparison.Ordinal) && int.TryParse(role[12..], out var number)
            ? "bottle:" + (number - 1) : role.StartsWith("bag_", StringComparison.Ordinal)
                ? "bag:" + (role[4..] == "quiver" ? "arrows" : role[4..]) : null;
        if (key is not null && _choices.TryGetValue(key, out var choice)) {
            if (_choiceIcons.TryGetValue(key, out var icons)) {
                _selector.Content = null;
                _view.ShowIconChoices(role, icons, index => choice.SelectedIndex = index);
                return;
            }
            _selector.Content = choice; choice.Focus(); choice.IsDropDownOpen = true; return;
        }
        if (AmountIds.TryGetValue(role, out var amountId) && _amounts.TryGetValue(amountId, out var amount)) {
            _loading = true;
            try {
                int next = amount > 0 ? 0 : 1;
                var result = await Ask(new JsonObject { ["cmd"] = "set_stat", ["key"] = "n:" + amountId, ["value"] = next.ToString() });
                ReadAmounts(result); _view.UpdateVisual(_summary, _icons, _theme); Changed(result);
            } catch (Exception ex) { _status.Text = ex.Message; }
            finally { _loading = false; }
            return;
        }
        if (!ItemIds.TryGetValue(role, out var id) || !_owned.TryGetValue(id, out var have)) return;
        _loading = true;
        try {
            string? bagKey = id switch { "Weapon_Range_SlingShot" => "bag:seeds", "Weapon_Range_Bow" => "bag:arrows", _ => null };
            bool addedBag = false;
            if (!have && bagKey is not null)
            {
                if (!_stagedChoices.TryGetValue(bagKey, out int capacity) || !_choices.TryGetValue(bagKey, out var bagChoice))
                    throw new InvalidOperationException("This save does not have an editable ammo bag for that weapon.");
                if (capacity == 0)
                {
                    var bagResult = await Ask(new JsonObject { ["cmd"] = "set_stat", ["key"] = bagKey, ["value"] = "30" });
                    _stagedChoices[bagKey] = 30;
                    bagChoice.SelectedIndex = 1;
                    ReadAmounts(bagResult);
                    _view.UpdateVisual(_summary, _icons, _theme);
                    Changed(bagResult);
                    addedBag = true;
                }
            }
            var result = await Ask(new JsonObject { ["cmd"] = "give", ["items"] = new JsonArray(id), ["give"] = !have, ["forget"] = false });
            var problem = result["problems"]?.AsArray().FirstOrDefault()?.ToString();
            if (!string.IsNullOrEmpty(problem)) { _status.Text = problem; return; }
            _owned[id] = !have;
            _view.UpdateVisual(_summary, _icons, _theme);
            Changed(result);
            if (addedBag) _status.Text += bagKey == "bag:seeds" ? " Basic seed bag added." : " Basic quiver added.";
        } catch (Exception ex) { _status.Text = ex.Message; }
        finally { _loading = false; }
    }
    private void ReadAmounts(JsonObject stats)
    {
        if (stats["amounts"] is not JsonArray amounts) return;
        foreach (var amount in amounts)
        {
            if (amount?["n"] is not JsonValue value || !value.TryGetValue<int>(out var count)) continue;
            var id = amount["item"]!.ToString();
            _amounts[id] = count;
            _amountLimits[id] = amount["most"]?.GetValue<int>() ?? 0;
        }
    }
    private void ReadHearts(JsonObject stats)
    {
        _heartCurrent = stats["hearts"] is JsonValue current && current.TryGetValue<double>(out var c) ? c : null;
        _heartMaximum = stats["max_hearts"] is JsonValue maximum && maximum.TryGetValue<double>(out var m) ? m : null;
    }
    private void ReadMagic(JsonObject stats) => _magicLevel = stats["max_magic"] is JsonValue max && max.TryGetValue<double>(out _)
        ? stats["meter"]?.GetValue<int>() : null;
    private async void CycleMagic()
    {
        if (_loading || _magicLevel is not int level) return;
        _loading = true;
        try {
            var result = await Ask(new JsonObject { ["cmd"] = "set_stat", ["key"] = "meter", ["value"] = ((level + 1) % 3).ToString() });
            ReadMagic(result); _view.UpdateVisual(_summary, _icons, _theme); Changed(result);
        } catch (Exception ex) { _status.Text = ex.Message; }
        finally { _loading = false; }
    }
    private async void SetHeartContainers(int count)
    {
        if (IsBusy || _heartMaximum is null || count == _heartMaximum) return;
        _loading = true;
        try {
            var result = await Ask(new JsonObject { ["cmd"] = "set_stat", ["key"] = "max_hearts", ["value"] = count.ToString(System.Globalization.CultureInfo.InvariantCulture) });
            ReadHearts(result); _view.UpdateVisual(_summary, _icons, _theme); Changed(result);
        } catch (Exception ex) { _status.Text = ex.Message; }
        finally { _loading = false; }
    }
    private async void SetQuantity(string role, int value)
    {
        if (IsBusy || !QuantityIds.TryGetValue(role, out var id) || !_amounts.TryGetValue(id, out var current) || value == current) return;
        _loading = true;
        try {
            var result = await Ask(new JsonObject { ["cmd"] = "set_stat", ["key"] = "n:" + id, ["value"] = value.ToString() });
            ReadAmounts(result); _view.UpdateVisual(_summary, _icons, _theme); Changed(result);
        } catch (Exception ex) { _status.Text = ex.Message; _view.UpdateVisual(_summary, _icons, _theme); }
        finally { _loading = false; }
    }
    public async Task<bool> Save()
    {
        if (IsBusy) return false;
        if (HookshotGame.IsRunning()) { _status.Text = "Close Hookshot before saving changes."; return false; }
        _save.IsEnabled = false;
        try { var result = await Ask(new JsonObject { ["cmd"] = "save" }); _edited = 0; _status.Text = "Saved. Backup: " + result["backup"]; return true; }
        catch (Exception ex) { _status.Text = "Could not save: " + ex.Message; _save.IsEnabled = _edited > 0; return false; }
    }
}
