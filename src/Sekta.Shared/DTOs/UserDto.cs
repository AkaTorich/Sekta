using Sekta.Shared.Enums;

namespace Sekta.Shared.DTOs;

public record UserDto(
    Guid Id,
    string Username,
    string? DisplayName,
    string? AvatarUrl,
    string? Bio,
    UserStatus Status,
    DateTime? LastSeen
);

public record UpdateProfileDto(string? DisplayName, string? Bio, string? AvatarUrl);

public record UserSearchResultDto(Guid Id, string Username, string? DisplayName, string? AvatarUrl);
