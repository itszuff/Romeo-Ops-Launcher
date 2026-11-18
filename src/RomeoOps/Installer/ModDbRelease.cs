using System.Net;
using System.Text.RegularExpressions;

namespace RomeoOps.Installer;

public static class ModDbRelease
{
    private static readonly HttpClient Http = new() { Timeout=TimeSpan.FromMinutes(3) };
    private static readonly Regex Links = new("""(?:href|content|url)\s*=\s*["'](?<url>[^"'<>]+)["']""",RegexOptions.IgnoreCase);
    private static readonly Regex Md5 = new(@"MD5\s+Hash.{0,400}?\b([0-9a-f]{32})\b",RegexOptions.IgnoreCase|RegexOptions.Singleline);

    public static async Task<string> Check(CoopAddon addon,InstalledAddon installed,CancellationToken token,HttpClient? client=null)
    {
        using var page=await Get(client ?? Http,addon.PageUrl,null,token);
        var html=await page.Content.ReadAsStringAsync(token);
        var match=Md5.Match(html);
        if(!match.Success) throw new InvalidDataException("Release checksum unavailable.");
        if(installed.Md5!=null)
            return installed.Md5.Equals(match.Groups[1].Value,StringComparison.OrdinalIgnoreCase) ? "Matches release" : "Different release available";

        // Pre-2.4 installations need the published ZIP to compare their SHA-256 or payload.
        string temp=Path.Combine(Path.GetTempPath(),"romeo-check-"+Guid.NewGuid().ToString("N")+".zip");
        try
        {
            await Fetch(addon,temp,_=>throw new IOException("Browser download required."),token,client);
            return installed.CompareZip(addon,temp);
        }
        finally { File.Delete(temp); }
    }
    public static async Task Fetch(CoopAddon addon,string destination,Func<CoopAddon,Task<string?>> fallback,CancellationToken token,HttpClient? client=null)
    {
        string? checksum=null;
        try
        {
            using var page=await Get(client ?? Http,addon.PageUrl,null,token);
            var html=await page.Content.ReadAsStringAsync(token);
            var match=Md5.Match(html);
            if (!match.Success) throw new InvalidDataException("Could not read the current release checksum.");
            checksum=match.Groups[1].Value;
            await DownloadFromPage(client ?? Http, addon.PageUrl, html, destination, checksum, token, null);
        }
        catch(Exception ex) when(!token.IsCancellationRequested && ex is HttpRequestException or IOException or InvalidDataException or TaskCanceledException)
        {
            token.ThrowIfCancellationRequested();
            var local=await fallback(addon);
            if(local==null) throw new OperationCanceledException("Download cancelled.");
            token.ThrowIfCancellationRequested();
            File.Copy(local,destination,true);
            if(checksum!=null) FileHasher.AssertMatch(destination,"MD5",checksum);
        }
    }
    public static async Task FetchPinned(HttpClient client, string pageUrl, string destination, string expectedMd5,
        CancellationToken token, Action<TransferSnapshot>? progress = null)
    {
        var partial = destination + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            for (int attempt = 0; ; attempt++)
            {
                using var page = await Get(client, pageUrl, null, token);
                var html = await page.Content.ReadAsStringAsync(token);
                try
                {
                    await DownloadFromPage(client, pageUrl, html, partial, expectedMd5, token, progress);
                    token.ThrowIfCancellationRequested();
                    File.Move(partial, destination, true);
                    return;
                }
                catch (HttpRequestException ex) when (attempt == 0 && ex.StatusCode == HttpStatusCode.Gone)
                {
                    // ModDB issues mirror links from the start page. Resolve a new link once.
                }
            }
        }
        finally { File.Delete(partial); }
    }

    private static async Task DownloadFromPage(HttpClient client, string pageUrl, string html, string destination,
        string checksum, CancellationToken token, Action<TransferSnapshot>? progress)
    {
        var url=FindLink(html,new Uri(pageUrl),true) ?? throw new InvalidDataException("Download link unavailable.");
        var referer=pageUrl;
        var alternatives = new Queue<Uri>();
        bool listedMirrors = false;
        for(int hop=0;hop<5;hop++)
        {
            HttpResponseMessage response;
            try { response=await Get(client,url.AbsoluteUri,referer,token); }
            catch (HttpRequestException ex) when (alternatives.Count > 0 &&
                ex.StatusCode is HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), token);
                url = alternatives.Dequeue();
                continue;
            }
            using var ownedResponse = response;
            var final=response.RequestMessage!.RequestUri!;
            if(!Trusted(final)) throw new InvalidDataException("Unrecognized mirror host.");
            using var input=await response.Content.ReadAsStreamAsync(token);
            var header=new byte[4];
            await input.ReadExactlyAsync(header,token);
            if (header.SequenceEqual(new byte[]{80,75,3,4}))
            {
                await using(var output=File.Create(destination))
                {
                    await output.WriteAsync(header,token);
                    await Downloader.CopyWithProgress(input, output, response.Content.Headers.ContentLength, header.Length, progress, token);
                }
                FileHasher.AssertMatch(destination,"MD5",checksum);
                return;
            }
            using var reader=new StreamReader(input);
            var markup=System.Text.Encoding.ASCII.GetString(header)+await reader.ReadToEndAsync(token);
            // Ask ModDB for its current mirror choices, never persist expiring mirror tokens.
            var all = FindAllMirrors(markup, final);
            if (!listedMirrors && all != null)
            {
                listedMirrors = true;
                url = all;
                referer = final.AbsoluteUri;
                continue;
            }
            var mirrors = FindMirrors(markup, final);
            if (mirrors.Length == 0) throw new InvalidDataException("ModDB requires a browser to choose a mirror.");
            // Spread downloads across advertised mirrors rather than always choosing the first.
            if (listedMirrors && mirrors.Length > 1) Random.Shared.Shuffle(mirrors);
            url = mirrors[0];
            alternatives = new Queue<Uri>(mirrors.Skip(1).Take(2));
            referer=final.AbsoluteUri;
        }
        throw new IOException("ModDB returned too many mirror pages.");
    }
    private static async Task<HttpResponseMessage> Get(HttpClient client,string url,string? referer,CancellationToken token)
    {
        using var request=new HttpRequestMessage(HttpMethod.Get,url);
        request.Headers.UserAgent.ParseAdd("RomeoOps/2.5.0");
        request.Headers.CacheControl=new System.Net.Http.Headers.CacheControlHeaderValue {NoCache=true};
        if(referer!=null) request.Headers.Referrer=new Uri(referer);
        var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);
        if (client==Http && response.StatusCode==HttpStatusCode.Forbidden)
        {
            response.Dispose();
            return await GetWithWindowsCurl(url,referer,token);
        }
        try {response.EnsureSuccessStatusCode();return response;} catch {response.Dispose();throw;}
    }
    private static async Task<HttpResponseMessage> GetWithWindowsCurl(string url,string? referer,CancellationToken token)
    {
        var binary=Path.Combine(Environment.SystemDirectory,"curl.exe");
        if(!File.Exists(binary)) throw new HttpRequestException("ModDB requires a browser download.");
        var start=new System.Diagnostics.ProcessStartInfo(binary) {UseShellExecute=false,CreateNoWindow=true,
            RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var arg in new[]{"--disable","--fail","--silent","--location","--proto","=https","--proto-redir","=https",
            "--max-time","90","--user-agent","RomeoOps/2.5.0","--write-out","%{stderr}%{url_effective}"}) start.ArgumentList.Add(arg);
        if(referer!=null){ start.ArgumentList.Add("--referer"); start.ArgumentList.Add(referer); }
        start.ArgumentList.Add(url);
        using var process=System.Diagnostics.Process.Start(start) ?? throw new IOException("Could not start the Windows download client.");
        using var cancel=token.Register(()=>{try{process.Kill(true);}catch(InvalidOperationException){}});
        var spool=new FileStream(Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N")+".tmp"),FileMode.CreateNew,
            FileAccess.ReadWrite,FileShare.None,65536,FileOptions.DeleteOnClose|FileOptions.Asynchronous);
        try
        {
            var endpoint=process.StandardError.ReadToEndAsync(token);
            await process.StandardOutput.BaseStream.CopyToAsync(spool,token);
            await process.WaitForExitAsync(token);
            var final=await endpoint;
            if(process.ExitCode!=0 || !Uri.TryCreate(final,UriKind.Absolute,out var uri) || !Trusted(uri))
                throw new HttpRequestException("ModDB requires a browser download.");
            spool.Position=0;
            return new HttpResponseMessage(HttpStatusCode.OK){Content=new StreamContent(spool),RequestMessage=new HttpRequestMessage(HttpMethod.Get,uri)};
        }
        catch {spool.Dispose();throw;}
    }
    private static bool Trusted(Uri uri) => uri.Scheme=="https" && new[]{"moddb.com","dbolical.com","modcdn.io"}.Any(h=>uri.Host==h || uri.Host.EndsWith("."+h,StringComparison.OrdinalIgnoreCase));
    public static Uri? FindAllMirrors(string html, Uri page) => ParsedLinks(html, page)
        .FirstOrDefault(link => Regex.IsMatch(link.AbsolutePath, @"^/(addons|downloads)/start/\d+/all/?$"));

    public static Uri[] FindMirrors(string html, Uri page) => ParsedLinks(html, page)
        .Where(link => Regex.IsMatch(link.AbsolutePath, @"^/(addons|downloads)/mirror/|^/dl/|\.zip$", RegexOptions.IgnoreCase))
        .Distinct().ToArray();

    private static IEnumerable<Uri> ParsedLinks(string html, Uri page)
    {
        foreach (Match match in Links.Matches(html))
        {
            string raw = WebUtility.HtmlDecode(match.Groups["url"].Value);
            if (raw.Contains("url=", StringComparison.OrdinalIgnoreCase)) raw = raw[(raw.IndexOf("url=", StringComparison.OrdinalIgnoreCase)+4)..].Trim();
            if (Uri.TryCreate(page, raw, out var link) && Trusted(link)) yield return link;
        }
    }
    public static Uri? FindLink(string html, Uri page,bool start)
    {
        foreach(Match match in Links.Matches(html))
        {
            string raw=WebUtility.HtmlDecode(match.Groups["url"].Value);
            if(raw.Contains("url=",StringComparison.OrdinalIgnoreCase)) raw=raw[(raw.IndexOf("url=",StringComparison.OrdinalIgnoreCase)+4)..].Trim();
            if(!Uri.TryCreate(page,raw,out var link) || !Trusted(link)) continue;
            if(start ? Regex.IsMatch(link.AbsolutePath,@"^/(addons|downloads)/start/\d+") :
                Regex.IsMatch(link.AbsolutePath,@"^/(addons|downloads)/mirror/|^/dl/|\.zip$",RegexOptions.IgnoreCase))
                return link;
        }
        return null;
    }
}

