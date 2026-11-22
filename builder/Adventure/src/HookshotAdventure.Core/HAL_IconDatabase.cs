using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using SkiaSharp;

namespace HookshotAdventure.Core;

public static class HAL_IconDatabase
{
    public static string CacheDirectory
    {
        get
        {
            var cache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
            if (string.IsNullOrWhiteSpace(cache)) cache = OperatingSystem.IsWindows()
                ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
            return Path.Combine(cache, "hookshot", "save-assets");
        }
    }

    public static Task<string> BuildAsync(string root, string game) => Task.Run(() => Build(root, game, CacheDirectory));

    public static string Build(string root, string game, string cache)
    {
        if (!HookshotGame.IsInstalled(game)) throw new InvalidOperationException("Select an installed Hookshot game before building icons.");
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "builder", "Adventure", "resources", "icon_manifest.json")));
        var entries = manifest.RootElement.GetProperty("icons").EnumerateObject()
            .Where(x => x.Value.TryGetProperty("verified", out var verified) && verified.ValueKind == JsonValueKind.True).ToArray();
        if (entries.Length == 0) throw new InvalidDataException("No verified icon mappings are available.");
        string romfs = Path.Combine(Path.GetFullPath(game), "romfs"); var dictionaries = HAL_RuntimeOverlay.Dictionaries(romfs);
        string? codec = null;
        foreach (var entry in entries)
        {
            if (entry.Name.Length is < 1 or > 96 || entry.Name.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '_' or '-')))
                throw new InvalidDataException("Invalid icon role.");
            string archive = entry.Value.GetProperty("archive").GetString() ?? "";
            if (Path.IsPathRooted(archive) || archive.Contains('\\') || archive.Split('/').Any(p => p is "" or "." or ".."))
                throw new InvalidDataException("Invalid icon archive path.");
            string current = romfs;
            foreach (string part in archive.Split('/')) { current = Path.Combine(current, part); if (ProfileRepository.IsLink(current)) throw new InvalidDataException("Linked icon archive."); }
        }
        Directory.CreateDirectory(cache); string stage = Path.Combine(cache, ".building-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage);
        try
        {
            var good = new List<string>(); var failures = new Dictionary<string,string>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                try
                {
                    string archive = entry.Value.GetProperty("archive").GetString()!, name = entry.Value.GetProperty("texture").GetString()!;
                    byte[] raw = HAL_Zstd.Unpack(File.ReadAllBytes(Path.Combine(romfs, archive.Replace('/', Path.DirectorySeparatorChar))), dictionaries);
                    var texture = HAL_IconTexture.Read(raw, name); byte[] rgba;
                    if ((texture.Format >> 8) == 0x2d)
                    {
                        codec ??= HAL_AstcCodec.Find(root);
                        rgba = HAL_AstcCodec.Decode(codec, texture.Blocks, texture.Width, texture.Height, 4, 4);
                    }
                    else
                    {
                        byte[] gray = HAL_IconTexture.DecodeBc4(texture); rgba = new byte[checked(gray.Length * 4)];
                        bool mask = entry.Value.TryGetProperty("alpha_mode", out var mode) && mode.GetString() == "mask";
                        for (int i = 0; i < gray.Length; i++)
                        {
                            rgba[i*4] = rgba[i*4+1] = rgba[i*4+2] = mask ? (byte)255 : gray[i]; rgba[i*4+3] = mask ? gray[i] : (byte)255;
                        }
                    }
                    int x = 0, y = 0, width = texture.Width, height = texture.Height;
                    if (entry.Value.TryGetProperty("crop", out var crop) && crop.ValueKind == JsonValueKind.Array)
                    {
                        var values = crop.EnumerateArray().Select(v => v.GetInt32()).ToArray();
                        if (values.Length != 4 || values[0] < 0 || values[1] < 0 || values[2] <= 0 || values[3] <= 0 ||
                            (long)values[0]+values[2] > width || (long)values[1]+values[3] > height) throw new InvalidDataException("Invalid icon crop.");
                        (x,y,width,height) = (values[0],values[1],values[2],values[3]);
                    }
                    using var bitmap = new SKBitmap(new SKImageInfo(width,height,SKColorType.Rgba8888,SKAlphaType.Unpremul));
                    var cropped = new byte[checked(width*height*4)];
                    for (int row=0; row<height; row++) Array.Copy(rgba, ((y+row)*texture.Width+x)*4, cropped, row*width*4, width*4);
                    Marshal.Copy(cropped,0,bitmap.GetPixels(),cropped.Length);
                    using var image = SKImage.FromBitmap(bitmap); using var png = image.Encode(SKEncodedImageFormat.Png,100);
                    using (var output = File.Create(Path.Combine(stage,entry.Name+".png"))) png.SaveTo(output);
                    good.Add(entry.Name);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or ArgumentException)
                {
                    string message = ex.GetType().Name + ": " + ex.Message; failures[entry.Name] = message[..Math.Min(message.Length,800)];
                }
            }
            if (good.Count == 0) throw new InvalidDataException("No icons decoded: " + failures.Values.FirstOrDefault());
            var index = new JsonObject { ["cache_schema"] = 2, ["source"] = "local game installation; never distribute",
                ["roles"] = JsonSerializer.SerializeToNode(good), ["requested_roles"] = JsonSerializer.SerializeToNode(entries.Select(x => x.Name).ToArray()),
                ["failures"] = JsonSerializer.SerializeToNode(failures) };
            File.WriteAllText(Path.Combine(stage,"index.json"), index.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
            string target = Path.Combine(cache,"adventure-" + Guid.NewGuid().ToString("N")); Directory.Move(stage,target); return target;
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage,true); }
    }
}
