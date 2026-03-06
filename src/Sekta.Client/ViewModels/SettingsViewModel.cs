using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sekta.Client.Services;

namespace Sekta.Client.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly INotificationService _notificationService;

    public SettingsViewModel(
        ISettingsService settingsService,
        INotificationService notificationService)
    {
        _settingsService = settingsService;
        _notificationService = notificationService;

        // Load current settings
        _serverUrl = _settingsService.ServerUrl;
        _darkMode = _settingsService.DarkMode;
        _notificationsEnabled = _settingsService.NotificationsEnabled;
    }

    [ObservableProperty]
    private string _serverUrl = string.Empty;

    [ObservableProperty]
    private bool _darkMode;

    [ObservableProperty]
    private bool _notificationsEnabled;

    partial void OnDarkModeChanged(bool value)
    {
        _settingsService.DarkMode = value;

        if (Application.Current is not null)
        {
            Application.Current.UserAppTheme = value ? AppTheme.Dark : AppTheme.Light;
        }

        MessagingCenter.Send(this, "ThemeChanged");
    }

    partial void OnNotificationsEnabledChanged(bool value)
    {
        _settingsService.NotificationsEnabled = value;

        // Register or unregister push notifications based on the toggle
        _ = TogglePushRegistrationAsync(value);
    }

    private async Task TogglePushRegistrationAsync(bool enabled)
    {
        try
        {
            if (enabled)
            {
                await _notificationService.InitializeAsync();
            }
            else
            {
                await _notificationService.UnregisterDeviceAsync();
            }
        }
        catch (Exception)
        {
            // Silently handle — the notification service logs errors internally
        }
    }

    [RelayCommand]
    private Task SaveAsync()
    {
        _settingsService.ServerUrl = ServerUrl;
        _settingsService.DarkMode = DarkMode;
        _settingsService.NotificationsEnabled = NotificationsEnabled;

        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task CloseAsync()
    {
        // Navigate back to wherever we came from (login page or chats)
        await Shell.Current.GoToAsync("..");
    }
}
