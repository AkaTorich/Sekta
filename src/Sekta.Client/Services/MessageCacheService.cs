using System.Text.Json;
using SQLite;
using Sekta.Shared.DTOs;
using Sekta.Shared.Enums;

namespace Sekta.Client.Services;

public interface IMessageCacheService
{
    Task<List<MessageDto>> GetMessagesAsync(Guid chatId, int limit = 20);
    Task SaveMessagesAsync(Guid chatId, IEnumerable<MessageDto> messages);
    Task AddMessageAsync(Guid chatId, MessageDto message);
    Task UpdateMessageAsync(MessageDto message);
    Task DeleteMessageAsync(Guid messageId);
    DateTime? GetLatestTimestamp(Guid chatId);
}

public class MessageCacheService : IMessageCacheService
{
    private readonly SQLiteAsyncConnection _db;
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public MessageCacheService()
    {
        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "messages.db3");
        _db = new SQLiteAsyncConnection(dbPath);
        _db.CreateTableAsync<CachedMessage>().Wait();
    }

    public async Task<List<MessageDto>> GetMessagesAsync(Guid chatId, int limit = 20)
    {
        var chatIdStr = chatId.ToString();
        var rows = await _db.Table<CachedMessage>()
            .Where(m => m.ChatId == chatIdStr)
            .OrderByDescending(m => m.CreatedAt)
            .Take(limit)
            .ToListAsync();

        rows.Reverse();
        return rows.Select(ToDto).ToList();
    }

    public async Task SaveMessagesAsync(Guid chatId, IEnumerable<MessageDto> messages)
    {
        var rows = messages.Select(m => ToRow(chatId, m)).ToList();
        foreach (var row in rows)
            await _db.InsertOrReplaceAsync(row);
    }

    public async Task AddMessageAsync(Guid chatId, MessageDto message)
    {
        await _db.InsertOrReplaceAsync(ToRow(chatId, message));
    }

    public async Task UpdateMessageAsync(MessageDto message)
    {
        var messageIdStr = message.Id.ToString();
        var existing = await _db.Table<CachedMessage>()
            .Where(m => m.Id == messageIdStr)
            .FirstOrDefaultAsync();

        if (existing is not null)
        {
            existing.Content = message.Content;
            existing.IsEdited = message.IsEdited;
            existing.IsDeleted = message.IsDeleted;
            existing.Status = (int)message.Status;
            existing.LinkPreviewJson = message.LinkPreview is not null
                ? JsonSerializer.Serialize(message.LinkPreview, _json) : null;
            await _db.UpdateAsync(existing);
        }
    }

    public async Task DeleteMessageAsync(Guid messageId)
    {
        var messageIdStr = messageId.ToString();
        await _db.Table<CachedMessage>()
            .DeleteAsync(m => m.Id == messageIdStr);
    }

    public DateTime? GetLatestTimestamp(Guid chatId)
    {
        var chatIdStr = chatId.ToString();
        var row = _db.Table<CachedMessage>()
            .Where(m => m.ChatId == chatIdStr)
            .OrderByDescending(m => m.CreatedAt)
            .FirstOrDefaultAsync()
            .Result;
        return row?.CreatedAt;
    }

    private static CachedMessage ToRow(Guid chatId, MessageDto m) => new()
    {
        Id = m.Id.ToString(),
        ChatId = chatId.ToString(),
        SenderId = m.SenderId.ToString(),
        SenderName = m.SenderName,
        Content = m.Content,
        Type = (int)m.Type,
        MediaUrl = m.MediaUrl,
        FileName = m.FileName,
        FileSize = m.FileSize,
        ReplyToId = m.ReplyToId?.ToString(),
        ReplyToJson = m.ReplyTo is not null ? JsonSerializer.Serialize(m.ReplyTo, _json) : null,
        Status = (int)m.Status,
        IsEdited = m.IsEdited,
        IsDeleted = m.IsDeleted,
        CreatedAt = m.CreatedAt,
        LinkPreviewJson = m.LinkPreview is not null ? JsonSerializer.Serialize(m.LinkPreview, _json) : null,
        ForwardedFrom = m.ForwardedFrom
    };

    private static MessageDto ToDto(CachedMessage r)
    {
        MessageDto? replyTo = null;
        if (r.ReplyToJson is not null)
        {
            try { replyTo = JsonSerializer.Deserialize<MessageDto>(r.ReplyToJson, _json); }
            catch { }
        }

        LinkPreviewDto? linkPreview = null;
        if (r.LinkPreviewJson is not null)
        {
            try { linkPreview = JsonSerializer.Deserialize<LinkPreviewDto>(r.LinkPreviewJson, _json); }
            catch { }
        }

        return new MessageDto(
            Guid.Parse(r.Id),
            Guid.Parse(r.ChatId),
            Guid.Parse(r.SenderId),
            r.SenderName,
            r.Content,
            (MessageType)r.Type,
            r.MediaUrl,
            r.FileName,
            r.FileSize,
            r.ReplyToId is not null ? Guid.Parse(r.ReplyToId) : null,
            replyTo,
            (MessageStatus)r.Status,
            r.IsEdited,
            r.IsDeleted,
            r.CreatedAt,
            linkPreview,
            r.ForwardedFrom
        );
    }
}

[Table("Messages")]
internal class CachedMessage
{
    [PrimaryKey]
    public string Id { get; set; } = "";

    [Indexed]
    public string ChatId { get; set; } = "";

    public string SenderId { get; set; } = "";
    public string? SenderName { get; set; }
    public string? Content { get; set; }
    public int Type { get; set; }
    public string? MediaUrl { get; set; }
    public string? FileName { get; set; }
    public long? FileSize { get; set; }
    public string? ReplyToId { get; set; }
    public string? ReplyToJson { get; set; }
    public int Status { get; set; }
    public bool IsEdited { get; set; }
    public bool IsDeleted { get; set; }

    [Indexed]
    public DateTime CreatedAt { get; set; }

    public string? LinkPreviewJson { get; set; }
    public string? ForwardedFrom { get; set; }
}
