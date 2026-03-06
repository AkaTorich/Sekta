namespace Sekta.Client.Services;

public interface INotificationService
{
    /// <summary>
    /// Initializes push notification registration and requests platform permissions.
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Registers the current device token with the server.
    /// </summary>
    Task RegisterDeviceAsync();

    /// <summary>
    /// Unregisters the device from push notifications on the server.
    /// </summary>
    Task UnregisterDeviceAsync();

    /// <summary>
    /// Shows a local notification (used as fallback or when app is in foreground).
    /// </summary>
    Task ShowLocalNotificationAsync(string title, string body);

    /// <summary>
    /// Whether push notifications are currently registered.
    /// </summary>
    bool IsRegistered { get; }
}
