using System.Diagnostics;
using System.Text.RegularExpressions;

namespace HookshotAdventure.Core;

public sealed class HAL_GpuLaunchMonitor
{
    private readonly Dictionary<string, string> _vendors = new(StringComparer.Ordinal);
    private bool _warned;

    public string? Observe(string line)
    {
        var card = Regex.Match(line, @"vk: device \d+: (.+), Vulkan \d+\.\d+, driver .*?, type \d+, id ([0-9a-fA-F]{4}):");
        if (card.Success)
            _vendors[card.Groups[1].Value] = card.Groups[2].Value.ToLowerInvariant();
        var selected = Regex.Match(line, @"vk: (.+), Vulkan \d+\.\d+, driver .*; mesh shaders ");
        if (_warned || !selected.Success ||
            !_vendors.TryGetValue(selected.Groups[1].Value, out var vendor) || vendor != "8086" ||
            !_vendors.ContainsValue("10de")) return null;
        _warned = true;
        return "Hookshot started on Intel graphics while an NVIDIA GPU is available. " +
            "In Hookshot, open Insert → Graphics → GPU device, select the NVIDIA card, and restart the game. " +
            "If a launch override (RX_VK_DEVICE) is set, remove it or select the NVIDIA device there too.";
    }

    // Own the process until stderr is drained; never leave a redirected pipe unread.
    public static string? TraceStatus(string line)
    {
        if (line.Contains("[DEBUG] f9 Started", StringComparison.Ordinal))
            return "F9 recording started. Press F9 again to stop.";
        if (line.Contains("[DEBUG] f9 Stopped", StringComparison.Ordinal))
            return "F9 recording stopped. Check the game folder's logs directory for the capture.";
        if (line.Contains("gameplay trace: could not create timestamped capture", StringComparison.Ordinal))
            return "F9 could not create a capture file in the game folder's logs directory.";
        const string marker = "gameplay trace: capture file ";
        int index = line.IndexOf(marker, StringComparison.Ordinal);
        return index >= 0 ? "F9 capture: " + line[(index + marker.Length)..] : null;
    }

    public static async Task WatchAsync(Process process, string logPath, Action<string> warn, Action<string>? trace = null)
    {
        using (process)
        {
            StreamWriter? log = null;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                log = new StreamWriter(new FileStream(logPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            var monitor = new HAL_GpuLaunchMonitor();
            try
            {
                while (await process.StandardError.ReadLineAsync() is { } line)
                {
                    if (line.Contains("gameplay trace:", StringComparison.Ordinal))
                        Console.WriteLine(line);
                    var traceStatus = TraceStatus(line);
                    if (traceStatus is not null)
                    {
                        Console.WriteLine("Hookshot " + traceStatus);
                        trace?.Invoke(traceStatus);
                    }
                    if (log is not null)
                    {
                        try { await log.WriteLineAsync(line); }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        { log.Dispose(); log = null; }
                    }
                    var warning = monitor.Observe(line);
                    if (warning is not null) warn(warning);
                }
            }
            catch (IOException) { }
            finally { log?.Dispose(); }
        }
    }
}
