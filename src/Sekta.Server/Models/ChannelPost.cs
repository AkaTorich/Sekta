namespace Sekta.Server.Models;

public class ChannelPost
{
    public Guid Id { get; set; }
    public Guid ChannelId { get; set; }
    public string? Content { get; set; }
    public string? MediaUrl { get; set; }
    public DateTime CreatedAt { get; set; }

    public Channel Channel { get; set; } = null!;
}
