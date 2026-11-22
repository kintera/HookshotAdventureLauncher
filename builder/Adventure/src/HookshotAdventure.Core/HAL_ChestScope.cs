namespace HookshotAdventure.Core;

/// <summary>Local loose-map discovery for each supported chest scope.</summary>
public static class HAL_ChestScope
{
    public static readonly string[] ChildhoodRegions = ["KokiriForest", "LostWood", "SacredForestMeadow", "HyruleField", "Market",
        "HyruleCastle", "LonLonRanch", "LakeHylia", "KakarikoVillage", "Graveyard", "BottomOfTheWell", "DeathMountainTrail",
        "GoronCity", "DodongoCavern", "DeathMountainCrater", "ZoraRiver", "ZoraDomain", "ZoraFountain", "InsideJabuJabuBelly",
        "GerudoValley", "HountedWasteland", "DesertColossus", "InsideTheDekuTree", "SpiritTemple"];
    public static IReadOnlyList<string> Files(string romfs, string scope)
    {
        if (scope is not ("kokiri_deku" or "childhood" or "full_game")) throw new ArgumentException("Unsupported chest scope.");
        var paths = new List<string>();
        foreach (string prefix in new[] { "Banc", "AoC1/Banc" })
        {
            string baseDir = Path.Combine(romfs, prefix.Replace('/', Path.DirectorySeparatorChar));
            var directories = scope == "full_game" ? new[] { baseDir } :
                (scope == "childhood" ? ChildhoodRegions : ["KokiriForest", "InsideTheDekuTree"]).Select(region => Path.Combine(baseDir, region));
            foreach (string directory in directories)
            {
                if (!Directory.Exists(directory)) continue;
                if (ProfileRepository.IsLink(directory)) throw new InvalidDataException("Linked chest map directory.");
                var options = new EnumerationOptions { RecurseSubdirectories = scope == "full_game", AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false };
                foreach (string path in Directory.EnumerateFiles(directory, "*.bcett.byml.zs", options))
                {
                    string name = Path.GetFileName(path);
                    if ((scope == "childhood" && name.Contains("_Adult", StringComparison.Ordinal)) ||
                        (scope != "kokiri_deku" && (name.StartsWith("Demo_", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("Dream", StringComparison.OrdinalIgnoreCase)))) continue;
                    if (ProfileRepository.IsLink(path)) throw new InvalidDataException("Linked chest map.");
                    paths.Add(path);
                }
            }
        }
        return paths.OrderBy(path => Path.GetRelativePath(romfs, path).Replace('\\', '/'), StringComparer.Ordinal).ToArray();
    }
}
