using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sekta.Server.Data;
using Sekta.Server.Models;
using Sekta.Shared.DTOs;

namespace Sekta.Server.Controllers;

[ApiController]
[Route("api/folders")]
[Authorize]
public class FoldersController : ControllerBase
{
    private readonly AppDbContext _db;

    public FoldersController(AppDbContext db)
    {
        _db = db;
    }

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<ActionResult<List<ChatFolderDto>>> GetFolders()
    {
        var userId = GetUserId();
        var folders = await _db.ChatFolders
            .Where(f => f.UserId == userId)
            .Include(f => f.Chats)
            .OrderBy(f => f.SortOrder)
            .ToListAsync();

        return Ok(folders.Select(f => new ChatFolderDto(
            f.Id, f.Name, f.Icon, f.SortOrder,
            f.Chats.Select(c => c.ChatId).ToList()
        )).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<ChatFolderDto>> CreateFolder(CreateFolderDto dto)
    {
        var userId = GetUserId();
        var maxOrder = await _db.ChatFolders
            .Where(f => f.UserId == userId)
            .MaxAsync(f => (int?)f.SortOrder) ?? 0;

        var folder = new ChatFolder
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = dto.Name.Length > 10 ? dto.Name[..10] : dto.Name,
            Icon = dto.Icon ?? "folder_regular",
            SortOrder = maxOrder + 1,
            CreatedAt = DateTime.UtcNow
        };

        _db.ChatFolders.Add(folder);
        await _db.SaveChangesAsync();

        return Ok(new ChatFolderDto(folder.Id, folder.Name, folder.Icon, folder.SortOrder, new List<Guid>()));
    }

    [HttpPut("{folderId:guid}")]
    public async Task<IActionResult> UpdateFolder(Guid folderId, UpdateFolderDto dto)
    {
        var userId = GetUserId();
        var folder = await _db.ChatFolders
            .FirstOrDefaultAsync(f => f.Id == folderId && f.UserId == userId);

        if (folder is null) return NotFound();

        if (dto.Name is not null)
            folder.Name = dto.Name.Length > 10 ? dto.Name[..10] : dto.Name;
        if (dto.Icon is not null)
            folder.Icon = dto.Icon;
        if (dto.SortOrder.HasValue)
            folder.SortOrder = dto.SortOrder.Value;

        await _db.SaveChangesAsync();
        return Ok();
    }

    [HttpDelete("{folderId:guid}")]
    public async Task<IActionResult> DeleteFolder(Guid folderId)
    {
        var userId = GetUserId();
        var folder = await _db.ChatFolders
            .FirstOrDefaultAsync(f => f.Id == folderId && f.UserId == userId);

        if (folder is null) return NotFound();

        _db.ChatFolders.Remove(folder);
        await _db.SaveChangesAsync();
        return Ok();
    }

    [HttpPost("{folderId:guid}/chats/{chatId:guid}")]
    public async Task<IActionResult> AddChatToFolder(Guid folderId, Guid chatId)
    {
        var userId = GetUserId();
        var folder = await _db.ChatFolders
            .FirstOrDefaultAsync(f => f.Id == folderId && f.UserId == userId);

        if (folder is null) return NotFound();

        var exists = await _db.ChatFolderChats
            .AnyAsync(fc => fc.FolderId == folderId && fc.ChatId == chatId);

        if (exists) return Ok();

        _db.ChatFolderChats.Add(new ChatFolderChat
        {
            Id = Guid.NewGuid(),
            FolderId = folderId,
            ChatId = chatId
        });

        await _db.SaveChangesAsync();
        return Ok();
    }

    [HttpDelete("{folderId:guid}/chats/{chatId:guid}")]
    public async Task<IActionResult> RemoveChatFromFolder(Guid folderId, Guid chatId)
    {
        var userId = GetUserId();
        var folder = await _db.ChatFolders
            .FirstOrDefaultAsync(f => f.Id == folderId && f.UserId == userId);

        if (folder is null) return NotFound();

        var entry = await _db.ChatFolderChats
            .FirstOrDefaultAsync(fc => fc.FolderId == folderId && fc.ChatId == chatId);

        if (entry is not null)
        {
            _db.ChatFolderChats.Remove(entry);
            await _db.SaveChangesAsync();
        }

        return Ok();
    }
}
