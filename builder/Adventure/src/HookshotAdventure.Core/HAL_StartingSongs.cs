namespace HookshotAdventure.Core;

/// <summary>Stable order of the twelve supported starting song flags.</summary>
public static class HAL_StartingSongs
{
    public static readonly string[] Names = { "ZeldaLullaby", "EponaSong", "SariaSong", "SunSong", "TimeSong", "StormSong",
        "ForestMinuet", "FireBolero", "WaterSerenade", "ShadowNocturne", "SpiritRequiem", "LightPrelude" };
    public static int Mask(IEnumerable<string> names)
    {
        int mask = 0;
        foreach (string name in names)
        {
            int index = Array.IndexOf(Names, name);
            if (index < 0 || (mask & (1 << index)) != 0) throw new InvalidDataException("Invalid or duplicate starting song.");
            mask |= 1 << index;
        }
        return mask;
    }
}
