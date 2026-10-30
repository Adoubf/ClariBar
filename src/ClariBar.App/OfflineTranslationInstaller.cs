using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using ClariBar.Core;
using L=ClariBar.Core.Localizer;

namespace ClariBar.App;

internal sealed record TranslationArtifact(string Name,string Url,string Sha256,string Destination,long Bytes);
internal static class OfflineTranslationInstaller
{
    private static readonly SemaphoreSlim gate=new(1,1);
    internal static async Task InstallAsync(Action<string>? progress,CancellationToken cancellation=default)
    {
        if(!await gate.WaitAsync(0,cancellation))throw new InvalidOperationException(L.T("An offline model download is already running."));
        string staging=Path.Combine(AppPaths.Cache,"temp","translation-install-"+Guid.NewGuid().ToString("N"));
        try
        {
            var artifacts=JsonSerializer.Deserialize<TranslationArtifact[]>(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"Translation","offline-manifest.json"),cancellation))??throw new IOException("Missing offline manifest");
            string cache=Path.Combine(AppPaths.Cache,"downloads","translation");Directory.CreateDirectory(cache);Directory.CreateDirectory(staging);
            using var client=new HttpClient{Timeout=TimeSpan.FromMinutes(20)};client.DefaultRequestHeaders.UserAgent.ParseAdd("ClariBar/"+ProductInfo.DisplayVersion);
            foreach(var artifact in artifacts)
            {
                cancellation.ThrowIfCancellationRequested();string archive=Path.Combine(cache,artifact.Name);
                if(!await VerifiedAsync(archive,artifact,cancellation))
                {
                    string temporary=archive+".part";
                    try
                    {
                        using var response=await client.GetAsync(artifact.Url,HttpCompletionOption.ResponseHeadersRead,cancellation);response.EnsureSuccessStatusCode();
                        await using(var source=await response.Content.ReadAsStreamAsync(cancellation))
                        await using(var file=new FileStream(temporary,FileMode.Create,FileAccess.Write,FileShare.None,81920,true))
                        {
                            byte[] block=new byte[81920];long downloaded=0;int count;var last=DateTime.MinValue;
                            while((count=await source.ReadAsync(block,cancellation))>0)
                            {
                                downloaded+=count;if(downloaded>artifact.Bytes)throw new IOException(L.T("The offline download size does not match its manifest."));
                                await file.WriteAsync(block.AsMemory(0,count),cancellation);
                                if((DateTime.UtcNow-last).TotalMilliseconds>200){last=DateTime.UtcNow;progress?.Invoke(L.F("Downloading {0}: {1:0}%",artifact.Name,downloaded*100d/artifact.Bytes));}
                            }
                        }
                        if(!await VerifiedAsync(temporary,artifact,cancellation))throw new IOException(L.T("The offline download checksum is invalid. Retry the download."));
                        File.Move(temporary,archive,true);
                    }
                    finally{if(File.Exists(temporary))File.Delete(temporary);}
                }
                progress?.Invoke(L.F("Installing {0}…",artifact.Name));await Task.Run(()=>Extract(archive,Path.Combine(staging,artifact.Destination),artifact.Destination.StartsWith("models/",StringComparison.Ordinal),cancellation),cancellation);
            }
            await File.WriteAllTextAsync(Path.Combine(staging,"python","python312._pth"),"python312.zip\n.\nimport site\n",cancellation);
            cancellation.ThrowIfCancellationRequested();string destination=TranslationService.OfflineDirectory;Directory.CreateDirectory(Path.GetDirectoryName(destination)!);string backup=destination+".previous";
            if(Directory.Exists(backup))Directory.Delete(backup,true);if(Directory.Exists(destination))Directory.Move(destination,backup);
            try
            {
                Directory.Move(staging,destination);progress?.Invoke(L.T("Checking offline translation…"));
                await TranslationService.LocalAsync(["Hello world."],"en","zh",cancellation);await TranslationService.LocalAsync(["你好，世界。"],"zh","en",cancellation);
                await File.WriteAllTextAsync(Path.Combine(destination,"ready.json"),JsonSerializer.Serialize(new{installedAt=DateTimeOffset.Now,languages=new[]{"en","zh"}}),cancellation);
                if(Directory.Exists(backup))Directory.Delete(backup,true);
            }
            catch
            {
                if(Directory.Exists(destination))Directory.Delete(destination,true);if(Directory.Exists(backup))Directory.Move(backup,destination);throw;
            }
        }
        finally{if(Directory.Exists(staging))Directory.Delete(staging,true);gate.Release();}
    }
    private static async Task<bool> VerifiedAsync(string path,TranslationArtifact artifact,CancellationToken cancellation)
    {
        if(!File.Exists(path) || new FileInfo(path).Length!=artifact.Bytes)return false;
        await using var input=File.OpenRead(path);string hash=Convert.ToHexString(await SHA256.HashDataAsync(input,cancellation));return hash.Equals(artifact.Sha256,StringComparison.OrdinalIgnoreCase);
    }
    internal static void Extract(string archive,string destination,bool stripRoot,CancellationToken cancellation)
    {
        Directory.CreateDirectory(destination);string root=Path.GetFullPath(destination)+Path.DirectorySeparatorChar;using var zip=ZipFile.OpenRead(archive);
        string prefix=stripRoot?zip.Entries.First(entry=>entry.FullName.Contains('/')).FullName.Split('/')[0]+"/":"";
        long size=0;
        foreach(var entry in zip.Entries)
        {
            cancellation.ThrowIfCancellationRequested();if(entry.FullName.EndsWith('/'))continue;
            string name=entry.FullName;if(prefix.Length>0){if(!name.StartsWith(prefix,StringComparison.Ordinal))throw new IOException("Unexpected model archive layout");name=name[prefix.Length..];}
            string target=Path.GetFullPath(Path.Combine(destination,name));if(!target.StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new IOException("Invalid archive path");
            size+=entry.Length;if(size>1024L*1024*1024)throw new IOException("Offline archive is too large");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);entry.ExtractToFile(target,true);
        }
    }
}
