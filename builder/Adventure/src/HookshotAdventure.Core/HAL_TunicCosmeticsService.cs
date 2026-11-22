namespace HookshotAdventure.Core;

public static class HAL_TunicCosmeticsService
{
    public static IReadOnlyDictionary<string, string>? PrepareLaunchEnvironment(
        string gameDirectory, string profileDirectory, HAL_TunicCosmetics settings,
        IReadOnlyDictionary<string, string>? existing)
    {
        var env = existing is null ? new Dictionary<string, string>(StringComparer.Ordinal) :
            new Dictionary<string, string>(existing, StringComparer.Ordinal);
        if (!settings.Enabled) return existing;
        var root = FindSourceRoot();
        var codec = HAL_AstcCodec.Find(root);
        env.TryGetValue("HOS_FS_OVERLAY", out var baseOverlay);
        var overlay = HAL_CosmeticOverlay.Prepare(gameDirectory, profileDirectory, settings, baseOverlay, codec);
        if (!Directory.Exists(overlay) || ProfileRepository.IsLink(overlay))
            throw new InvalidDataException("Tunic runtime overlay is missing or linked.");
        env["HOS_FS_OVERLAY"] = Path.GetFullPath(overlay);
        return env;
    }

    private static string FindSourceRoot()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory }) {
            var current = new DirectoryInfo(Path.GetFullPath(start));
            while (current is not null) {
                if (File.Exists(Path.Combine(current.FullName, "nx2", "build.py")) &&
                    Directory.Exists(Path.Combine(current.FullName, "builder", "Adventure"))) return current.FullName;
                current = current.Parent;
            }
        }
        throw new InvalidOperationException("Tunic texture preparation requires the Hookshot source checkout.");
    }
}
