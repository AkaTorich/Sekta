namespace Sekta.Shared.DTOs;

public record StickerPackDto(Guid Id, string Title, string? AuthorName, List<StickerDto> Stickers);

public record StickerDto(Guid Id, Guid PackId, string ImageUrl);
