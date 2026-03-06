namespace Sekta.Server.Models;

public class Channel
{
    public Guid Id { get; set; }
    public string Title { get; set; } = null!;
    public string? Description { get; set; }
    public string? AvatarUrl { get; set; }
    public Guid OwnerId { get; set; }
    public DateTime CreatedAt { get; set; }

    public User Owner { get; set; } = null!;
    public ICollection<ChannelSubscriber> Subscribers { get; set; } = new List<ChannelSubscriber>();
    public ICollection<ChannelPost> Posts { get; set; } = new List<ChannelPost>();
}
