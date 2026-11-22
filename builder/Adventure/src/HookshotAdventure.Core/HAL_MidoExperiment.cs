namespace HookshotAdventure.Core;

/// <summary>The old local-only Mido experiment is unavailable in this source release.</summary>
public static class HAL_MidoExperiment
{
    public static string Prepare(string developmentDirectory) =>
        throw new InvalidDataException("The local Mido research experiment is not included in this release.");
}
