namespace HookshotAdventure.Core;

// Stable theme IDs and exact surface palette values from Hookshot v2.1.0 Adventure v0.8 ui_theme.py.
public sealed record ThemePalette(string Id, string Label, string Dark, string Light, string Bottom, string Rim);

public static class ThemeCatalog
{
    public const string DefaultTheme = "zelda";
    public static readonly IReadOnlyList<ThemePalette> All = new[]
    {
        new ThemePalette("goron", "Darunia", "#18191c", "#3d3430", "#09090c", "#e05b28"),
        new ThemePalette("gannon", "Gannon", "#35232f", "#563440", "#191018", "#8a4854"),
        new ThemePalette("zelda", "Link", "#193831", "#294e42", "#0b1917", "#426b53"),
        new ThemePalette("purple", "Royal", "#251c32", "#403047", "#17111f", "#6c506e"),
        new ThemePalette("ruby", "Ruby", "#390911", "#a51a35", "#140407", "#fa3c55"),
        new ThemePalette("blue_crystal", "Ruto", "#1c334d", "#314d69", "#101a30", "#5682ae"),
        new ThemePalette("zelda_gold", "Tri-force", "#3c2b13", "#64481c", "#17120b", "#b38626"),
        new ThemePalette("zelda_pearl", "Zelda", "#30275e", "#4b4b8b", "#11152f", "#ca9a70")
    };
    public static ThemePalette Resolve(string? id) => All.FirstOrDefault(x => x.Id == id)
        ?? All.First(x => x.Id == DefaultTheme);
}
