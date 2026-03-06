using Sekta.Shared.Enums;

namespace Sekta.Server.Models;

public class Chat
{
    public Guid Id { get; set; }
    public ChatType Type { get; set; }
    public string? Title { get; set; }
    public string? AvatarUrl { get; set; }
    public DateTime CreatedAt { get; set; }

    public ICollection<ChatMember> Members { get; set; } = new List<ChatMember>();
    public ICollection<Message> Messages { get; set; } = new List<Message>();
}
