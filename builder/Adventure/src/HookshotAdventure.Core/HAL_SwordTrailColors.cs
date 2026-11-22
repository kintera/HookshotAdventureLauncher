using System.Security.Cryptography;

namespace HookshotAdventure.Core;

/// <summary>Resolves trail preferences for one profile and launch; effect application is separate.</summary>
public static class HAL_SwordTrailColors
{
    public static IReadOnlyDictionary<string, string> Resolve(HAL_SwordCosmetics settings, string seed)
    {
        var random = new HAL_PythonRandom(seed);
        string Pick(HAL_SwordSetting setting)
        {
            string color = setting.TrailColor;
            if (color.Length != 7 || color[0] != '#' || color.Skip(1).Any(c => !Uri.IsHexDigit(c)))
                throw new InvalidDataException("Invalid sword trail color.");
            if (setting.TrailMode == "locked") return color.ToUpperInvariant();
            if (setting.TrailMode is not ("seed" or "random_launch")) throw new InvalidDataException("Invalid sword trail mode.");
            int Next() => setting.TrailMode == "seed" ? random.ColorComponent() : RandomNumberGenerator.GetInt32(24, 232);
            return $"#{Next():X2}{Next():X2}{Next():X2}";
        }
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["kokiri"] = Pick(settings.Kokiri), ["master"] = Pick(settings.Master), ["biggoron"] = Pick(settings.Biggoron)
        };
    }
}
