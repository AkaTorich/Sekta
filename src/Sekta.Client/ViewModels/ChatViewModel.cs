using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sekta.Client.Services;
using Sekta.Shared;
using Sekta.Shared.DTOs;
using Sekta.Shared.Enums;

namespace Sekta.Client.ViewModels;

public partial class ChatViewModel : ObservableObject, IQueryAttributable
{
    private readonly IApiService _apiService;
    private readonly ISignalRService _signalRService;
    private readonly IAuthService _authService;
    private readonly IAudioRecorderService _audioRecorderService;
    private readonly InAppNotificationService _inAppNotification;
    private readonly IMessageCacheService _messageCache;

    private CancellationTokenSource? _typingCts;
    private int _currentPage;
    private const int PageSize = 20;
    [ObservableProperty]
    private bool _isInitialLoadComplete;

    [ObservableProperty]
    private bool _showScrollToBottom;

    public ChatViewModel(IApiService apiService, ISignalRService signalRService, IAuthService authService,
        IAudioRecorderService audioRecorderService, InAppNotificationService inAppNotification,
        IMessageCacheService messageCache)
    {
        _apiService = apiService;
        _signalRService = signalRService;
        _authService = authService;
        _audioRecorderService = audioRecorderService;
        _inAppNotification = inAppNotification;
        _messageCache = messageCache;

        // Listen for recording duration updates
        if (_audioRecorderService is AudioRecorderService recorderImpl)
        {
            recorderImpl.DurationUpdated += (_, duration) =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    RecordingDuration = duration;
                    RecordingDurationText = duration.ToString(@"m\:ss");
                });
            };
        }

        _signalRService.MessageReceived += OnMessageReceived;
        _signalRService.MessageEdited += OnMessageEdited;
        _signalRService.MessageDeleted += OnMessageDeleted;
        _signalRService.MessagesDelivered += OnMessagesDelivered;
        _signalRService.MessagesStatusRead += OnMessagesStatusRead;
        _signalRService.UserTyping += OnUserTyping;
        _signalRService.UserStoppedTyping += OnUserStoppedTyping;

        // Listen for sticker selection
        MessagingCenter.Subscribe<StickersViewModel, StickerDto>(this, "StickerSelected", async (_, sticker) =>
        {
            var dto = new SendMessageDto(ChatId, null, MessageType.Sticker, sticker.ImageUrl, null, null, null);
            await _signalRService.SendMessage(dto);
        });

    }

    [ObservableProperty]
    private ObservableCollection<MessageDto> _messages = [];

    [ObservableProperty]
    private string _messageText = string.Empty;

    [ObservableProperty]
    private string _chatTitle = string.Empty;

    [ObservableProperty]
    private bool _isTyping;

    [ObservableProperty]
    private string? _typingUserName;

    [ObservableProperty]
    private Guid _chatId;

    [ObservableProperty]
    private MessageDto? _replyToMessage;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private Guid? _editingMessageId;

    [ObservableProperty]
    private bool _isRecording;

    [ObservableProperty]
    private TimeSpan _recordingDuration;

    [ObservableProperty]
    private string _recordingDurationText = string.Empty;

    [ObservableProperty]
    private bool _hasMoreMessages = true;

    [ObservableProperty]
    private string? _chatAvatarUrl;

    [ObservableProperty]
    private bool _isGroupChat;

    [ObservableProperty]
    private bool _isEmotePanelOpen;

    [ObservableProperty]
    private ObservableCollection<string> _emotes = [];

    private Guid? _otherUserId;

    [RelayCommand]
    private async Task OpenChatInfoAsync()
    {
        if (IsGroupChat)
        {
            await Shell.Current.GoToAsync($"groupinfo?chatId={ChatId}&chatTitle={Uri.EscapeDataString(ChatTitle)}");
        }
        else if (_otherUserId.HasValue)
        {
            await Shell.Current.GoToAsync($"userprofile?userId={_otherUserId.Value}");
        }
    }

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

        _inAppNotification.ActiveChatId = ChatId;
        _ = InitializeAsync();
    }

    /// <summary>
    /// Load a chat directly (for embedded desktop mode, no Shell navigation).
    /// </summary>
    public void LoadChat(Guid chatId, string chatTitle)
    {
        ChatId = chatId;
        ChatTitle = chatTitle;
        _inAppNotification.ActiveChatId = chatId;
        _ = InitializeAsync();
    }

    /// <summary>
    /// Raised when we need the View to scroll to a specific message object.
    /// Using object reference instead of index to bypass MAUI Header offset bug.
    /// </summary>
    public event Action<object>? ScrollToItem;

    private async Task InitializeAsync()
    {
        DebugLog.Clear();
        DebugLog.Log($"=== InitializeAsync START for ChatId={ChatId}, Title={ChatTitle} ===");

        try
        {
            DebugLog.Log("JoinChat...");
            await _signalRService.JoinChat(ChatId);
            DebugLog.Log("JoinChat done");

            if (string.IsNullOrEmpty(ChatTitle))
                ChatTitle = "Chat";

            // Reset state for re-entry
            _currentPage = 0;
            IsInitialLoadComplete = false;
            HasMoreMessages = true;

            // Load chat details (avatar, title) from API
            DebugLog.Log("Fetching chatInfo from API...");
            var chatInfo = await _apiService.GetAsync<ChatDto>($"{ApiRoutes.Chats}/{ChatId}");
            DebugLog.Log($"chatInfo: {(chatInfo is null ? "DONT LOADED (null)" : $"LOADED title={chatInfo.Title} type={chatInfo.Type}")}");

            if (chatInfo is not null)
            {
                ChatAvatarUrl = chatInfo.AvatarUrl;
                IsGroupChat = chatInfo.Type == ChatType.Group;
                if (!string.IsNullOrEmpty(chatInfo.Title))
                    ChatTitle = chatInfo.Title;

                // For private chats, find the other user's ID
                if (!IsGroupChat)
                {
                    var members = await _apiService.GetAsync<List<ChatMemberDto>>(
                        $"{ApiRoutes.Chats}/{ChatId}/members");
                    _otherUserId = members?.FirstOrDefault(m => m.UserId != _authService.CurrentUser?.Id)?.UserId;
                }
            }

            // 1) Show cached messages instantly
            DebugLog.Log("Loading cached messages...");
            var cached = await _messageCache.GetMessagesAsync(ChatId, PageSize);
            DebugLog.Log($"Cache: {(cached.Count > 0 ? $"LOADED {cached.Count} messages" : "DONT LOADED (0 messages)")}");
            var lastReadAt = chatInfo?.LastReadAt;
            var myId = _authService.CurrentUser?.Id;
            object? scrollTarget = null;

            if (cached.Count > 0)
            {
                DebugLog.Log($"Showing {cached.Count} cached messages on UI...");
                var tcs1 = new TaskCompletionSource();
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    Messages = new ObservableCollection<MessageDto>(cached);
                    IsInitialLoadComplete = true;
                    DebugLog.Log($"UI: set {cached.Count} cached msgs, Messages.Count={Messages.Count}");
                    tcs1.SetResult();
                });
                await tcs1.Task;
            }

            // 2) Fetch only newer messages from server (since last cached)
            var latestCached = cached.Count > 0 ? cached[^1].CreatedAt : DateTime.MinValue;
            DebugLog.Log($"Fetching server messages page=1 pageSize={PageSize}...");
            var serverMessages = await _apiService.GetAsync<List<MessageDto>>(
                $"{ApiRoutes.Chats}/{ChatId}/messages?page=1&pageSize={PageSize}");

            DebugLog.Log($"Server messages: {(serverMessages is null ? "DONT LOADED (null)" : $"LOADED {serverMessages.Count} messages")}");

            if (serverMessages is { Count: > 0 })
            {
                // Save all to cache
                _ = _messageCache.SaveMessagesAsync(ChatId, serverMessages);

                // Find new messages not in current view
                var existingIds = new HashSet<Guid>(Messages.Select(m => m.Id));
                var newMessages = serverMessages.Where(m => !existingIds.Contains(m.Id)).ToList();

                // Also update existing messages (status, edits)
                var updatedExisting = serverMessages.Where(m => existingIds.Contains(m.Id)).ToList();

                DebugLog.Log($"Merge: existingInView={existingIds.Count}, newFromServer={newMessages.Count}, updatedExisting={updatedExisting.Count}");

                var newMsgCopy = newMessages;
                var updatedCopy = updatedExisting;
                HasMoreMessages = serverMessages.Count >= PageSize;

                // Find first unread message
                if (lastReadAt.HasValue && myId.HasValue)
                {
                    for (int i = 0; i < serverMessages.Count; i++)
                    {
                        if (serverMessages[i].SenderId != myId.Value && serverMessages[i].CreatedAt > lastReadAt.Value)
                        {
                            scrollTarget = serverMessages[i];
                            break;
                        }
                    }
                }

                var tcs2 = new TaskCompletionSource();
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    // If no cache was shown, replace everything
                    if (cached.Count == 0)
                    {
                        DebugLog.Log($"No cache, replacing all with {serverMessages.Count} server messages");
                        Messages = new ObservableCollection<MessageDto>(serverMessages);
                        DebugLog.Log($"Messages replaced, Count={Messages.Count}");
                    }
                    else
                    {
                        DebugLog.Log($"Merging: updating {updatedCopy.Count} in-place, appending {newMsgCopy.Count} new");
                        // Update changed messages in-place
                        foreach (var updated in updatedCopy)
                        {
                            for (int i = 0; i < Messages.Count; i++)
                            {
                                if (Messages[i].Id == updated.Id && Messages[i] != updated)
                                {
                                    Messages[i] = updated;
                                    break;
                                }
                            }
                        }

                        // Append new messages at end (batch to avoid iOS BindableLayout hang)
                        if (newMsgCopy.Count > 0)
                        {
                            var all = Messages.ToList();
                            all.AddRange(newMsgCopy.OrderBy(m => m.CreatedAt));
                            Messages = new ObservableCollection<MessageDto>(all);
                        }
                    }

                    IsInitialLoadComplete = true;
                    DebugLog.Log($"UI FINAL: Messages.Count={Messages.Count}, HasMoreMessages={HasMoreMessages}");
                    tcs2.SetResult();
                });
                await tcs2.Task;
            }
            else if (cached.Count == 0)
            {
                HasMoreMessages = false;
                IsInitialLoadComplete = true;
                DebugLog.Log("No cache + no server messages => empty chat");
            }
            else
            {
                DebugLog.Log($"Server returned null/empty but cache had {cached.Count} msgs, keeping cache view");
            }

            DebugLog.Log($"scrollTarget={scrollTarget is not null}, Messages.Count={Messages.Count}");

            // Scroll to first unread or stay at bottom
            if (scrollTarget is not null)
                ScrollToItem?.Invoke(scrollTarget);

            // Mark as read
            await _signalRService.MarkAsRead(ChatId);
            DebugLog.Log("=== InitializeAsync END ===");
        }
        catch (Exception ex)
        {
            DebugLog.Log($"EXCEPTION in InitializeAsync: {ex.GetType().Name}: {ex.Message}");
            DebugLog.Log($"StackTrace: {ex.StackTrace}");
            System.Diagnostics.Debug.WriteLine($"Failed to initialize chat: {ex.Message}");
        }
    }

    private async Task LoadMoreMessagesAsync()
    {
        _currentPage++;
        var messages = await _apiService.GetAsync<List<MessageDto>>(
            $"{ApiRoutes.Chats}/{ChatId}/messages?page={_currentPage + 1}&pageSize={PageSize}");

        if (messages is not null)
        {
            for (var i = messages.Count - 1; i >= 0; i--)
                Messages.Insert(0, messages[i]);

            HasMoreMessages = messages.Count >= PageSize;
        }
        else
        {
            HasMoreMessages = false;
        }
    }

    [RelayCommand]
    private async Task SendMessageAsync(CancellationToken cancellationToken)
    {
        DebugLog.Log($"SendMessageAsync called: MessageText='{MessageText}', ChatId={ChatId}, IsEditing={IsEditing}");

        if (string.IsNullOrWhiteSpace(MessageText))
        {
            DebugLog.Log("SendMessageAsync: MessageText is empty, returning");
            return;
        }

        try
        {
            if (IsEditing && EditingMessageId.HasValue)
            {
                DebugLog.Log($"Editing message {EditingMessageId.Value}...");
                await _signalRService.EditMessage(EditingMessageId.Value, new EditMessageDto(MessageText.Trim()));
                IsEditing = false;
                EditingMessageId = null;
            }
            else
            {
                var dto = new SendMessageDto(
                    ChatId: ChatId,
                    Content: MessageText.Trim(),
                    Type: MessageType.Text,
                    MediaUrl: null,
                    FileName: null,
                    FileSize: null,
                    ReplyToId: ReplyToMessage?.Id);

                DebugLog.Log($"Sending message to ChatId={dto.ChatId}...");
                await _signalRService.SendMessage(dto);
                DebugLog.Log("Message sent OK");
                ReplyToMessage = null;
            }

            MessageText = string.Empty;
            await _signalRService.StopTyping(ChatId);
        }
        catch (Exception ex)
        {
            DebugLog.Log($"SendMessage EXCEPTION: {ex.GetType().Name}: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Failed to send message: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task LoadMoreAsync(CancellationToken cancellationToken)
    {
        await LoadMoreMessagesAsync();
    }

    [RelayCommand]
    private async Task PickPhotoAsync(CancellationToken cancellationToken)
    {
        try
        {
            var photo = await MediaPicker.Default.PickPhotoAsync(new MediaPickerOptions
            {
                Title = "Select a photo"
            });

            if (photo is null)
                return;

            await using var stream = await photo.OpenReadAsync();
            var (url, fileName, size) = await _apiService.UploadFileAsync(stream, photo.FileName);

            var dto = new SendMessageDto(
                ChatId: ChatId,
                Content: null,
                Type: MessageType.Photo,
                MediaUrl: url,
                FileName: fileName,
                FileSize: size,
                ReplyToId: null);

            await _signalRService.SendMessage(dto);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to pick/send photo: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task AttachFileAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Select a file"
            });

            if (result is null)
                return;

            await using var stream = await result.OpenReadAsync();
            var (url, fileName, size) = await _apiService.UploadFileAsync(stream, result.FileName);

            var messageType = GetMessageTypeFromFileName(result.FileName);

            var dto = new SendMessageDto(
                ChatId: ChatId,
                Content: null,
                Type: messageType,
                MediaUrl: url,
                FileName: fileName,
                FileSize: size,
                ReplyToId: null);

            await _signalRService.SendMessage(dto);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to attach/send file: {ex.Message}");
        }
    }

    private static MessageType GetMessageTypeFromFileName(string fileName)
    {
        var ext = Path.GetExtension(fileName)?.ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".svg" => MessageType.Photo,
            ".mp4" or ".avi" or ".mov" or ".mkv" or ".webm" => MessageType.Video,
            ".mp3" or ".ogg" or ".wav" or ".m4a" or ".aac" or ".opus" => MessageType.Voice,
            _ => MessageType.File
        };
    }

    [RelayCommand]
    private async Task RecordVoiceAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!IsRecording)
            {
                // Start recording
                await _audioRecorderService.StartRecordingAsync();
                IsRecording = true;
                RecordingDuration = TimeSpan.Zero;
                RecordingDurationText = "0:00";
            }
            else
            {
                // Stop recording
                IsRecording = false;
                var filePath = await _audioRecorderService.StopRecordingAsync();

                if (string.IsNullOrEmpty(filePath))
                {
                    RecordingDurationText = string.Empty;
                    await Shell.Current.DisplayAlert("Voice", "Recording failed — no audio captured. Check microphone permissions.", "OK");
                    return;
                }

                // Upload the audio file
                await using var stream = File.OpenRead(filePath);
                var fileName = Path.GetFileName(filePath);
                var (url, uploadedName, size) = await _apiService.UploadFileAsync(stream, fileName);

                // Send as Voice message
                var dto = new SendMessageDto(
                    ChatId: ChatId,
                    Content: $"Voice message ({RecordingDurationText})",
                    Type: MessageType.Voice,
                    MediaUrl: url,
                    FileName: uploadedName,
                    FileSize: size,
                    ReplyToId: ReplyToMessage?.Id);

                await _signalRService.SendMessage(dto);
                ReplyToMessage = null;
                RecordingDurationText = string.Empty;

                // Clean up temp file
                try { File.Delete(filePath); } catch { /* best effort */ }
            }
        }
        catch (Exception ex)
        {
            IsRecording = false;
            RecordingDurationText = string.Empty;
            System.Diagnostics.Debug.WriteLine($"Failed to record/send voice: {ex.Message}");
            await Shell.Current.DisplayAlert("Voice", $"Failed to send voice message: {ex.Message}", "OK");
        }
    }

    [RelayCommand]
    private async Task MessageActionAsync(MessageDto? message)
    {
        if (message == null) return;

        var isOwn = message.SenderId == _authService.CurrentUser?.Id;
        var actions = new List<string> { "Reply", "Forward" };
        if (isOwn) { actions.Add("Edit"); actions.Add("Delete"); }

        var action = await Shell.Current.DisplayActionSheet("Message", "Cancel", null, actions.ToArray());

        switch (action)
        {
            case "Reply":
                ReplyToMessage = message;
                break;
            case "Edit" when isOwn:
                IsEditing = true;
                EditingMessageId = message.Id;
                MessageText = message.Content ?? "";
                break;
            case "Delete" when isOwn:
                await DeleteMessageAsync(message);
                break;
            case "Forward":
                await ForwardMessageAsync(message);
                break;
        }
    }

    [RelayCommand]
    private void CancelReply()
    {
        ReplyToMessage = null;
        IsEditing = false;
        EditingMessageId = null;
        MessageText = string.Empty;
    }

    private async Task DeleteMessageAsync(MessageDto message)
    {
        var confirm = await Shell.Current.DisplayAlert("Delete", "Delete this message?", "Yes", "No");
        if (!confirm) return;

        try
        {
            await _signalRService.DeleteMessage(message.Id, ChatId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to delete message: {ex.Message}");
        }
    }

    private async Task ForwardMessageAsync(MessageDto message)
    {
        var navParams = new Dictionary<string, object> { { "message", message } };
        await Shell.Current.GoToAsync("forward", navParams);
    }

    [RelayCommand]
    private async Task GoBackAsync()
    {
        Cleanup();
        await Shell.Current.GoToAsync("//main/chats");
    }

    /// <summary>
    /// Unsubscribe from SignalR events to prevent ghost MarkAsRead calls
    /// when the user is no longer viewing this chat.
    /// </summary>
    public void Cleanup()
    {
        if (_inAppNotification.ActiveChatId == ChatId)
            _inAppNotification.ActiveChatId = null;

        _signalRService.MessageReceived -= OnMessageReceived;
        _signalRService.MessageEdited -= OnMessageEdited;
        _signalRService.MessageDeleted -= OnMessageDeleted;
        _signalRService.MessagesDelivered -= OnMessagesDelivered;
        _signalRService.MessagesStatusRead -= OnMessagesStatusRead;
        _signalRService.UserTyping -= OnUserTyping;
        _signalRService.UserStoppedTyping -= OnUserStoppedTyping;
        MessagingCenter.Unsubscribe<StickersViewModel, StickerDto>(this, "StickerSelected");
    }

    [RelayCommand]
    private async Task OpenStickersAsync()
    {
        await Shell.Current.GoToAsync("stickers");
    }

    [RelayCommand]
    private void ToggleEmotePanel()
    {
        IsEmotePanelOpen = !IsEmotePanelOpen;
        if (IsEmotePanelOpen && Emotes.Count == 0)
        {
            var codes = new List<string>();
            for (var first = 'a'; first <= 'e'; first++)
            {
                for (var second = 'a'; second <= 'z'; second++)
                {
                    codes.Add($"{first}{second}");
                    if (first == 'e' && second == 'a') break;
                }
            }
            Emotes = new ObservableCollection<string>(codes);
        }
    }

    [RelayCommand]
    private async Task SendEmoteAsync(string emoteCode)
    {
        if (string.IsNullOrEmpty(emoteCode)) return;
        IsEmotePanelOpen = false;
        var dto = new SendMessageDto(ChatId, null, MessageType.Sticker, $"emote:{emoteCode}.gif", null, null, null);
        await _signalRService.SendMessage(dto);
    }

    partial void OnMessageTextChanged(string value)
    {
        _ = HandleTypingIndicatorAsync(value);
    }

    private async Task HandleTypingIndicatorAsync(string text)
    {
        try
        {
            if (string.IsNullOrEmpty(text))
            {
                _typingCts?.Cancel();
                _typingCts = null;
                await _signalRService.StopTyping(ChatId);
                return;
            }

            _typingCts?.Cancel();
            await _signalRService.StartTyping(ChatId);

            _typingCts = new CancellationTokenSource();
            var token = _typingCts.Token;

            await Task.Delay(3000, token);

            if (!token.IsCancellationRequested)
            {
                await _signalRService.StopTyping(ChatId);
            }
        }
        catch (TaskCanceledException)
        {
            // Expected when user types again within 3 seconds
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Typing indicator error: {ex.Message}");
        }
    }

    private void OnMessageReceived(MessageDto message)
    {
        if (message.ChatId != ChatId)
            return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            Messages.Add(message);
        });

        // Cache the new message
        _ = _messageCache.AddMessageAsync(ChatId, message);

        // If this message is from someone else, mark it as read since we have the chat open
        if (message.SenderId != _authService.CurrentUser?.Id)
        {
            _ = _signalRService.MarkAsRead(ChatId);
        }
    }

    private void OnMessageEdited(MessageDto message)
    {
        if (message.ChatId != ChatId) return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            for (int i = 0; i < Messages.Count; i++)
            {
                if (Messages[i].Id == message.Id)
                {
                    Messages[i] = message;
                    break;
                }
            }
        });

        _ = _messageCache.UpdateMessageAsync(message);
    }

    private void OnMessageDeleted(Guid messageId, Guid chatId)
    {
        if (chatId != ChatId) return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            var msg = Messages.FirstOrDefault(m => m.Id == messageId);
            if (msg != null) Messages.Remove(msg);
        });

        _ = _messageCache.DeleteMessageAsync(messageId);
    }

    private void OnMessagesDelivered(Guid chatId, List<Guid> messageIds)
    {
        if (chatId != ChatId) return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            var idSet = new HashSet<Guid>(messageIds);
            for (int i = 0; i < Messages.Count; i++)
            {
                if (idSet.Contains(Messages[i].Id) && Messages[i].Status == MessageStatus.Sent)
                {
                    Messages[i] = Messages[i] with { Status = MessageStatus.Delivered };
                }
            }
        });
    }

    private void OnMessagesStatusRead(Guid chatId, List<Guid> messageIds)
    {
        if (chatId != ChatId) return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            var idSet = new HashSet<Guid>(messageIds);
            for (int i = 0; i < Messages.Count; i++)
            {
                if (idSet.Contains(Messages[i].Id) && Messages[i].Status != MessageStatus.Read)
                {
                    Messages[i] = Messages[i] with { Status = MessageStatus.Read };
                }
            }
        });
    }

    private void OnUserTyping(Guid userId, Guid chatId, string userName)
    {
        if (chatId != ChatId || userId == _authService.CurrentUser?.Id)
            return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            IsTyping = true;
            TypingUserName = userName;
        });
    }

    private void OnUserStoppedTyping(Guid userId, Guid chatId)
    {
        if (chatId != ChatId)
            return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            IsTyping = false;
            TypingUserName = null;
        });
    }
}
