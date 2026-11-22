using System.Buffers.Binary;

namespace HookshotAdventure.Core;

/// <summary>Finds candidate item-giving subflows in the local game, without modifying its resources.</summary>
public static class HAL_EventRewardScan
{
    public sealed record Candidate(string Resource, int EventIndex, string Actor, string Parameter = "ActorName");
    public sealed record Failure(string Resource, string Error);
    public sealed record Result(int Scanned, IReadOnlyList<Candidate> Candidates, IReadOnlyList<Failure> Failures,
        int ScannedTimelines, IReadOnlyList<HAL_EventTimelineScan.Candidate> TimelineCandidates);

    /// <summary>Timeline-only files have no flowchart subflows for this scanner to inspect.</summary>
    public static bool IsTimelineOnly(ReadOnlySpan<byte> data) => data.Length >= 36 &&
        data[..8].SequenceEqual("BFEVFL\0\0"u8) && BinaryPrimitives.ReadUInt16LittleEndian(data[8..]) == 0x400 &&
        BinaryPrimitives.ReadUInt16LittleEndian(data[12..]) == 0xfeff &&
        BinaryPrimitives.ReadUInt16LittleEndian(data[32..]) == 0 && BinaryPrimitives.ReadUInt16LittleEndian(data[34..]) == 1;

    public static IReadOnlyList<Candidate> Inspect(string resource, string flowName, byte[] data)
    {
        HAL_EventFlow flow;
        try { flow = new HAL_EventFlow(data, flowName); }
        catch (InvalidDataException ex) when (ex.Message == "Unexpected event flowchart." &&
            flowName.LastIndexOf('.') is int dot && dot > 0 &&
            flowName.AsSpan(dot + 1).Length > 0 && flowName.AsSpan(dot + 1).ToString().All(char.IsAsciiDigit))
        {
            // Versioned resource filenames can contain a numeric suffix that is
            // absent from their internal flowchart name.
            flow = new HAL_EventFlow(data, flowName[..flowName.LastIndexOf('.')]);
        }
        var result = new List<Candidate>();
        for (int index = 0; index < flow.Count; index++)
        {
            if (flow.UInt(flow.EventOffset(index) + 8, 1) != 4) continue;
            var parameters = flow.StringParameters(index);
            foreach (string parameter in new[] { "ActorName", "TakenRewardActor" })
            {
                if (!parameters.TryGetValue(parameter, out var actor) ||
                    !(actor.StartsWith("Item_", StringComparison.Ordinal) || actor.StartsWith("TBox_", StringComparison.Ordinal) ||
                      actor.StartsWith("Weapon_", StringComparison.Ordinal) || actor.StartsWith("Obj_Heart", StringComparison.Ordinal) ||
                      actor.StartsWith("Obj_Rupee", StringComparison.Ordinal))) continue;
                result.Add(new(resource, index, actor, parameter));
            }
        }
        return result;
    }

    public static Result Scan(string romfs)
    {
        romfs = Path.GetFullPath(romfs);
        string directory = Path.Combine(romfs, "Event", "EventFlow");
        if (!Directory.Exists(directory) || ProfileRepository.IsLink(directory)) throw new InvalidDataException("Event-flow directory is missing or linked.");
        var dictionaries = HAL_RuntimeOverlay.Dictionaries(romfs);
        var candidates = new List<Candidate>(); var timelineCandidates = new List<HAL_EventTimelineScan.Candidate>();
        var failures = new List<Failure>(); int scanned = 0, scannedTimelines = 0;
        foreach (string file in Directory.EnumerateFiles(directory, "*.bfevfl.zs").Order(StringComparer.Ordinal))
        {
            if (ProfileRepository.IsLink(file)) throw new InvalidDataException("Linked event-flow resource.");
            string relative = Path.GetRelativePath(romfs, file).Replace('\\', '/');
            string name = Path.GetFileName(file)[..^".bfevfl.zs".Length];
            try
            {
                byte[] data = HAL_Zstd.Unpack(File.ReadAllBytes(file), dictionaries);
                if (IsTimelineOnly(data)) { timelineCandidates.AddRange(HAL_EventTimelineScan.Inspect(relative, data)); scannedTimelines++; continue; }
                candidates.AddRange(Inspect(relative, name, data)); scanned++;
            }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentException or OverflowException)
            {
                failures.Add(new(relative, ex.Message));
            }
        }
        return new(scanned, candidates, failures, scannedTimelines, timelineCandidates);
    }
}
