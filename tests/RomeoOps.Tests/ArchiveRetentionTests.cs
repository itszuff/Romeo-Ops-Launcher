using RomeoOps.Installer;

namespace RomeoOps.Tests;

public class ArchiveRetentionTests
{
    [Fact]
    public async Task CopiesAndChecksExistingArchiveWithProgress()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root,"source.7z");
            var data = new byte[300000];
            Random.Shared.NextBytes(data);
            await File.WriteAllBytesAsync(source,data);
            var downloads = Path.Combine(root,"downloads");
            var phases = new List<string>();
            Assert.True(await RomeoDownloads.RetainAsync(source,downloads,"test.7z",default,(phase,_)=>phases.Add(phase)));
            Assert.Equal(data,await File.ReadAllBytesAsync(Path.Combine(downloads,"test.7z")));
            Assert.False(await RomeoDownloads.RetainAsync(source,downloads,"test.7z",default,(phase,_)=>phases.Add(phase)));
            Assert.Contains("Copying archive to downloads",phases);
            Assert.Contains("Checking source archive",phases);
            Assert.Contains("Checking retained archive",phases);
            data[0] ^= 1;
            await File.WriteAllBytesAsync(source,data);
            await Assert.ThrowsAsync<PipelineException>(()=>RomeoDownloads.RetainAsync(source,downloads,"test.7z",default));
        }
        finally { Directory.Delete(root,true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationPreservesExistingFilesAndRemovesPartialCopy(bool existing)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root,"source.7z");
            await File.WriteAllBytesAsync(source,new byte[300000]);
            var downloads = Path.Combine(root,"downloads");
            Directory.CreateDirectory(downloads);
            var target = Path.Combine(downloads,"test.7z");
            if(existing) File.Copy(source,target);
            using var cts = new CancellationTokenSource();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>RomeoDownloads.RetainAsync(source,downloads,"test.7z",cts.Token,(_,_)=>cts.Cancel()));
            Assert.Equal(existing,File.Exists(target));
            Assert.True(File.Exists(source));
            Assert.Empty(Directory.GetFiles(downloads,"*.partial"));
        }
        finally { Directory.Delete(root,true); }
    }
}
