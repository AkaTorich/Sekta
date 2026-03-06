using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sekta.Server.Services;

namespace Sekta.Server.Controllers;

[ApiController]
[Route("api/files")]
[Authorize]
public class FilesController : ControllerBase
{
    private readonly IFileService _fileService;

    public FilesController(IFileService fileService)
    {
        _fileService = fileService;
    }

    [HttpPost("upload")]
    public async Task<IActionResult> Upload(IFormFile file)
    {
        if (file.Length == 0)
            return BadRequest(new { message = "File is empty" });

        if (file.Length > 50 * 1024 * 1024) // 50MB limit
            return BadRequest(new { message = "File too large. Max 50MB." });

        var (url, fileName, size) = await _fileService.UploadFile(file);
        return Ok(new { url, fileName, size });
    }

    [HttpGet("{fileName}")]
    [AllowAnonymous]
    public IActionResult GetFile(string fileName)
    {
        var path = _fileService.GetFilePath(fileName);
        if (!System.IO.File.Exists(path))
            return NotFound();

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var contentType = ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            ".mp3" => "audio/mpeg",
            ".ogg" => "audio/ogg",
            ".wav" => "audio/wav",
            ".pdf" => "application/pdf",
            _ => "application/octet-stream"
        };

        return PhysicalFile(path, contentType);
    }
}
