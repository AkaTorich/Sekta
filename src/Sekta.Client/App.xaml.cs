using Sekta.Client.Services;
using Sekta.Shared;
using Sekta.Shared.DTOs;

namespace Sekta.Client;

public partial class App : Application
{
    private readonly IAuthService _authService;
    private readonly ISignalRService _signalRService;
    private readonly INotificationService _notificationService;
    private readonly IApiService _apiService;
    private readonly IChatCacheService _chatCache;

    public App(IAuthService authService, ISignalRService signalRService,
        ISettingsService settingsService, INotificationService notificationService,
        IApiService apiService, IChatCacheService chatCache)
    {
        InitializeComponent();
        _authService = authService;
        _signalRService = signalRService;
        _notificationService = notificationService;
        _apiService = apiService;
        _chatCache = chatCache;

        // Apply saved theme on startup (default: Dark)
        UserAppTheme = settingsService.DarkMode ? AppTheme.Dark : AppTheme.Light;

        // Update window title when user logs in/out
        _authService.AuthStateChanged += user =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                var window = Windows.FirstOrDefault();
                if (window is not null)
                {
                    window.Title = user is not null
                        ? $"Sekta — {user.DisplayName ?? user.Username}"
                        : "Sekta Messenger";
                }
            });
        };
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new AppShell()) { Title = "Sekta Messenger" };
        window.Created += async (s, e) =>
        {
            DebugLog.Clear();
            DebugLog.Log("App startup: going to login");
            await Shell.Current.GoToAsync("//login");
        };
        return window;
    }
}
