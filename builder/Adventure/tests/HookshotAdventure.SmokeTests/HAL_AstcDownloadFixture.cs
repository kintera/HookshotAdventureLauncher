using System.Net;
using System.Runtime.InteropServices;
using HookshotAdventure.Core;

static class HAL_AstcDownloadFixture
{
    private sealed class Handler(byte[] archive, byte[] license) : HttpMessageHandler
    {
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            byte[] data = request.RequestUri!.Host == "raw.githubusercontent.com" ? license : archive;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) });
        }
    }

    public static void Check(string root)
    {
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64 || !(OperatingSystem.IsLinux() || OperatingSystem.IsWindows())) return;
        string dir = Path.Combine(root, "astc-download"), app = Path.Combine(dir, "app"), cache = Path.Combine(dir, "cache");
        Directory.CreateDirectory(app);
        string name = OperatingSystem.IsWindows() ? "astcenc-sse2.exe" : "astcenc-sse2";
        string local = Path.Combine(app, name);
        var oldPath = Environment.GetEnvironmentVariable("PATH");
        var oldOverride = Environment.GetEnvironmentVariable("HOOKSHOT_ASTCENC");
        try
        {
            Environment.SetEnvironmentVariable("PATH", "");
            Environment.SetEnvironmentVariable("HOOKSHOT_ASTCENC", null);
            var bad = new Handler([1, 2, 3], []);
            using var badClient = new HttpClient(bad);
            File.WriteAllText(local, "local codec");
            AssertTest.True(HAL_AstcDownload.EnsureAsync(dir, badClient, cache, app).GetAwaiter().GetResult() == local && bad.Requests == 0,
                "existing ASTC codec is used without downloading");
            File.Delete(local);
            bool rejected = false;
            try { HAL_AstcDownload.EnsureAsync(dir, badClient, cache, app).GetAwaiter().GetResult(); }
            catch (InvalidDataException) { rejected = true; }
            AssertTest.True(rejected && !File.Exists(Path.Combine(cache, name)), "unverified ASTC downloads are rejected without installing a codec");

            string? archivePath = Environment.GetEnvironmentVariable("HAL_TEST_ASTC_ARCHIVE");
            string? licensePath = Environment.GetEnvironmentVariable("HAL_TEST_ASTC_LICENSE");
            if (!string.IsNullOrEmpty(archivePath) && !string.IsNullOrEmpty(licensePath))
            {
                var handler = new Handler(File.ReadAllBytes(archivePath), File.ReadAllBytes(licensePath));
                using var client = new HttpClient(handler);
                string installed = HAL_AstcDownload.EnsureAsync(dir, client, cache, app).GetAwaiter().GetResult();
                AssertTest.True(File.Exists(installed) && File.Exists(Path.Combine(cache, "LICENSE.txt")) && handler.Requests == 2,
                    "verified ASTC archive installs its codec and license");
                AssertTest.True(HAL_AstcDownload.EnsureAsync(dir, client, cache, app).GetAwaiter().GetResult() == installed && handler.Requests == 2,
                    "installed ASTC codec is reused without another download");
                byte[] rgba = Enumerable.Range(0, 16).SelectMany(_ => new byte[] { 255, 0, 0, 255 }).ToArray();
                var decoded = HAL_AstcCodec.Decode(installed, HAL_AstcCodec.Encode(installed, rgba, 4, 4, 4, 4), 4, 4, 4, 4);
                AssertTest.True(decoded.Length == 64 && decoded[3] == 255, "downloaded ASTC codec runs and round-trips a synthetic image");
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", oldPath);
            Environment.SetEnvironmentVariable("HOOKSHOT_ASTCENC", oldOverride);
        }
    }
}
