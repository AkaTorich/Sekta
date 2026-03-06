using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sekta.Client.Services;
using Sekta.Shared;
using Sekta.Shared.DTOs;

namespace Sekta.Client.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly IAuthService _authService;
    private readonly ISignalRService _signalRService;
    private readonly INotificationService _notificationService;
    private readonly IApiService _apiService;
    private readonly IChatCacheService _chatCache;

    public LoginViewModel(IAuthService authService, ISignalRService signalRService,
        INotificationService notificationService, IApiService apiService, IChatCacheService chatCache)
    {
        _authService = authService;
        _signalRService = signalRService;
        _notificationService = notificationService;
        _apiService = apiService;
        _chatCache = chatCache;
    }

    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [RelayCommand]
    private async Task LoginAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Please enter email and password.";
            return;
        }

        try
        {
            IsLoading = true;
            ErrorMessage = null;

            await _authService.Login(Email.Trim(), Password);

            await _signalRService.ConnectAsync(_authService.AccessToken!);
            await _notificationService.InitializeAsync();

            // Sync chats from server before showing the app
            try
            {
                var chats = await _apiService.GetAsync<List<ChatDto>>(ApiRoutes.Chats);
                if (chats is not null)
                    await _chatCache.SaveChatsAsync(chats);
            }
            catch { }

            await Shell.Current.GoToAsync("//main/chats");
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Login failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task GoToRegisterAsync()
    {
        await Shell.Current.GoToAsync("//register");
    }

    [RelayCommand]
    private async Task GoToSettingsAsync()
    {
        await Shell.Current.GoToAsync("settings-standalone");
    }
}
