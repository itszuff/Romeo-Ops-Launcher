using System.Net;
using System.Security.Cryptography;
using RomeoOps.Installer;

namespace RomeoOps.Tests;

public class ModDbMirrorTests
{
    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.Forbidden, false)]
    [InlineData(HttpStatusCode.TooManyRequests, false)]
    public async Task AlternateMirrorsRecoverServerErrorsButDoNotRetryBlocks(HttpStatusCode failure, bool succeeds)
    {
        var handler = new Mirrors(failure);
        using var http = new HttpClient(handler);
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "xr.zip");
        try
        {
            var download = ModDbRelease.FetchPinned(http, Pins.XrReferer, path,
                Convert.ToHexString(MD5.HashData(Mirrors.Payload)), default);
            if (succeeds)
            {
                await download;
                Assert.Equal(Mirrors.Payload, File.ReadAllBytes(path));
                Assert.Equal(2, handler.MirrorRequests);
            }
            else
            {
                await Assert.ThrowsAsync<HttpRequestException>(() => download);
                Assert.Equal(1, handler.MirrorRequests);
                Assert.False(File.Exists(path));
            }
            Assert.Empty(Directory.GetFiles(folder, "*.partial"));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void MirrorParsingRejectsUntrustedHostsAndDecodesQueries()
    {
        var links = ModDbRelease.FindMirrors("""
            <a href='https://evil.example/downloads/mirror/1'>bad</a>
            <a href='/downloads/mirror/1/131/token?a=1&amp;b=2'>good</a>
            <a href='http://www.moddb.com/downloads/mirror/1'>insecure</a>
            """, new Uri(Pins.XrReferer));
        Assert.Single(links);
        Assert.Equal("?a=1&b=2", links[0].Query);
    }

    private sealed class Mirrors(HttpStatusCode failure) : HttpMessageHandler
    {
        public static readonly byte[] Payload = [80, 75, 3, 4, 42, 43];
        public int MirrorRequests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var path = request.RequestUri!.AbsolutePath;
            var status = HttpStatusCode.OK;
            HttpContent content;
            if (path.Contains("/mirror/"))
            {
                if (++MirrorRequests == 1) status = failure;
                content = new ByteArrayContent(Payload);
            }
            else content = new StringContent(path.EndsWith("/all")
                ? "<a href='/downloads/mirror/1/131/token'>A</a><a href='/downloads/mirror/1/122/token'>B</a>"
                : path.Contains("/start/") ? "<a href='/downloads/start/1/all'>mirrors</a>"
                : "<a href='/downloads/start/1'>Download</a>");
            return Task.FromResult(new HttpResponseMessage(status) { Content = content, RequestMessage = request });
        }
    }
}
