namespace Sekta.Server.Models;

public class StickerPack
{
    public Guid Id { get; set; }
    public string Title { get; set; } = null!;
    public Guid AuthorId { get; set; }
    public DateTime CreatedAt { get; set; }

    public User Author { get; set; } = null!;
    public ICollection<Sticker> Stickers { get; set; } = new List<Sticker>();
}
