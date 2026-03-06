using Microsoft.EntityFrameworkCore;
using Sekta.Server.Data;
using Sekta.Server.Models;
using Sekta.Shared.DTOs;
using Sekta.Shared.Enums;

namespace Sekta.Server.Services;

public interface IChatService
{
    Task<List<ChatDto>> GetUserChats(Guid userId);
    Task<ChatDto?> GetChatById(Guid chatId, Guid userId);
    Task<ChatDto> GetOrCreatePrivateChat(Guid userId, Guid targetUserId);
    Task<ChatDto> CreateGroupChat(Guid userId, CreateGroupChatDto dto);
    Task<List<MessageDto>> GetChatMessages(Guid chatId, Guid userId, int page = 1, int pageSize = 50);
    Task<MessageDto> SendMessage(Guid senderId, SendMessageDto dto);
    Task<MessageDto> EditMessage(Guid messageId, Guid userId, EditMessageDto dto);
    Task<bool> DeleteMessage(Guid messageId, Guid userId);
    Task<List<Guid>> MarkMessagesDelivered(Guid chatId, Guid userId);
    Task<List<Guid>> MarkMessagesRead(Guid chatId, Guid userId);
    Task MarkAsRead(Guid chatId, Guid userId);
    Task<List<ChatMemberDto>> GetChatMembers(Guid chatId);
    Task AddMemberToGroup(Guid chatId, Guid userId, Guid newMemberId);
    Task RemoveMemberFromGroup(Guid chatId, Guid userId, Guid memberId);
    Task UpdateGroupChat(Guid chatId, Guid userId, UpdateGroupChatDto dto);
    Task<bool> TogglePinChat(Guid chatId, Guid userId);
}

public class ChatService : IChatService
{
    private readonly AppDbContext _db;
    private readonly ILinkPreviewService _linkPreviewService;

    public ChatService(AppDbContext db, ILinkPreviewService linkPreviewService)
    {
        _db = db;
        _linkPreviewService = linkPreviewService;
    }

    public async Task<List<ChatDto>> GetUserChats(Guid userId)
    {
        var chatIds = await _db.ChatMembers
            .Where(cm => cm.UserId == userId)
            .Select(cm => cm.ChatId)
            .ToListAsync();

        var chats = await _db.Chats
            .Where(c => chatIds.Contains(c.Id))
            .Include(c => c.Members).ThenInclude(m => m.User)
            .Include(c => c.Messages.OrderByDescending(m => m.CreatedAt).Take(1))
                .ThenInclude(m => m.Sender)
            .OrderByDescending(c => c.Messages.Max(m => (DateTime?)m.CreatedAt) ?? c.CreatedAt)
            .ToListAsync();

        var result = new List<ChatDto>();

        foreach (var chat in chats)
        {
            var membership = chat.Members.FirstOrDefault(m => m.UserId == userId);
            var lastReadAt = membership?.LastReadAt ?? DateTime.MinValue;

            var unreadCount = await _db.Messages
                .Where(m => m.ChatId == chat.Id && !m.IsDeleted && m.SenderId != userId && m.CreatedAt > lastReadAt)
                .CountAsync();

            var lastMsg = chat.Messages.FirstOrDefault();
            MessageDto? lastMessageDto = lastMsg is null ? null : MapMessageToDto(lastMsg);

            // For private chats, show the other person's name
            var title = chat.Title;
            var avatarUrl = chat.AvatarUrl;
            if (chat.Type == ChatType.Private)
            {
                var other = chat.Members.FirstOrDefault(m => m.UserId != userId)?.User;
                if (other is not null)
                {
                    title = other.DisplayName ?? other.Username;
                    avatarUrl = other.AvatarUrl;
                }
            }

            result.Add(new ChatDto(
                chat.Id,
                chat.Type,
                title,
                avatarUrl,
                lastMessageDto,
                unreadCount,
                chat.CreatedAt,
                membership?.LastReadAt,
                membership?.IsPinned ?? false
            ));
        }

        // Pinned chats first, then by last message time
        return result.OrderByDescending(c => c.IsPinned).ThenByDescending(c => c.LastMessage?.CreatedAt ?? c.CreatedAt).ToList();
    }

    public async Task<ChatDto?> GetChatById(Guid chatId, Guid userId)
    {
        var chat = await _db.Chats
            .Where(c => c.Id == chatId)
            .Include(c => c.Members).ThenInclude(m => m.User)
            .Include(c => c.Messages.OrderByDescending(m => m.CreatedAt).Take(1))
                .ThenInclude(m => m.Sender)
            .FirstOrDefaultAsync();

        if (chat is null)
            return null;

        var isMember = chat.Members.Any(m => m.UserId == userId);
        if (!isMember)
            return null;

        var membership = chat.Members.FirstOrDefault(m => m.UserId == userId);
        var lastReadAt = membership?.LastReadAt ?? DateTime.MinValue;

        var unreadCount = await _db.Messages
            .Where(m => m.ChatId == chat.Id && !m.IsDeleted && m.SenderId != userId && m.CreatedAt > lastReadAt)
            .CountAsync();

        var lastMsg = chat.Messages.FirstOrDefault();
        MessageDto? lastMessageDto = lastMsg is null ? null : MapMessageToDto(lastMsg);

        var title = chat.Title;
        var avatarUrl = chat.AvatarUrl;
        if (chat.Type == ChatType.Private)
        {
            var other = chat.Members.FirstOrDefault(m => m.UserId != userId)?.User;
            if (other is not null)
            {
                title = other.DisplayName ?? other.Username;
                avatarUrl = other.AvatarUrl;
            }
        }

        return new ChatDto(chat.Id, chat.Type, title, avatarUrl, lastMessageDto, unreadCount, chat.CreatedAt, membership?.LastReadAt, membership?.IsPinned ?? false);
    }

    public async Task<ChatDto> GetOrCreatePrivateChat(Guid userId, Guid targetUserId)
    {
        var existingChat = await _db.Chats
            .Where(c => c.Type == ChatType.Private)
            .Where(c => c.Members.Any(m => m.UserId == userId) && c.Members.Any(m => m.UserId == targetUserId))
            .Include(c => c.Messages.OrderByDescending(m => m.CreatedAt).Take(1))
                .ThenInclude(m => m.Sender)
            .Include(c => c.Members)
            .FirstOrDefaultAsync();

        if (existingChat is not null)
        {
            var membership = existingChat.Members.FirstOrDefault(m => m.UserId == userId);
            var lastReadAt = membership?.LastReadAt ?? DateTime.MinValue;
            var unreadCount = await _db.Messages
                .Where(m => m.ChatId == existingChat.Id && !m.IsDeleted && m.SenderId != userId && m.CreatedAt > lastReadAt)
                .CountAsync();

            var lastMsg = existingChat.Messages.FirstOrDefault();
            var otherUser = existingChat.Members.FirstOrDefault(m => m.UserId != userId)?.User;

            return new ChatDto(
                existingChat.Id,
                existingChat.Type,
                otherUser?.DisplayName ?? otherUser?.Username ?? existingChat.Title,
                otherUser?.AvatarUrl ?? existingChat.AvatarUrl,
                lastMsg is null ? null : MapMessageToDto(lastMsg),
                unreadCount,
                existingChat.CreatedAt,
                membership?.LastReadAt,
                membership?.IsPinned ?? false
            );
        }

        var chat = new Chat
        {
            Id = Guid.NewGuid(),
            Type = ChatType.Private,
            CreatedAt = DateTime.UtcNow
        };

        _db.Chats.Add(chat);

        _db.ChatMembers.Add(new ChatMember
        {
            Id = Guid.NewGuid(),
            ChatId = chat.Id,
            UserId = userId,
            Role = ChatMemberRole.Member,
            JoinedAt = DateTime.UtcNow
        });

        _db.ChatMembers.Add(new ChatMember
        {
            Id = Guid.NewGuid(),
            ChatId = chat.Id,
            UserId = targetUserId,
            Role = ChatMemberRole.Member,
            JoinedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        var targetUser = await _db.Users.FindAsync(targetUserId);
        return new ChatDto(
            chat.Id, chat.Type,
            targetUser?.DisplayName ?? targetUser?.Username,
            targetUser?.AvatarUrl,
            null, 0, chat.CreatedAt, null);
    }

    public async Task<ChatDto> CreateGroupChat(Guid userId, CreateGroupChatDto dto)
    {
        var chat = new Chat
        {
            Id = Guid.NewGuid(),
            Type = ChatType.Group,
            Title = dto.Title,
            CreatedAt = DateTime.UtcNow
        };

        _db.Chats.Add(chat);

        _db.ChatMembers.Add(new ChatMember
        {
            Id = Guid.NewGuid(),
            ChatId = chat.Id,
            UserId = userId,
            Role = ChatMemberRole.Owner,
            JoinedAt = DateTime.UtcNow
        });

        foreach (var memberId in dto.MemberIds.Where(id => id != userId))
        {
            _db.ChatMembers.Add(new ChatMember
            {
                Id = Guid.NewGuid(),
                ChatId = chat.Id,
                UserId = memberId,
                Role = ChatMemberRole.Member,
                JoinedAt = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync();

        return new ChatDto(chat.Id, chat.Type, chat.Title, chat.AvatarUrl, null, 0, chat.CreatedAt, null);
    }

    public async Task<List<MessageDto>> GetChatMessages(Guid chatId, Guid userId, int page = 1, int pageSize = 50)
    {
        var isMember = await _db.ChatMembers.AnyAsync(cm => cm.ChatId == chatId && cm.UserId == userId);
        if (!isMember)
            throw new Exception("You are not a member of this chat.");

        // Get latest messages first (descending), then reverse for chronological display
        var messages = await _db.Messages
            .Where(m => m.ChatId == chatId && !m.IsDeleted)
            .OrderByDescending(m => m.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(m => m.Sender)
            .Include(m => m.ReplyTo).ThenInclude(r => r!.Sender)
            .ToListAsync();

        // Reverse so messages are in chronological order (oldest first)
        messages.Reverse();

        return messages.Select(MapMessageToDto).ToList();
    }

    public async Task<MessageDto> SendMessage(Guid senderId, SendMessageDto dto)
    {
        var isMember = await _db.ChatMembers.AnyAsync(cm => cm.ChatId == dto.ChatId && cm.UserId == senderId);
        if (!isMember)
            throw new Exception("You are not a member of this chat.");

        var message = new Message
        {
            Id = Guid.NewGuid(),
            ChatId = dto.ChatId,
            SenderId = senderId,
            Content = dto.Content,
            Type = dto.Type,
            MediaUrl = dto.MediaUrl,
            FileName = dto.FileName,
            FileSize = dto.FileSize,
            ReplyToId = dto.ReplyToId,
            ForwardedFrom = dto.ForwardedFrom,
            Status = MessageStatus.Sent,
            IsEdited = false,
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow
        };

        // Fetch link preview before saving (for text messages with URLs)
        if (message.Type == MessageType.Text && !string.IsNullOrWhiteSpace(message.Content))
        {
            try
            {
                var preview = await _linkPreviewService.FetchPreviewAsync(message.Content);
                if (preview is not null)
                {
                    message.LinkPreviewUrl = preview.Url;
                    message.LinkPreviewTitle = preview.Title;
                    message.LinkPreviewDescription = preview.Description;
                    message.LinkPreviewImageUrl = preview.ImageUrl;
                    message.LinkPreviewDomain = preview.Domain;
                }
            }
            catch { /* Don't fail message send if preview fails */ }
        }

        _db.Messages.Add(message);
        await _db.SaveChangesAsync();

        var sender = await _db.Users.FindAsync(senderId);
        message.Sender = sender!;

        // Load ReplyTo navigation so the DTO includes the quoted message
        if (message.ReplyToId.HasValue)
        {
            message.ReplyTo = await _db.Messages
                .Include(m => m.Sender)
                .FirstOrDefaultAsync(m => m.Id == message.ReplyToId.Value);
        }

        return MapMessageToDto(message);
    }

    public async Task<MessageDto> EditMessage(Guid messageId, Guid userId, EditMessageDto dto)
    {
        var message = await _db.Messages
            .Include(m => m.Sender)
            .FirstOrDefaultAsync(m => m.Id == messageId);

        if (message is null)
            throw new Exception("Message not found.");

        if (message.SenderId != userId)
            throw new Exception("You can only edit your own messages.");

        message.Content = dto.Content;
        message.IsEdited = true;

        await _db.SaveChangesAsync();

        return MapMessageToDto(message);
    }

    public async Task<bool> DeleteMessage(Guid messageId, Guid userId)
    {
        var message = await _db.Messages.FindAsync(messageId);
        if (message is null)
            throw new Exception("Message not found.");

        if (message.SenderId != userId)
            throw new Exception("You can only delete your own messages.");

        message.IsDeleted = true;
        await _db.SaveChangesAsync();

        return true;
    }

    public async Task<List<Guid>> MarkMessagesDelivered(Guid chatId, Guid userId)
    {
        var isMember = await _db.ChatMembers.AnyAsync(cm => cm.ChatId == chatId && cm.UserId == userId);
        if (!isMember)
            throw new Exception("You are not a member of this chat.");

        var now = DateTime.UtcNow;
        var messages = await _db.Messages
            .Where(m => m.ChatId == chatId && !m.IsDeleted && m.SenderId != userId && m.Status == MessageStatus.Sent)
            .ToListAsync();

        var updatedIds = new List<Guid>();
        foreach (var msg in messages)
        {
            msg.Status = MessageStatus.Delivered;
            msg.DeliveredAt = now;
            updatedIds.Add(msg.Id);
        }

        if (updatedIds.Count > 0)
            await _db.SaveChangesAsync();

        return updatedIds;
    }

    public async Task<List<Guid>> MarkMessagesRead(Guid chatId, Guid userId)
    {
        var isMember = await _db.ChatMembers.AnyAsync(cm => cm.ChatId == chatId && cm.UserId == userId);
        if (!isMember)
            throw new Exception("You are not a member of this chat.");

        var now = DateTime.UtcNow;
        var messages = await _db.Messages
            .Where(m => m.ChatId == chatId && !m.IsDeleted && m.SenderId != userId && m.Status != MessageStatus.Read)
            .ToListAsync();

        var updatedIds = new List<Guid>();
        foreach (var msg in messages)
        {
            msg.Status = MessageStatus.Read;
            msg.ReadAt = now;
            if (msg.DeliveredAt is null)
                msg.DeliveredAt = now;
            updatedIds.Add(msg.Id);
        }

        if (updatedIds.Count > 0)
            await _db.SaveChangesAsync();

        return updatedIds;
    }

    public async Task MarkAsRead(Guid chatId, Guid userId)
    {
        var membership = await _db.ChatMembers
            .FirstOrDefaultAsync(cm => cm.ChatId == chatId && cm.UserId == userId);

        if (membership is null)
            throw new Exception("You are not a member of this chat.");

        membership.LastReadAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task<List<ChatMemberDto>> GetChatMembers(Guid chatId)
    {
        var members = await _db.ChatMembers
            .Where(cm => cm.ChatId == chatId)
            .Include(cm => cm.User)
            .ToListAsync();

        return members.Select(cm => new ChatMemberDto(
            cm.UserId,
            cm.User.Username,
            cm.User.DisplayName,
            cm.User.AvatarUrl,
            cm.Role
        )).ToList();
    }

    public async Task AddMemberToGroup(Guid chatId, Guid userId, Guid newMemberId)
    {
        var chat = await _db.Chats.FindAsync(chatId);
        if (chat is null || chat.Type != ChatType.Group)
            throw new Exception("Group chat not found.");

        var requester = await _db.ChatMembers
            .FirstOrDefaultAsync(cm => cm.ChatId == chatId && cm.UserId == userId);
        if (requester is null)
            throw new Exception("You are not a member of this group.");

        var alreadyMember = await _db.ChatMembers
            .AnyAsync(cm => cm.ChatId == chatId && cm.UserId == newMemberId);
        if (alreadyMember)
            throw new Exception("User is already a member of this chat.");

        _db.ChatMembers.Add(new ChatMember
        {
            Id = Guid.NewGuid(),
            ChatId = chatId,
            UserId = newMemberId,
            Role = ChatMemberRole.Member,
            JoinedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
    }

    public async Task RemoveMemberFromGroup(Guid chatId, Guid userId, Guid memberId)
    {
        var chat = await _db.Chats.FindAsync(chatId);
        if (chat is null || chat.Type != ChatType.Group)
            throw new Exception("Group chat not found.");

        var requester = await _db.ChatMembers
            .FirstOrDefaultAsync(cm => cm.ChatId == chatId && cm.UserId == userId);
        if (requester is null || requester.Role == ChatMemberRole.Member)
            throw new Exception("You do not have permission to remove members.");

        var member = await _db.ChatMembers
            .FirstOrDefaultAsync(cm => cm.ChatId == chatId && cm.UserId == memberId);
        if (member is null)
            throw new Exception("User is not a member of this chat.");

        if (member.Role == ChatMemberRole.Owner)
            throw new Exception("Cannot remove the chat owner.");

        _db.ChatMembers.Remove(member);
        await _db.SaveChangesAsync();
    }

    public async Task UpdateGroupChat(Guid chatId, Guid userId, UpdateGroupChatDto dto)
    {
        var chat = await _db.Chats.FindAsync(chatId);
        if (chat is null || chat.Type != ChatType.Group)
            throw new Exception("Group chat not found.");

        var requester = await _db.ChatMembers
            .FirstOrDefaultAsync(cm => cm.ChatId == chatId && cm.UserId == userId);
        if (requester is null || requester.Role != ChatMemberRole.Owner)
            throw new Exception("Only the group owner can update the group.");

        if (dto.Title is not null)
            chat.Title = dto.Title;
        if (dto.AvatarUrl is not null)
            chat.AvatarUrl = dto.AvatarUrl;

        await _db.SaveChangesAsync();
    }

    public async Task<bool> TogglePinChat(Guid chatId, Guid userId)
    {
        var membership = await _db.ChatMembers
            .FirstOrDefaultAsync(cm => cm.ChatId == chatId && cm.UserId == userId);

        if (membership is null)
            throw new Exception("You are not a member of this chat.");

        membership.IsPinned = !membership.IsPinned;
        await _db.SaveChangesAsync();
        return membership.IsPinned;
    }

    private static LinkPreviewDto? MapLinkPreview(Message message)
    {
        if (string.IsNullOrEmpty(message.LinkPreviewUrl))
            return null;
        return new LinkPreviewDto(
            message.LinkPreviewUrl,
            message.LinkPreviewTitle,
            message.LinkPreviewDescription,
            message.LinkPreviewImageUrl,
            message.LinkPreviewDomain);
    }

    private static MessageDto MapMessageToDto(Message message)
    {
        MessageDto? replyDto = null;
        if (message.ReplyTo is not null)
        {
            replyDto = new MessageDto(
                message.ReplyTo.Id,
                message.ReplyTo.ChatId,
                message.ReplyTo.SenderId,
                message.ReplyTo.Sender?.DisplayName ?? message.ReplyTo.Sender?.Username,
                message.ReplyTo.Content,
                message.ReplyTo.Type,
                message.ReplyTo.MediaUrl,
                message.ReplyTo.FileName,
                message.ReplyTo.FileSize,
                message.ReplyTo.ReplyToId,
                null,
                message.ReplyTo.Status,
                message.ReplyTo.IsEdited,
                message.ReplyTo.IsDeleted,
                message.ReplyTo.CreatedAt,
                MapLinkPreview(message.ReplyTo),
                message.ReplyTo.ForwardedFrom
            );
        }

        return new MessageDto(
            message.Id,
            message.ChatId,
            message.SenderId,
            message.Sender?.DisplayName ?? message.Sender?.Username,
            message.Content,
            message.Type,
            message.MediaUrl,
            message.FileName,
            message.FileSize,
            message.ReplyToId,
            replyDto,
            message.Status,
            message.IsEdited,
            message.IsDeleted,
            message.CreatedAt,
            MapLinkPreview(message),
            message.ForwardedFrom
        );
    }
}
