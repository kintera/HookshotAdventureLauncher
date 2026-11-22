using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using HookshotAdventure.Core;

namespace HookshotAdventure.App;

/// <summary>
/// Native Avalonia reproduction of the approved Qt ZeldaStatsWidget's fixed
/// 1120 x 456 scene. Viewbox scales the *same* coordinates for smaller windows.
/// No web images, no proprietary textures in the source package.
/// </summary>
public sealed class HAL_AdventureSummaryView : UserControl
{
    private SaveSummary? _save;
    private HAL_IconCacheService? _cache;
    private ThemePalette _theme = ThemeCatalog.Resolve(ThemeCatalog.DefaultTheme);
    private Canvas? _scene;
    private readonly Dictionary<string, (double X, double Y, double Size)> _editPositions = new(StringComparer.Ordinal);
    private Control? _choiceMenu;
    public bool Compact { get; set; }
    public bool ExpandToFill { get; set; }
    public Action<string>? EditAction { get; set; }
    public Func<string, bool?>? EditOwnership { get; set; }
    public Func<string, bool>? CanEdit { get; set; }
    public Func<int, BottleSlotInfo?>? EditBottle { get; set; }
    public Func<string, int?>? EditBag { get; set; }
    public Func<string, int?>? EditAmount { get; set; }
    public Func<string, (int Value, int Maximum)?>? EditQuantity { get; set; }
    public Action<string, int>? SetEditQuantity { get; set; }
    public Func<(double Current, double Maximum)?>? EditHearts { get; set; }
    public Action<int>? SetEditHearts { get; set; }
    public Func<int?>? EditMagicLevel { get; set; }
    public Action? CycleEditMagic { get; set; }
    private static readonly string[] InventoryRoles =
    {
        "slingshot", "boomerang", "deku_sticks", "deku_nuts", "magic_bean", "progressive_ocarina", "lens_of_truth",
        "bow", "progressive_hookshot", "hammer", "bomb", "bombchu", "business_scrub_seed", "boots_iron",
        "dins_fire", "farores_wind", "nayrus_love", "fire_arrow", "ice_arrow", "light_arrow", "boots_hover",
        "bottle_slot_1", "bottle_slot_2", "bottle_slot_3", "bottle_slot_4", "bottle_slot_5", "agony_stone", "progressive_strength"
    };
    private static readonly string[] Masks =
    {
        "mask_keaton", "mask_skull", "mask_spooky", "mask_bunny", "mask_truth",
        "mask_goron", "mask_zora", "mask_gerudo", "mask_amiibo_0", "mask_amiibo_1"
    };
    private static readonly string[] Equip =
    {
        "sword_kokiri", "sword_master", "sword_biggoron", "shield_deku", "shield_hylian", "shield_mirror"
    };
    private static readonly string[] Tunics = { "tunic_kokiri", "tunic_goron", "tunic_zora" };
    private static readonly string[] AdultTrade =
    {
        "trade_pocket_egg", "trade_pocket_cucco", "trade_cojiro", "trade_odd_mushroom", "trade_odd_poultice",
        "trade_poachers_saw", "trade_broken_goron_sword", "trade_prescription", "trade_eyeball_frog",
        "trade_eye_drops", "trade_claim_check"
    };
    private static readonly string[] ChildTrade =
    {
        "trade_weird_egg", "trade_wake_up_cucco", "trade_zelda_letter"
    };
    private Color Accent => Color.Parse(_theme.Rim);
    private Color Surface => Color.Parse(_theme.Dark);
    private Color Rim => Color.Parse(_theme.Rim);
    private static readonly Color Ink = Color.Parse("#eee9e2");
    private static readonly Color Muted = Color.Parse("#b7b0c1");
    private static IBrush Brush(Color color) => new SolidColorBrush(color);

    public HAL_AdventureSummaryView() => UpdateVisual(null, null, ThemeCatalog.DefaultTheme);

    public void UpdateVisual(SaveSummary? save, HAL_IconCacheService? cache, string? themeId)
    {
        _save = save;
        _cache = cache;
        _theme = ThemeCatalog.Resolve(themeId);
        var canvas = new Canvas { Width = Compact ? 690 : 1120, Height = Compact ? 58 : 456 };
        _scene = canvas;
        _choiceMenu = null;
        _editPositions.Clear();
        if (Compact) DrawCompact(canvas);
        else DrawExpanded(canvas);
        Content = new Viewbox
        {
            Child = canvas,
            Stretch = Stretch.Uniform,
            StretchDirection = ExpandToFill && !Compact ? StretchDirection.Both : StretchDirection.DownOnly,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top
        };
    }

    private static void Place(Canvas scene, Control item, double x, double y)
    {
        Canvas.SetLeft(item, x);
        Canvas.SetTop(item, y);
        scene.Children.Add(item);
    }

    private void Frame(Canvas c, double x, double y, double width, double height, bool inner = false)
    {
        var back = inner ? Color.Parse("#0d0b11") : Surface;
        var b = new Border
        {
            Width = width, Height = height, CornerRadius = new CornerRadius(inner ? 9 : 10),
            Background = Brush(back), BorderBrush = Brush(Rim),
            BorderThickness = inner ? new Thickness(2, 2, 1, 1) : new Thickness(1),
            Opacity = inner ? .91 : .98
        };
        Place(c, b, x, y);
    }

    private void Label(Canvas c, string text, double x, double y, double width, double height,
        double font = 12, bool bold = false, Color? ink = null, TextAlignment align = TextAlignment.Left)
    {
        var item = new TextBlock
        {
            Text = text, Width = width, Height = height, FontSize = font,
            FontWeight = bold ? FontWeight.Bold : FontWeight.Normal,
            Foreground = Brush(ink ?? Ink), TextAlignment = align,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
        };
        Place(c, item, x, y);
    }

    private ItemState? State(string role) => _save?.Items.FirstOrDefault(x => x.Role == role);
    private bool? Owned(string role) => EditOwnership?.Invoke(role) ?? (State(role)?.State switch
    {
        OwnershipState.Owned => true,
        OwnershipState.NotCarried => false,
        _ => null
    });
    private bool Equipped(string role) => State(role)?.Evidence.StartsWith("Equipped:", StringComparison.Ordinal) == true;
    private static double ItemOpacity(bool? owned, bool available = false) => owned is true ? 1 : available ? .76 : owned is null ? .28 : .13;

    private string ResolveProgressive(string role)
    {
        string[] choices = role switch
        {
            "progressive_ocarina" => new[] { "ocarina_fairy", "ocarina_time" },
            "progressive_hookshot" => new[] { "hookshot", "longshot" },
            "progressive_strength" => new[] { "bracelet_goron", "gauntlets_silver", "gauntlets_gold" },
            "progressive_scale" => new[] { "scale_silver", "scale_gold" },
            _ => new[] { role }
        };
        for (var i = choices.Length - 1; i >= 0; i--)
            if (Owned(choices[i]) is true) return choices[i];
        return choices[0];
    }

    private static string Glyph(string role) => role switch
    {
        var name when name.StartsWith("song_", StringComparison.Ordinal) => "♫",
        var name when name.StartsWith("sword_", StringComparison.Ordinal) => "⚔",
        var name when name.StartsWith("shield_", StringComparison.Ordinal) => "⬡",
        var name when name.StartsWith("mask_", StringComparison.Ordinal) => "◉",
        var name when name.StartsWith("bottle_", StringComparison.Ordinal) => "♧",
        var name when name.StartsWith("stone_", StringComparison.Ordinal) => "◆",
        var name when name.StartsWith("medallion_", StringComparison.Ordinal) => "✹",
        var name when name.StartsWith("trade_", StringComparison.Ordinal) => "✦",
        var name when name.StartsWith("bag_", StringComparison.Ordinal) => "▣",
        var name when name.StartsWith("wallet_", StringComparison.Ordinal) => "▣",
        "gold_skulltula" => "✹",
        "slingshot" => "⌁", "bow" => "⌁", "ocarina_fairy" or "ocarina_time" => "♫",
        "gerudo_pass" => "✧", "deku_sticks" => "♣", "deku_nuts" => "●",
        _ => "◇"
    };

    private void Icon(Canvas c, string role, double x, double y, double size, bool? owned,
                      string? tooltip = null, bool equipped = false, string? badge = null, bool available = false,
                      bool framed = true, string? editRole = null, Color? badgeInk = null)
    {
        owned = EditOwnership?.Invoke(editRole ?? role) ?? owned;
        if (framed)
        {
            var tile = new Border
            {
                Width = size + 6, Height = size + 6,
                CornerRadius = new CornerRadius(6), Background = Brush(Color.Parse("#0a0b10")),
                BorderBrush = Brush(equipped ? Accent : Color.Parse("#55424a")),
                BorderThickness = new Thickness(equipped ? 2.3 : 1)
            };
            if (!string.IsNullOrEmpty(tooltip)) ToolTip.SetTip(tile, tooltip);
            Place(c, tile, x - 3, y - 3);
        }
        var opacity = ItemOpacity(owned, available);
        var bitmap = _cache?.Get(role);
        if (bitmap is not null)
        {
            var image = new Image
            {
                Source = bitmap, Width = size, Height = size, Stretch = Stretch.Uniform, Opacity = opacity
            };
            if (!string.IsNullOrEmpty(tooltip)) ToolTip.SetTip(image, tooltip);
            Place(c, image, x, y);
        }
        else
        {
            var placeholder = new TextBlock
            {
                Text = Glyph(role), Width = size, Height = size, FontSize = size * .72,
                Foreground = Brush(Accent), Opacity = opacity,
                TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            ToolTip.SetTip(placeholder, (tooltip ?? role) + " · PNG unavailable; vector placeholder");
            Place(c, placeholder, x, y);
        }
        if (badge is not null)
        {
            var bw = Math.Max(16, Math.Min(size - 3, 8 + badge.Length * 7));
            var num = new Border
            {
                Width = bw, Height = 15, CornerRadius = new CornerRadius(3),
                Background = Brush(Color.Parse("#3210141b"))
            };
            // A translucent badge should not hide the original artwork.
            num.Opacity = .75;
            Place(c, num, x + size - bw - 1, y + size - 15);
            Label(c, badge, x + size - bw + 1, y + size - 15, bw - 4, 15,
                badge.Length >= 3 ? 9 : 10, true, badgeInk ?? Color.Parse("#ffdfa1"), TextAlignment.Right);
        }
        var action = editRole ?? role;
        if (EditAction is not null && CanEdit?.Invoke(action) == true)
        {
            _editPositions[action] = (x, y, size);
            var hit = new Button { Tag = action, Width = size + 6, Height = size + 6, Background = Brushes.Transparent,
                BorderBrush = Brush(Accent), BorderThickness = new Thickness(1), Padding = new Thickness(0), Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
            string bagHint = action switch {
                "slingshot" => " Adding the slingshot also adds a basic seed bag if needed.",
                "bow" => " Adding the bow also adds a basic quiver if needed.",
                _ => ""
            };
            ToolTip.SetTip(hit, "Edit " + (tooltip ?? role) + bagHint);
            hit.Click += (_, _) => EditAction(action);
            hit.PointerEntered += (_, _) => {
                if (EditQuantity?.Invoke(action) is { } quantity && SetEditQuantity is not null)
                    ShowQuantitySlider(action, quantity.Value, quantity.Maximum, value => SetEditQuantity(action, value));
            };
            Place(c, hit, x - 3, y - 3);
        }
        else if (EditAction is not null)
        {
            var hit = new Border { Width = size + 6, Height = size + 6, Background = Brushes.Transparent };
            ToolTip.SetTip(hit, (tooltip ?? role) + " · not editable in this save");
            Place(c, hit, x - 3, y - 3);
        }
    }

    public void ShowQuantitySlider(string anchor, int current, int maximum, Action<int> selected)
    {
        if (_scene is null || !_editPositions.TryGetValue(anchor, out var pos)) return;
        if (_choiceMenu is not null) _scene.Children.Remove(_choiceMenu);
        var label = new TextBlock { Text = $"{current} / {maximum}", Width = 42, FontSize = 10, Foreground = Brush(Ink), VerticalAlignment = VerticalAlignment.Center };
        var slider = new Slider { Tag = "quantity:" + anchor, Minimum = 0, Maximum = Math.Max(current, maximum), Value = current,
            Width = 80, Height = 22, Foreground = Brush(Accent), Background = Brush(Color.Parse(_theme.Light)), TickFrequency = 1, IsSnapToTickEnabled = true };
        var thumbBrush = Brush(Accent);
        slider.AttachedToVisualTree += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() => {
            foreach (var thumb in slider.GetVisualDescendants().OfType<Thumb>()) { thumb.Width = 12; thumb.Height = 12; thumb.Background = thumbBrush; }
        });
        slider.PropertyChanged += (_, e) => { if (e.Property == Slider.ValueProperty) label.Text = $"{(int)Math.Round(slider.Value)} / {maximum}"; };
        slider.PointerReleased += (_, _) => selected((int)Math.Round(slider.Value));
        slider.KeyUp += (_, _) => selected((int)Math.Round(slider.Value));
        var panel = new Border { Background = Brush(Surface), BorderBrush = Brush(Rim), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5), Padding = new Thickness(3), Child = new StackPanel {
                Orientation = Orientation.Horizontal, Spacing = 3, Children = { slider, label } } };
        var x = Math.Clamp(pos.X, 0, Math.Max(0, _scene.Width - 135));
        var y = pos.Y + pos.Size + 5;
        if (y + 30 > _scene.Height) y = Math.Max(0, pos.Y - 30);
        Place(_scene, panel, x, y);
        _choiceMenu = panel;
    }

    public void ShowIconChoices(string anchor, IReadOnlyList<(string Label, string? Role)> choices, Action<int> selected)
    {
        if (_scene is null || !_editPositions.TryGetValue(anchor, out var pos) || choices.Count == 0) return;
        if (_choiceMenu is not null) _scene.Children.Remove(_choiceMenu);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        for (int i = 0; i < choices.Count; i++)
        {
            var index = i;
            var (label, role) = choices[i];
            var picture = role is null ? null : _cache?.Get(role);
            var button = new Button { Tag = $"choice:{anchor}:{i}", Width = 34, Height = 34, Padding = new Thickness(2), Background = Brush(Color.Parse(_theme.Bottom)), BorderBrush = Brush(Rim), Foreground = Brush(Ink),
                Content = picture is null ? new TextBlock { Text = role is null ? "×" : Glyph(role), Foreground = Brush(Ink), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } :
                    new Image { Source = picture, Width = 28, Height = 28, Stretch = Stretch.Uniform } };
            ToolTip.SetTip(button, label);
            button.Click += (_, _) => { if (_choiceMenu is not null) _scene?.Children.Remove(_choiceMenu); _choiceMenu = null; selected(index); };
            row.Children.Add(button);
        }
        var panel = new Border { Background = Brush(Surface), BorderBrush = Brush(Rim), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5), Padding = new Thickness(3), Child = row };
        var width = choices.Count * 36 + 6;
        var x = Math.Clamp(pos.X, 0, Math.Max(0, _scene.Width - width));
        var y = pos.Y + pos.Size + 5;
        if (y + 40 > _scene.Height) y = Math.Max(0, pos.Y - 40);
        Place(_scene, panel, x, y);
        _choiceMenu = panel;
    }

    private void DrawHeart(Canvas c, int i, double x, double y, double size)
    {
        var edited = EditHearts?.Invoke();
        double max = edited?.Maximum ?? _save?.Hearts ?? -1;
        double current = edited?.Current ?? _save?.CurrentHearts ?? -1;
        bool collected = max >= 0 && i < max;
        double fill = collected && current >= 0 ? Math.Clamp(current - i, 0, 1) : 0;
        var role = fill switch
        {
            >= 1 => "heart_full", >= .75 => "heart_three_quarters",
            >= .5 => "heart_half", >= .25 => "heart_quarter", _ => "heart_empty"
        };
        if (_cache?.Get(role) is { } bmp)
        {
            Place(c, new Image { Source = bmp, Width = size, Height = size, Stretch = Stretch.Uniform,
                Opacity = collected ? 1 : .14 }, x, y);
        }
        else Label(c, "♥", x, y - 2, size, size + 3, size * 1.05, true,
            collected && fill > 0 ? Color.Parse("#e9535e") : Color.Parse("#53464d"), TextAlignment.Center);
        if (!Compact && edited is not null && SetEditHearts is not null)
        {
            var count = Math.Max(3, i + 1);
            var hit = new Button { Tag = $"heart:{i + 1}", Width = size, Height = size, Background = Brushes.Transparent,
                BorderBrush = Brush(Accent), BorderThickness = new Thickness(1), Padding = new Thickness(0),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
            ToolTip.SetTip(hit, $"Set heart containers to {count}");
            hit.Click += (_, _) => SetEditHearts(count);
            Place(c, hit, x, y);
        }
    }

    private void Magic(Canvas c, double x, double y, double width, double height)
    {
        Frame(c, x, y, width, height);
        double? max = _save?.MaxMagic;
        int level = EditMagicLevel?.Invoke() ?? (max == 24 ? 1 : max == 48 ? 2 : 0);
        if (level > 0)
        {
            Place(c, new Border { Width = width / 2 * level - 4, Height = height - 4,
                Background = Brush(Color.Parse("#56be80")), CornerRadius = new CornerRadius(3) }, x + 2, y + 2);
        }
        if (!Compact && EditMagicLevel?.Invoke() is int && CycleEditMagic is not null)
        {
            var hit = new Button { Tag = "magic_level", Width = width, Height = Math.Max(height, 20), Background = Brushes.Transparent,
                BorderBrush = Brush(Accent), BorderThickness = new Thickness(1), Padding = new Thickness(0), Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
            ToolTip.SetTip(hit, $"Magic: {new[] { "none", "normal", "double" }[level]}. Click to change level.");
            hit.Click += (_, _) => CycleEditMagic();
            Place(c, hit, x, y - 4);
        }
    }

    private void DrawCompact(Canvas c)
    {
        for (int i = 0; i < 20; i++) DrawHeart(c, i, 7 + i % 10 * 14, 6 + i / 10 * 20, 12);
        Label(c, "MAGIC", 7, 43, 36, 10, 8, true, Muted);
        Magic(c, 45, 44, 103, 8);
        var walletRole = _save?.Wallet switch { "Kokiri" => "wallet_child", "Adult" => "wallet_adult", "Giant" => "wallet_giant", _ => "wallet_child" };
        Icon(c, walletRole, 170, 14, 23, _save?.Wallet is null ? null : true, "Wallet", framed: false);
        Label(c, _save?.Rupees is int r ? $"{r}/{_save.WalletCapacity?.ToString() ?? "—"}" : "—/—", 199, 10, 104, 32, 16, true);
        Icon(c, "gold_skulltula", 371, 12, 27, _save?.Skulltulas is null ? null : true, "Gold Skulltulas", framed: false);
        Label(c, _save?.Skulltulas?.ToString() ?? "—", 403, 11, 42, 31, 15, true);
        string[] stones = { "stone_kokiri", "stone_goron", "stone_zora" };
        string[] stocks = { "stone_kokiri_emerald", "stone_goron_ruby", "stone_zora_sapphire" };
        for (int i = 0; i < 3; i++) Icon(c, stones[i], 451 + i * 25, 16, 21, Owned(stocks[i]), stones[i], framed: false);
        string[] meds = { "forest", "fire", "water", "spirit", "shadow", "light" };
        for (int i = 0; i < meds.Length; i++) Icon(c, "medallion_" + meds[i], 538 + i * 25, 16, 20,
            Owned("medallion_" + meds[i]), meds[i] + " Medallion", framed: false);
    }

    private void DrawExpanded(Canvas c)
    {
        Frame(c, 1, 1, 1118, 454);
        Frame(c, 13, 12, 415, 430, inner: true);
        Frame(c, 443, 12, 665, 430, inner: true);
        Label(c, "LIFE & QUEST", 24, 18, 190, 22, 15, true, Accent);
        Label(c, "INVENTORY", 465, 22, 220, 20, 15, true, Accent);
        var editedHealth = EditHearts?.Invoke();
        var health = editedHealth is { } h ? $"{h.Current:0.##} / {h.Maximum:0.##} hearts" :
            _save is null ? "— / — hearts" : $"{_save.CurrentHearts?.ToString("0.##") ?? "—"} / {_save.Hearts?.ToString("0.##") ?? "—"} hearts";
        Label(c, health, 225, 21, 186, 18, 12, true, Ink, TextAlignment.Right);
        for (int i = 0; i < 20; i++) DrawHeart(c, i, 25 + i % 10 * 38, 45 + i / 10 * 36, 29);
        Label(c, "MAGIC", 25, 115, 57, 17, 10, true, Muted);
        Magic(c, 89, 117, 233, 12);
        Label(c, "WALLET / RUPEES", 25, 137, 170, 18, 10, false, Muted);
        Label(c, "SKULLTULAS", 294, 137, 124, 18, 10, false, Muted);
        var walletRole = _save?.Wallet switch { "Kokiri" => "wallet_child", "Adult" => "wallet_adult", "Giant" => "wallet_giant", _ => "wallet_child" };
        Icon(c, walletRole, 27, 159, 36, _save?.Wallet is null ? null : true, "Wallet", framed: false);
        Label(c, _save?.Rupees is int rup ? $"{rup}/{_save.WalletCapacity?.ToString() ?? "—"}" : "—/—",
            70, 163, 128, 31, 17, true, Ink);
        Icon(c, "gold_skulltula", 296, 159, 36, _save?.Skulltulas is null ? null : true, "Gold Skulltulas", framed: false);
        Label(c, _save?.Skulltulas?.ToString() ?? "—", 337, 163, 66, 31, 17, true);
        Label(c, "SPIRITUAL STONES", 25, 208, 235, 19, 11, false, Muted);
        string[] stoneRoles = { "stone_kokiri", "stone_goron", "stone_zora" };
        string[] stoneStockRoles = { "stone_kokiri_emerald", "stone_goron_ruby", "stone_zora_sapphire" };
        for (int i = 0; i < 3; i++) Icon(c, stoneRoles[i], 259 + i * 49, 203, 33,
            Owned(stoneStockRoles[i]), stoneStockRoles[i], framed: false);
        Label(c, "MEDALLIONS", 25, 249, 160, 18, 11, false, Muted);
        string[] medallions = { "forest", "fire", "water", "spirit", "shadow", "light" };
        for (int i = 0; i < medallions.Length; i++) Icon(c, "medallion_" + medallions[i], 156 + i * 43, 246, 32,
            Owned("medallion_" + medallions[i]), medallions[i] + " Medallion", framed: false);
        Label(c, "MASKS", 25, 275, 72, 17, 10, false, Muted);
        bool masksUnlocked = Owned("mask_truth") == true;
        for (int i = 0; i < Masks.Length; i++)
        {
            var role = Masks[i];
            // The two Amiibo-only masks must never illuminate from Mask of Truth.
            var available = masksUnlocked && role is not ("mask_amiibo_0" or "mask_amiibo_1");
            Icon(c, role, 24 + i * 40, 300, 34, Owned(role), role, available: available);
        }
        Label(c, "SONGS", 25, 350, 130, 18, 11, true, Muted);
        for (int i = 0; i < HAL_SaveDisplayFields.Songs.Count; i++)
        {
            var song = HAL_SaveDisplayFields.Songs[i];
            int row = i < 7 ? 0 : 1, songColumn = i < 7 ? i : i - 7;
            Icon(c, song.Role, (row == 0 ? 35 : 60) + songColumn * 50, 371 + row * 37, 27,
                Owned(song.Role), song.Label + " · " + (State(song.Role)?.Status ?? "Unknown"));
        }
        for (int i = 0; i < InventoryRoles.Length; i++)
        {
            var slot = InventoryRoles[i];
            double x = 455 + i % 7 * 69, y = 65 + i / 7 * 54;
            if (slot.StartsWith("bottle_slot_", StringComparison.Ordinal))
            {
                int number = int.Parse(slot[^1..]);
                var bottle = EditBottle?.Invoke(number) ?? _save?.BottleSlots.FirstOrDefault(b => b.Number == number);
                Icon(c, bottle?.Role ?? "bottle_empty", x, y, 32, bottle?.Owned,
                    $"Bottle {number}: {bottle?.Caption ?? "Unknown"}", editRole: slot);
                Label(c, bottle?.Caption ?? "Unknown", x - 20, y + 37, 72, 12, 9, false, Muted, TextAlignment.Center);
            }
            else
            {
                var role = ResolveProgressive(slot);
                var supplyKey = slot switch { "slingshot" => "seeds", "bow" => "arrows", "deku_sticks" => "sticks", "deku_nuts" => "nuts",
                    "magic_bean" => "beans", "bomb" => "bombs", "bombchu" => "bombchus", "business_scrub_seed" => "business_seeds", _ => null };
                int? count = EditAmount?.Invoke(slot);
                if (count is null && supplyKey is not null && _save?.Supplies.TryGetValue(supplyKey, out var savedCount) == true) count = savedCount;
                // Slingshot and bow ownership is independent of their ammo;
                // consumable icons instead represent the quantity carried.
                bool? owned = slot is not ("slingshot" or "bow") && supplyKey is not null && count.HasValue
                    ? count.Value > 0 : Owned(role);
                Icon(c, role, x, y, 32, owned, role, badge: count?.ToString(), editRole: slot);
            }
        }
        Label(c, "SWORDS", 455, 295, 220, 17, 10, true, Muted);
        Label(c, "SHIELDS", 731, 295, 196, 17, 10, true, Muted);
        var col = new[] { 0, 1, 2, 4, 5, 6 };
        for (int i = 0; i < Equip.Length; i++)
        {
            var role = Equip[i];
            Icon(c, role, 455 + col[i] * 69, 317, 35, Owned(role), role, Equipped(role));
        }
        Label(c, "TUNICS", 455, 363, 220, 16, 10, true, Muted);
        for (int i = 0; i < Tunics.Length; i++)
        {
            var role = Tunics[i];
            Icon(c, role, 455 + i * 69, 385, 32, Owned(role), role, Equipped(role));
        }
        var passOwned = Owned("gerudo_pass");
        Label(c, "PASS", 731 - 27, 363, 90, 16, 10, true, Muted, TextAlignment.Center);
        Icon(c, "gerudo_pass", 731, 385, 36, passOwned, "Gerudo Membership Pass");
        var scale = ResolveProgressive("progressive_scale");
        var silverScale = Owned("scale_silver");
        var goldScale = Owned("scale_gold");
        bool? hasScale = silverScale == true || goldScale == true ? true :
            silverScale == false && goldScale == false ? false : null;
        Label(c, "SCALE", 800 - 27, 363, 90, 16, 10, true, Muted, TextAlignment.Center);
        Icon(c, scale, 800, 385, 36, hasScale, scale == "scale_gold" ? "Golden Scale" : "Silver Scale", editRole: "progressive_scale");
        var trade = LatestTrade();
        Label(c, "TRADING QUEST", 869 - 32, 363, 100, 16, 10, true, Muted, TextAlignment.Center);
        Icon(c, trade ?? "trade_weird_egg", 869, 385, 36, trade is null ? null : true,
            trade is null ? "No currently held trade item confirmed" : "Currently held: " + trade, editRole: "trade_quest");
        Place(c, new Border { Width = 1, Height = 360, Background = Brush(Rim), Opacity = .7 }, 940, 61);
        Label(c, "BAGS", 953, 24, 126, 20, 14, true, Accent);
        var bags = new[] { ("sticks", "Sticks", new[] {10,20,30}), ("nuts", "Nuts", new[] {20,30,40}),
            ("seeds", "Seed / Bullet", new[] {30,40,50}), ("quiver", "Quiver", new[] {30,40,50}),
            ("bombs", "Bomb Bag", new[] {20,30,40}) };
        for (int i = 0; i < bags.Length; i++)
        {
            var (key, shortName, validLevels) = bags[i];
            int? capacity = null;
            if (_save is not null && _save.BagCapacities.TryGetValue(key, out var known)) capacity = known;
            capacity = EditBag?.Invoke(key) ?? capacity;
            int tier = capacity is int n ? Array.IndexOf(validLevels, n) + 1 : 0;
            var role = $"bag_{key}_{Math.Max(1, tier)}";
            double y = 70 + i * 68;
            Icon(c, role, 955, y, 33, capacity.HasValue ? capacity.Value > 0 : null, shortName,
                badge: tier > 0 ? capacity.ToString() : null, framed: false, editRole: "bag_" + key, badgeInk: Color.Parse("#72d48a"));
            Label(c, shortName, 997, y + 1, 94, 16, 11, true);
            Label(c, capacity is null ? "— · Unknown" : capacity == 0 ? "Not acquired" : $"Level {tier}/3",
                997, y + 19, 96, 16, 10, false, Muted);
        }
    }

    private string EquipmentLabel(string prefix)
    {
        var selected = _save?.Items.FirstOrDefault(i => i.Role.StartsWith(prefix, StringComparison.Ordinal)
            && i.State == OwnershipState.Owned && i.Evidence.StartsWith("Equipped:", StringComparison.Ordinal));
        return selected?.Label ?? "Unknown";
    }

    private string? LatestTrade()
    {
        // Stock is current possession, not past trading-history completion.
        foreach (var role in AdultTrade.Reverse().Concat(ChildTrade.Reverse()))
            if (Owned(role) == true) return role;
        return null;
    }
}
