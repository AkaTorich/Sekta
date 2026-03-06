using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sekta.Client.Services;
using Sekta.Shared;
using Sekta.Shared.DTOs;

namespace Sekta.Client.ViewModels;

public partial class ProfileViewModel : ObservableObject
{
    private readonly IAuthService _authService;
    private readonly IApiService _apiService;
    private readonly ISignalRService _signalRService;

    public ProfileViewModel(IAuthService authService, IApiService apiService, ISignalRService signalRService)
    {
        _authService = authService;
        _apiService = apiService;
        _signalRService = signalRService;
    }

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _bio = string.Empty;

    [ObservableProperty]
    private string? _avatarUrl;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public void LoadCurrentUser()
    {
        var user = _authService.CurrentUser;
        if (user is null) return;

        Username = user.Username;
        DisplayName = user.DisplayName ?? string.Empty;
        Bio = user.Bio ?? string.Empty;
        AvatarUrl = user.AvatarUrl;
        Email = string.Empty; // Email is not exposed on UserDto; kept for display binding
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            IsLoading = true;

            var dto = new UpdateProfileDto(
                DisplayName: string.IsNullOrWhiteSpace(DisplayName) ? null : DisplayName.Trim(),
                Bio: string.IsNullOrWhiteSpace(Bio) ? null : Bio.Trim(),
                AvatarUrl: AvatarUrl);

            await _apiService.PutAsync<object>($"{ApiRoutes.Users}/me", dto);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save profile: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ChangeAvatarAsync(CancellationToken cancellationToken)
    {
        try
        {
            var photo = await MediaPicker.Default.PickPhotoAsync(new MediaPickerOptions
            {
                Title = "Select avatar"
            });

            if (photo is null)
                return;

            IsLoading = true;

            await using var stream = await photo.OpenReadAsync();
            var (url, _, _) = await _apiService.UploadFileAsync(stream, photo.FileName);

            AvatarUrl = url;

            // Auto-save profile with new avatar
            await SaveAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to change avatar: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task CloseAsync()
    {
        await Shell.Current.GoToAsync("//main/chats");
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        try
        {
            await _signalRService.DisconnectAsync();
            await _authService.Logout();
            await Shell.Current.GoToAsync("//login");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to logout: {ex.Message}");
        }
    }
}
