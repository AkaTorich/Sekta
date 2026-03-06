namespace Sekta.Client.Services;

public interface ISettingsService
{
    string ServerUrl { get; set; }
    bool DarkMode { get; set; }
    bool NotificationsEnabled { get; set; }
}

public class SettingsService : ISettingsService
{
    private const string ServerUrlKey = "server_url";
    private const string DarkModeKey = "dark_mode";
    private const string NotificationsEnabledKey = "notifications_enabled";

    private const string DefaultServerUrl = "http://94.158.246.172:5000";

    public string ServerUrl
    {
        get => Preferences.Default.Get(ServerUrlKey, DefaultServerUrl);
        set => Preferences.Default.Set(ServerUrlKey, value);
    }

    public bool DarkMode
    {
        get => Preferences.Default.Get(DarkModeKey, true);
        set => Preferences.Default.Set(DarkModeKey, value);
    }

    public bool NotificationsEnabled
    {
        get => Preferences.Default.Get(NotificationsEnabledKey, true);
        set => Preferences.Default.Set(NotificationsEnabledKey, value);
    }
}
