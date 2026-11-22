using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace HookshotAdventure.App;

public sealed class HAL_RenameAdventureDialog : Window
{
    public HAL_RenameAdventureDialog(string currentName)
    {
        Title = "Rename adventure";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var name = new TextBox { Text = currentName, MaxLength = 80, Watermark = "Adventure name" };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Classes = { "raised" } };
        var rename = new Button { Content = "Rename", IsDefault = true, Classes = { "raised" } };
        name.TextChanged += (_, _) => rename.IsEnabled = !string.IsNullOrWhiteSpace(name.Text);
        cancel.Click += (_, _) => Close(null);
        rename.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(name.Text)) Close(name.Text.Trim()); };
        Content = new StackPanel
        {
            Margin = new Thickness(18), Spacing = 12,
            Children =
            {
                new TextBlock { Text = "Adventure name" }, name,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8, Children = { cancel, rename }
                }
            }
        };
        Opened += (_, _) => { name.Focus(); name.SelectAll(); };
    }
}
