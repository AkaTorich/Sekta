using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sekta.Server.Data;

namespace Sekta.Server.Services;

public interface IPushNotificationService
{
    Task RegisterDevice(Guid userId, string token, string platform);
    Task SendPushNotification(Guid userId, string title, string body);
}

public class PushNotificationService : IPushNotificationService
{
    private readonly AppDbContext _db;
    private readonly HttpClient _httpClient;
    private readonly ILogger<PushNotificationService> _logger;
    private readonly IConfiguration _configuration;

    // Placeholder project ID — replace with actual Firebase project ID when integrating
    private const string DefaultFcmProjectId = "sekta-messenger";

    public PushNotificationService(
        AppDbContext db,
        HttpClient httpClient,
        ILogger<PushNotificationService> logger,
        IConfiguration configuration)
    {
        _db = db;
        _httpClient = httpClient;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task RegisterDevice(Guid userId, string token, string platform)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user is null)
        {
            _logger.LogWarning("Cannot register device: user {UserId} not found", userId);
            return;
        }

        user.DeviceToken = token;
        user.Platform = platform;
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Device registered for user {UserId}, platform: {Platform}",
            userId, platform);
    }

    public async Task SendPushNotification(Guid userId, string title, string body)
    {
        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user is null)
        {
            _logger.LogWarning("Cannot send push: user {UserId} not found", userId);
            return;
        }

        if (string.IsNullOrEmpty(user.DeviceToken))
        {
            _logger.LogDebug(
                "No device token for user {UserId}, skipping push notification", userId);
            return;
        }

        _logger.LogInformation(
            "Sending push notification to user {UserId} ({Platform}): {Title}",
            userId, user.Platform, title);

        try
        {
            await SendViaFcm(user.DeviceToken, user.Platform, title, body);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send push notification to user {UserId}", userId);
        }
    }

    private async Task SendViaFcm(string deviceToken, string? platform, string title, string body)
    {
        var projectId = _configuration["Firebase:ProjectId"] ?? DefaultFcmProjectId;
        var url = $"https://fcm.googleapis.com/v1/projects/{projectId}/messages:send";

        var message = new
        {
            message = new
            {
                token = deviceToken,
                notification = new
                {
                    title,
                    body
                },
                android = platform?.ToLowerInvariant() == "android" ? new
                {
                    priority = "high",
                    notification = new
                    {
                        channel_id = "sekta_messages",
                        sound = "default"
                    }
                } : null,
                apns = platform?.ToLowerInvariant() == "ios" ? new
                {
                    payload = new
                    {
                        aps = new
                        {
                            sound = "default",
                            badge = 1
                        }
                    }
                } : null
            }
        };

        var json = JsonSerializer.Serialize(message, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        // The Authorization header should carry a valid OAuth2 access token
        // obtained from Firebase Admin SDK. For now, read from configuration
        // so that integration is straightforward once credentials are available.
        var accessToken = _configuration["Firebase:AccessToken"];
        if (!string.IsNullOrEmpty(accessToken))
        {
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        }
        else
        {
            _logger.LogWarning(
                "Firebase access token is not configured. " +
                "Push notification will be logged but not delivered. " +
                "Set Firebase:AccessToken in configuration to enable delivery.");

            _logger.LogInformation(
                "Push payload (not sent): {Payload}", json);
            return;
        }

        var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync();
            _logger.LogError(
                "FCM returned {StatusCode}: {ResponseBody}",
                response.StatusCode, responseBody);
        }
        else
        {
            _logger.LogInformation("Push notification sent successfully via FCM");
        }
    }
}
