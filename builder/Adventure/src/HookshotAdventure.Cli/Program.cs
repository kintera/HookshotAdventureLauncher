using System.Text.Json;
using HookshotAdventure.Core;

try
{
    if (args.Length > 0 && args[0] == "scan-skulltulas")
    {
        if (args.Length != 5 || args[1] != "--romfs" || args[3] != "--out")
            throw new ArgumentException("Usage: scan-skulltulas --romfs <game romfs> --out <report.json>");
        string path=Path.GetFullPath(args[4]);
        if (ProfileRepository.IsLink(path)) throw new InvalidDataException("Report destination is linked.");
        var skullReport=HAL_SkulltulaScan.Scan(args[2]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path,JsonSerializer.Serialize(skullReport,new JsonSerializerOptions { WriteIndented=true })+"\n");
        Console.WriteLine($"Scanned {skullReport.Maps} maps; found {skullReport.Candidates.Count} candidate placements. Variants and access need review.");
        return;
    }
    if (args.Length > 0 && args[0] == "scan-event-rewards")
    {
        if (args.Length != 5 || args[1] != "--romfs" || args[3] != "--out")
            throw new ArgumentException("Usage: scan-event-rewards --romfs <game romfs> --out <report.json>");
        string reportPath = Path.GetFullPath(args[4]);
        if (ProfileRepository.IsLink(reportPath)) throw new InvalidDataException("Report destination is linked.");
        var eventReport = HAL_EventRewardScan.Scan(args[2]);
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        File.WriteAllText(reportPath, JsonSerializer.Serialize(eventReport, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Console.WriteLine($"Scanned {eventReport.Scanned} flows and {eventReport.ScannedTimelines} timelines; found {eventReport.Candidates.Count} flow candidates and {eventReport.TimelineCandidates.Count} timeline references; {eventReport.Failures.Count} files need review. Timeline references require manual review.");
        return;
    }
    if (args.Length > 0 && args[0] == "build-astc")
    {
        if (args.Length != 3 || args[1] != "--source") throw new ArgumentException("Usage: build-astc --source <Arm astc-encoder checkout> (run from the Hookshot source root)");
        Console.WriteLine("ASTC codec ready: " + HAL_AstcCodec.Build(Environment.CurrentDirectory, args[2], Math.Max(1, Math.Min(Environment.ProcessorCount, 8))));
        return;
    }
    if (args.Length == 0 || args[0] != "scan-chests") throw new ArgumentException("Usage: build-astc --source <checkout> OR scan-chests --romfs <path> --out <directory> [--scope kokiri_deku|childhood|full_game] [--seed <seed>] [--heart-pieces] [--starting-maps]");
    string? romfs = null, output = null, seed = null; string scope = "kokiri_deku"; bool hearts = false, maps = false, legacy = false;
    for (int i = 1; i < args.Length; i++)
    {
        string Value() => ++i < args.Length ? args[i] : throw new ArgumentException("Missing option value.");
        switch (args[i])
        {
            case "--romfs": romfs = Value(); break; case "--out": output = Value(); break;
            case "--scope": scope = Value(); break; case "--seed": seed = Value(); break;
            case "--heart-pieces": hearts = true; break; case "--starting-maps": maps = true; break;
            case "--legacy-pools": legacy = true; break;
            default: throw new ArgumentException("Unknown option: " + args[i]);
        }
    }
    if (romfs is null || output is null) throw new ArgumentException("Both --romfs and --out are required.");
    string locationsPath = Path.Combine(output,"locations.json");
    if (ProfileRepository.IsLink(locationsPath)) throw new InvalidDataException("Location JSON must not be a symlink.");
    using var savedLocations = File.Exists(locationsPath) ? JsonDocument.Parse(File.ReadAllBytes(locationsPath)) : null;
    var report = HAL_ChestResearch.Scan(romfs,scope,hearts,seed,maps,legacy,savedLocations?.RootElement); Directory.CreateDirectory(output);
    var options = new JsonSerializerOptions { WriteIndented = true };
    foreach (var (name, document) in new[] { ("scan.json",report.Scan), ("catalog.json",report.Catalog), ("locations.json",report.Locations),
        ("items.json",report.Items), ("audit.json",report.Audit), ("plan.json",report.Plan) })
        if (document is not null && (name != "locations.json" || !File.Exists(locationsPath))) File.WriteAllText(Path.Combine(output,name),document.ToJsonString(options)+"\n");
    Console.WriteLine("Wrote local research reports to " + Path.GetFullPath(output));
}
catch (Exception ex) when (ex is ArgumentException or InvalidDataException or IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
{
    Console.Error.WriteLine(ex.Message); Environment.ExitCode = 1;
}
