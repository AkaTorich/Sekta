using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sekta.Client.Services;
using Sekta.Shared;
using Sekta.Shared.DTOs;

namespace Sekta.Client.ViewModels;

public partial class ContactsViewModel : ObservableObject
{
    private readonly IApiService _apiService;

    public ContactsViewModel(IApiService apiService)
    {
        _apiService = apiService;
    }

    [ObservableProperty]
    private ObservableCollection<UserDto> _contacts = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<UserSearchResultDto> _searchResults = [];

    [ObservableProperty]
    private bool _isSearching;

    [RelayCommand]
    private async Task SearchUsersAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            SearchResults.Clear();
            IsSearching = false;
            return;
        }

        try
        {
            var results = await _apiService.GetAsync<List<UserSearchResultDto>>(
                $"{ApiRoutes.Users}/search?q={Uri.EscapeDataString(SearchText.Trim())}");

            if (results is not null)
            {
                SearchResults = new ObservableCollection<UserSearchResultDto>(results);
            }

            IsSearching = SearchResults.Count > 0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to search users: {ex.Message}");
            IsSearching = false;
        }
    }

    [RelayCommand]
    private async Task AddContactAsync(UserSearchResultDto user)
    {
        if (user is null) return;

        // Add contact (ignore if already exists)
        try
        {
            await _apiService.PostAsync<object>($"{ApiRoutes.Users}/contacts/{user.Id}");
        }
        catch { /* contact may already exist */ }

        await LoadContactsAsync();
        SearchResults.Remove(user);

        // Always open chat
        try
        {
            var chat = await _apiService.PostAsync<ChatDto>(
                $"{ApiRoutes.Chats}/private/{user.Id}");

            if (chat is not null)
            {
                await NavigateToChatAsync(chat.Id, user.Username ?? "Chat");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to open chat: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task RemoveContactAsync(UserDto user)
    {
        if (user is null) return;

        try
        {
            await _apiService.DeleteAsync($"{ApiRoutes.Users}/contacts/{user.Id}");
            Contacts.Remove(user);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to remove contact: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task StartChatAsync(UserDto user)
    {
        if (user is null) return;

        try
        {
            var chat = await _apiService.PostAsync<ChatDto>(
                $"{ApiRoutes.Chats}/private/{user.Id}");

            if (chat is not null)
            {
                await NavigateToChatAsync(chat.Id, user.Username ?? "Chat");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to start chat: {ex.Message}");
        }
    }

    private static async Task NavigateToChatAsync(Guid chatId, string title)
    {
        // Switch to Chats tab first, then push chat page
        await Shell.Current.GoToAsync($"//main/chats");
        await Shell.Current.GoToAsync($"chat?chatId={chatId}&chatTitle={Uri.EscapeDataString(title)}");
    }

    public async Task OnAppearingAsync()
    {
        await LoadContactsAsync();
    }

    public async Task LoadContactsAsync()
    {
        try
        {
            var contacts = await _apiService.GetAsync<List<UserDto>>($"{ApiRoutes.Users}/contacts");

            if (contacts is not null)
            {
                Contacts = new ObservableCollection<UserDto>(contacts);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load contacts: {ex.Message}");
        }
    }
}
