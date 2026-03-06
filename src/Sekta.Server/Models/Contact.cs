namespace Sekta.Server.Models;

public class Contact
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid ContactUserId { get; set; }
    public DateTime CreatedAt { get; set; }

    public User User { get; set; } = null!;
    public User ContactUser { get; set; } = null!;
}
