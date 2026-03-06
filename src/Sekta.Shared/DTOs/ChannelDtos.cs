namespace Sekta.Shared.DTOs;

public record ChannelDto(
    Guid Id,
    string Title,
    string? Description,
    string? AvatarUrl,
    Guid OwnerId,
    int SubscriberCount,
    DateTime CreatedAt
);

public record CreateChannelDto(string Title, string? Description);

public record ChannelPostDto(
    Guid Id,
    Guid ChannelId,
    string? Content,
    string? MediaUrl,
    DateTime CreatedAt
);

public record CreateChannelPostDto(string? Content, string? MediaUrl);
