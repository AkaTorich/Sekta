using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sekta.Client.Services;
using Sekta.Shared;
using Sekta.Shared.DTOs;

namespace Sekta.Client.ViewModels;

public partial class ChannelViewModel : ObservableObject, IQueryAttributable
{
    private readonly IApiService _apiService;
    private readonly IAuthService _authService;

    public ChannelViewModel(IApiService apiService, IAuthService authService)
    {
        _apiService = apiService;
        _authService = authService;
    }

    [ObservableProperty]
    private Guid _channelId;

    [ObservableProperty]
    private string _channelTitle = string.Empty;

    [ObservableProperty]
    private string? _description;

    [ObservableProperty]
    private string? _avatarUrl;

    [ObservableProperty]
    private int _subscriberCount;

    [ObservableProperty]
    private bool _isOwner;

    [ObservableProperty]
    private bool _isSubscribed;

    [ObservableProperty]
    private string _subscribeButtonText = "Subscribe";

    [ObservableProperty]
    private ObservableCollection<ChannelPostDto> _posts = [];

    [ObservableProperty]
    private string _newPostText = string.Empty;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("channelId", out var idObj) && idObj is string idStr && Guid.TryParse(idStr, out var id))
            ChannelId = id;

        _ = LoadChannelAsync();
    }

    private async Task LoadChannelAsync()
    {
        try
        {
            var channel = await _apiService.GetAsync<ChannelDto>($"{ApiRoutes.Channels}/{ChannelId}");
            if (channel != null)
            {
                ChannelTitle = channel.Title;
                Description = channel.Description;
                AvatarUrl = channel.AvatarUrl;
                SubscriberCount = channel.SubscriberCount;
                IsOwner = channel.OwnerId == _authService.CurrentUser?.Id;
            }

            var posts = await _apiService.GetAsync<List<ChannelPostDto>>($"{ApiRoutes.Channels}/{ChannelId}/posts");
            if (posts != null)
                Posts = new ObservableCollection<ChannelPostDto>(posts);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load channel: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task ToggleSubscribeAsync()
    {
        try
        {
            if (IsSubscribed)
            {
                await _apiService.DeleteAsync($"{ApiRoutes.Channels}/{ChannelId}/subscribe");
                IsSubscribed = false;
                SubscribeButtonText = "Subscribe";
                SubscriberCount--;
            }
            else
            {
                await _apiService.PostAsync<object>($"{ApiRoutes.Channels}/{ChannelId}/subscribe");
                IsSubscribed = true;
                SubscribeButtonText = "Unsubscribe";
                SubscriberCount++;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to toggle subscribe: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task CreatePostAsync()
    {
        if (string.IsNullOrWhiteSpace(NewPostText)) return;

        try
        {
            var dto = new CreateChannelPostDto(NewPostText.Trim(), null);
            var post = await _apiService.PostAsync<ChannelPostDto>($"{ApiRoutes.Channels}/{ChannelId}/posts", dto);

            if (post != null)
            {
                Posts.Insert(0, post);
                NewPostText = string.Empty;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to create post: {ex.Message}");
        }
    }
}
