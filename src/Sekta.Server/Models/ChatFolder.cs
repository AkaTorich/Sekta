namespace Sekta.Server.Models;

public class ChatFolder
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = "folder_regular";
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }

    public User User { get; set; } = null!;
    public ICollection<ChatFolderChat> Chats { get; set; } = new List<ChatFolderChat>();
}

public class ChatFolderChat
{
    public Guid Id { get; set; }
    public Guid FolderId { get; set; }
    public Guid ChatId { get; set; }

    public ChatFolder Folder { get; set; } = null!;
    public Chat Chat { get; set; } = null!;
}
