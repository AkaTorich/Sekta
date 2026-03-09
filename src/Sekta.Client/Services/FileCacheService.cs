using System.Security.Cryptography;

namespace Sekta.Client.Services;

public interface IFileCacheService
{
    /// <summary>Get local cached path for a URL, or null if not cached.</summary>
    string? GetCachedPath(string remoteUrl);

    /// <summary>Download file from server and cache locally. Returns local file path.</summary>
    Task<string> DownloadAndCacheAsync(string remoteUrl);

    /// <summary>Save decrypted bytes to cache. Returns local file path.</summary>
    Task<string> CacheBytesAsync(string remoteUrl, byte[] data);
}

public class FileCacheService : IFileCacheService
{
    private readonly IApiService _apiService;
    private readonly string _cacheDir;

    public FileCacheService(IApiService apiService)
    {
        _apiService = apiService;
        _cacheDir = Path.Combine(FileSystem.CacheDirectory, "media");
        Directory.CreateDirectory(_cacheDir);
    }

    public string? GetCachedPath(string remoteUrl)
    {
        var localPath = GetLocalPath(remoteUrl);
        return File.Exists(localPath) ? localPath : null;
    }

    public async Task<string> DownloadAndCacheAsync(string remoteUrl)
    {
        var localPath = GetLocalPath(remoteUrl);
        if (File.Exists(localPath))
            return localPath;

        var bytes = await _apiService.DownloadBytesAsync(remoteUrl);
        if (bytes is null)
            throw new Exception($"Failed to download: {remoteUrl}");

        await File.WriteAllBytesAsync(localPath, bytes);
        return localPath;
    }

    public async Task<string> CacheBytesAsync(string remoteUrl, byte[] data)
    {
        var localPath = GetLocalPath(remoteUrl);
        await File.WriteAllBytesAsync(localPath, data);
        return localPath;
    }

    private string GetLocalPath(string remoteUrl)
    {
        // Use SHA256 hash of URL as filename, preserve extension
        var hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(remoteUrl)))[..16];
        var ext = Path.GetExtension(remoteUrl.Split('?')[0]);
        if (string.IsNullOrEmpty(ext)) ext = ".bin";
        return Path.Combine(_cacheDir, $"{hash}{ext}");
    }
}
