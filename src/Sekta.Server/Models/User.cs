using Sekta.Shared.Enums;

namespace Sekta.Server.Models;

public class User
{
    public Guid Id { get; set; }
    public string Username { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? AvatarUrl { get; set; }
    public string? Bio { get; set; }
    public UserStatus Status { get; set; }
    public DateTime LastSeen { get; set; }
    public string? RefreshToken { get; set; }
    public DateTime? RefreshTokenExpiry { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? DeviceToken { get; set; }
    public string? Platform { get; set; }

    public ICollection<Message> SentMessages { get; set; } = new List<Message>();
    public ICollection<ChatMember> ChatMembers { get; set; } = new List<ChatMember>();
    public ICollection<Contact> Contacts { get; set; } = new List<Contact>();
    public ICollection<Channel> OwnedChannels { get; set; } = new List<Channel>();
}
