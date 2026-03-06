using System.Text.Json;
using SQLite;
using Sekta.Shared.DTOs;
using Sekta.Shared.Enums;

namespace Sekta.Client.Services;

public interface IChatCacheService
{
    Task<List<ChatDto>> GetChatsAsync();
    Task SaveChatsAsync(IEnumerable<ChatDto> chats);
    Task UpsertChatAsync(ChatDto chat);
    Task UpdateChatInfoAsync(Guid chatId, string? title, string? avatarUrl);
    Task DeleteChatAsync(Guid chatId);
}

public class ChatCacheService : IChatCacheService
{
    private readonly SQLiteAsyncConnection _db;
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public ChatCacheService()
    {
        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "chats.db3");
        _db = new SQLiteAsyncConnection(dbPath);
        _db.CreateTableAsync<CachedChat>().Wait();
    }

    public async Task<List<ChatDto>> GetChatsAsync()
    {
        var rows = await _db.Table<CachedChat>()
            .OrderByDescending(c => c.IsPinned)
            .ThenByDescending(c => c.LastMessageAt)
            .ToListAsync();

        return rows.Select(ToDto).ToList();
    }

    public async Task SaveChatsAsync(IEnumerable<ChatDto> chats)
    {
        // Clear and repopulate
        await _db.DeleteAllAsync<CachedChat>();
        var rows = chats.Select(ToRow).ToList();
        await _db.InsertAllAsync(rows);
    }

    public async Task UpsertChatAsync(ChatDto chat)
    {
        await _db.InsertOrReplaceAsync(ToRow(chat));
    }

    public async Task UpdateChatInfoAsync(Guid chatId, string? title, string? avatarUrl)
    {
        var existing = await _db.Table<CachedChat>()
            .Where(c => c.Id == chatId.ToString())
            .FirstOrDefaultAsync();

        if (existing is not null)
        {
            if (title is not null)
                existing.Title = title;
            if (avatarUrl is not null)
                existing.AvatarUrl = avatarUrl;
            await _db.UpdateAsync(existing);
        }
    }

    public async Task DeleteChatAsync(Guid chatId)
    {
        await _db.Table<CachedChat>()
            .DeleteAsync(c => c.Id == chatId.ToString());
    }

    private static CachedChat ToRow(ChatDto c) => new()
    {
        Id = c.Id.ToString(),
        Type = (int)c.Type,
        Title = c.Title,
        AvatarUrl = c.AvatarUrl,
        LastMessageJson = c.LastMessage is not null ? JsonSerializer.Serialize(c.LastMessage, _json) : null,
        LastMessageAt = c.LastMessage?.CreatedAt ?? c.CreatedAt,
        UnreadCount = c.UnreadCount,
        CreatedAt = c.CreatedAt,
        IsPinned = c.IsPinned
    };

    private static ChatDto ToDto(CachedChat r)
    {
        MessageDto? lastMessage = null;
        if (r.LastMessageJson is not null)
        {
            try { lastMessage = JsonSerializer.Deserialize<MessageDto>(r.LastMessageJson, _json); }
            catch { }
        }

        return new ChatDto(
            Guid.Parse(r.Id),
            (ChatType)r.Type,
            r.Title,
            r.AvatarUrl,
            lastMessage,
            r.UnreadCount,
            r.CreatedAt,
            null,
            r.IsPinned
        );
    }
}

[Table("Chats")]
internal class CachedChat
{
    [PrimaryKey]
    public string Id { get; set; } = "";

    public int Type { get; set; }
    public string? Title { get; set; }
    public string? AvatarUrl { get; set; }
    public string? LastMessageJson { get; set; }

    [Indexed]
    public DateTime LastMessageAt { get; set; }

    public int UnreadCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsPinned { get; set; }
}
