using Sekta.Shared.Enums;

namespace Sekta.Server.Models;

public class ChatMember
{
    public Guid Id { get; set; }
    public Guid ChatId { get; set; }
    public Guid UserId { get; set; }
    public ChatMemberRole Role { get; set; }
    public DateTime JoinedAt { get; set; }
    public DateTime? LastReadAt { get; set; }
    public bool IsPinned { get; set; }

    public Chat Chat { get; set; } = null!;
    public User User { get; set; } = null!;
}
