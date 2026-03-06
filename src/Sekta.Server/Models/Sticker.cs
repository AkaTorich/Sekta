namespace Sekta.Server.Models;

public class Sticker
{
    public Guid Id { get; set; }
    public Guid PackId { get; set; }
    public string ImageUrl { get; set; } = null!;

    public StickerPack Pack { get; set; } = null!;
}
