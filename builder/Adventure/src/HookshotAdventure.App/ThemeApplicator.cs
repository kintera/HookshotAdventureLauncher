using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using HookshotAdventure.Core;
namespace HookshotAdventure.App;

public static class ThemeApplicator
{
    private static Color Mix(Color a, Color b, double weight)
    {
        byte W(byte x, byte y) => (byte)Math.Clamp(Math.Round(x * (1 - weight) + y * weight), 0, 255);
        return Color.FromRgb(W(a.R, b.R), W(a.G, b.G), W(a.B, b.B));
    }
    public static void Apply(string themeId)
    {
        var app = Application.Current ?? throw new InvalidOperationException("No active Avalonia application.");
        var p = ThemeCatalog.Resolve(themeId);
        Color dark = Color.Parse(p.Dark), light = Color.Parse(p.Light), bottom = Color.Parse(p.Bottom), rim = Color.Parse(p.Rim);
        // Fluent templates use their own accent palette for selection, focus and tab indicators.
        foreach (var fluent in app.Styles.OfType<FluentTheme>())
        {
            foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
            {
                if (!fluent.Palettes.TryGetValue(variant, out var palette))
                    fluent.Palettes[variant] = palette = new ColorPaletteResources();
                palette.Accent = rim;
            }
        }
        app.Resources["ChromeBrush"] = new LinearGradientBrush
        {
            StartPoint = new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Relative),
            EndPoint = new Avalonia.RelativePoint(1, 1, Avalonia.RelativeUnit.Relative),
            GradientStops = new GradientStops { new GradientStop(dark, 0), new GradientStop(bottom, .8), new GradientStop(light, 1) }
        };
        app.Resources["CardBrush"] = new LinearGradientBrush
        {
            StartPoint = new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Relative),
            EndPoint = new Avalonia.RelativePoint(1, 1, Avalonia.RelativeUnit.Relative),
            GradientStops = new GradientStops { new GradientStop(light, 0), new GradientStop(dark, .35), new GradientStop(bottom, 1) }
        };
        app.Resources["ProfileCardBrush"] = new LinearGradientBrush
        {
            StartPoint = new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Relative),
            EndPoint = new Avalonia.RelativePoint(0, 1, Avalonia.RelativeUnit.Relative),
            GradientStops = new GradientStops
            {
                new GradientStop(Mix(light, Colors.White, .14), 0),
                new GradientStop(Mix(light, dark, .15), .12),
                new GradientStop(Mix(dark, light, .10), .55),
                new GradientStop(Mix(bottom, dark, .35), 1)
            }
        };
        app.Resources["InsetBrush"] = new SolidColorBrush(Mix(bottom, dark, .46));
        app.Resources["RaisedBrush"] = new LinearGradientBrush
        {
            StartPoint = new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Relative),
            EndPoint = new Avalonia.RelativePoint(0, 1, Avalonia.RelativeUnit.Relative),
            GradientStops = new GradientStops { new GradientStop(Mix(light, Colors.White, .12), 0), new GradientStop(Mix(dark, light, .3), .48), new GradientStop(bottom, 1) }
        };
        app.Resources["HoverBrush"] = new SolidColorBrush(Mix(light, rim, .25));
        app.Resources["RimBrush"] = new SolidColorBrush(Mix(rim, light, .25));
        app.Resources["SelectionBrush"] = new SolidColorBrush(Mix(light, rim, .48));
        app.Resources["SelectionForegroundBrush"] = new SolidColorBrush(Colors.White);
        app.Resources["AccentBrush"] = new SolidColorBrush(Mix(rim, Colors.White, .25));
        app.Resources["HeadingBrush"] = new SolidColorBrush(p.Id == "zelda" ? Color.Parse("#DAA520") : Mix(rim, Colors.White, .45));
        app.Resources["ForegroundBrush"] = new SolidColorBrush(Colors.White);
        app.Resources["SubtleBrush"] = new SolidColorBrush(Mix(light, Colors.White, .57));
    }
}
