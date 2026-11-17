using System.Net;
using System.Reflection;
using RomeoOps.Installer;

namespace RomeoOps.Tests;

public class XrDownloadRecoveryTests
{
    [Theory]
    [InlineData("403")]
    [InlineData("410")]
    [InlineData("network")]
    [InlineData("timeout")]
    [InlineData("html")]
    [InlineData("checksum")]
    public async Task DownloadFailureOffersBrowserRecovery(string failure)
    {
        int prompts = 0;
        await WithPipeline(failure, () => { prompts++; return Task.FromResult<string?>(null); }, default,
            async download => await Assert.ThrowsAnyAsync<OperationCanceledException>(() => download));
        Assert.Equal(1, prompts);
    }

    [Fact]
    public async Task UserCancellationDoesNotOpenBrowser()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await WithPipeline("403", () => throw new Exception("Unexpected browser prompt"), cancellation.Token,
            async download => await Assert.ThrowsAnyAsync<OperationCanceledException>(() => download));
    }

    [Fact]
    public async Task BrowserSelectedArchiveMustMatchPinnedChecksum()
    {
        var local = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(local, "wrong release");
            await WithPipeline("403", () => Task.FromResult<string?>(local), default,
                async download => await Assert.ThrowsAsync<InvalidDataException>(() => download));
        }
        finally { File.Delete(local); }
    }

    [Fact]
    public async Task MissingBrowserCallbackExplainsHowToResume()
    {
        await WithPipeline("403", null, default, async download =>
        {
            var error = await Assert.ThrowsAsync<PipelineException>(() => download);
            Assert.Contains("Settings > xrRazom > Local", error.Message);
            Assert.Contains("Resume Install", error.Message);
        });
    }

    private static async Task WithPipeline(string failure, Func<Task<string?>>? picker, CancellationToken token,
        Func<Task<string>, Task> verify)
    {
        var root = Path.Combine(Path.GetTempPath(), "romeo-xr-test-" + Guid.NewGuid().ToString("N"));
        var layout = InstallLayout.Create(Path.Combine(root, "Anomaly"), Path.Combine(root, "GAMMA"));
        Directory.CreateDirectory(layout.Staging);
        using var log = new InstallLog(_ => { });
        using var http = new HttpClient(new FailedDownload(failure));
        var pipeline = new InstallPipeline(new PipelineCallbacks
        {
            Log = _ => { }, Progress = _ => { }, Confirm = _ => false, PickXrArchive = picker
        });
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        typeof(InstallPipeline).GetField("_log", flags)!.SetValue(pipeline, log);
        try
        {
            var download = (Task<string>)typeof(InstallPipeline).GetMethod("GetXrArchiveAsync", flags)!
                .Invoke(pipeline, new object?[] { http, layout, null, token })!;
            await verify(download);
            Assert.Empty(Directory.GetFiles(layout.Staging));
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class FailedDownload(string failure) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (failure == "network") throw new HttpRequestException("Connection failed");
            if (failure == "timeout") throw new TaskCanceledException("Request timed out");
            var status = failure == "403" ? HttpStatusCode.Forbidden :
                failure == "410" ? HttpStatusCode.Gone : HttpStatusCode.OK;
            HttpContent content = failure == "checksum" && request.RequestUri!.AbsolutePath == "/addons/start/4321"
                ? new ByteArrayContent(new byte[] { 80, 75, 3, 4, 12, 34 })
                : new StringContent(failure == "checksum" ? "<a href='/addons/start/4321'>Download</a>" : "<html>Browser verification required</html>");
            return Task.FromResult(new HttpResponseMessage(status) { RequestMessage = request, Content = content });
        }
    }
}
