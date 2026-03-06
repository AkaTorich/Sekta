using Microsoft.EntityFrameworkCore;
using Sekta.Server.Data;
using Sekta.Server.Models;
using Sekta.Shared.DTOs;

namespace Sekta.Server.Services;

public interface IStickerService
{
    Task<List<StickerPackDto>> GetAllPacks();
    Task<StickerPackDto?> GetPack(Guid packId);
    Task<StickerPackDto> CreatePack(Guid userId, string title);
    Task<StickerDto> AddSticker(Guid packId, string imageUrl);
    Task DeletePack(Guid packId, Guid userId);
}

public class StickerService : IStickerService
{
    private readonly AppDbContext _db;

    public StickerService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<StickerPackDto>> GetAllPacks()
    {
        var packs = await _db.StickerPacks
            .Include(p => p.Author)
            .Include(p => p.Stickers)
            .OrderBy(p => p.Title)
            .ToListAsync();

        return packs.Select(MapToDto).ToList();
    }

    public async Task<StickerPackDto?> GetPack(Guid packId)
    {
        var pack = await _db.StickerPacks
            .Include(p => p.Author)
            .Include(p => p.Stickers)
            .FirstOrDefaultAsync(p => p.Id == packId);

        return pack is null ? null : MapToDto(pack);
    }

    public async Task<StickerPackDto> CreatePack(Guid userId, string title)
    {
        var pack = new StickerPack
        {
            Id = Guid.NewGuid(),
            Title = title,
            AuthorId = userId,
            CreatedAt = DateTime.UtcNow
        };

        _db.StickerPacks.Add(pack);
        await _db.SaveChangesAsync();

        // Reload with Author for DTO mapping
        var author = await _db.Users.FindAsync(userId);

        return new StickerPackDto(
            pack.Id,
            pack.Title,
            author?.DisplayName ?? author?.Username,
            new List<StickerDto>()
        );
    }

    public async Task<StickerDto> AddSticker(Guid packId, string imageUrl)
    {
        var packExists = await _db.StickerPacks.AnyAsync(p => p.Id == packId);
        if (!packExists)
            throw new Exception("Sticker pack not found.");

        var sticker = new Sticker
        {
            Id = Guid.NewGuid(),
            PackId = packId,
            ImageUrl = imageUrl
        };

        _db.Stickers.Add(sticker);
        await _db.SaveChangesAsync();

        return new StickerDto(sticker.Id, sticker.PackId, sticker.ImageUrl);
    }

    public async Task DeletePack(Guid packId, Guid userId)
    {
        var pack = await _db.StickerPacks.FindAsync(packId);
        if (pack is null)
            throw new Exception("Sticker pack not found.");

        if (pack.AuthorId != userId)
            throw new Exception("Only the pack author can delete it.");

        _db.StickerPacks.Remove(pack);
        await _db.SaveChangesAsync();
    }

    private static StickerPackDto MapToDto(StickerPack pack) =>
        new(
            pack.Id,
            pack.Title,
            pack.Author?.DisplayName ?? pack.Author?.Username,
            pack.Stickers.Select(s => new StickerDto(s.Id, s.PackId, s.ImageUrl)).ToList()
        );
}
