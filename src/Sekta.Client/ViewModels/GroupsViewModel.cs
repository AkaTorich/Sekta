using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Sekta.Client.Services;
using Sekta.Shared;
using Sekta.Shared.DTOs;
using Sekta.Shared.Enums;

namespace Sekta.Client.ViewModels;

public partial class GroupsViewModel : ObservableObject
{
    private readonly IApiService _apiService;
    private readonly ISignalRService _signalRService;
    private readonly IAuthService _authService;
    private readonly IServiceProvider _serviceProvider;

    public GroupsViewModel(IApiService apiService, ISignalRService signalRService, IAuthService authService,
        IServiceProvider serviceProvider)
    {
        _apiService = apiService;
        _signalRService = signalRService;
        _authService = authService;
        _serviceProvider = serviceProvider;
        _signalRService.MessageReceived += OnMessageReceived;
        _signalRService.MessagesRead += OnMessagesRead;
    }

    [ObservableProperty]
    private ObservableCollection<ChatDto> _groups = [];

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private ChatViewModel? _activeChatViewModel;

    [ObservableProperty]
    private bool _isChatOpen;

    public bool IsDesktopMode { get; set; }

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            IsRefreshing = true;
            var chats = await _apiService.GetAsync<List<ChatDto>>(ApiRoutes.Chats);
            if (chats is not null)
            {
                var groupChats = chats.Where(c => c.Type == ChatType.Group).ToList();
                Groups = new ObservableCollection<ChatDto>(groupChats);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load groups: {ex.Message}");
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    [RelayCommand]
    private async Task OpenGroupAsync(ChatDto group)
    {
        if (group is null) return;

        if (IsDesktopMode)
        {
            var chatVm = _serviceProvider.GetRequiredService<ChatViewModel>();
            chatVm.LoadChat(group.Id, group.Title ?? "Group");
            ActiveChatViewModel = chatVm;
            IsChatOpen = true;
        }
        else
        {
            await Shell.Current.GoToAsync($"//main/chats");
            var url = $"chat?chatId={group.Id}&chatTitle={Uri.EscapeDataString(group.Title ?? "Group")}";
            await Shell.Current.GoToAsync(url);
        }
    }

    [RelayCommand]
    private async Task CreateGroupAsync()
    {
        await Shell.Current.GoToAsync("creategroup");
    }

    private void OnMessageReceived(MessageDto message)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var existing = Groups.FirstOrDefault(g => g.Id == message.ChatId);
            if (existing is not null)
            {
                var index = Groups.IndexOf(existing);
                var isFromOther = message.SenderId != _authService.CurrentUser?.Id;
                var newUnread = isFromOther ? existing.UnreadCount + 1 : existing.UnreadCount;
                var updated = existing with { LastMessage = message, UnreadCount = newUnread };
                Groups.RemoveAt(index);
                Groups.Insert(0, updated);
            }
        });
    }

    private void OnMessagesRead(Guid chatId, Guid userId)
    {
        if (userId != _authService.CurrentUser?.Id) return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            var idx = Groups.IndexOf(Groups.FirstOrDefault(g => g.Id == chatId)!);
            if (idx >= 0)
                Groups[idx] = Groups[idx] with { UnreadCount = 0 };
        });
    }
}
