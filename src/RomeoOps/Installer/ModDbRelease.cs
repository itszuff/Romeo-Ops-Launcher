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
            var url=FindLink(html,new Uri(addon.PageUrl),true) ?? throw new InvalidDataException("Download link unavailable.");
            var referer=addon.PageUrl;
            for(int hop=0;hop<5;hop++)
            {
                using var response=await Get(client ?? Http,url.AbsoluteUri,referer,token);
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
                        await input.CopyToAsync(output,token);
                    }
                    FileHasher.AssertMatch(destination,"MD5",checksum);
                    return;
                }
                using var reader=new StreamReader(input);
                var markup=System.Text.Encoding.ASCII.GetString(header)+await reader.ReadToEndAsync(token);
                url=FindLink(markup,final,false) ?? throw new InvalidDataException("ModDB requires a browser to choose a mirror.");
                referer=final.AbsoluteUri;
            }
            throw new IOException("ModDB returned too many mirror pages.");
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

