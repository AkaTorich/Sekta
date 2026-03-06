using Microsoft.Extensions.Logging;
using Sekta.Shared;
using Sekta.Shared.DTOs;

namespace Sekta.Client.Services;

public class PushNotificationService : INotificationService
{
    private readonly IApiService _apiService;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<PushNotificationService> _logger;

    private string? _deviceToken;
    private static int _notificationId;

    public bool IsRegistered { get; private set; }

    public PushNotificationService(
        IApiService apiService,
        ISettingsService settingsService,
        ILogger<PushNotificationService> logger)
    {
        _apiService = apiService;
        _settingsService = settingsService;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        if (!_settingsService.NotificationsEnabled)
        {
            _logger.LogInformation("Notifications are disabled in settings, skipping initialization");
            return;
        }

        try
        {
            // Request notification permissions
            var permissionStatus = await RequestNotificationPermissionAsync();
            if (!permissionStatus)
            {
                _logger.LogWarning("Notification permission was denied");
                return;
            }

            // Obtain device token from platform-specific push service
            _deviceToken = await GetPlatformDeviceTokenAsync();

            if (!string.IsNullOrEmpty(_deviceToken))
            {
                await RegisterDeviceAsync();
            }
            else
            {
                _logger.LogWarning(
                    "Could not obtain device token. " +
                    "Push notifications will use local notification fallback.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize push notifications");
        }
    }

    public async Task RegisterDeviceAsync()
    {
        if (string.IsNullOrEmpty(_deviceToken))
        {
            _logger.LogWarning("No device token available for registration");
            return;
        }

        try
        {
            var platform = GetCurrentPlatform();
            var dto = new RegisterDeviceDto(_deviceToken, platform);

            await _apiService.PostAsync<object>(
                $"{ApiRoutes.Notifications}/register", dto);

            IsRegistered = true;
            _logger.LogInformation(
                "Device registered for push notifications (platform: {Platform})", platform);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register device for push notifications");
            IsRegistered = false;
        }
    }

    public async Task UnregisterDeviceAsync()
    {
        try
        {
            // Register with an empty token to effectively unregister
            var platform = GetCurrentPlatform();
            var dto = new RegisterDeviceDto(string.Empty, platform);

            await _apiService.PostAsync<object>(
                $"{ApiRoutes.Notifications}/register", dto);

            IsRegistered = false;
            _logger.LogInformation("Device unregistered from push notifications");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unregister device from push notifications");
        }
    }

    public async Task ShowLocalNotificationAsync(string title, string body)
    {
        // Local notification fallback using MAUI's built-in capabilities.
        // This works when the app is in the foreground or when push is unavailable.
        try
        {
            var id = Interlocked.Increment(ref _notificationId);

            // Use MainThread to ensure UI operations are on the correct thread
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                // Display an alert as a simple local notification fallback.
                // For production, integrate Plugin.LocalNotification or
                // platform-specific notification channels.
                if (Application.Current?.MainPage is not null)
                {
                    Application.Current.MainPage.DisplayAlert(title, body, "OK");
                }
            });

            _logger.LogInformation(
                "Local notification shown: {Title} - {Body}", title, body);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to show local notification");
        }
    }

    /// <summary>
    /// Requests notification permission from the platform.
    /// Returns true if permission was granted.
    /// </summary>
    private static async Task<bool> RequestNotificationPermissionAsync()
    {
#if ANDROID
        // Android 13+ (API 33) requires POST_NOTIFICATIONS permission
        var status = await Permissions.CheckStatusAsync<Permissions.PostNotifications>();
        if (status != PermissionStatus.Granted)
        {
            status = await Permissions.RequestAsync<Permissions.PostNotifications>();
        }
        return status == PermissionStatus.Granted;
#elif IOS
        // iOS permission is typically handled by the push registration itself.
        // Return true so the flow continues to token retrieval.
        return await Task.FromResult(true);
#else
        // Windows and other platforms — assume granted
        return await Task.FromResult(true);
#endif
    }

    /// <summary>
    /// Retrieves the platform-specific push device token.
    /// In production, this would come from Firebase (Android) or APNs (iOS).
    /// For now, returns a placeholder to demonstrate the registration flow.
    /// </summary>
    private async Task<string?> GetPlatformDeviceTokenAsync()
    {
        // In a production app, you would:
        // - Android: Use FirebaseMessaging.Instance.GetToken() via a platform-specific service
        // - iOS: Register with APNs via UIApplication.SharedApplication.RegisterForRemoteNotifications()
        //   and receive the token in AppDelegate
        // - Windows: Use WNS (Windows Notification Service) channel URI
        //
        // For now, generate a stable pseudo-token based on the device for testing purposes.
        // Replace this with actual platform integration when Firebase/APNs SDKs are added.

        await Task.CompletedTask;

        try
        {
            // Use a stable device identifier as a placeholder token
            var deviceId = Preferences.Default.Get("device_push_token", string.Empty);
            if (string.IsNullOrEmpty(deviceId))
            {
                deviceId = $"sekta-dev-{Guid.NewGuid():N}";
                Preferences.Default.Set("device_push_token", deviceId);
            }

            _logger.LogInformation(
                "Platform device token obtained (placeholder): {Token}", deviceId[..16] + "...");

            return deviceId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to obtain platform device token");
            return null;
        }
    }

    private static string GetCurrentPlatform()
    {
#if ANDROID
        return "android";
#elif IOS
        return "ios";
#elif MACCATALYST
        return "macos";
#elif WINDOWS
        return "windows";
#else
        return "unknown";
#endif
    }
}
