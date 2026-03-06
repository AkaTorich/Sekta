using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sekta.Client.Services;
using Sekta.Shared;
using Sekta.Shared.DTOs;

namespace Sekta.Client.ViewModels;

public partial class UserProfileViewModel : ObservableObject, IQueryAttributable
{
    private readonly IApiService _apiService;

    public UserProfileViewModel(IApiService apiService)
    {
        _apiService = apiService;
    }

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string? _avatarUrl;

    [ObservableProperty]
    private string _bio = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    private Guid _userId;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("userId", out var idObj) && Guid.TryParse(idObj?.ToString(), out var id))
        {
            _userId = id;
            _ = LoadUserAsync();
        }
    }

    private async Task LoadUserAsync()
    {
        try
        {
            IsLoading = true;
            var user = await _apiService.GetAsync<UserDto>($"{ApiRoutes.Users}/{_userId}");
            if (user is not null)
            {
                Username = user.Username;
                DisplayName = user.DisplayName ?? user.Username;
                AvatarUrl = user.AvatarUrl;
                Bio = user.Bio ?? string.Empty;
                StatusText = user.Status == Shared.Enums.UserStatus.Online
                    ? "online"
                    : user.LastSeen.HasValue
                        ? $"last seen {user.LastSeen.Value:g}"
                        : "offline";
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load user profile: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task CloseAsync()
    {
        await Shell.Current.GoToAsync("..");
    }
}
