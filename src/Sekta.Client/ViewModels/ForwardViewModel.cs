using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sekta.Client.Services;
using Sekta.Shared;
using Sekta.Shared.DTOs;
using Sekta.Shared.Enums;

namespace Sekta.Client.ViewModels;

public partial class ForwardViewModel : ObservableObject, IQueryAttributable
{
    private readonly IApiService _apiService;
    private readonly ISignalRService _signalRService;

    private MessageDto? _message;

    public ForwardViewModel(IApiService apiService, ISignalRService signalRService)
    {
        _apiService = apiService;
        _signalRService = signalRService;
    }

    [ObservableProperty]
    private ObservableCollection<ChatDto> _chats = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    private List<ChatDto> _allChats = [];

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("message", out var msgObj) && msgObj is MessageDto msg)
            _message = msg;

        _ = LoadChatsAsync();
    }

    partial void OnSearchTextChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Chats = new ObservableCollection<ChatDto>(_allChats);
        }
        else
        {
            var filtered = _allChats.Where(c =>
                c.Title?.Contains(value, StringComparison.OrdinalIgnoreCase) == true).ToList();
            Chats = new ObservableCollection<ChatDto>(filtered);
        }
    }

    private async Task LoadChatsAsync()
    {
        try
        {
            var chats = await _apiService.GetAsync<List<ChatDto>>(ApiRoutes.Chats);
            if (chats != null)
            {
                _allChats = chats;
                Chats = new ObservableCollection<ChatDto>(chats);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load chats for forward: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task SelectChatAsync(ChatDto? chat)
    {
        if (chat == null || _message == null) return;

        try
        {
            var dto = new SendMessageDto(
                ChatId: chat.Id,
                Content: _message.Content,
                Type: _message.Type,
                MediaUrl: _message.MediaUrl,
                FileName: _message.FileName,
                FileSize: _message.FileSize,
                ReplyToId: null,
                ForwardedFrom: _message.SenderName ?? "Unknown");

            await _signalRService.SendMessage(dto);
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", $"Failed to forward: {ex.Message}", "OK");
        }
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        await Shell.Current.GoToAsync("..");
    }
}
