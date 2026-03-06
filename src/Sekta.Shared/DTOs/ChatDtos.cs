using Sekta.Shared.Enums;

namespace Sekta.Shared.DTOs;

public record ChatDto(
    Guid Id,
    ChatType Type,
    string? Title,
    string? AvatarUrl,
    MessageDto? LastMessage,
    int UnreadCount,
    DateTime CreatedAt,
    DateTime? LastReadAt = null,
    bool IsPinned = false
);

public record CreateGroupChatDto(string Title, List<Guid> MemberIds);

public record UpdateGroupChatDto(string? Title, string? AvatarUrl);

public record ChatMemberDto(Guid UserId, string Username, string? DisplayName, string? AvatarUrl, ChatMemberRole Role);
