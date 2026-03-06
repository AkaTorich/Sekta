using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Sekta.Client.Services;

public interface IApiService
{
    Task<T?> GetAsync<T>(string endpoint);
    Task<T?> PostAsync<T>(string endpoint, object? body = null);
    Task PostAsync(string endpoint, object? body = null);
    Task<T?> PutAsync<T>(string endpoint, object? body = null);
    Task PutAsync(string endpoint, object? body = null);
    Task DeleteAsync(string endpoint);
    Task<(string url, string fileName, long size)> UploadFileAsync(Stream stream, string fileName);
}

public class ApiService : IApiService
{
    private readonly IAuthService _authService;
    private readonly ISettingsService _settingsService;
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;

    public ApiService(IAuthService authService, ISettingsService settingsService)
    {
        _authService = authService;
        _settingsService = settingsService;
        _httpClient = new HttpClient();

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };
    }

    public async Task<T?> GetAsync<T>(string endpoint)
    {
        var request = CreateRequest(HttpMethod.Get, endpoint);
        return await SendAsync<T>(request);
    }

    public async Task<T?> PostAsync<T>(string endpoint, object? body = null)
    {
        var request = CreateRequest(HttpMethod.Post, endpoint);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: _jsonOptions);
        }

        return await SendAsync<T>(request);
    }

    public async Task PostAsync(string endpoint, object? body = null)
    {
        var request = CreateRequest(HttpMethod.Post, endpoint);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: _jsonOptions);
        }

        var response = await SendRequestAsync(request);
        response.EnsureSuccessStatusCode();
    }

    public async Task<T?> PutAsync<T>(string endpoint, object? body = null)
    {
        var request = CreateRequest(HttpMethod.Put, endpoint);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: _jsonOptions);
        }

        return await SendAsync<T>(request);
    }

    public async Task PutAsync(string endpoint, object? body = null)
    {
        var request = CreateRequest(HttpMethod.Put, endpoint);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: _jsonOptions);
        }

        var response = await SendRequestAsync(request);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteAsync(string endpoint)
    {
        var request = CreateRequest(HttpMethod.Delete, endpoint);
        var response = await SendRequestAsync(request);
        response.EnsureSuccessStatusCode();
    }

    public async Task<(string url, string fileName, long size)> UploadFileAsync(Stream stream, string fileName)
    {
        var request = CreateRequest(HttpMethod.Post, $"{Sekta.Shared.ApiRoutes.Files}/upload");

        using var content = new MultipartFormDataContent();
        var streamContent = new StreamContent(stream);
        content.Add(streamContent, "file", fileName);
        request.Content = content;

        var response = await SendRequestAsync(request);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<UploadResult>(_jsonOptions);

        return (result?.Url ?? string.Empty, result?.FileName ?? fileName, result?.Size ?? 0);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string endpoint)
    {
        var url = $"{_settingsService.ServerUrl.TrimEnd('/')}{endpoint}";
        var request = new HttpRequestMessage(method, url);

        if (!string.IsNullOrEmpty(_authService.AccessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _authService.AccessToken);
        }

        return request;
    }

    private async Task<HttpResponseMessage> SendRequestAsync(HttpRequestMessage request)
    {
        var response = await _httpClient.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            var refreshed = await _authService.TryAutoLogin();
            if (refreshed)
            {
                var retryRequest = await CloneRequest(request);
                retryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _authService.AccessToken);
                response = await _httpClient.SendAsync(retryRequest);
            }
        }

        return response;
    }

    private async Task<T?> SendAsync<T>(HttpRequestMessage request)
    {
        var response = await SendRequestAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(_jsonOptions);
    }

    private static async Task<HttpRequestMessage> CloneRequest(HttpRequestMessage original)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri);

        if (original.Content is not null)
        {
            var content = await original.Content.ReadAsByteArrayAsync();
            clone.Content = new ByteArrayContent(content);

            foreach (var header in original.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        foreach (var header in original.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return clone;
    }

    private record UploadResult(string Url, string FileName, long Size);
}
