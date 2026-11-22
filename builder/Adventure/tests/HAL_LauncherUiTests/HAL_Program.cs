using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Media;
using Avalonia.Controls.Presenters;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HookshotAdventure.App;
using HookshotAdventure.Core;
using System.Buffers.Binary;

internal static class HAL_Program
{
    public static ProfileRepository Repository = null!;
    public static string Output = "";
    [STAThread]
    public static int Main(string[] args)
    {
        var root = Path.Combine(Path.GetTempPath(), "HAL-ui-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Output = args.FirstOrDefault(a => !a.StartsWith("--")) ?? Path.Combine(root, "screenshots");
            Directory.CreateDirectory(Output);
            TestStagedDevInstallKeepsExistingLogs(root);
            Repository = new ProfileRepository(Path.Combine(root, "profiles"), Path.Combine(root, "no-legacy.json"));
            File.WriteAllText(Path.Combine(Repository.Root, "preferences.json"),
                System.Text.Json.JsonSerializer.Serialize(new { game_directory = Path.Combine(root, "missing-game") }));
            var vanilla = Repository.Create("Link");
            var random = Repository.CreateRandomized("Randomized", "0123456789ABCDEF", (directory, id) =>
            {
                Directory.CreateDirectory(Path.Combine(directory, "native-randomizer"));
                File.WriteAllText(Path.Combine(directory, "native-randomizer", "seed.json"),
                    System.Text.Json.JsonSerializer.Serialize(new
                    {
                        seed = "0123456789ABCDEF",
                        settings = new
                        {
                            chest_scope = "childhood", shuffle_chests = true, shuffle_heart_pieces = true,
                            start_with_dungeon_maps_compasses = false, child_stealth = true
                        },
                        metadata = new { plan = new { assignments = new[] { new { id = "check-1" }, new { id = "check-2" } }, logic = "childhood checks; experimental logic" } },
                        binding = new { profile_id = id }
                    }));
            });
            WriteSave(vanilla.UserDir);
            WriteSave(random.UserDir);
            WriteSaveSlot(random.UserDir, 1, 31, 5);
            Repository.Pin(vanilla.Id, 0);
            Repository.Pin(random.Id, 1);
            if (args.Contains("--native"))
                return AppBuilder.Configure<HAL_NativeTestApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(Array.Empty<string>());

            AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .UseSkia().SetupWithoutStarting();
            var window = new MainWindow(Repository);
            window.Show();
            Pump();
            var initialSkullMaximum = window.FindControl<ComboBox>("MaximumSkulltulasCount")!;
            Check(initialSkullMaximum.Items.Cast<ComboBoxItem>().Select(i => i.Tag?.ToString()).SequenceEqual(new[] { "0", "10", "20", "30", "40", "50", "100" }) &&
                (initialSkullMaximum.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "50",
                "Skulltula maximum offers reward milestones and defaults to 50");
            var skullTree = new TreeView();
            typeof(MainWindow).GetMethod("AddSkulltulaLocations", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .Invoke(null, new object[] { skullTree, true });
            Check(skullTree.Items.Cast<TreeViewItem>().Single().Items.Count == 6,
                "HAL Skulltula resource populates all six House rewards");
            var skullStatuses = skullTree.Items.Cast<TreeViewItem>().Single().Items.Cast<TreeViewItem>()
                .Select(item => ((Grid)item.Header!).Children.OfType<TextBlock>().Last().Text).ToArray();
            Check(skullStatuses.Take(5).All(status => status == "Selected") && skullStatuses[5] == "Vanilla",
                "Skulltula preview selects one-time rewards and keeps the repeatable reward vanilla");
            var fakeGame = Path.Combine(root, "available-game");
            Directory.CreateDirectory(Path.Combine(fakeGame, "romfs"));
            File.WriteAllText(Path.Combine(fakeGame, OperatingSystem.IsWindows() ? "hookshot.exe" : "hookshot"), "fixture");
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(Path.Combine(fakeGame, "hookshot"), UnixFileMode.UserRead | UnixFileMode.UserExecute);
            File.WriteAllText(Path.Combine(fakeGame, OperatingSystem.IsWindows() ? "hookshot_vk.dll" : "hookshot_vk.so"), "fixture");
            File.WriteAllText(Path.Combine(fakeGame, "image.bin"), "fixture");
            var preferences = (PreferenceStore)typeof(MainWindow).GetField("_preferences", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(window)!;
            var previousGame = preferences.Value.GameDirectory;
            preferences.Value.GameDirectory = fakeGame;
            var external = new ProfileRecord { Id = Guid.NewGuid().ToString("N"), UserDir = vanilla.UserDir, External = true, Kind = "vanilla" };
            var reason = typeof(MainWindow).GetMethod("CanLaunchReason", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(window, new object[] { external });
            Check(reason is null, "An external adventure can launch when its selected game exists");
            preferences.Value.GameDirectory = previousGame;
            Check(window.FindControl<Button>("CopyExternalButton") is null, "Adventure details does not offer an unsolicited copy button");
            var suppliesView = new HAL_AdventureSummaryView();
            var suppliesWindow = new Window { Content = suppliesView };
            suppliesWindow.Show(window);
            var supplies = new SaveSummary {
                Supplies = new Dictionary<string, int?> { ["nuts"] = 17, ["sticks"] = 0, ["seeds"] = 12 },
                Items = new[] { new ItemState("slingshot", "Slingshot", "Gear", OwnershipState.NotCarried, false, "fixture"),
                    new ItemState("hammer", "Hammer", "Gear", OwnershipState.Owned, false, "fixture"),
                    new ItemState("lens_of_truth", "Lens", "Gear", OwnershipState.NotCarried, false, "fixture"),
                    new ItemState("longshot", "Longshot", "Gear", OwnershipState.Owned, false, "fixture"),
                    new ItemState("scale_silver", "Silver Scale", "Upgrades", OwnershipState.NotCarried, false, "fixture"),
                    new ItemState("scale_gold", "Golden Scale", "Upgrades", OwnershipState.Owned, false, "fixture") }
            };
            suppliesView.UpdateVisual(supplies, null, ThemeCatalog.DefaultTheme); Pump();
            double SupplyOpacity(string role) => suppliesView.GetVisualDescendants().OfType<TextBlock>()
                .Single(x => ToolTip.GetTip(x)?.ToString()?.StartsWith(role + " · PNG", StringComparison.Ordinal) == true).Opacity;
            Check(SupplyOpacity("deku_nuts") == 1 && SupplyOpacity("deku_sticks") == .13 && SupplyOpacity("magic_bean") == .28,
                "Consumable icons distinguish carried, empty and unknown quantities");
            Check(SupplyOpacity("slingshot") == .13, "Seeds do not make an unowned slingshot appear owned");
            Check(SupplyOpacity("hammer") == 1 && SupplyOpacity("lens_of_truth") == .13 && SupplyOpacity("longshot") == 1,
                "Equipment and progressive item icons use the saved ownership state");
            Check(suppliesView.GetVisualDescendants().OfType<TextBlock>().Any(t =>
                ToolTip.GetTip(t)?.ToString()?.StartsWith("Golden Scale · PNG", StringComparison.Ordinal) == true && t.Opacity == 1),
                "Owned golden scale appears in the save summary");
            suppliesView.UpdateVisual(new SaveSummary { Items = new[] {
                new ItemState("mask_truth", "Mask of Truth", "Masks", OwnershipState.Owned, false, "fixture") } }, null, ThemeCatalog.DefaultTheme);
            Pump();
            Check(suppliesView.GetVisualDescendants().OfType<TextBlock>().Any(t =>
                ToolTip.GetTip(t)?.ToString()?.StartsWith("No currently held trade item confirmed", StringComparison.Ordinal) == true),
                "An owned mask alone does not appear as a trade item");
            suppliesView.UpdateVisual(new SaveSummary { Supplies = new Dictionary<string, int?> {
                ["nuts"] = 17, ["sticks"] = 1, ["beans"] = 1, ["bombs"] = 1, ["bombchus"] = 1, ["business_seeds"] = 1 },
                Items = supplies.Items }, null, ThemeCatalog.DefaultTheme); Pump();
            Check(new[] { "deku_nuts", "deku_sticks", "magic_bean", "bomb", "bombchu", "business_scrub_seed" }
                .All(role => SupplyOpacity(role) == 1), "Every carried consumable icon is fully visible");
            suppliesView.EditAmount = role => role == "deku_nuts" ? 0 : null;
            suppliesView.UpdateVisual(new SaveSummary { Supplies = new Dictionary<string, int?> { ["nuts"] = 17 } }, null, ThemeCatalog.DefaultTheme); Pump();
            Check(SupplyOpacity("deku_nuts") == .13, "Staged consumable counts control icon ownership in edit mode");
            suppliesWindow.Close(); Pump();
            var editUser = Path.Combine(root, "editor-user");
            var editSlot = Path.Combine(editUser, "save", "fixture", "slot_00");
            Directory.CreateDirectory(editSlot);
            var editFile = Path.Combine(editSlot, "progress.sav");
            var fixture = new byte[0x175D8 + 128];
            BinaryPrimitives.WriteUInt32LittleEndian(fixture.AsSpan(0), 0x01020304);
            BinaryPrimitives.WriteUInt32LittleEndian(fixture.AsSpan(4), 0x002D80EB);
            BinaryPrimitives.WriteUInt32LittleEndian(fixture.AsSpan(8), 0x175D8);
            BinaryPrimitives.WriteUInt32LittleEndian(fixture.AsSpan(40), 0xF39CE410); // bottle in-use flag
            BinaryPrimitives.WriteUInt32LittleEndian(fixture.AsSpan(52), 2); // Int table
            BinaryPrimitives.WriteUInt32LittleEndian(fixture.AsSpan(56), 0x80D34CE9); // slingshot stock
            BinaryPrimitives.WriteUInt32LittleEndian(fixture.AsSpan(64), 0xD53CE3A7); // bottle stock
            BinaryPrimitives.WriteUInt32LittleEndian(fixture.AsSpan(72), 0xE753508B); // bomb bag capacity
            BinaryPrimitives.WriteUInt32LittleEndian(fixture.AsSpan(80), 0x5FADF7FC); // seed count
            BinaryPrimitives.WriteUInt32LittleEndian(fixture.AsSpan(84), 12);
            BinaryPrimitives.WriteUInt32LittleEndian(fixture.AsSpan(88), 0xAE8CC742); // seed capacity
            BinaryPrimitives.WriteUInt32LittleEndian(fixture.AsSpan(92), 30);
            BinaryPrimitives.WriteUInt32LittleEndian(fixture.AsSpan(96), 0x31AB5580); // current hearts
            BinaryPrimitives.WriteUInt32LittleEndian(fixture.AsSpan(100), 12);
            BinaryPrimitives.WriteUInt32LittleEndian(fixture.AsSpan(104), 0xFBE01DA1); // heart containers
            BinaryPrimitives.WriteUInt32LittleEndian(fixture.AsSpan(108), 12);
            BinaryPrimitives.WriteUInt32LittleEndian(fixture.AsSpan(112), 0xB7DF60F1); // fairy ocarina
            BinaryPrimitives.WriteUInt32LittleEndian(fixture.AsSpan(120), 0x54E2A3D1); // ocarina of time
            BinaryPrimitives.WriteUInt32LittleEndian(fixture.AsSpan(128), 0xD1317941); // bracelet
            BinaryPrimitives.WriteUInt32LittleEndian(fixture.AsSpan(136), 0x0012B8C0); // silver gauntlets
            BinaryPrimitives.WriteUInt32LittleEndian(fixture.AsSpan(144), 0x40AE1183); // gold gauntlets
            BinaryPrimitives.WriteUInt32LittleEndian(fixture.AsSpan(152), 0x24596E8E); // weird egg
            BinaryPrimitives.WriteUInt32LittleEndian(fixture.AsSpan(160), 0x07F10AAF); // pocket egg
            BinaryPrimitives.WriteUInt32LittleEndian(fixture.AsSpan(168), 0x12D63A97); // maximum magic
            BinaryPrimitives.WriteUInt32LittleEndian(fixture.AsSpan(176), 0x76B18200); // current magic
            BinaryPrimitives.WriteUInt32LittleEndian(fixture.AsSpan(184), 0x1240044B); // silver scale
            BinaryPrimitives.WriteUInt32LittleEndian(fixture.AsSpan(192), 0x4AEB140A); // golden scale
            BinaryPrimitives.WriteUInt32LittleEndian(fixture.AsSpan(200), 0xB5C10899); // Keaton mask
            BinaryPrimitives.WriteUInt32LittleEndian(fixture.AsSpan(212), 20); // UInt table
            BinaryPrimitives.WriteUInt32LittleEndian(fixture.AsSpan(216), 0x7D48DF76); // bottle content
            BinaryPrimitives.WriteUInt32LittleEndian(fixture.AsSpan(220), 0xBE69DE49); // empty
            BinaryPrimitives.WriteUInt32LittleEndian(fixture.AsSpan(232), 0x03B3450E); // Song of Time learned flag
            BinaryPrimitives.WriteUInt32LittleEndian(fixture.AsSpan(240), 0x46080375); // child age
            BinaryPrimitives.WriteUInt32LittleEndian(fixture.AsSpan(244), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(fixture.AsSpan(252), 16); // String64 table
            BinaryPrimitives.WriteUInt32LittleEndian(fixture.AsSpan(256), 0x1D6189DA); // current map
            BinaryPrimitives.WriteUInt32LittleEndian(fixture.AsSpan(260), 0x175D8);
            BinaryPrimitives.WriteUInt32LittleEndian(fixture.AsSpan(264), 0xB72CD3FC); // equipped ocarina
            BinaryPrimitives.WriteUInt32LittleEndian(fixture.AsSpan(268), 0x17618);
            System.Text.Encoding.UTF8.GetBytes("HyruleField_Child\0").CopyTo(fixture, 0x175D8);
            File.WriteAllBytes(editFile, fixture);
            using (var iconCache = new HAL_IconCacheService(null))
            {
                var editView = new HAL_AdventureSummaryView();
                var editStatus = new TextBlock();
                var editSave = new Button { Content = "Save changes" };
                var editSelector = new ContentControl();
                var editAge = new ComboBox { ItemsSource = new[] { "Child", "Adult" } };
                var editor = new Window { Content = new StackPanel { Children = { editView, editStatus, editSelector, editAge, editSave } } };
                using var session = new HAL_SaveEditorSession(Environment.CurrentDirectory, fakeGame, editUser,
                    new SaveSummary { Account = "fixture", Slot = 0 }, iconCache, ThemeCatalog.DefaultTheme,
                    editView, editStatus, editSave, editSelector, editAge);
                editSave.Click += async (_, _) => await session.Save();
                editor.Show(window);
                _ = session.Load();
                try { WaitFor(() => editor.GetVisualDescendants().OfType<Button>().Any(b => b.Tag?.ToString() == "slingshot")); }
                catch { Console.WriteLine(string.Join(" | ", editor.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text))); throw; }
                Check(editAge.IsEnabled && editAge.SelectedIndex == 0, "Edit mode shows the current age for a supported outdoor save");
                editAge.SelectedIndex = 1;
                WaitFor(() => editAge.IsEnabled && editAge.SelectedIndex == 1 && editSave.IsEnabled);
                Check(File.ReadAllBytes(editFile).SequenceEqual(fixture), "Changing age is staged until Save");
                editAge.SelectedIndex = 0;
                WaitFor(() => editAge.IsEnabled && editAge.SelectedIndex == 0 && !editSave.IsEnabled);
                editor.GetVisualDescendants().OfType<Button>().Single(b => b.Tag?.ToString() == "slingshot")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WaitFor(() => editor.GetVisualDescendants().OfType<Button>().Any(b => b.Content?.ToString() == "Save changes" && b.IsEnabled));
                Check(File.ReadAllBytes(editFile).SequenceEqual(fixture), "Clicking a save icon stages changes without writing the save");
                editor.GetVisualDescendants().OfType<Button>().Single(b => b.Tag?.ToString() == "slingshot")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WaitFor(() => editor.GetVisualDescendants().OfType<Button>().Any(b => b.Content?.ToString() == "Save changes" && !b.IsEnabled));
                Check(File.ReadAllBytes(editFile).SequenceEqual(fixture), "Clicking the owned icon again removes the staged item");
                editor.GetVisualDescendants().OfType<Button>().Single(b => b.Tag?.ToString() == "slingshot")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WaitFor(() => editor.GetVisualDescendants().OfType<Button>().Any(b => b.Content?.ToString() == "Save changes" && b.IsEnabled));
                Check(editView.EditQuantity?.Invoke("slingshot") == (12, 30), "Ammo slider uses the saved count and bag capacity");
                editView.ShowQuantitySlider("slingshot", 12, 30, value => editView.SetEditQuantity!("slingshot", value));
                Check(editor.GetVisualDescendants().OfType<Slider>().Any(s => s.Tag?.ToString() == "quantity:slingshot"),
                    "Consumables display an edit-mode quantity slider");
                Pump();
                var quantitySlider = editor.GetVisualDescendants().OfType<Slider>().Single(s => s.Tag?.ToString() == "quantity:slingshot");
                var sliderThumb = quantitySlider.GetVisualDescendants().OfType<Thumb>().First();
                Check(sliderThumb.Width == 12 && sliderThumb.Height == 12 &&
                    (sliderThumb.Background as ISolidColorBrush)?.Color == Color.Parse(ThemeCatalog.Resolve(ThemeCatalog.DefaultTheme).Rim),
                    "Quantity slider has a small theme-colored thumb");
                editView.UpdateVisual(new SaveSummary { Account = "fixture", Slot = 0 }, iconCache, "goron");
                editView.ShowQuantitySlider("slingshot", 12, 30, _ => { });
                Pump();
                quantitySlider = editor.GetVisualDescendants().OfType<Slider>().Single(s => s.Tag?.ToString() == "quantity:slingshot");
                sliderThumb = quantitySlider.GetVisualDescendants().OfType<Thumb>().First();
                Check((sliderThumb.Background as ISolidColorBrush)?.Color == Color.Parse(ThemeCatalog.Resolve("goron").Rim) &&
                    quantitySlider.GetVisualAncestors().OfType<Border>().Any(b => (b.Background as ISolidColorBrush)?.Color == Color.Parse(ThemeCatalog.Resolve("goron").Dark)),
                    "Editor slider and popup use the active theme");
                editView.UpdateVisual(new SaveSummary { Account = "fixture", Slot = 0 }, iconCache, ThemeCatalog.DefaultTheme);
                editView.SetEditQuantity!("slingshot", 17);
                WaitFor(() => editView.EditQuantity?.Invoke("slingshot") == (17, 30));
                Check(editView.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "17"), "Updated ammo count appears on the inventory icon");
                editor.GetVisualDescendants().OfType<Button>().Single(b => b.Tag?.ToString() == "heart:6")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WaitFor(() => editView.EditHearts?.Invoke()?.Maximum == 6);
                Check(File.ReadAllBytes(editFile).SequenceEqual(fixture), "Clicking a heart stages the new container count");
                foreach (var expected in new[] { 1, 2, 0, 1 }) {
                    editor.GetVisualDescendants().OfType<Button>().Single(b => b.Tag?.ToString() == "magic_level")
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    WaitFor(() => editView.EditMagicLevel?.Invoke() == expected);
                }
                Check(File.ReadAllBytes(editFile).SequenceEqual(fixture), "Magic cycles through none, normal and double without writing until Save");
                editor.GetVisualDescendants().OfType<Button>().Single(b => b.Tag?.ToString() == "progressive_ocarina")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                editor.GetVisualDescendants().OfType<Button>().Single(b => b.Tag?.ToString() == "choice:progressive_ocarina:2")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WaitFor(() => editView.EditOwnership?.Invoke("ocarina_time") == true);
                editor.GetVisualDescendants().OfType<Button>().Single(b => b.Tag?.ToString() == "progressive_strength")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                editor.GetVisualDescendants().OfType<Button>().Single(b => b.Tag?.ToString() == "choice:progressive_strength:2")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WaitFor(() => editView.EditOwnership?.Invoke("gauntlets_silver") == true);
                Check(true, "Ocarina and strength use horizontal progression icons");
                var scaleButton = editor.GetVisualDescendants().OfType<Button>().Single(b => b.Tag?.ToString() == "progressive_scale");
                Check(Canvas.GetLeft(scaleButton) == 797 && Canvas.GetTop(scaleButton) == 382,
                    "Scale edit slot sits under the Hylian Shield");
                scaleButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(editor.GetVisualDescendants().OfType<Button>().Count(b => b.Tag?.ToString()?.StartsWith("choice:progressive_scale:") == true) == 3,
                    "Scale menu offers none, silver and gold as icons");
                editor.GetVisualDescendants().OfType<Button>().Single(b => b.Tag?.ToString() == "choice:progressive_scale:2")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WaitFor(() => editView.EditOwnership?.Invoke("scale_gold") == true);
                Check(File.ReadAllBytes(editFile).SequenceEqual(fixture), "Scale choice is staged until saving");
                editor.GetVisualDescendants().OfType<Button>().Single(b => b.Tag?.ToString() == "song_time")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WaitFor(() => editView.EditOwnership?.Invoke("song_time") == true);
                Check(!editor.GetVisualDescendants().OfType<Button>().Any(b => b.Tag?.ToString() == "song_scarecrow") &&
                    File.ReadAllBytes(editFile).SequenceEqual(fixture), "Song toggles are staged and the recorded scarecrow melody stays unavailable");
                editor.GetVisualDescendants().OfType<Button>().Single(b => b.Tag?.ToString() == "trade_quest")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(editor.GetVisualDescendants().OfType<Button>().Count(b => b.Tag?.ToString()?.StartsWith("choice:trade_quest:") == true) == 2,
                    "Trading choices include the child and adult items present in the save");
                editor.GetVisualDescendants().OfType<Button>().Single(b => b.Tag?.ToString() == "choice:trade_quest:0")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WaitFor(() => editView.EditOwnership?.Invoke("trade_weird_egg") == true);
                editor.GetVisualDescendants().OfType<Button>().Single(b => b.Tag?.ToString() == "mask_keaton")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WaitFor(() => editView.EditOwnership?.Invoke("mask_keaton") == true);
                Check(editor.GetVisualDescendants().OfType<Button>().Single(b => b.Tag?.ToString() == "trade_quest")
                    is Button tradeButton && ToolTip.GetTip(tradeButton)?.ToString()?.Contains("trade_weird_egg") == true,
                    "Enabling a mask leaves the child trade item displayed");
                editor.GetVisualDescendants().OfType<Button>().Single(b => b.Tag?.ToString() == "mask_keaton")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WaitFor(() => editView.EditOwnership?.Invoke("mask_keaton") == false);
                editor.GetVisualDescendants().OfType<Button>().Single(b => b.Tag?.ToString() == "trade_quest")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                editor.GetVisualDescendants().OfType<Button>().Single(b => b.Tag?.ToString() == "choice:trade_quest:1")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WaitFor(() => editView.EditOwnership?.Invoke("trade_pocket_egg") == true);
                editor.GetVisualDescendants().OfType<Button>().Single(b => b.Tag?.ToString() == "trade_quest")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                editor.GetVisualDescendants().OfType<Button>().Single(b => b.Tag?.ToString() == "choice:trade_quest:0")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WaitFor(() => editView.EditOwnership?.Invoke("trade_weird_egg") == false);
                Check(editView.EditOwnership?.Invoke("trade_pocket_egg") == true && File.ReadAllBytes(editFile).SequenceEqual(fixture),
                    "Trading icons toggle independently and remain staged until saving");
                editor.GetVisualDescendants().OfType<Button>().Single(b => b.Tag?.ToString() == "bottle_slot_1")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var choices = (Dictionary<string, ComboBox>)typeof(HAL_SaveEditorSession).GetField("_choices", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(session)!;
                Check(editor.GetVisualDescendants().OfType<Button>().Any(b => b.Tag?.ToString() == "choice:bottle_slot_1:2"),
                    "Clicking a bottle opens horizontal content icons");
                editor.GetVisualDescendants().OfType<Button>().Single(b => b.Tag?.ToString() == "choice:bottle_slot_1:2")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var staged = (Dictionary<string, int>)typeof(HAL_SaveEditorSession).GetField("_stagedChoices", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(session)!;
                WaitFor(() => staged.TryGetValue("bottle:0", out var value) && value == 2);
                editor.GetVisualDescendants().OfType<Button>().Single(b => b.Tag?.ToString() == "bag_bombs")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(editor.GetVisualDescendants().OfType<Button>().Any(b => b.Tag?.ToString() == "choice:bag_bombs:1"),
                    "Clicking a bag opens horizontal capacity icons");
                editor.GetVisualDescendants().OfType<Button>().Single(b => b.Tag?.ToString() == "choice:bag_bombs:1")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WaitFor(() => staged.TryGetValue("bag:bombs", out var value) && value == 20);
                Check(editView.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "20" &&
                    (t.Foreground as ISolidColorBrush)?.Color == Color.Parse("#72d48a")), "Bag capacity is shown in green");
                editor.GetVisualDescendants().OfType<Button>().Single(b => b.Tag?.ToString() == "bag_bombs")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                editor.GetVisualDescendants().OfType<Button>().Single(b => b.Tag?.ToString() == "choice:bag_bombs:0")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WaitFor(() => staged.TryGetValue("bag:bombs", out var value) && value == 0);
                Check(editView.EditBag?.Invoke("bombs") == 0, "A bag can be removed through the icon menu");
                editor.GetVisualDescendants().OfType<Button>().Single(b => b.Tag?.ToString() == "bag_bombs")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                editor.GetVisualDescendants().OfType<Button>().Single(b => b.Tag?.ToString() == "choice:bag_bombs:1")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WaitFor(() => staged.TryGetValue("bag:bombs", out var value) && value == 20);
                editAge.SelectedIndex = 1;
                WaitFor(() => editAge.IsEnabled && editAge.SelectedIndex == 1);
                editor.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString() == "Save changes")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WaitFor(() => !File.ReadAllBytes(editFile).SequenceEqual(fixture));
                var savedFixture = File.ReadAllBytes(editFile);
                Check(BinaryPrimitives.ReadUInt32LittleEndian(savedFixture.AsSpan(196)) == 1 &&
                    BinaryPrimitives.ReadUInt32LittleEndian(savedFixture.AsSpan(188)) == 0,
                    "Saving the golden scale persists the selected tier");
                Check(BinaryPrimitives.ReadUInt32LittleEndian(savedFixture.AsSpan(244)) == 0 &&
                    System.Text.Encoding.UTF8.GetString(savedFixture.AsSpan(0x175D8, 17)) == "HyruleField_Adult",
                    "Saving age updates both Link and the outdoor map");
                Check(BinaryPrimitives.ReadInt32LittleEndian(savedFixture.AsSpan(60)) == 1 &&
                    BinaryPrimitives.ReadInt32LittleEndian(savedFixture.AsSpan(68)) == 1 &&
                    BinaryPrimitives.ReadInt32LittleEndian(savedFixture.AsSpan(76)) == 20 &&
                    BinaryPrimitives.ReadInt32LittleEndian(savedFixture.AsSpan(84)) == 17 &&
                    BinaryPrimitives.ReadInt32LittleEndian(savedFixture.AsSpan(108)) == 24 &&
                    BinaryPrimitives.ReadInt32LittleEndian(savedFixture.AsSpan(124)) == 1 &&
                    BinaryPrimitives.ReadInt32LittleEndian(savedFixture.AsSpan(140)) == 1 &&
                    BinaryPrimitives.ReadInt32LittleEndian(savedFixture.AsSpan(156)) == 0 &&
                    BinaryPrimitives.ReadInt32LittleEndian(savedFixture.AsSpan(164)) == 1 &&
                    BinaryPrimitives.ReadInt32LittleEndian(savedFixture.AsSpan(172)) == 240 &&
                    BinaryPrimitives.ReadUInt32LittleEndian(savedFixture.AsSpan(236)) == 1 &&
                    BinaryPrimitives.ReadUInt32LittleEndian(savedFixture.AsSpan(220)) == 0xEABA6067 &&
                    Directory.GetFiles(Path.Combine(fakeGame, "save-backups"), "progress.sav", SearchOption.AllDirectories).Length == 1,
                    "Saving an icon change writes the selected synthetic save and creates a backup");
                editor.GetVisualDescendants().OfType<Button>().Single(b => b.Tag?.ToString() == "slingshot")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WaitFor(() => editView.EditOwnership?.Invoke("slingshot") == false);
                editor.GetVisualDescendants().OfType<Button>().Single(b => b.Tag?.ToString() == "bag_seeds")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                editor.GetVisualDescendants().OfType<Button>().Single(b => b.Tag?.ToString() == "choice:bag_seeds:0")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WaitFor(() => editView.EditBag?.Invoke("seeds") == 0);
                var slingButton = editor.GetVisualDescendants().OfType<Button>().Single(b => b.Tag?.ToString() == "slingshot");
                Check(ToolTip.GetTip(slingButton)?.ToString()?.Contains("basic seed bag", StringComparison.Ordinal) == true,
                    "Slingshot edit explains the automatic seed bag");
                slingButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WaitFor(() => editView.EditOwnership?.Invoke("slingshot") == true && editView.EditBag?.Invoke("seeds") == 30);
                Check(editView.EditQuantity?.Invoke("slingshot")?.Maximum == 30 && File.ReadAllBytes(editFile).SequenceEqual(savedFixture),
                    "Adding a slingshot stages a basic seed bag and updates its ammo capacity");
                Check(editStatus.Text?.Contains("Basic seed bag added", StringComparison.Ordinal) == true,
                    "Editor confirms that it added the seed bag");
                editor.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString() == "Save changes")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WaitFor(() => !File.ReadAllBytes(editFile).SequenceEqual(savedFixture));
                var slingSaved = File.ReadAllBytes(editFile);
                Check(BinaryPrimitives.ReadInt32LittleEndian(slingSaved.AsSpan(60)) == 1 &&
                    BinaryPrimitives.ReadInt32LittleEndian(slingSaved.AsSpan(92)) == 30,
                    "Saving the slingshot also saves its basic seed bag");
                editor.GetVisualDescendants().OfType<Button>().Single(b => b.Tag?.ToString() == "bag_seeds")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                editor.GetVisualDescendants().OfType<Button>().Single(b => b.Tag?.ToString() == "choice:bag_seeds:3")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WaitFor(() => editView.EditBag?.Invoke("seeds") == 50);
                editor.GetVisualDescendants().OfType<Button>().Single(b => b.Tag?.ToString() == "slingshot")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WaitFor(() => editView.EditOwnership?.Invoke("slingshot") == false);
                editor.GetVisualDescendants().OfType<Button>().Single(b => b.Tag?.ToString() == "slingshot")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WaitFor(() => editView.EditOwnership?.Invoke("slingshot") == true);
                Check(editView.EditBag?.Invoke("seeds") == 50, "Re-adding a slingshot preserves an upgraded seed bag");
                editor.Close(); Pump();
            }
            var bowSlot = Path.Combine(editUser, "save", "fixture", "slot_02");
            Directory.CreateDirectory(bowSlot);
            var bowFixture = (byte[])fixture.Clone();
            BinaryPrimitives.WriteUInt32LittleEndian(bowFixture.AsSpan(152), 0x938D56BF); // bow stock
            BinaryPrimitives.WriteUInt32LittleEndian(bowFixture.AsSpan(160), 0x418DB791); // quiver capacity
            BinaryPrimitives.WriteUInt32LittleEndian(bowFixture.AsSpan(168), 0x518EC74B); // arrow count
            File.WriteAllBytes(Path.Combine(bowSlot, "progress.sav"), bowFixture);
            using (var icons = new HAL_IconCacheService(null))
            {
                var status = new TextBlock(); var save = new Button(); var view = new HAL_AdventureSummaryView();
                var bowWindow = new Window { Content = new StackPanel { Children = { view, status, save } } };
                using var bowEditor = new HAL_SaveEditorSession(Environment.CurrentDirectory, fakeGame, editUser,
                    new SaveSummary { Account = "fixture", Slot = 2 }, icons, ThemeCatalog.DefaultTheme,
                    view, status, save, new ContentControl());
                save.Click += async (_, _) => await bowEditor.Save();
                bowWindow.Show(window);
                _ = bowEditor.Load();
                WaitFor(() => bowWindow.GetVisualDescendants().OfType<Button>().Any(b => b.Tag?.ToString() == "bow"));
                var bowButton = bowWindow.GetVisualDescendants().OfType<Button>().Single(b => b.Tag?.ToString() == "bow");
                Check(ToolTip.GetTip(bowButton)?.ToString()?.Contains("basic quiver", StringComparison.Ordinal) == true,
                    "Bow edit explains the automatic quiver");
                bowButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WaitFor(() => view.EditOwnership?.Invoke("bow") == true && view.EditBag?.Invoke("arrows") == 30);
                Check(view.EditQuantity?.Invoke("bow")?.Maximum == 30 && File.ReadAllBytes(Path.Combine(bowSlot, "progress.sav")).SequenceEqual(bowFixture),
                    "Adding a bow stages a basic quiver and updates its ammo capacity");
                Check(status.Text?.Contains("Basic quiver added", StringComparison.Ordinal) == true,
                    "Editor confirms that it added the quiver");
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WaitFor(() => !File.ReadAllBytes(Path.Combine(bowSlot, "progress.sav")).SequenceEqual(bowFixture));
                var bowSaved = File.ReadAllBytes(Path.Combine(bowSlot, "progress.sav"));
                Check(BinaryPrimitives.ReadInt32LittleEndian(bowSaved.AsSpan(156)) == 1 &&
                    BinaryPrimitives.ReadInt32LittleEndian(bowSaved.AsSpan(164)) == 30,
                    "Saving the bow also saves its basic quiver");
                bowWindow.GetVisualDescendants().OfType<Button>().Single(b => b.Tag?.ToString() == "bag_quiver")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                bowWindow.GetVisualDescendants().OfType<Button>().Single(b => b.Tag?.ToString() == "choice:bag_quiver:3")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WaitFor(() => view.EditBag?.Invoke("arrows") == 50);
                bowWindow.GetVisualDescendants().OfType<Button>().Single(b => b.Tag?.ToString() == "bow")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WaitFor(() => view.EditOwnership?.Invoke("bow") == false);
                bowWindow.GetVisualDescendants().OfType<Button>().Single(b => b.Tag?.ToString() == "bow")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WaitFor(() => view.EditOwnership?.Invoke("bow") == true);
                Check(view.EditBag?.Invoke("arrows") == 50, "Re-adding a bow preserves an upgraded quiver");
                bowWindow.Close(); Pump();
            }
            File.Delete(Path.Combine(bowSlot, "progress.sav"));
            Directory.Delete(bowSlot);
            var incompleteSlot = Path.Combine(editUser, "save", "fixture", "slot_03");
            Directory.CreateDirectory(incompleteSlot);
            var incompleteFixture = (byte[])bowFixture.Clone();
            BinaryPrimitives.WriteUInt32LittleEndian(incompleteFixture.AsSpan(160), 0x12345678); // no quiver capacity
            File.WriteAllBytes(Path.Combine(incompleteSlot, "progress.sav"), incompleteFixture);
            using (var icons = new HAL_IconCacheService(null))
            {
                var status = new TextBlock(); var save = new Button(); var view = new HAL_AdventureSummaryView();
                var incompleteWindow = new Window { Content = new StackPanel { Children = { view, status, save } } };
                using var incompleteEditor = new HAL_SaveEditorSession(Environment.CurrentDirectory, fakeGame, editUser,
                    new SaveSummary { Account = "fixture", Slot = 3 }, icons, ThemeCatalog.DefaultTheme,
                    view, status, save, new ContentControl());
                incompleteWindow.Show(window);
                _ = incompleteEditor.Load();
                WaitFor(() => incompleteWindow.GetVisualDescendants().OfType<Button>().Any(b => b.Tag?.ToString() == "bow"));
                incompleteWindow.GetVisualDescendants().OfType<Button>().Single(b => b.Tag?.ToString() == "bow")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WaitFor(() => status.Text?.Contains("does not have an editable ammo bag", StringComparison.Ordinal) == true);
                Check(view.EditOwnership?.Invoke("bow") == false && !save.IsEnabled &&
                    File.ReadAllBytes(Path.Combine(incompleteSlot, "progress.sav")).SequenceEqual(incompleteFixture),
                    "A save without quiver fields cannot stage a bow without its bag");
                incompleteWindow.Close(); Pump();
            }
            File.Delete(Path.Combine(incompleteSlot, "progress.sav"));
            Directory.Delete(incompleteSlot);
            var dungeonSlot = Path.Combine(editUser, "save", "fixture", "slot_01");
            Directory.CreateDirectory(dungeonSlot);
            var dungeonFixture = (byte[])fixture.Clone();
            dungeonFixture.AsSpan(0x175D8, 64).Clear();
            System.Text.Encoding.UTF8.GetBytes("InsideTheDekuTree\0").CopyTo(dungeonFixture, 0x175D8);
            File.WriteAllBytes(Path.Combine(dungeonSlot, "progress.sav"), dungeonFixture);
            using (var icons = new HAL_IconCacheService(null))
            {
                var age = new ComboBox { ItemsSource = new[] { "Child", "Adult" } };
                var status = new TextBlock(); var save = new Button(); var view = new HAL_AdventureSummaryView();
                using var dungeonEditor = new HAL_SaveEditorSession(Environment.CurrentDirectory, fakeGame, editUser,
                    new SaveSummary { Account = "fixture", Slot = 1 }, icons, ThemeCatalog.DefaultTheme,
                    view, status, save, new ContentControl(), age);
                _ = dungeonEditor.Load();
                WaitFor(() => status.Text?.StartsWith("Outlined icons", StringComparison.Ordinal) == true);
                Check(age.SelectedIndex == 0 && !age.IsEnabled, "Age selection is disabled in areas without both age maps");
            }
            File.Delete(Path.Combine(dungeonSlot, "progress.sav"));
            Directory.Delete(dungeonSlot);
            preferences.Value.GameDirectory = fakeGame;
            var editProfile = new ProfileRecord { Id = Guid.NewGuid().ToString("N"), Name = "Synthetic", UserDir = editUser, External = true, Kind = "vanilla" };
            typeof(MainWindow).GetMethod("OpenProfile", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(window, new object[] { editProfile });
            Pump();
            Check(window.FindControl<Button>("DeleteSelectedSaveButton") is { IsVisible: true, IsEnabled: true, Content: "Delete…" },
                "External saves show a delete action for the selected slot");
            window.FindControl<TextBlock>("DetailsSeed")!.Text = "Seed: 0123456789ABCDEF";
            var sameSummary = window.FindControl<HAL_AdventureSummaryView>("SummaryView")!;
            window.Width = 1000; window.Height = 640; Pump(); window.UpdateLayout();
            double normalInventoryHeight = sameSummary.Bounds.Height;
            double normalHeaderHeight = window.FindControl<Grid>("HAL_DetailsHeader")!.Bounds.Height;
            Save(window, "details-normal-1000x640.png");
            window.Width = 1280; window.Height = 800; Pump(); window.UpdateLayout();
            double deckInventoryHeight = sameSummary.Bounds.Height;
            Save(window, "details-normal-deck.png");
            var windowsBeforeEdit = window.OwnedWindows.Count;
            Click(window, "EditSaveButton");
            WaitFor(() => sameSummary.GetVisualDescendants().OfType<Button>().Any(b => b.Tag?.ToString() == "slingshot"));
            Pump(); window.UpdateLayout();
            var editHeaderHeight = window.FindControl<Grid>("HAL_DetailsHeader")!.Bounds.Height;
            Console.WriteLine($"Details layout: deck inventory {deckInventoryHeight:F1}->{sameSummary.Bounds.Height:F1}; header {normalHeaderHeight:F1}->{editHeaderHeight:F1}");
            Check(sameSummary.Bounds.Height >= deckInventoryHeight - 1 &&
                window.FindControl<Grid>("HAL_DetailsHeader")!.Bounds.Height <= normalHeaderHeight + 2,
                "Edit controls preserve the inventory area at Steam Deck resolution");
            Save(window, "details-edit-deck.png");
            window.Width = 1000; window.Height = 640; Pump(); window.UpdateLayout();
            Check(sameSummary.Bounds.Height >= normalInventoryHeight - 1 &&
                window.FindControl<Grid>("HAL_DetailsHeader")!.Bounds.Height <= normalHeaderHeight + 2,
                "Edit controls preserve the inventory area at 1000 by 640");
            Save(window, "details-edit-1000x640.png");
            var layoutStatus = window.FindControl<TextBlock>("HAL_EditStatus")!;
            var statusText = layoutStatus.Text;
            layoutStatus.Text = new string('X', 400); Pump(); window.UpdateLayout();
            Check(sameSummary.Bounds.Height >= normalInventoryHeight - 1,
                "A long editor status does not shrink the inventory");
            layoutStatus.Text = statusText;
            Check(window.FindControl<Control>("DetailsPage")!.IsVisible && window.OwnedWindows.Count == windowsBeforeEdit &&
                  ReferenceEquals(sameSummary, window.FindControl<HAL_AdventureSummaryView>("SummaryView")),
                "Edit enables the existing adventure inventory without opening a window");
            var cancelEdit = window.FindControl<Button>("HAL_CancelEditsButton")!;
            var saveEdit = window.FindControl<Button>("HAL_SaveEditsButton")!;
            Check(!window.FindControl<Button>("EditSaveButton")!.IsVisible && cancelEdit.IsVisible && saveEdit.IsVisible &&
                  cancelEdit.Parent is StackPanel controls && controls.Children.IndexOf(cancelEdit) < controls.Children.IndexOf(saveEdit),
                "Cancel replaces Edit with Save beside it");
            var beforeCancel = File.ReadAllBytes(editFile);
            sameSummary.GetVisualDescendants().OfType<Button>().Single(b => b.Tag?.ToString() == "slingshot")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitFor(() => window.FindControl<Button>("HAL_SaveEditsButton")!.IsEnabled);
            Click(window, "HAL_CancelEditsButton");
            Check(File.ReadAllBytes(editFile).SequenceEqual(beforeCancel), "Cancelling in-place edits leaves the save unchanged");
            Click(window, "DeleteSelectedSaveButton"); Pump();
            var deleteChoice = window.OwnedWindows.Single(w => w.Title == "Delete");
            Check(deleteChoice.GetVisualDescendants().OfType<Border>().Any(b => b.Classes.Contains("panel")) &&
                deleteChoice.GetVisualDescendants().OfType<Image>().Any(i => i.Name == "HAL_DeletePreview") &&
                deleteChoice.GetVisualDescendants().OfType<Button>().Where(b => b.Content?.ToString() is "Cancel" or "Delete adventure").All(b => b.Classes.Contains("raised")),
                "Delete dialog uses themed launcher controls and includes the selected save preview");
            Check(deleteChoice.GetVisualDescendants().OfType<Button>().Any(b => b.Content?.ToString()?.StartsWith("Delete Save 1") == true) &&
                  deleteChoice.GetVisualDescendants().OfType<Button>().Any(b => b.Content?.ToString() == "Delete adventure"),
                "Delete offers the selected save slot and the adventure");
            deleteChoice.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString()?.StartsWith("Delete Save 1") == true)
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            var deleteDialog = window.OwnedWindows.Single(w => w.Title == "Delete save slot");
            Check(deleteDialog.GetVisualDescendants().OfType<Image>().Any(i => i.Name == "HAL_ConfirmSavePreview") &&
                  deleteDialog.GetVisualDescendants().OfType<Border>().Any(b => b.Classes.Contains("panel")) &&
                  deleteDialog.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("Save 1") == true),
                "Slot confirmation uses the theme and identifies the selected save");
            deleteDialog.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString() == "Cancel")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump();
            Check(File.Exists(editFile), "Cancelling slot deletion preserves the save");
            Click(window, "DeleteSelectedSaveButton"); Pump();
            deleteChoice = window.OwnedWindows.Single(w => w.Title == "Delete");
            deleteChoice.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString() == "Delete adventure")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            var externalConfirm = window.OwnedWindows.Single(w => w.Title == "Remove external adventure");
            Check(externalConfirm.GetVisualDescendants().OfType<Image>().Any(i => i.Name == "HAL_ConfirmSavePreview") &&
                  externalConfirm.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("remain on disk") == true) &&
                  externalConfirm.GetVisualDescendants().OfType<Button>().Where(b => b.Content?.ToString() is "Cancel" or "Remove adventure").All(b => b.Classes.Contains("raised")),
                "External adventure confirmation shows save context, themed actions and explains file handling");
            externalConfirm.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString() == "Cancel")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Click(window, "DeleteSelectedSaveButton"); Pump();
            deleteChoice = window.OwnedWindows.Single(w => w.Title == "Delete");
            deleteChoice.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString()?.StartsWith("Delete Save 1") == true)
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            deleteDialog = window.OwnedWindows.Single(w => w.Title == "Delete save slot");
            deleteDialog.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString() == "Delete slot")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitFor(() => !Directory.Exists(editSlot));
            Check(Directory.Exists(editUser) && Directory.Exists(Path.GetDirectoryName(editSlot)!), "Confirmed deletion removes only the selected external slot");
            Click(window, "HAL_DetailsBackButton");
            preferences.Value.GameDirectory = previousGame;
            if (OperatingSystem.IsLinux() && HAL_LinuxBuildTools.HAL_FindPreparedDirectory(root) is null)
            {
                var readiness = HAL_LinuxBuildToolsDialog.EnsureReadyAsync(window, root);
                Pump();
                var setup = window.OwnedWindows.OfType<HAL_LinuxBuildToolsDialog>().Single();
                Check(setup.IsVisible && !readiness.IsCompleted, "missing build tools prompt before a game build starts");
                Check(setup.GetLogicalDescendants().OfType<Expander>().Any(e => e.IsExpanded),
                    "Linux tool setup opens its log so compiler download progress is visible");
                setup.Close(); Pump();
                Check(!readiness.GetAwaiter().GetResult(), "cancelling dependency setup prevents the game build");
            }
            var settingsSections = window.FindControl<TabControl>("SettingsSections")!;
            var settingsHeaders = settingsSections.Items.OfType<TabItem>().Select(x => x.Header?.ToString()).ToArray();
            Check(settingsHeaders.SequenceEqual(new[] { "Appearance", "Debug", "Reinstall", "Tools", "Tweaks", "Warnings & Errors" }) &&
                  window.FindControl<TabControl>("SettingsToolsSections") is not null &&
                  window.FindControl<TabControl>("TweaksSections") is not null &&
                  window.FindControl<RadioButton>("LaunchDevRadio") is not null &&
                  window.FindControl<RadioButton>("LaunchStableRadio") is not null &&
                  window.FindControl<CheckBox>("VideoDebugCheck") is not null &&
                  window.FindControl<CheckBox>("HosDebugCheck") is not null &&
                  window.FindControl<CheckBox>("A64DebugCheck") is not null &&
                  window.FindControl<CheckBox>("DebugKeyCheck") is not null,
                "Settings follows Qt sections with nested Tools and Tweaks subtabs");
            Click(window, "SettingsButton");
            Pump();
            Check(window.FindControl<TextBlock>("WarningTitle") is not null &&
                  window.FindControl<TextBox>("DevSourceBox") is not null &&
                  window.FindControl<Button>("RestoreCompressedBackupButton") is not null &&
                  window.FindControl<Button>("BuildDevButton") is not null &&
                  window.FindControl<TextBox>("DevShaderWorkersBox") is not null &&
                  window.FindControl<CheckBox>("DevShaderWorkersMaxCheck") is not null &&
                  window.FindControl<Button>("BrowseDevSourceFileButton") is not null &&
                  window.FindControl<Button>("BrowseDevSourceFolderButton") is not null &&
                  window.FindControl<Button>("BrowseDevOutputButton") is not null,
                "Settings exposes diagnostics and browsable local dev build paths");
            settingsSections.SelectedIndex = 3;
            window.FindControl<TabControl>("TweaksSections")!.SelectedIndex = 0;
            Click(window, "OpenDevBuildSetupButton");
            Check(settingsSections.SelectedIndex == 3 &&
                  window.FindControl<TabControl>("SettingsToolsSections")!.SelectedItem == window.FindControl<TabItem>("HAL_DevBuildTab"),
                "Open dev build setup navigates from Tweaks to Tools → Dev Build Setup");
            var buildSource = Path.Combine(root, "authored-source");
            Directory.CreateDirectory(Path.Combine(buildSource, "romfs"));
            Directory.CreateDirectory(Path.Combine(buildSource, "exefs"));
            var customOutput = Path.Combine(root, "Remakes", "hookshot-dev");
            window.FindControl<TextBox>("DevSourceBox")!.Text = buildSource;
            window.FindControl<TextBox>("DevOutputBox")!.Text = customOutput + Path.DirectorySeparatorChar;
            Click(window, "ReviewDevBuildButton");
            Check(window.FindControl<TextBlock>("DevBuildStatus")!.Text!.StartsWith("Ready to build"),
                "review accepts a custom development install directory before installation");
            window.FindControl<TextBox>("DevOutputBox")!.Text = HookshotGame.StableGameDirectory();
            Click(window, "ReviewDevBuildButton");
            Check(window.FindControl<TextBlock>("DevBuildStatus")!.Text!.Contains("protected"),
                "review protects the stable installation");
            window.FindControl<TextBox>("DevOutputBox")!.Text = customOutput;
            Click(window, "ReviewDevBuildButton");
            Pump(); window.UpdateLayout();
            var setupScroll = window.FindControl<ScrollViewer>("SettingsPage")!;
            Check(setupScroll.Extent.Height <= setupScroll.Viewport.Height + 1,
                "development setup fits the default 1000 by 640 window without scrolling");
            Save(window, "dev-build-setup.png");
            var installPreferences = new PreferenceStore(Repository.Root);
            var threeCardInstaller = new HAL_DevelopmentBuildDialog(Environment.CurrentDirectory, installPreferences,
                buildSource, customOutput, false, true, true, 4);
            threeCardInstaller.Show(window); Pump();
            var installScreen = threeCardInstaller.Screen;
            var introPage = installScreen.FindControl<Border>("InstallIntroCard")!;
            var setupPage = installScreen.FindControl<Border>("InstallConfigurationCard")!;
            var progressPage = installScreen.FindControl<Border>("InstallStatusCard")!;
            Check(installScreen.FindControl<Button>("InstallDownloadHookshotButton") is not null &&
                  installScreen.FindControl<StackPanel>("InstallDownloadOffer")!.IsVisible ==
                    (HAL_HookshotDownload.ShouldOffer(HookshotGame.StableGameDirectory(), installPreferences.Value.DevGameDirectory,
                        installPreferences.Value.GameDirectory) && (OperatingSystem.IsLinux() || OperatingSystem.IsWindows())),
                "first-run setup offers the official builder only when no installation is found");
            Check(introPage.IsVisible && !setupPage.IsVisible && !progressPage.IsVisible &&
                  !installScreen.FindControl<Button>("InstallStartButton")!.IsVisible,
                "installer opens only its introduction screen and cannot start a build there");
            Save(threeCardInstaller, "installer-01-introduction.png");
            installScreen.FindControl<Button>("InstallNextButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(!introPage.IsVisible && setupPage.IsVisible && !progressPage.IsVisible,
                "Next opens only the locations and build setup screen");
            installScreen.FindControl<Button>("InstallReviewButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(installScreen.FindControl<HAL_BuildProgressView>("InstallProgress")!.ResultText.Contains("Setup is ready"),
                "wizard reviews a fresh source and custom output without building");
            var installScroll = installScreen.FindControl<ScrollViewer>("InstallCardsScroll")!;
            Pump(); threeCardInstaller.UpdateLayout();
            Save(threeCardInstaller, "installer-02-locations.png");
            Check(installScroll.Extent.Height <= installScroll.Viewport.Height + 1,
                $"locations screen fits at 980 by 660 without scrolling (extent {installScroll.Extent.Height}, viewport {installScroll.Viewport.Height})");
            installScreen.FindControl<TextBox>("InstallJobsBox")!.Text = "invalid";
            installScreen.FindControl<Button>("InstallReviewButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(installScreen.FindControl<HAL_BuildProgressView>("InstallProgress")!.ResultText.Contains("Build jobs must"),
                "installer validates compiler worker settings before setup or building");
            Check(!Directory.Exists(customOutput), "reviewing installer configuration creates no game output");
            installScreen.FindControl<Button>("InstallStartButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(setupPage.IsVisible && !progressPage.IsVisible,
                "invalid configuration stays on the setup screen when installation is requested");
            installScreen.FindControl<TextBox>("InstallJobsBox")!.Text = "";
            installScreen.FindControl<Button>("InstallBackButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(introPage.IsVisible && !setupPage.IsVisible && installScreen.OutputDirectory == customOutput,
                "Back returns to introduction and retains installation locations");
            installScreen.FindControl<Button>("InstallNextButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (OperatingSystem.IsLinux() && HAL_LinuxBuildTools.HAL_FindPreparedDirectory(Environment.CurrentDirectory) is null)
            {
                installScreen.FindControl<Button>("InstallStartButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Pump();
                Check(!introPage.IsVisible && !setupPage.IsVisible && progressPage.IsVisible &&
                      !installScreen.FindControl<Button>("InstallBackButton")!.IsEnabled &&
                      progressPage.GetLogicalDescendants().OfType<Expander>().Any(e => e.IsExpanded),
                    "installation opens only the progress screen with live log and blocks navigation while running");
                Save(threeCardInstaller, "installer-03-progress.png");
                threeCardInstaller.OwnedWindows.OfType<HAL_LinuxBuildToolsDialog>().Single().Close(); Pump();
                Check(!Directory.Exists(customOutput), "cancelling tool preparation starts no game build");
                installScreen.FindControl<Button>("InstallBackButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(setupPage.IsVisible && !progressPage.IsVisible, "cancelled installation can return to locations");
            }
            threeCardInstaller.Close(); Pump();
            var missingToolsInstaller = new HAL_DevelopmentBuildDialog(Path.Combine(root, "missing-tools"), installPreferences,
                buildSource, customOutput, false, true, true, 4);
            missingToolsInstaller.Show(window); Pump();
            var missingToolsScreen = missingToolsInstaller.Screen;
            Check(missingToolsScreen.FindControl<TextBlock>("InstallMissingToolsNotice")!.IsVisible &&
                  missingToolsScreen.FindControl<Button>("InstallDownloadHookshotButton") is not null,
                "standalone setup explains missing build tools while retaining the download option");
            missingToolsScreen.FindControl<Button>("InstallNextButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            missingToolsScreen.FindControl<Button>("InstallReviewButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(missingToolsScreen.FindControl<HAL_BuildProgressView>("InstallProgress")!.ResultText.Contains("build tools are missing"),
                "setup blocks a development build when its build tools are missing");
            missingToolsInstaller.Close(); Pump();
            var existingGame = Path.Combine(root, "existing-hookshot");
            Directory.CreateDirectory(Path.Combine(existingGame, "romfs"));
            Directory.CreateDirectory(Path.Combine(existingGame, ".hookshot_exefs"));
            File.WriteAllText(Path.Combine(existingGame, ".hookshot_exefs", "main"), "authored game fixture");
            File.WriteAllText(HookshotGame.ResolveExecutable(existingGame), "authored executable");
            File.WriteAllText(HookshotGame.ResolveRenderer(existingGame), "authored renderer");
            File.WriteAllText(Path.Combine(existingGame, "image.bin"), "authored image");
            installPreferences.SetGameDirectory(existingGame);
            var existingInstaller = new HAL_DevelopmentBuildDialog(Environment.CurrentDirectory, installPreferences,
                "", customOutput, false, true, true, 4);
            existingInstaller.Show(window); Pump();
            var existingScreen = existingInstaller.Screen;
            Check(!existingScreen.FindControl<StackPanel>("InstallDownloadOffer")!.IsVisible,
                "an existing Hookshot installation hides the builder download offer");
            existingScreen.FindControl<Button>("InstallNextButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(existingScreen.SourcePath == existingGame && existingScreen.FindControl<ComboBox>("InstallSourceFormat")!.SelectedIndex == 2 &&
                existingScreen.FindControl<TextBlock>("InstallSourceStatus")!.Text!.Contains("separate copy"),
                "installer detects a selected existing Hookshot installation and explains the separate copy");
            existingScreen.FindControl<Button>("InstallReviewButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(existingScreen.FindControl<HAL_BuildProgressView>("InstallProgress")!.ResultText.Contains("Setup is ready"),
                "detected installation can be reviewed without rebuilding or changing it");
            existingInstaller.Close(); Pump();
            if (OperatingSystem.IsLinux())
            {
                var builtin = HAL_StockInstallDialog.HAL_CreateBuiltinStartInfo(Environment.CurrentDirectory);
                Check(builtin.ArgumentList.Contains(Path.Combine(Environment.CurrentDirectory, "builder", "linux", "hookshot_builder.py")) &&
                      !builtin.Environment.ContainsKey("HOOKSHOT_DEV_RANDOMIZER"),
                    "stock installer opens the built-in Linux Builder without randomizer build mode");
            }
            var stockInstaller = new HAL_StockInstallDialog(Environment.CurrentDirectory, true, true);
            stockInstaller.Show(window); Pump();
            Check(stockInstaller.Screen.FindControl<CheckBox>("InstallReinstallCheck")!.IsChecked == true,
                "stock reinstall uses the same wizard with reinstall selected");
            Save(stockInstaller, "stock-installer-introduction.png");
            stockInstaller.Close(); Pump();
            var maxWorkers = window.FindControl<CheckBox>("DevShaderWorkersMaxCheck")!;
            maxWorkers.IsChecked = true;
            Check(maxWorkers.IsChecked == true && window.FindControl<TextBox>("DevShaderWorkersBox")!.Text ==
                  Math.Clamp(Environment.ProcessorCount - 2, 1, 32).ToString(System.Globalization.CultureInfo.InvariantCulture),
                "Dev build Max worker override selects available CPUs minus two");
            maxWorkers.IsChecked = false;
            Check(string.IsNullOrWhiteSpace(window.FindControl<TextBox>("DevShaderWorkersBox")!.Text),
                "Turning off Max restores automatic shader worker count");
            Click(window, "SettingsBackButton");
            Pump();
            Check(window.Icon is not null, "Hookshot ICO is applied to the launcher window");
            Save(window, "adventures.png");
            Check(window.FindControl<Control>("LaunchNavigation")!.IsVisible, "Adventure navigation visible");
            Check(window.FindControl<TextBlock>("SlotKind0")!.Text == "Vanilla Save", "Vanilla pill label");
            Check(window.FindControl<TextBlock>("SlotKind1")!.Text == "Rando Save", "Seeded rando pill label");
            Check(window.FindControl<Border>("SlotBadgeFrame0")!.Classes.Contains("vanilla"), "Green vanilla badge style");
            Check(window.FindControl<Border>("SlotBadgeFrame1")!.Classes.Contains("rando"), "Purple rando badge style");
            Click(window, "SlotAction1");
            Check(window.FindControl<Control>("DetailsPage")!.IsVisible, "Selecting a card opens details");
            Check(!window.FindControl<Control>("LaunchNavigation")!.IsVisible, "Details match Qt header navigation");
            Check(window.FindControl<TextBlock>("DetailsSeed")!.Text!.Contains("0123456789ABCDEF"), "Seed remains visible in graphical details");
            var detailsSlots = window.FindControl<ComboBox>("HAL_DetailsSlotPicker")!;
            Check(detailsSlots.Items.Count == 2, "Adventure details exposes all save slots");
            Check(detailsSlots.Parent is StackPanel slotPanel && slotPanel.Children.IndexOf(window.FindControl<Border>("DetailsBadge")!) < slotPanel.Children.IndexOf(detailsSlots) &&
                window.FindControl<Button>("DeleteSelectedSaveButton")?.Parent is Grid deleteHeader && Grid.GetColumn(window.FindControl<Button>("DeleteSelectedSaveButton")!) == 1 &&
                deleteHeader.Children.Contains(window.FindControl<Button>("HAL_DetailsBackButton")!),
                "Save slot selection is below the type badge and Delete Adventure is in the header");
            Check(window.FindControl<Button>("EditSaveButton") is { IsEnabled: true }, "Adventure details offers editing for a readable save");
            detailsSlots.SelectedItem = detailsSlots.Items.Cast<SaveSummary>().Single(slot => slot.Slot == 1);
            Pump();
            var displayedSave = typeof(HAL_AdventureSummaryView).GetField("_save", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .GetValue(window.FindControl<HAL_AdventureSummaryView>("SummaryView")) as SaveSummary;
            Check(displayedSave?.Slot == 1, "Selecting a details slot updates the displayed inventory");
            preferences.Value.GameDirectory = fakeGame;
            var originalSummary = window.FindControl<HAL_AdventureSummaryView>("SummaryView");
            var originalWindows = window.OwnedWindows.Count;
            Click(window, "EditSaveButton");
            WaitFor(() => window.FindControl<TextBlock>("HAL_EditStatus")!.Text != "Loading save…");
            Check(window.FindControl<Control>("DetailsPage")!.IsVisible &&
                  ReferenceEquals(originalSummary, window.FindControl<HAL_AdventureSummaryView>("SummaryView")) &&
                  window.OwnedWindows.Count == originalWindows && window.FindControl<Button>("HAL_CancelEditsButton")!.IsVisible,
                  "Edit mode stays on the adventure screen without opening another window");
            Click(window, "HAL_CancelEditsButton");
            preferences.Value.GameDirectory = previousGame;
            Check(!window.FindControl<Button>("LaunchButton")!.IsEnabled, "Missing game cannot launch");
            Check(window.FindControl<HAL_AdventureSummaryView>("SummaryView") is { ExpandToFill: true, Compact: false } &&
                  window.FindControl<HAL_AdventureSummaryView>("ManagedSummaryView") is { ExpandToFill: false, Compact: true },
                "Adventure details summary expands to its card while compact profile summaries keep their fixed scale");
            Save(window, "details.png");
            window.Width = 750; window.Height = 475;
            Save(window, "details-small.png");
            window.Width = 1000; window.Height = 640;
            var selectionBrushes = new HashSet<string>();
            foreach (var theme in ThemeCatalog.All)
            {
                window.FindControl<ComboBox>("ThemePicker")!.SelectedItem = theme;
                Pump();
                Check(window.FindControl<HAL_AdventureSummaryView>("SummaryView")!.Content is not null, theme.Label + " redraws inventory");
                Check(Application.Current!.Resources["SelectionBrush"] is { } selectionBrush, theme.Label + " supplies a themed selection brush");
                selectionBrushes.Add(Application.Current!.Resources["SelectionBrush"]!.ToString() ?? "");
            }
            Check(selectionBrushes.Count > 1, "Selected-row highlight changes with the active theme palette");
            window.FindControl<ComboBox>("ThemePicker")!.SelectedItem = ThemeCatalog.Resolve("zelda");
            ClickContent(window, "Choose Another Adventure");
            ClickContent(window, "Randomizer");
            Check(window.FindControl<Control>("RandomizerPage")!.IsVisible, "Top-level Randomizer navigation");
            var randomizerTabs = window.FindControl<TabControl>("RandomizerCategories")!;
            randomizerTabs.SelectedItem = randomizerTabs.Items.Cast<TabItem>().Single(i => i.Header?.ToString() == "Shuffles");
            Pump();
            var sariaShuffle = window.FindControl<CheckBox>("SariaFairyOcarinaShuffleCheck")!;
            var skullShuffle = window.FindControl<CheckBox>("SkulltulaRewardsCheck")!;
            Check(sariaShuffle.IsEnabled && skullShuffle.IsEnabled &&
                sariaShuffle.Content!.ToString()!.Contains("experimental") && skullShuffle.Content!.ToString()!.Contains("experimental"),
                "Saria and Skulltula reward exchanges are selectable and marked experimental");
            File.WriteAllText(Path.Combine(fakeGame, "hookshot_randomizer_capability.txt"), "HOOKSHOT_DEV_RANDOMIZER=1\n");
            preferences.Value.GameDirectory = fakeGame;
            typeof(MainWindow).GetMethod("LoadEnhancementSettings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(window, null);
            var fastText = window.FindControl<CheckBox>("RandomizerFastTextCheck")!;
            Check(fastText.IsEnabled && fastText.Content?.ToString() == "Fast text (instant)",
                "Fast text is available for the selected development installation");
            sariaShuffle.IsChecked = true; skullShuffle.IsChecked = true;
            fastText.IsChecked = true;
            ClickContent(window, "Save preferences");
            var savedShufflePrefs = new PreferenceStore(Repository.Root).Value;
            Check(savedShufflePrefs.RandomizerSariaOcarina && savedShufflePrefs.RandomizerSkulltulaRewards && HAL_EnhancementPreferences.Load(fakeGame).FastText,
                "Saria, Skulltula and Fast text selections survive saving preferences");
            sariaShuffle.IsChecked = false; skullShuffle.IsChecked = false;
            fastText.IsChecked = false;
            ClickContent(window, "Save preferences");
            preferences.Value.GameDirectory = previousGame;
            initialSkullMaximum.SelectedItem = initialSkullMaximum.Items.Cast<ComboBoxItem>().Single(i => i.Tag?.ToString() == "100");
            Pump();
            var selectedMaximum = (HAL_RandomizerSettings)typeof(MainWindow).GetMethod("ReadRandomizerSettings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(window, null)!;
            Check(selectedMaximum.MaximumSkulltulas == 100, "Selected Skulltula milestone reaches seed settings");
            initialSkullMaximum.SelectedItem = initialSkullMaximum.Items.Cast<ComboBoxItem>().Single(i => i.Tag?.ToString() == "50");
            Save(window, "skulltula-maximum.png");
            randomizerTabs.SelectedItem = randomizerTabs.Items.Cast<TabItem>().Single(i => i.Header?.ToString() == "Logic/Access");
            Pump();
            Check(window.FindControl<CheckBox>("OpenDekuTreeCheck")?.Content?.ToString() == "Open Deku Tree (Experimental)" &&
                  window.FindControl<CheckBox>("OpenKokiriForestCheck")?.Content?.ToString() == "Open forest (Experimental)",
                "Open Deku and Open forest are in Randomizer Logic/Access");
            var randoOwl = window.FindControl<CheckBox>("RandomizerSkipFirstOwlCheck")!;
            var enhancementOwl = window.FindControl<CheckBox>("SkipFirstOwlCheck")!;
            Check(randoOwl.Content?.ToString() == "Skip Owl", "Randomizer Time Savers labels the owl option Skip Owl");
            randoOwl.IsChecked = true;
            Check(enhancementOwl.IsChecked == true, "randomizer owl control updates the shared enhancement");
            enhancementOwl.IsChecked = false;
            Check(randoOwl.IsChecked == false, "enhancement owl control updates the randomizer page");
            var randoOpening = window.FindControl<CheckBox>("RandomizerSkipOpeningCheck")!;
            var enhancementOpening = window.FindControl<CheckBox>("SkipOpeningCheck")!;
            randoOpening.IsChecked = false;
            Check(enhancementOpening.IsChecked == false, "randomizer opening control updates the shared enhancement");
            enhancementOpening.IsChecked = true;
            Check(randoOpening.IsChecked == true, "enhancement opening control updates the randomizer page");
            var openForest = window.FindControl<CheckBox>("OpenKokiriForestCheck")!;
            var openDeku = window.FindControl<CheckBox>("OpenDekuTreeCheck")!;
            Check(openForest.IsEnabled && openDeku.IsEnabled,
                "Experimental forest and Deku access settings can be selected");
            Check(window.FindControl<CheckBox>("OpenTimeDoorCheck")!.IsEnabled == randoOpening.IsEnabled &&
                  window.FindControl<CheckBox>("OpenJabuCheck")!.IsEnabled == randoOpening.IsEnabled &&
                  window.FindControl<CheckBox>("SkipTrialsCheck")!.IsEnabled == randoOpening.IsEnabled,
                "Experimental access probes follow development install availability");
            Check(Grid.GetRow(openForest) == 0 && Grid.GetColumn(openForest) == 0 &&
                  Grid.GetRow(openDeku) == 0 && Grid.GetColumn(openDeku) == 1,
                "Logic/Access access settings use two columns to reduce vertical scrolling");
            randomizerTabs.SelectedItem = randomizerTabs.Items.Cast<TabItem>().First();
            Save(window, "randomizer.png");
            ClickContent(window, "Enhancements");
            Check(window.FindControl<Control>("EnhancementsPage")!.IsVisible, "Enhancements navigation");
            Check(window.FindControl<Button>("EnhancementsAllButton") is not null, "Enhancements All action is present");
            Check(window.FindControl<CheckBox>("SkipGreatDekuCheck")?.Content?.ToString() == "Great Deku Skips" &&
                  window.FindControl<CheckBox>("SkipDekuMeetingCheck") is null &&
                  window.FindControl<CheckBox>("SkipDekuClearCheck") is null,
                "Meeting and clear skips are consolidated under Great Deku Skips");
            Check(window.FindControl<CheckBox>("EnhancementChildStealthCheck") is not null, "Child Stealth settings are shared");
            Check(window.FindControl<CheckBox>("SkipEntranceCutscenesCheck") is { IsEnabled: true } &&
                  window.FindControl<CheckBox>("SkipFirstOwlCheck")?.Content?.ToString() == "Skip Owl" &&
                  window.FindControl<CheckBox>("SkipBossIntroductionsCheck")?.Content?.ToString() == "Skip Boss Introductions" &&
                  window.FindControl<CheckBox>("SkipHyruleFieldEntranceCheck") is null,
                "Entrance cutscenes and automatic owl scenes have separate settings, as do boss introductions");
            Check(window.FindControl<Control>("EnhancementChildStealthCheck")!.IsVisible, "Child Stealth remains in Enhancements");
            var enhancementTabs = window.FindControl<TabControl>("EnhancementCategories")!;
            Save(window, "enhancements-skips.png");
            Check(window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Cutscenes") &&
                  window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Misc") &&
                  window.FindControl<CheckBox>("SkipDekuEntranceCheck") is { IsEnabled: true } &&
                  !window.GetVisualDescendants().OfType<CheckBox>().Any(c => c.Content?.ToString() == "Skip Story Cutscenes"),
                "Skip settings retain implemented controls and hide unfinished options");
            Check(!window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("New seeds start") == true),
                "Child Stealth has no seed-start explanation text");
            enhancementTabs.SelectedItem = enhancementTabs.Items.Cast<TabItem>().Single(i => i.Header?.ToString() == "Cosmetics");
            Pump();
            Check(window.FindControl<Button>("KokiriTunicColor") is not null &&
                  window.FindControl<CheckBox>("CustomCosmeticsCheck") is not null &&
                  window.FindControl<Button>("RandomizeUnlockedTunicsButton") is null &&
                  window.FindControl<RadioButton>("KokiriTunicLocked") is not null &&
                  window.FindControl<RadioButton>("KokiriTunicRandomLaunch") is not null &&
                  window.FindControl<RadioButton>("KokiriTunicSeedRandom") is not null,
                "Cosmetics has a Custom cosmetics checkbox, swatch picker and three modes");
            window.FindControl<CheckBox>("CustomCosmeticsCheck")!.IsChecked = true;
            window.FindControl<Button>("KokiriTunicColor")!.Content = "#D53A7B";
            window.FindControl<RadioButton>("KokiriTunicLocked")!.IsChecked = true;
            window.FindControl<Button>("GoronTunicColor")!.Content = "#010203";
            window.FindControl<RadioButton>("GoronTunicRandomLaunch")!.IsChecked = true;
            window.FindControl<Button>("ZoraTunicColor")!.Content = "#040506";
            window.FindControl<RadioButton>("ZoraTunicSeedRandom")!.IsChecked = true;
            window.FindControl<CheckBox>("HammerColorEnabled")!.IsChecked = true;
            window.FindControl<Button>("HammerColor")!.Content = "#AC1245";
            var hammerMode = window.FindControl<ComboBox>("HammerMode")!;
            hammerMode.SelectedItem = hammerMode.Items.Cast<ComboBoxItem>().Single(x => x.Tag?.ToString() == "seed");
            Click(window, "SaveTunicCosmeticsButton");
            var cosmeticsStatus = window.FindControl<TextBlock>("StatusText")!.Text ?? string.Empty;
            Check(cosmeticsStatus.Contains("apply on the next game launch") &&
                  !cosmeticsStatus.Contains("not available"),
                "Cosmetic save confirms runtime application on the next launch");
            using (var cosmeticJson = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(Repository.Root, "preferences.json"))))
                Check(cosmeticJson.RootElement.GetProperty("tunic_cosmetics").GetProperty("enabled").GetBoolean() &&
                      cosmeticJson.RootElement.GetProperty("tunic_cosmetics").GetProperty("kokiri").GetProperty("mode").GetString() == "locked" &&
                      cosmeticJson.RootElement.GetProperty("tunic_cosmetics").GetProperty("kokiri").GetProperty("color").GetString() == "#D53A7B" &&
                      cosmeticJson.RootElement.GetProperty("tunic_cosmetics").GetProperty("goron").GetProperty("mode").GetString() == "random_launch" &&
                      cosmeticJson.RootElement.GetProperty("tunic_cosmetics").GetProperty("zora").GetProperty("mode").GetString() == "seed" &&
                      cosmeticJson.RootElement.GetProperty("tunic_cosmetics").GetProperty("hammer_enabled").GetBoolean() &&
                      cosmeticJson.RootElement.GetProperty("tunic_cosmetics").GetProperty("hammer").GetProperty("color").GetString() == "#AC1245" &&
                      cosmeticJson.RootElement.GetProperty("tunic_cosmetics").GetProperty("hammer").GetProperty("mode").GetString() == "seed",
                    "Locked, launch-random and seed-random modes persist with their color values");
            var startingMapsPicker = window.FindControl<ComboBox>("StartingMapsModePicker")!;
            Check(window.FindControl<CheckBox>("StartingSwordCheck") is { IsEnabled: true } &&
                  window.FindControl<CheckBox>("StartingSlingshotCheck") is { IsEnabled: true } &&
                  window.FindControl<CheckBox>("StartingOcarinaCheck") is { IsEnabled: true } &&
                  window.FindControl<WrapPanel>("StartingSongsPanel")!.Children.OfType<CheckBox>().Count() == 12 &&
                  window.FindControl<NumericUpDown>("StartingSticksCount") is { Maximum: 10 } &&
                  window.FindControl<NumericUpDown>("StartingBombsCount") is { Maximum: 20 },
                "Starting equipment, Ocarina, songs and bounded consumable counts are available");
            Check(window.FindControl<TextBlock>("StartingConsumablesHelp")!.Text!.Contains("full 30-seed bag", StringComparison.Ordinal),
                "Starting inventory explains the slingshot's initial seed fill");
            window.FindControl<CheckBox>("StartingSlingshotCheck")!.IsChecked = true;
            window.FindControl<NumericUpDown>("StartingSeedsCount")!.Value = 12;
            window.FindControl<WrapPanel>("StartingSongsPanel")!.Children.OfType<CheckBox>()
                .Single(c => c.Tag?.ToString() == "TimeSong").IsChecked = true;
            var draftStarts = (HAL_RandomizerSettings)typeof(MainWindow).GetMethod("ReadRandomizerSettings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(window, null)!;
            Check(draftStarts.StartingSlingshot && draftStarts.StartingConsumables![2] == 12 && draftStarts.StartingSongs!.Contains("TimeSong"),
                "Starting menu selections reach the randomizer settings");
            window.FindControl<NumericUpDown>("StartingSeedsCount")!.Value = 0;
            window.FindControl<CheckBox>("StartingSlingshotCheck")!.IsChecked = false;
            window.FindControl<WrapPanel>("StartingSongsPanel")!.Children.OfType<CheckBox>()
                .Single(c => c.Tag?.ToString() == "TimeSong").IsChecked = false;
            Check(startingMapsPicker.Items.Count == 2,
                "Maps and compasses uses the working Vanilla/Start With selector");
            startingMapsPicker.SelectedItem = startingMapsPicker.Items.Cast<ComboBoxItem>()
                .Single(item => item.Tag?.ToString() == "start_with");
            Pump();
            Check(new PreferenceStore(Repository.Root).Value.RandomizerStartingMaps,
                "Changing Start With saves the setting for the next randomized seed immediately");
            startingMapsPicker.SelectedItem = startingMapsPicker.Items.Cast<ComboBoxItem>()
                .Single(item => item.Tag?.ToString() == "vanilla");
            Pump();
            Check(!window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Seed behavior"),
                "Randomizer preferences omit the redundant seed behavior panel");
            Check(window.FindControl<TextBlock>("RandomizerCoverageText") is not null,
                "Randomizer General shows coverage notes");
            Check(window.FindControl<TextBox>("GeneralSeedBox") is null && window.FindControl<CheckBox>("NativeItemRandomizerCheck") is null,
                "General omits duplicate seed entry and the native randomizer status checkbox");
            Check(window.FindControl<CheckBox>("RandomRupeeNamesCheck") is { IsEnabled: false },
                "General shows the unavailable random rupee names enhancement");
            Check(window.FindControl<Button>("DeleteSelectedSaveButton") is not null, "Selected-adventure delete action is present");
            Check(window.FindControl<Button>("DeleteManagedButton") is not null, "Profile manager delete action is present");
            Save(window, "cosmetics.png");
            ClickContent(window, "Adventures");
            ClickContent(window, "Manage profiles");
            Check(window.FindControl<Control>("ManagePage")!.IsVisible, "Tabbed profile manager opens from Adventure Select");
            var managerTabs = window.FindControl<TabControl>("ProfileManagerTabs");
            Check(managerTabs is not null && managerTabs.Items.Count == 5 &&
                  managerTabs.Items.Cast<TabItem>().Any(t => t.Header?.ToString() == "Spiritual Stones"),
                "Profile manager includes Overview, Save files, Spiritual Stones, Randomizer and Manage tabs");
            Check(window.FindControl<ListBox>("ProfileList") is not null && window.FindControl<TextBlock>("ManagedProfileTitle") is not null,
                "Profile manager uses the Qt left-list and right-detail layout");
            Check(window.FindControl<Button>("ManagedLaunchButton") is not null && window.FindControl<Button>("ManagedDeleteButton") is not null,
                "Selected isolated profile has persistent Launch and Delete actions");
            Check(window.FindControl<Button>("ManagedCopyButton") is null && window.FindControl<TextBox>("ExternalPathBox") is null,
                "Save information omits registration and copy-to-isolated controls");
            managerTabs!.SelectedItem = managerTabs.Items.Cast<TabItem>().Single(t => t.Header?.ToString() == "Manage");
            Pump();
            Check(window.FindControl<Button>("Pin0Button") is not null && window.FindControl<Button>("Pin1Button") is not null &&
                  window.FindControl<Button>("Pin2Button") is not null &&
                  window.GetVisualDescendants().OfType<Button>().Any(button => button.Content?.ToString() == "Back up to ZIP…") &&
                  window.GetVisualDescendants().OfType<Button>().Any(button => button.Content?.ToString() == "Recover profiles"),
                "Manage tab groups three shortcut pins and profile actions in the Qt-style layout");
            Check(window.FindControl<HAL_AdventureSummaryView>("ManagedSummaryView")!.Compact,
                "Manager overview uses the compact Qt-sized save summary");
            window.FindControl<ListBox>("ProfileList")!.SelectedItem = Repository.Get(random.Id);
            Pump(); window.UpdateLayout();
            var saveSlots = window.FindControl<ComboBox>("HAL_ManagedSlotPicker")!;
            Check(saveSlots.Items.Count == 2, "Profile manager exposes all save slots for the selected adventure");
            saveSlots.SelectedItem = saveSlots.Items.Cast<SaveSummary>().Single(slot => slot.Slot == 0);
            Pump();
            Check(window.FindControl<TextBlock>("ManagedInventoryInfo")!.Text!.Contains("Arrows: 12"),
                "Selecting a save slot displays that slot's live inventory");
            saveSlots.SelectedItem = saveSlots.Items.Cast<SaveSummary>().Single(slot => slot.Slot == 1);
            Pump();
            Check(window.FindControl<TextBlock>("ManagedInventoryInfo")!.Text!.Contains("Arrows: 31") &&
                  window.FindControl<TextBlock>("ManagedInventoryInfo")!.Text!.Contains("Sticks: 5"),
                "Changing save slot refreshes the displayed inventory");
            managerTabs!.SelectedItem = managerTabs.Items.Cast<TabItem>().Single(t => t.Header?.ToString() == "Save files");
            Pump();
            Check(window.FindControl<TextBlock>("ManagedSlotInfo")!.Text!.Contains("Save 2"),
                "Save files details follow the selected game slot");
            managerTabs.SelectedItem = managerTabs.Items.Cast<TabItem>().Single(t => t.Header?.ToString() == "Manage");
            Pump();
            var profileList = window.FindControl<ListBox>("ProfileList")!;
            var sidebar = window.FindControl<Grid>("HAL_ManageSidebar")!;
            var newAdventure = window.FindControl<Button>("HAL_ManageNewButton")!;
            var backAdventure = window.FindControl<Button>("HAL_ManageBackButton")!;
            Check(profileList.Bounds.Bottom < newAdventure.Bounds.Top && newAdventure.Bounds.Bottom < backAdventure.Bounds.Top &&
                  Math.Abs(backAdventure.Bounds.Bottom - sidebar.Bounds.Height) < 1,
                "Sidebar fills its height with New adventure then Back anchored below the list");
            var listHeight = profileList.Bounds.Height;
            window.Height += 100; Pump(); window.UpdateLayout();
            Check(profileList.Bounds.Height >= listHeight + 95, "Adventure list grows with available window height");
            window.Height -= 100; Pump(); window.UpdateLayout();
            var renderedSelections = new HashSet<Color>();
            foreach (var themeId in new[] { "goron", "zelda" })
            {
                window.FindControl<ComboBox>("ThemePicker")!.SelectedItem = ThemeCatalog.Resolve(themeId);
                Pump(); window.UpdateLayout();
                var selectedRow = (ListBoxItem)profileList.ContainerFromIndex(profileList.SelectedIndex)!;
                selectedRow.Focus(); Pump();
                var presenter = selectedRow.GetVisualDescendants().OfType<ContentPresenter>()
                    .Single(control => control.Name == "PART_ContentPresenter");
                var rendered = (presenter.Background as ISolidColorBrush)?.Color;
                var expected = ((ISolidColorBrush)Application.Current!.Resources["SelectionBrush"]!).Color;
                Check(rendered == expected, themeId + " renders the selected profile with its theme color");
                renderedSelections.Add(rendered!.Value);
            }
            Check(renderedSelections.Count == 2, "Visible selected-row highlight changes when switching themes");
            Save(window, "manage-sidebar.png");
            Click(window, "HAL_RenameButton");
            Pump();
            var renameDialog = window.OwnedWindows.OfType<HAL_RenameAdventureDialog>().Single();
            var renameInput = renameDialog.GetVisualDescendants().OfType<TextBox>().Single();
            Check(renameInput.Text == random.Name && window.FindControl<TextBox>("RenameBox") is null,
                "Rename opens a popup prefilled with the current adventure name");
            renameInput.Text = "Renamed adventure";
            renameDialog.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString() == "Rename")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump();
            Check(Repository.Get(random.Id).Name == "Renamed adventure" &&
                  window.FindControl<TextBlock>("ManagedProfileTitle")!.Text == "Renamed adventure",
                "Rename popup saves the name and keeps the adventure selected");
            window.FindControl<ComboBox>("HAL_ManagedSlotPicker")!.SelectedItem =
                window.FindControl<ComboBox>("HAL_ManagedSlotPicker")!.Items.Cast<SaveSummary>().Single(slot => slot.Slot == 0);
            Pump();
            managerTabs!.SelectedItem = managerTabs.Items.Cast<TabItem>().Single(t => t.Header?.ToString() == "Save files");
            Pump();
            Check(window.FindControl<TextBlock>("ManagedDiskSummary")!.Text!.Contains("files ·") &&
                  window.FindControl<TextBlock>("ManagedSlotInfo")!.Text!.Contains("game slot(s) detected") &&
                  window.FindControl<TextBlock>("ManagedInventoryInfo")!.Text!.Contains("Arrows: 12") &&
                  window.FindControl<TextBlock>("ManagedInventoryInfo")!.Text!.Contains("Sticks: 9"),
                "Save files tab displays on-disk data and decoded live inventory quantities");
            managerTabs!.SelectedItem = managerTabs.Items.Cast<TabItem>().Single(t => t.Header?.ToString() == "Randomizer");
            Pump();
            var managedRando = window.FindControl<TextBlock>("ManagedRandomizerSummary")!.Text ?? "";
            Check(managedRando.Contains("Location scope: Childhood") && managedRando.Contains("Heart Pieces: On") &&
                  managedRando.Contains("Child Stealth: On") && managedRando.Contains("Shuffled checks: 2") &&
                  managedRando.Contains("Logic status: childhood checks; experimental logic"),
                "Profile manager Randomizer tab shows this seed's immutable stored settings and check summary");
            Check(window.GetVisualDescendants().OfType<Button>().All(button => button.Content?.ToString() != "Open Randomizer settings"),
                "Selected seed settings remain a read-only profile snapshot");
            ClickContent(window, "Randomizer");
            var managerRandomizerTabs = window.FindControl<TabControl>("RandomizerCategories");
            var randomizerFooter = window.FindControl<StackPanel>("RandomizerActionFooter");
            Check(randomizerFooter is { HorizontalAlignment: Avalonia.Layout.HorizontalAlignment.Right } &&
                  Grid.GetRow(randomizerFooter) == 1,
                "Randomizer actions stay in a bottom-right footer outside the scroll area");
            Check(window.FindControl<Button>("SaveRandomizerButton") is not null &&
                  window.FindControl<Button>("CreateRandomizedButton") is not null,
                "Both Randomizer footer actions remain visible and available");
            Check(managerRandomizerTabs is not null && managerRandomizerTabs.Items.Count == 7 &&
                  managerRandomizerTabs.Items.Cast<TabItem>().All(t => t.Header?.ToString() is not ("Entrance Tracker" or "Check Tracker" or "Hints/Traps")),
                "Randomizer hides tracker and hints/traps pages while keeping other categories available");
            managerRandomizerTabs!.SelectedItem = managerRandomizerTabs.Items.Cast<TabItem>().Single(t => t.Header?.ToString() == "General");
            Save(window, "randomizer-general.png");
            Check(managerRandomizerTabs!.Items.Cast<TabItem>().Any(t => t.Header?.ToString() == "Starting Inventory"),
                "Randomizer uses the Starting Inventory category name");
            Check(window.FindControl<CheckBox>("ChildStealthCheck") is null,
                "Child Stealth is configured in Enhancements only");
            Check(managerRandomizerTabs.Items.Cast<TabItem>().Any(t => t.Header?.ToString() == "Time Savers"),
                "Randomizer includes the Time Savers menu for upcoming owl skip work");
            Check(window.FindControl<ComboBox>("RandomizerScopePicker") is null &&
                  window.FindControl<TextBlock>("RandomizerCoverageText")?.Text?.Contains("reachability is not verified") == true &&
                  ((HAL_RandomizerSettings)typeof(MainWindow).GetMethod("ReadRandomizerSettings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(window, null)!).Scope == "full_game",
                "New seed settings use full-game chest coverage without a scope selector");
            Check(startingMapsPicker.IsEnabled, "Starting maps remain selectable");
            Check(new[] { "StartingBombBagCheck", "StartingBoomerangCheck", "StartingLensCheck", "StartingShieldCheck" }
                .All(name => window.FindControl<CheckBox>(name) is { IsEnabled: true }),
                "Experimental bomb bag, boomerang, lens and shield starts remain selectable");
            startingMapsPicker.SelectedItem = startingMapsPicker.Items.Cast<ComboBoxItem>().Single(i => i.Tag?.ToString() == "start_with");
            Pump();
            Check(new PreferenceStore(Repository.Root).Value.RandomizerStartingMaps, "Starting maps preference persists");
            startingMapsPicker.SelectedItem = startingMapsPicker.Items.Cast<ComboBoxItem>().Single(i => i.Tag?.ToString() == "vanilla");
            Check(window.FindControl<CheckBox>("HeartPiecesCheck") is { IsEnabled: true, Content: "Heart Pieces (experimental)" },
                "Experimental heart-piece shuffle remains enabled");
            randomizerTabs.SelectedItem = randomizerTabs.Items.Cast<TabItem>().Single(t => t.Header?.ToString() == "Shuffles");
            Pump();
            Check(window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("Chest rewards — active") == true),
                "Implemented chest rewards are shown as active, not unavailable");
            var sariaOption = window.FindControl<CheckBox>("SariaFairyOcarinaShuffleCheck")!;
            Check(sariaOption.IsEnabled && sariaOption.Content?.ToString() == "Saria's Fairy Ocarina (experimental)" &&
                  Grid.GetColumn(sariaOption) == 1 && Grid.GetRow(sariaOption) == 0 &&
                  ReferenceEquals(sariaOption.Parent, window.FindControl<CheckBox>("HeartPiecesCheck")!.Parent),
                "Saria's Fairy Ocarina is at the top of the second shuffle column");
            window.FindControl<CheckBox>("HeartPiecesCheck")!.IsChecked = true;
            Check(window.FindControl<CheckBox>("HeartPiecesCheck")!.IsChecked == true,
                "Reviewed heart-piece shuffle can be selected");
            randomizerTabs.SelectedItem = randomizerTabs.Items.Cast<TabItem>().Single(t => t.Header?.ToString() == "Dungeons");
            Pump();
            Check(!window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("Master Quest") == true),
                "Dungeon settings do not reference Master Quest");
            var locationsTab = randomizerTabs.Items.Cast<TabItem>().Single(t => t.Header?.ToString() == "Locations");
            randomizerTabs.SelectedItem = locationsTab;
            Pump();
            Check(window.FindControl<TreeView>("RandomizerLocationsTree")!.Parent is Border { Classes: var classes } && classes.Contains("panel"),
                "Location scroll area sits inside a theme-aware panel");
            Check(window.FindControl<TextBlock>("RandomizerLocationCoverage")!.Text!.Contains("Select an installed game"),
                "Locations requests a local game instead of bundling a game-derived catalog");
            Check(window.FindControl<TreeView>("RandomizerLocationsTree")!.Items.Cast<TreeViewItem>()
                .Any(group => group.Header?.ToString() == "Skulltula House (6)"),
                "Locations retains the authored Skulltula House reward summary");
            var skullMaximum = window.FindControl<ComboBox>("MaximumSkulltulasCount")!;
            Check(skullMaximum.IsEnabled && (skullMaximum.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "50",
                "Requested Skulltula maximum uses the saved milestone");
            Save(window, "randomizer-locations.png");
            Save(window, "randomizer-categories.png");
            ClickContent(window, "Adventures");
            Click(window, "SlotAction2");
            Check(window.FindControl<Control>("CreatePage")!.IsVisible, "Empty slot opens profile creation");
            Save(window, "create-adventure.png");
            Check(window.FindControl<CheckBox>("CreateRandomizedCheck")!.IsChecked == false, "Profile type is chosen at creation");
            Check(!window.FindControl<TextBox>("CreateSeedBox")!.IsEnabled &&
                  window.FindControl<Button>("CreateSeedButton")!.IsEnabled == false &&
                  window.FindControl<Button>("CreateActionButton")!.Content?.ToString() == "Create profile",
                "Vanilla creation keeps seed controls disabled and uses the plain create action");
            window.FindControl<CheckBox>("CreateRandomizedCheck")!.IsChecked = true;
            Pump();
            var seedInput = window.FindControl<TextBox>("CreateSeedBox")!;
            Check(seedInput.IsEnabled && seedInput.Text?.Length == 16 && seedInput.Text.All(Uri.IsHexDigit) &&
                  window.FindControl<Button>("CreateSeedButton")!.IsEnabled &&
                  window.FindControl<Button>("CreateActionButton")!.Content?.ToString() == "Create profile and generate seed",
                "Randomized creation exposes a generated and editable seed");
            Check(!window.FindControl<Button>("CreateActionButton")!.IsEnabled &&
                  window.FindControl<TextBlock>("CreateNotice")!.Text!.Contains("compiled with native randomizer support"),
                "Randomized creation clearly reports and blocks when the selected game build lacks native support");
            var priorSeed = seedInput.Text;
            Click(window, "CreateSeedButton");
            Check(seedInput.Text?.Length == 16 && seedInput.Text != priorSeed && seedInput.Text!.All(Uri.IsHexDigit),
                "New seed value creates a valid replacement seed before profile creation");
            window.FindControl<CheckBox>("CreateRandomizedCheck")!.IsChecked = false;
            Pump();
            Check(window.FindControl<Button>("CreateActionButton")!.IsEnabled &&
                  window.FindControl<Button>("CreateActionButton")!.Content?.ToString() == "Create profile",
                "Vanilla profile creation remains available when randomized support is missing");
            window.Close(); Pump();
            Console.WriteLine("PASS GUI opened, navigated, rendered all themes, and closed with an isolated fixture registry.");
            return 0;
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    public static void Save(Window w, string name)
    {
        Pump();
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)w.Bounds.Width, (int)w.Bounds.Height));
        bitmap.Render(w); bitmap.Save(Path.Combine(Output, name));
    }
    static void Pump() { Dispatcher.UIThread.RunJobs(); }
    static void WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!condition() && DateTime.UtcNow < deadline) { Pump(); Thread.Sleep(10); }
        Pump();
        if (!condition()) throw new TimeoutException("Editor UI did not reach the expected state.");
    }
    static void Click(Window w, string name) => w.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    static void TestStagedDevInstallKeepsExistingLogs(string root)
    {
        var output = Path.Combine(root, "install-output");
        var staged = Path.Combine(root, "install-stage");
        Directory.CreateDirectory(Path.Combine(staged, "romfs"));
        Directory.CreateDirectory(Path.Combine(staged, "logs"));
        Directory.CreateDirectory(Path.Combine(output, "logs"));
        File.WriteAllText(HookshotGame.ResolveExecutable(staged), "executable");
        File.WriteAllText(Path.Combine(staged, "image.bin"), "image");
        File.WriteAllText(Path.Combine(staged, "hookshot_randomizer_capability.txt"), "HOOKSHOT_DEV_RANDOMIZER=1");
        File.WriteAllText(Path.Combine(staged, "logs", "build.log"), "staging log");
        File.WriteAllText(Path.Combine(output, "logs", "existing.log"), "keep this log");
        var plan = new HAL_DevelopmentBuild(root, output, staged, false, new System.Diagnostics.ProcessStartInfo(), "", null);
        bool rejected = false;
        try { plan.InstallCompletedBuild(); }
        catch (InvalidDataException) { rejected = true; }
        Check(rejected && !File.Exists(HookshotGame.ResolveExecutable(output)),
            "Staged install rejects a missing Vulkan renderer before installing files");
        File.WriteAllText(HookshotGame.ResolveRenderer(staged), "authored renderer fixture");
        plan.InstallCompletedBuild();
        Check(File.Exists(HookshotGame.ResolveExecutable(output)) &&
              File.Exists(Path.Combine(output, "logs", "existing.log")) &&
              !File.Exists(Path.Combine(output, "logs", "build.log")),
            "Staged install skips generated logs and preserves existing installation logs");
    }
    static void ClickContent(Window w, string caption) => w.GetVisualDescendants().OfType<Button>()
        .First(b => b.Content?.ToString() == caption).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    static void Check(bool pass, string name) { if (!pass) throw new Exception(name); Console.WriteLine("PASS " + name); }
    static void WriteSave(string user) => WriteSaveSlot(user, 0, 12, 9);
    static void WriteSaveSlot(string user, int slot, uint arrows, uint sticks)
    {
        var path = Path.Combine(user, "save", "fixture", $"slot_{slot:00}"); Directory.CreateDirectory(path);
        var bytes = new byte[0x175D8 + 256];
        void U(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, 4), value);
        U(0, 0x01020304); U(4, 0x002D80EB); U(8, 0x175D8);
        var fields = new (uint, uint)[] { (0xA77921D7, 136), (0x31AB5580, 16), (0xFBE01DA1, 16), (0x04735978, 0),
            (0x80D34CE9, 1), (0xEEB495B9, 1), (0xCBD1E58C, 1), (29491202, 1), (0x457616B0, 10), (0x735B00F7, 20),
            (0xAE8CC742, 30), (0x418DB791, 0), (0xE753508B, 0), (0x1D6189DA, 0x175D8) };
        fields = fields.Concat(new (uint,uint)[] { (0x518EC74B,arrows),(0xDB1BCD4F,13),(0xDF7C517A,0),(0x5FADF7FC,21),
            (0xF30D9F54,16),(0xB9494411,sticks),(0xB003124D,4) }).ToArray();
        for (int i=0;i<fields.Length;i++) { U(40+i*8,fields[i].Item1); U(44+i*8,fields[i].Item2); }
        System.Text.Encoding.UTF8.GetBytes("HyruleCastle_Child").CopyTo(bytes, 0x175D8);
        File.WriteAllBytes(Path.Combine(path,"progress.sav"),bytes);
    }
}

public class HAL_NativeTestApp : App
{
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow(HAL_Program.Repository);
            desktop.MainWindow = window;
            DispatcherTimer.RunOnce(() => HAL_Program.Save(window, "native-adventures.png"), TimeSpan.FromSeconds(2));
            DispatcherTimer.RunOnce(() => window.Close(), TimeSpan.FromSeconds(5));
        }
    }
}
