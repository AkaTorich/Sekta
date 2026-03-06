namespace Sekta.Server.Models;

public class ChannelSubscriber
{
    public Guid Id { get; set; }
    public Guid ChannelId { get; set; }
    public Guid UserId { get; set; }
    public DateTime JoinedAt { get; set; }

    public Channel Channel { get; set; } = null!;
    public User User { get; set; } = null!;
}
