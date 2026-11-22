namespace HookshotAdventure.Core;

/// <summary>Counts delivered once, on a newly initialized save.</summary>
public static class HAL_StartingConsumables
{
    public static readonly string[] Names = { "sticks", "nuts", "seeds", "bombs" };
    private static readonly int[] Maximums = { 10, 20, 30, 20 };
    public static int[] Validate(IReadOnlyList<int>? values, bool slingshot, bool bombBag)
    {
        if (values is null || values.Count == 0) return new int[Names.Length];
        if (values.Count != Names.Length) throw new InvalidDataException("Invalid starting consumable counts.");
        var result = values.ToArray();
        for (int i = 0; i < result.Length; i++)
            if (result[i] < 0 || result[i] > Maximums[i]) throw new InvalidDataException("A starting consumable count is outside its starting bag capacity.");
        if (result[2] > 0 && !slingshot) throw new InvalidDataException("Starting Deku Seeds require a starting Slingshot.");
        if (result[3] > 0 && !bombBag) throw new InvalidDataException("Starting Bombs require a starting Bomb Bag.");
        return result;
    }
}
