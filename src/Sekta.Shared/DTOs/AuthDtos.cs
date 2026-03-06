namespace Sekta.Shared.DTOs;

public record RegisterDto(string Username, string Email, string Password, string? DisplayName);

public record LoginDto(string Email, string Password);

public record AuthResponseDto(string AccessToken, string RefreshToken, UserDto User);

public record RefreshTokenDto(string RefreshToken);
