using Microsoft.AspNetCore.Hosting;

namespace Sekta.Server.Services;

public interface IFileService
{
    Task<(string Url, string FileName, long Size)> UploadFile(IFormFile file);
    string GetFilePath(string fileName);
}

public class FileService : IFileService
{
    private readonly IWebHostEnvironment _env;

    public FileService(IWebHostEnvironment env)
    {
        _env = env;

        // Ensure wwwroot exists (WebRootPath is null if the folder is missing)
        if (string.IsNullOrEmpty(_env.WebRootPath))
        {
            _env.WebRootPath = Path.Combine(_env.ContentRootPath, "wwwroot");
        }
        Directory.CreateDirectory(_env.WebRootPath);
    }

    public async Task<(string Url, string FileName, long Size)> UploadFile(IFormFile file)
    {
        if (file.Length == 0)
            throw new Exception("File is empty.");

        var uploadsDir = Path.Combine(_env.WebRootPath!, "uploads");
        Directory.CreateDirectory(uploadsDir);

        var extension = Path.GetExtension(file.FileName);
        var savedFileName = $"{Guid.NewGuid()}{extension}";
        var filePath = Path.Combine(uploadsDir, savedFileName);

        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        var url = $"/uploads/{savedFileName}";
        return (url, savedFileName, file.Length);
    }

    public string GetFilePath(string fileName)
    {
        return Path.Combine(_env.WebRootPath, "uploads", fileName);
    }
}
