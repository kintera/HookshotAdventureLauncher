using System.Diagnostics;

namespace HookshotAdventure.Core;

public static class HAL_VulkanReadiness
{
    // Probe only on launch, never while rendering profile cards. vulkaninfo is optional;
    // the installed renderer is checked separately even when this diagnostic tool is absent.
    public static Task<string?> CheckAsync()
    {
        var name = OperatingSystem.IsWindows() ? "vulkaninfo.exe" : "vulkaninfo";
        var executable = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.Combine(path, name)).FirstOrDefault(File.Exists);
        if (executable is null) return Task.FromResult<string?>(null);
        var info = new ProcessStartInfo(executable);
        info.ArgumentList.Add("--summary");
        return ProbeAsync(info, TimeSpan.FromSeconds(15));
    }

    public static async Task<string?> ProbeAsync(ProcessStartInfo info, TimeSpan timeout)
    {
        info.UseShellExecute = false;
        info.RedirectStandardOutput = info.RedirectStandardError = true;
        info.CreateNoWindow = true;
        try
        {
            using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start Vulkan diagnostics.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var deadline = new CancellationTokenSource(timeout);
            try { await process.WaitForExitAsync(deadline.Token); }
            catch (OperationCanceledException)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                await process.WaitForExitAsync();
                await Task.WhenAll(stdout, stderr);
                return "Vulkan driver check timed out. Check your GPU driver before launching Hookshot.";
            }
            var output = (await stderr + "\n" + await stdout).Trim();
            if (process.ExitCode == 0) return null;
            if (output.Length > 1200) output = output[..1200];
            return "Vulkan cannot initialize a usable GPU. If the graphics driver was just updated, restart the PC and try again. " +
                "Otherwise repair the system GPU driver. Rebuilding Hookshot does not install the system driver.\n" + output;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return "Could not check the Vulkan driver: " + ex.Message;
        }
    }
}
