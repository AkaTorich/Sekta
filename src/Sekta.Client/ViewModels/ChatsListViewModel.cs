using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Sekta.Client.Services;
using Sekta.Shared;
using Sekta.Shared.DTOs;
using Sekta.Shared.Enums;

namespace Sekta.Client.ViewModels;

public enum SearchResultType { Chat, Group, Contact, User }

public record SearchResultItem(
    Guid Id,
    string Name,
    string? AvatarUrl,
    string? Subtitle,
    SearchResultType Type,
    Guid? ChatId);

public partial class ChatsListViewModel : ObservableObject
{
    private readonly IApiService _apiService;
    private readonly ISignalRService _signalRService;
    private readonly IAuthService _authService;
    private readonly INotificationSoundService _notificationSoundService;
    private readonly InAppNotificationService _inAppNotification;
    private readonly IServiceProvider _serviceProvider;
    private readonly IChatCacheService _chatCache;

    private List<ChatDto> _allChats = [];

    public ChatsListViewModel(IApiService apiService, ISignalRService signalRService, IAuthService authService,
        INotificationSoundService notificationSoundService, InAppNotificationService inAppNotification,
        IServiceProvider serviceProvider, IChatCacheService chatCache)
    {
        _apiService = apiService;
        _signalRService = signalRService;
        _authService = authService;
        _notificationSoundService = notificationSoundService;
        _inAppNotification = inAppNotification;
        _serviceProvider = serviceProvider;
        _chatCache = chatCache;

        _signalRService.MessageReceived += OnMessageReceived;
        _signalRService.MessagesRead += OnMessagesRead;
        _signalRService.ChatUpdated += OnChatUpdated;
        _signalRService.AddedToChat += OnAddedToChat;

        // Close active chat when theme changes so bubbles re-render on next open
        MessagingCenter.Subscribe<SettingsViewModel>(this, "ThemeChanged", _ =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                ActiveChatViewModel?.Cleanup();
                ActiveChatViewModel = null;
                IsChatOpen = false;
            });
        });
    }

    [ObservableProperty]
    private ObservableCollection<ChatDto> _chats = [];

    [ObservableProperty]
    private ObservableCollection<ChatFolderDto> _folders = [];

    [ObservableProperty]
    private ChatFolderDto? _selectedFolder;

    [ObservableProperty]
    private string _searchText = string.Empty;

    private CancellationTokenSource? _searchCts;

    partial void OnSearchTextChanged(string value)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;
        _ = DebounceSearchAsync(value, token);
    }

    private async Task DebounceSearchAsync(string text, CancellationToken token)
    {
        try
        {
            await Task.Delay(300, token);
            if (token.IsCancellationRequested) return;
            await SearchAsync(token);
        }
        catch (TaskCanceledException) { }
    }

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private ChatViewModel? _activeChatViewModel;

    [ObservableProperty]
    private bool _isChatOpen;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private ObservableCollection<SearchResultItem> _searchResults = [];

    /// <summary>
    /// True when running on desktop with split-view layout.
    /// Set by the page on startup.
    /// </summary>
    public bool IsDesktopMode { get; set; }

    partial void OnSelectedFolderChanged(ChatFolderDto? value)
    {
        ApplyFolderFilter();
    }

    private void ApplyFolderFilter()
    {
        if (SelectedFolder is null)
        {
            Chats = new ObservableCollection<ChatDto>(_allChats);
        }
        else
        {
            var chatIds = SelectedFolder.ChatIds.ToHashSet();
            Chats = new ObservableCollection<ChatDto>(_allChats.Where(c => chatIds.Contains(c.Id)));
        }
    }

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            IsRefreshing = true;

            var chatsTask = _apiService.GetAsync<List<ChatDto>>(ApiRoutes.Chats);
            var foldersTask = _apiService.GetAsync<List<ChatFolderDto>>(ApiRoutes.Folders);

            await Task.WhenAll(chatsTask, foldersTask);

            var chats = await chatsTask;
            var folders = await foldersTask;

            if (chats is not null)
            {
                _allChats = chats;
                _ = _chatCache.SaveChatsAsync(chats);
            }

            if (folders is not null)
                Folders = new ObservableCollection<ChatFolderDto>(folders);

            ApplyFolderFilter();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load chats: {ex.Message}");
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    [RelayCommand]
    private void SelectFolder(ChatFolderDto? folder)
    {
        // Toggle: tap same folder again to deselect (show all)
        SelectedFolder = SelectedFolder?.Id == folder?.Id ? null : folder;
    }

    [RelayCommand]
    private async Task CreateFolderAsync()
    {
        var name = await Shell.Current.DisplayPromptAsync("New Folder", "Enter folder name (max 10 chars):",
            maxLength: 10, keyboard: Keyboard.Text);

        if (string.IsNullOrWhiteSpace(name)) return;

        try
        {
            var dto = new CreateFolderDto(name.Trim(), null);
            var created = await _apiService.PostAsync<ChatFolderDto>(ApiRoutes.Folders, dto);
            if (created is not null)
                Folders.Add(created);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to create folder: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task DeleteFolderAsync(ChatFolderDto folder)
    {
        if (folder is null) return;
        var confirm = await Shell.Current.DisplayAlert("Delete Folder",
            $"Delete folder '{folder.Name}'?", "Delete", "Cancel");
        if (!confirm) return;

        try
        {
            await _apiService.DeleteAsync($"{ApiRoutes.Folders}/{folder.Id}");
            Folders.Remove(folder);
            if (SelectedFolder?.Id == folder.Id)
                SelectedFolder = null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to delete folder: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task AddChatToFolderAsync(ChatDto chat)
    {
        if (chat is null || Folders.Count == 0) return;

        var folderNames = Folders.Select(f => f.Name).ToArray();
        var chosen = await Shell.Current.DisplayActionSheet("Add to folder", "Cancel", null, folderNames);
        if (chosen is null or "Cancel") return;

        var folder = Folders.FirstOrDefault(f => f.Name == chosen);
        if (folder is null) return;

        try
        {
            await _apiService.PostAsync($"{ApiRoutes.Folders}/{folder.Id}/chats/{chat.Id}");

            // Update local folder DTO
            var idx = Folders.IndexOf(folder);
            if (!folder.ChatIds.Contains(chat.Id))
            {
                var updatedIds = new List<Guid>(folder.ChatIds) { chat.Id };
                Folders[idx] = folder with { ChatIds = updatedIds };
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to add chat to folder: {ex.Message}");
        }
    }

    public async Task AddChatToSpecificFolderAsync(ChatDto chat, ChatFolderDto folder)
    {
        try
        {
            await _apiService.PostAsync($"{ApiRoutes.Folders}/{folder.Id}/chats/{chat.Id}");

            var idx = Folders.IndexOf(folder);
            if (idx >= 0 && !folder.ChatIds.Contains(chat.Id))
            {
                var updatedIds = new List<Guid>(folder.ChatIds) { chat.Id };
                Folders[idx] = folder with { ChatIds = updatedIds };
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to add chat to folder: {ex.Message}");
        }
    }

    public async Task RemoveChatFromFolderAsync(ChatDto chat)
    {
        if (SelectedFolder is null) return;
        try
        {
            await _apiService.DeleteAsync($"{ApiRoutes.Folders}/{SelectedFolder.Id}/chats/{chat.Id}");

            var idx = Folders.IndexOf(SelectedFolder);
            if (idx >= 0)
            {
                var updatedIds = new List<Guid>(SelectedFolder.ChatIds);
                updatedIds.Remove(chat.Id);
                var updated = SelectedFolder with { ChatIds = updatedIds };
                Folders[idx] = updated;
                SelectedFolder = updated;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to remove chat from folder: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task OpenChatAsync(ChatDto chat)
    {
        if (chat is null) return;

        if (IsDesktopMode)
        {
            // Cleanup previous embedded chat to stop ghost MarkAsRead calls
            ActiveChatViewModel?.Cleanup();

            // Desktop: load chat in the embedded panel
            var chatVm = _serviceProvider.GetRequiredService<ChatViewModel>();
            chatVm.LoadChat(chat.Id, chat.Title ?? "Chat");
            ActiveChatViewModel = chatVm;
            IsChatOpen = true;
        }
        else
        {
            // Mobile: navigate to ChatPage
            var url = $"chat?chatId={chat.Id}&chatTitle={Uri.EscapeDataString(chat.Title ?? "Chat")}";
            await Shell.Current.GoToAsync(url);
        }
    }

    [RelayCommand]
    private async Task NewChatAsync()
    {
        await Shell.Current.GoToAsync("//main/contacts");
    }

    [RelayCommand]
    private async Task CreateGroupAsync()
    {
        await Shell.Current.GoToAsync("creategroup");
    }

    [RelayCommand]
    private async Task OpenProfileAsync()
    {
        await Shell.Current.GoToAsync("profile-standalone");
    }

    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        await Shell.Current.GoToAsync("settings-standalone");
    }

    [RelayCommand]
    private async Task SearchAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            IsSearching = false;
            await RefreshAsync(cancellationToken);
            return;
        }

        IsSearching = true;
        var results = new List<SearchResultItem>();
        var q = SearchText.Trim();

        try
        {
            // Parallel: search chats (includes groups), contacts, users
            var chatsTask = _apiService.GetAsync<List<ChatDto>>(ApiRoutes.Chats);
            var contactsTask = _apiService.GetAsync<List<UserDto>>($"{ApiRoutes.Users}/contacts");
            var usersTask = _apiService.GetAsync<List<UserSearchResultDto>>($"{ApiRoutes.Users}/search?q={Uri.EscapeDataString(q)}");

            await Task.WhenAll(chatsTask, contactsTask, usersTask);

            var chats = await chatsTask ?? [];
            var contacts = await contactsTask ?? [];
            var users = await usersTask ?? [];

            var seenIds = new HashSet<Guid>();
            var isExact = false;

            // 1. Groups matching query (skip private chats — contacts/users cover them)
            foreach (var chat in chats.Where(c => c.Type == ChatType.Group && c.Title is not null &&
                         c.Title.Contains(q, StringComparison.OrdinalIgnoreCase)))
            {
                if (chat.Title!.Equals(q, StringComparison.OrdinalIgnoreCase))
                    isExact = true;

                results.Add(new SearchResultItem(chat.Id, chat.Title!, chat.AvatarUrl, "Group", SearchResultType.Group, chat.Id));
                seenIds.Add(chat.Id);
            }

            // 2. Contacts matching query
            foreach (var contact in contacts.Where(c =>
                         (c.Username.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                         (c.DisplayName is not null && c.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase))))
            {
                if (seenIds.Contains(contact.Id)) continue;

                if (contact.Username.Equals(q, StringComparison.OrdinalIgnoreCase))
                    isExact = true;

                // Find existing private chat with this contact
                var existingChat = chats.FirstOrDefault(c =>
                    c.Type == ChatType.Private && c.Title == (contact.DisplayName ?? contact.Username));
                results.Add(new SearchResultItem(contact.Id, contact.DisplayName ?? contact.Username,
                    contact.AvatarUrl, "Contact", SearchResultType.Contact, existingChat?.Id));
                seenIds.Add(contact.Id);
            }

            // 3. Users from server search (not yet in contacts/chats)
            foreach (var user in users)
            {
                if (seenIds.Contains(user.Id)) continue;

                if (user.Username.Equals(q, StringComparison.OrdinalIgnoreCase))
                    isExact = true;

                results.Add(new SearchResultItem(user.Id, user.DisplayName ?? user.Username,
                    user.AvatarUrl, $"@{user.Username}", SearchResultType.User, null));
                seenIds.Add(user.Id);
            }

            // Limit: exact match = 1, otherwise max 25
            if (isExact)
                results = results.Where(r =>
                    r.Name.Equals(q, StringComparison.OrdinalIgnoreCase)).Take(1).ToList();
            else
                results = results.Take(25).ToList();

            SearchResults = new ObservableCollection<SearchResultItem>(results);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Search failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task OpenSearchResultAsync(SearchResultItem item)
    {
        if (item is null) return;

        if (item.ChatId.HasValue)
        {
            // Open existing chat
            var chatType = item.Type == SearchResultType.Group ? ChatType.Group : ChatType.Private;
            var chat = new ChatDto(item.ChatId.Value, chatType, item.Name, item.AvatarUrl, null, 0, DateTime.UtcNow);
            await OpenChatAsync(chat);
        }
        else
        {
            // No existing chat — create a private chat first via API
            var created = await _apiService.PostAsync<ChatDto>($"{ApiRoutes.Chats}/private/{item.Id}");
            if (created is null) return;
            await OpenChatAsync(created);
        }

        // Clear search
        SearchText = string.Empty;
        IsSearching = false;
    }

    [RelayCommand]
    private async Task PinChatAsync(ChatDto chat)
    {
        if (chat is null) return;
        try
        {
            var isPinned = await _apiService.PostAsync<bool>($"{ApiRoutes.Chats}/{chat.Id}/pin");

            // Update in _allChats
            var allIdx = _allChats.FindIndex(c => c.Id == chat.Id);
            if (allIdx >= 0)
                _allChats[allIdx] = _allChats[allIdx] with { IsPinned = isPinned };

            // Re-sort: pinned first, then by last message time
            _allChats = _allChats
                .OrderByDescending(c => c.IsPinned)
                .ThenByDescending(c => c.LastMessage?.CreatedAt ?? c.CreatedAt)
                .ToList();

            ApplyFolderFilter();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to pin chat: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ToggleNotifications()
    {
        _notificationSoundService.IsEnabled = !_notificationSoundService.IsEnabled;
        var status = _notificationSoundService.IsEnabled ? "enabled" : "disabled";
        Shell.Current.DisplayAlert("Notifications", $"Notifications {status}", "OK");
    }

    public bool IsNotificationsEnabled => _notificationSoundService.IsEnabled;

    [RelayCommand]
    private async Task DeleteChatAsync(ChatDto chat)
    {
        var confirm = await Shell.Current.DisplayAlert("Delete Chat",
            $"Are you sure you want to delete '{chat.Title}'?", "Delete", "Cancel");
        if (confirm)
        {
            Chats.Remove(chat);
        }
    }

    public async Task OnAppearingAsync()
    {
        // Show cached chats instantly
        if (_allChats.Count == 0)
        {
            try
            {
                var cached = await _chatCache.GetChatsAsync();
                if (cached.Count > 0)
                {
                    _allChats = cached;
                    ApplyFolderFilter();
                }
            }
            catch { }
        }

        // Then sync with server
        await RefreshAsync(CancellationToken.None);
    }

    private void OnMessageReceived(MessageDto message)
    {
        // Play notification sound and show banner for messages from other users
        if (message.SenderId != _authService.CurrentUser?.Id)
        {
            _ = _notificationSoundService.PlayNewMessageSoundAsync();
            _inAppNotification.ShowMessageBanner(message);
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            var existingChat = Chats.FirstOrDefault(c => c.Id == message.ChatId);

            if (existingChat is not null)
            {
                var index = Chats.IndexOf(existingChat);
                var isFromOther = message.SenderId != _authService.CurrentUser?.Id;
                var newUnread = isFromOther ? existingChat.UnreadCount + 1 : existingChat.UnreadCount;
                var updated = existingChat with { LastMessage = message, UnreadCount = newUnread };
                Chats.RemoveAt(index);

                // Insert after pinned chats (unless this chat itself is pinned)
                if (updated.IsPinned)
                {
                    Chats.Insert(0, updated);
                }
                else
                {
                    var insertIdx = Chats.Count(c => c.IsPinned);
                    Chats.Insert(insertIdx, updated);
                }
            }

            // Also update _allChats
            var allIdx = _allChats.FindIndex(c => c.Id == message.ChatId);
            if (allIdx >= 0)
            {
                var isFromOther = message.SenderId != _authService.CurrentUser?.Id;
                var existing = _allChats[allIdx];
                var newUnread = isFromOther ? existing.UnreadCount + 1 : existing.UnreadCount;
                var updated = existing with { LastMessage = message, UnreadCount = newUnread };
                _allChats.RemoveAt(allIdx);

                if (updated.IsPinned)
                {
                    _allChats.Insert(0, updated);
                }
                else
                {
                    var insertIdx = _allChats.Count(c => c.IsPinned);
                    _allChats.Insert(insertIdx, updated);
                }

                _ = _chatCache.UpsertChatAsync(updated);
            }
        });
    }

    private void OnMessagesRead(Guid chatId, Guid userId)
    {
        if (userId != _authService.CurrentUser?.Id) return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            // Update displayed Chats
            var idx = Chats.IndexOf(Chats.FirstOrDefault(c => c.Id == chatId)!);
            if (idx >= 0)
                Chats[idx] = Chats[idx] with { UnreadCount = 0 };

            // Update _allChats
            var allIdx = _allChats.FindIndex(c => c.Id == chatId);
            if (allIdx >= 0)
            {
                _allChats[allIdx] = _allChats[allIdx] with { UnreadCount = 0 };
                _ = _chatCache.UpsertChatAsync(_allChats[allIdx]);
            }
        });
    }

    private void OnChatUpdated(Guid chatId, string? title, string? avatarUrl)
    {
        _ = _chatCache.UpdateChatInfoAsync(chatId, title, avatarUrl);

        MainThread.BeginInvokeOnMainThread(() =>
        {
            // Update in displayed Chats
            var chat = Chats.FirstOrDefault(c => c.Id == chatId);
            if (chat is not null)
            {
                var idx = Chats.IndexOf(chat);
                Chats[idx] = chat with
                {
                    Title = title ?? chat.Title,
                    AvatarUrl = avatarUrl ?? chat.AvatarUrl
                };
            }

            // Update in _allChats
            var allIdx = _allChats.FindIndex(c => c.Id == chatId);
            if (allIdx >= 0)
            {
                var existing = _allChats[allIdx];
                _allChats[allIdx] = existing with
                {
                    Title = title ?? existing.Title,
                    AvatarUrl = avatarUrl ?? existing.AvatarUrl
                };
            }
        });
    }

    private void OnAddedToChat(ChatDto chat)
    {
        _ = _chatCache.UpsertChatAsync(chat);

        MainThread.BeginInvokeOnMainThread(() =>
        {
            // Only add if not already present
            if (_allChats.Any(c => c.Id == chat.Id)) return;

            _allChats.Insert(0, chat);
            ApplyFolderFilter();
        });
    }
}
