using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sekta.Client.Services;
using Sekta.Shared;
using Sekta.Shared.DTOs;
using Sekta.Shared.Enums;

namespace Sekta.Client.ViewModels;

public partial class GroupInfoViewModel : ObservableObject, IQueryAttributable
{
    private readonly IApiService _apiService;
    private readonly IAuthService _authService;

    public GroupInfoViewModel(IApiService apiService, IAuthService authService)
    {
        _apiService = apiService;
        _authService = authService;
    }

    [ObservableProperty]
    private Guid _chatId;

    [ObservableProperty]
    private string _chatTitle = string.Empty;

    [ObservableProperty]
    private ObservableCollection<ChatMemberDto> _members = [];

    [ObservableProperty]
    private ObservableCollection<UserDto> _contacts = [];

    [ObservableProperty]
    private bool _isAddingMembers;

    [ObservableProperty]
    private bool _isOwner;

    [ObservableProperty]
    private string? _groupAvatarUrl;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("chatId", out var chatIdObj))
        {
            if (chatIdObj is string chatIdStr && Guid.TryParse(chatIdStr, out var id))
                ChatId = id;
            else if (chatIdObj is Guid guid)
                ChatId = guid;
        }

        if (query.TryGetValue("chatTitle", out var titleObj) && titleObj is string title)
            ChatTitle = title;

        _ = LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        await LoadMembersAsync();
        await LoadChatInfoAsync();
    }

    private async Task LoadChatInfoAsync()
    {
        try
        {
            var chat = await _apiService.GetAsync<ChatDto>($"{ApiRoutes.Chats}/{ChatId}");
            if (chat is not null)
            {
                GroupAvatarUrl = chat.AvatarUrl;
                if (!string.IsNullOrEmpty(chat.Title))
                    ChatTitle = chat.Title;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load chat info: {ex.Message}");
        }
    }

    private async Task LoadMembersAsync()
    {
        try
        {
            var members = await _apiService.GetAsync<List<ChatMemberDto>>(
                $"{ApiRoutes.Chats}/{ChatId}/members");
            if (members is not null)
            {
                Members = new ObservableCollection<ChatMemberDto>(members);
                var myId = _authService.CurrentUser?.Id;
                IsOwner = members.Any(m => m.UserId == myId && m.Role == ChatMemberRole.Owner);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load members: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task ShowAddMembersAsync()
    {
        IsAddingMembers = true;
        try
        {
            var contacts = await _apiService.GetAsync<List<UserDto>>($"{ApiRoutes.Users}/contacts");
            if (contacts is not null)
            {
                // Filter out users already in the group
                var memberIds = Members.Select(m => m.UserId).ToHashSet();
                var available = contacts.Where(c => !memberIds.Contains(c.Id)).ToList();
                Contacts = new ObservableCollection<UserDto>(available);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load contacts: {ex.Message}");
        }
    }

    [RelayCommand]
    private void HideAddMembers()
    {
        IsAddingMembers = false;
        Contacts.Clear();
    }

    [RelayCommand]
    private async Task AddMemberAsync(UserDto user)
    {
        if (user is null) return;

        try
        {
            await _apiService.PostAsync($"{ApiRoutes.Chats}/{ChatId}/members/{user.Id}");
            Contacts.Remove(user);
            await LoadMembersAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to add member: {ex.Message}");
            await Shell.Current.DisplayAlert("Error", "Failed to add member to group.", "OK");
        }
    }

    [RelayCommand]
    private async Task RemoveMemberAsync(ChatMemberDto member)
    {
        if (member is null) return;

        var confirm = await Shell.Current.DisplayAlert(
            "Remove Member",
            $"Remove {member.DisplayName ?? member.Username} from the group?",
            "Remove", "Cancel");

        if (!confirm) return;

        try
        {
            await _apiService.DeleteAsync($"{ApiRoutes.Chats}/{ChatId}/members/{member.UserId}");
            Members.Remove(member);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to remove member: {ex.Message}");
            await Shell.Current.DisplayAlert("Error", "Failed to remove member.", "OK");
        }
    }

    [RelayCommand]
    private async Task ChangeAvatarAsync()
    {
        if (!IsOwner) return;

        try
        {
            var result = await MediaPicker.Default.PickPhotoAsync(new MediaPickerOptions
            {
                Title = "Select group avatar"
            });

            if (result is null) return;

            await using var stream = await result.OpenReadAsync();
            var (url, _, _) = await _apiService.UploadFileAsync(stream, result.FileName);

            await _apiService.PutAsync($"{ApiRoutes.Chats}/{ChatId}", new UpdateGroupChatDto(null, url));
            GroupAvatarUrl = url;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to change avatar: {ex.Message}");
            await Shell.Current.DisplayAlert("Error", "Failed to update group avatar.", "OK");
        }
    }

    [RelayCommand]
    private async Task GoBackAsync()
    {
        await Shell.Current.GoToAsync("..");
    }
}
