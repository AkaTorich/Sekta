using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Sekta.Shared;
using Sekta.Shared.DTOs;

namespace Sekta.Client.Services;

public interface IAuthService
{
    string? AccessToken { get; }
    UserDto? CurrentUser { get; }
    bool IsAuthenticated { get; }
    event Action<UserDto?>? AuthStateChanged;
    Task<bool> Login(string email, string password);
    Task<bool> Register(string username, string email, string password, string? displayName);
    Task Logout();
    Task<bool> TryAutoLogin();
}

public class AuthService : IAuthService
{
    private const string AccessTokenKey = "access_token";
    private const string RefreshTokenKey = "refresh_token";

    private readonly ISettingsService _settingsService;
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;

    public string? AccessToken { get; private set; }
    public UserDto? CurrentUser { get; private set; }
    public bool IsAuthenticated => AccessToken is not null && CurrentUser is not null;

    public event Action<UserDto?>? AuthStateChanged;

    public AuthService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        _httpClient = new HttpClient();

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };
    }

    private string Url(string endpoint) => $"{_settingsService.ServerUrl.TrimEnd('/')}{endpoint}";

    public async Task<bool> Login(string email, string password)
    {
        var dto = new LoginDto(email, password);
        var response = await _httpClient.PostAsJsonAsync(Url($"{ApiRoutes.Auth}/login"), dto, _jsonOptions);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Server returned {(int)response.StatusCode}: {body}");
        }

        var authResponse = await response.Content.ReadFromJsonAsync<AuthResponseDto>(_jsonOptions);
        if (authResponse is null)
            throw new InvalidOperationException("Empty response from server.");

        await StoreAuthState(authResponse);
        return true;
    }

    public async Task<bool> Register(string username, string email, string password, string? displayName)
    {
        var dto = new RegisterDto(username, email, password, displayName);
        var response = await _httpClient.PostAsJsonAsync(Url($"{ApiRoutes.Auth}/register"), dto, _jsonOptions);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Server returned {(int)response.StatusCode}: {body}");
        }

        var authResponse = await response.Content.ReadFromJsonAsync<AuthResponseDto>(_jsonOptions);
        if (authResponse is null)
            throw new InvalidOperationException("Empty response from server.");

        await StoreAuthState(authResponse);
        return true;
    }

    public async Task Logout()
    {
        AccessToken = null;
        CurrentUser = null;

        SecureStorage.Default.Remove(AccessTokenKey);
        SecureStorage.Default.Remove(RefreshTokenKey);

        AuthStateChanged?.Invoke(null);

        await Task.CompletedTask;
    }

    public async Task<bool> TryAutoLogin()
    {
        try
        {
            var refreshToken = await SecureStorage.Default.GetAsync(RefreshTokenKey);
            if (string.IsNullOrEmpty(refreshToken))
                return false;

            var dto = new RefreshTokenDto(refreshToken);
            var response = await _httpClient.PostAsJsonAsync(Url($"{ApiRoutes.Auth}/refresh"), dto, _jsonOptions);

            if (!response.IsSuccessStatusCode)
            {
                SecureStorage.Default.Remove(AccessTokenKey);
                SecureStorage.Default.Remove(RefreshTokenKey);
                return false;
            }

            var authResponse = await response.Content.ReadFromJsonAsync<AuthResponseDto>(_jsonOptions);
            if (authResponse is null)
                return false;

            await StoreAuthState(authResponse);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task StoreAuthState(AuthResponseDto authResponse)
    {
        AccessToken = authResponse.AccessToken;
        CurrentUser = authResponse.User;

        await SecureStorage.Default.SetAsync(AccessTokenKey, authResponse.AccessToken);
        await SecureStorage.Default.SetAsync(RefreshTokenKey, authResponse.RefreshToken);

        AuthStateChanged?.Invoke(CurrentUser);
    }
}
