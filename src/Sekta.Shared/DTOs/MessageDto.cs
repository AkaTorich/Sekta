using Sekta.Shared.Enums;

namespace Sekta.Shared.DTOs;

public record LinkPreviewDto(
    string Url,
    string? Title,
    string? Description,
    string? ImageUrl,
    string? Domain
);

public record MessageDto(
    Guid Id,
    Guid ChatId,
    Guid SenderId,
    string? SenderName,
    string? Content,
    MessageType Type,
    string? MediaUrl,
    string? FileName,
    long? FileSize,
    Guid? ReplyToId,
    MessageDto? ReplyTo,
    MessageStatus Status,
    bool IsEdited,
    bool IsDeleted,
    DateTime CreatedAt,
    LinkPreviewDto? LinkPreview = null,
    string? ForwardedFrom = null
);

public record SendMessageDto(
    Guid ChatId,
    string? Content,
    MessageType Type,
    string? MediaUrl,
    string? FileName,
    long? FileSize,
    Guid? ReplyToId,
    string? ForwardedFrom = null
);

public record EditMessageDto(string Content);
