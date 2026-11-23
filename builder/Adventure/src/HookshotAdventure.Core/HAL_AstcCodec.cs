using System.Diagnostics;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace HookshotAdventure.Core;

/// <summary>Runs the local ASTC codec on temporary images and validates dimensions.</summary>
public static class HAL_AstcCodec
{
    public static string Find(string root, string? applicationDirectory = null, bool includeCache = true)
    {
        string executable = OperatingSystem.IsWindows() ? "astcenc-sse2.exe" : "astcenc-sse2";
        var candidates = new List<string?> { Environment.GetEnvironmentVariable("HOOKSHOT_ASTCENC"),
            Path.Combine(applicationDirectory ?? AppContext.BaseDirectory, executable), Path.Combine(root, "work", "tools", "astcenc", executable) };
        if (includeCache) candidates.Add(Path.Combine(HAL_AstcDownload.CacheDirectory, executable));
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            foreach (string name in OperatingSystem.IsWindows() ? new[] { "astcenc.exe", "astcenc-sse2.exe" } : new[] { "astcenc", "astcenc-sse2" })
                candidates.Add(Path.Combine(directory, name));
        return candidates.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path)) is string found
            ? Path.GetFullPath(found) : throw new InvalidOperationException("ASTC codec is missing. Build it with the Adventure C# CLI build-astc command or set HOOKSHOT_ASTCENC.");
    }
    public static string Build(string root, string source, int jobs)
    {
        source = Path.GetFullPath(source);
        if (jobs < 1 || !File.Exists(Path.Combine(source, "Source", "astcenc.h")))
            throw new ArgumentException("Choose an Arm astc-encoder source checkout and a positive job count.");
        string destination = Path.Combine(Path.GetFullPath(root), "work", "tools", "astcenc");
        Directory.CreateDirectory(destination);
        string build = Path.Combine(destination, ".build-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(build);
        void CMake(params string[] arguments)
        {
            var info = new ProcessStartInfo("cmake") { UseShellExecute = false };
            foreach (string argument in arguments) info.ArgumentList.Add(argument);
            using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start CMake.");
            process.WaitForExit();
            if (process.ExitCode != 0) throw new InvalidOperationException($"CMake failed with exit code {process.ExitCode}.");
        }
        try
        {
            CMake("-S", source, "-B", build, "-DCMAKE_BUILD_TYPE=Release", "-DASTCENC_ISA_SSE2=ON", "-DASTCENC_UNITTEST=OFF", "-DASTCENC_WERROR=OFF");
            CMake("--build", build, "--config", "Release", "--parallel", jobs.ToString(System.Globalization.CultureInfo.InvariantCulture));
            string name = OperatingSystem.IsWindows() ? "astcenc-sse2.exe" : "astcenc-sse2";
            var binaries = Directory.GetFiles(build, name, SearchOption.AllDirectories);
            if (binaries.Length != 1) throw new InvalidDataException("Expected one built ASTC codec executable.");
            string output = Path.Combine(destination, name);
            File.Copy(binaries[0], output, true);
            File.Copy(Path.Combine(source, "LICENSE.txt"), Path.Combine(destination, "LICENSE.txt"), true);
            return output;
        }
        finally { Directory.Delete(build, true); }
    }
    private static void Run(string codec, params string[] args)
    {
        var info = new ProcessStartInfo(codec) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (string arg in args) info.ArgumentList.Add(arg);
        foreach (string arg in new[] { "-silent", "-j", "4" }) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start ASTC codec.");
        var error = process.StandardError.ReadToEndAsync(); var output = process.StandardOutput.ReadToEndAsync();
        if (!process.WaitForExit(180000)) { process.Kill(true); process.WaitForExit(); throw new InvalidOperationException("ASTC codec timed out."); }
        string stderr = error.GetAwaiter().GetResult(); _ = output.GetAwaiter().GetResult();
        if (process.ExitCode != 0) throw new InvalidDataException("ASTC codec failed: " + stderr[^Math.Min(stderr.Length, 1000)..]);
    }
    public static byte[] Header(int width, int height, int blockWidth, int blockHeight)
    {
        if (width is < 1 or > 0xffffff || height is < 1 or > 0xffffff || blockWidth is < 1 or > 255 || blockHeight is < 1 or > 255)
            throw new ArgumentException("Invalid ASTC dimensions.");
        var header = new byte[] { 0x13,0xab,0xa1,0x5c,(byte)blockWidth,(byte)blockHeight,1,0,0,0,0,0,0,1,0,0 };
        for (int i = 0; i < 3; i++) { header[7+i] = (byte)(width >> (8*i)); header[10+i] = (byte)(height >> (8*i)); }
        return header;
    }
    public static byte[] Decode(string codec, byte[] blocks, int width, int height, int blockWidth, int blockHeight)
    {
        string root = Path.Combine(Path.GetTempPath(), "hookshot-astc-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            string astc = Path.Combine(root, "input.astc"), png = Path.Combine(root, "decoded.png");
            File.WriteAllBytes(astc, Header(width, height, blockWidth, blockHeight).Concat(blocks).ToArray());
            Run(codec, "-ds", astc, png);
            using var imageCodec = SKCodec.Create(png) ?? throw new InvalidDataException("ASTC codec returned an invalid image.");
            if (imageCodec.Info.Width != width || imageCodec.Info.Height != height) throw new InvalidDataException("ASTC codec changed image dimensions.");
            var imageInfo = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            using var bitmap = new SKBitmap(imageInfo);
            if (imageCodec.GetPixels(imageInfo, bitmap.GetPixels()) != SKCodecResult.Success) throw new InvalidDataException("Could not convert ASTC pixels.");
            var rgba = new byte[checked(width * height * 4)]; Marshal.Copy(bitmap.GetPixels(), rgba, 0, rgba.Length); return rgba;
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    public static byte[] Encode(string codec, byte[] rgba, int width, int height, int blockWidth, int blockHeight)
    {
        if ((long)width * height * 4 != rgba.Length) throw new InvalidDataException("Invalid ASTC input image.");
        string root = Path.Combine(Path.GetTempPath(), "hookshot-astc-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            string png = Path.Combine(root, "input.png"), astc = Path.Combine(root, "encoded.astc");
            using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
            Marshal.Copy(rgba, 0, bitmap.GetPixels(), rgba.Length);
            using (var image = SKImage.FromBitmap(bitmap)) using (var encoded = image.Encode(SKEncodedImageFormat.Png, 100))
            using (var file = File.Create(png)) encoded.SaveTo(file);
            Run(codec, "-cs", png, astc, $"{blockWidth}x{blockHeight}", "-medium", "-cw", "1", "1", "1", "4");
            byte[] output = File.ReadAllBytes(astc); byte[] header = Header(width, height, blockWidth, blockHeight);
            if (output.Length < header.Length || !output.AsSpan(0, header.Length).SequenceEqual(header)) throw new InvalidDataException("ASTC codec changed texture dimensions.");
            return output[header.Length..];
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
