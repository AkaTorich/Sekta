using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sekta.Client.Services;
using Sekta.Shared;
using Sekta.Shared.DTOs;

namespace Sekta.Client.ViewModels;

public partial class RegisterViewModel : ObservableObject
{
    private readonly IAuthService _authService;
    private readonly ISignalRService _signalRService;
    private readonly INotificationService _notificationService;
    private readonly IApiService _apiService;
    private readonly IChatCacheService _chatCache;

    public RegisterViewModel(IAuthService authService, ISignalRService signalRService,
        INotificationService notificationService, IApiService apiService, IChatCacheService chatCache)
    {
        _authService = authService;
        _signalRService = signalRService;
        _notificationService = notificationService;
        _apiService = apiService;
        _chatCache = chatCache;
    }

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [RelayCommand]
    private async Task RegisterAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Please fill in all required fields.";
            return;
        }

        try
        {
            IsLoading = true;
            ErrorMessage = null;

            var displayName = string.IsNullOrWhiteSpace(DisplayName) ? null : DisplayName.Trim();
            await _authService.Register(Username.Trim(), Email.Trim(), Password, displayName);

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
            ErrorMessage = $"Registration failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task GoToLoginAsync()
    {
        await Shell.Current.GoToAsync("//login");
    }
}
