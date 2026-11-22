using System.Net;
using System.Text;
using HookshotAdventure.Core;

static class HAL_HookshotDownloadFixture
{
    private const string Release = """
        {"assets":[
          {"name":"Hookshot-v3.0.2-x86_64.AppImage","size":4,"browser_download_url":"https://git.notrexed.dev/Hookshot/Hookshot/releases/download/v3.0.2/Hookshot-v3.0.2-x86_64.AppImage"},
          {"name":"HookshotBuilder.exe","size":4,"browser_download_url":"https://git.notrexed.dev/Hookshot/Hookshot/releases/download/v3.0.2/HookshotBuilder.exe"}
        ]}
        """;

    private sealed class Handler(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = request.RequestUri!.AbsolutePath.Contains("/api/")
                ? new StringContent(Release, Encoding.UTF8, "application/json") : new ByteArrayContent(body) });
    }

    public static void Check(string root)
    {
        var linux = HAL_HookshotDownload.SelectAsset(Release, false);
        var windows = HAL_HookshotDownload.SelectAsset(Release, true);
        AssertTest.True(linux.Name.EndsWith(".AppImage") && windows.Name == "HookshotBuilder.exe",
            "official release selection chooses the correct platform builder");
        bool rejected = false;
        try { HAL_HookshotDownload.SelectAsset(Release.Replace("git.notrexed.dev", "example.invalid"), false); }
        catch (InvalidDataException) { rejected = true; }
        AssertTest.True(rejected, "builder download rejects a release asset from an unexpected host");
        string folder = Path.Combine(root, "builder-download"); Directory.CreateDirectory(folder);
        string installed = Path.Combine(folder, "installed"); Directory.CreateDirectory(Path.Combine(installed, "romfs"));
        File.WriteAllText(HookshotGame.ResolveExecutable(installed), "fixture");
        File.WriteAllText(HookshotGame.ResolveRenderer(installed), "fixture");
        File.WriteAllText(Path.Combine(installed, "image.bin"), "fixture");
        AssertTest.True(HAL_HookshotDownload.ShouldOffer(null, Path.Combine(folder, "missing"), null) &&
            !HAL_HookshotDownload.ShouldOffer(null, null, installed),
            "download offer is shown only when no selected or known Hookshot installation exists");
        string destination = Path.Combine(folder, "builder");
        using (var client = new HttpClient(new Handler([1, 2, 3, 4])))
        {
            var found = HAL_HookshotDownload.LatestAsync(client, false).GetAwaiter().GetResult();
            HAL_HookshotDownload.DownloadAsync(client, found, destination).GetAwaiter().GetResult();
            AssertTest.True(File.ReadAllBytes(destination).SequenceEqual(new byte[] { 1, 2, 3, 4 }),
                "builder download saves the complete selected official asset");
            rejected = false;
            try { HAL_HookshotDownload.DownloadAsync(client, found, destination).GetAwaiter().GetResult(); }
            catch (IOException) { rejected = true; }
            AssertTest.True(rejected && File.ReadAllBytes(destination).SequenceEqual(new byte[] { 1, 2, 3, 4 }),
                "builder download never overwrites an existing file");
        }
        using (var client = new HttpClient(new Handler([1, 2])))
        {
            string incomplete = Path.Combine(folder, "incomplete"); rejected = false;
            try { HAL_HookshotDownload.DownloadAsync(client, linux, incomplete).GetAwaiter().GetResult(); }
            catch (InvalidDataException) { rejected = true; }
            AssertTest.True(rejected && !File.Exists(incomplete) && Directory.GetFiles(folder, "*.partial").Length == 0,
                "incomplete builder downloads leave no output or partial file");
        }
        using (var client = new HttpClient(new Handler([1, 2, 3, 4])))
        using (var cancellation = new CancellationTokenSource())
        {
            string cancelled = Path.Combine(folder, "cancelled"); cancellation.Cancel(); rejected = false;
            try { HAL_HookshotDownload.DownloadAsync(client, linux, cancelled, cancellationToken: cancellation.Token).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { rejected = true; }
            AssertTest.True(rejected && !File.Exists(cancelled) && Directory.GetFiles(folder, "*.partial").Length == 0,
                "cancelled builder downloads leave no output or partial file");
        }
    }
}
