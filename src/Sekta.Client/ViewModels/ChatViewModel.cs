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
    private readonly IEncryptionService _encryption;
    private readonly IFileCacheService _fileCache;

    private CancellationTokenSource? _typingCts;
    private int _currentPage;
    private const int PageSize = 20;
    [ObservableProperty]
    private bool _isInitialLoadComplete;

    [ObservableProperty]
    private bool _showScrollToBottom;

    // E2E encryption: peer public key cache (shared across instances within session)
    private static readonly Dictionary<Guid, byte[]> PeerPublicKeys = new();
    private static bool _keysUploadedThisSession;

    public ChatViewModel(IApiService apiService, ISignalRService signalRService, IAuthService authService,
        IAudioRecorderService audioRecorderService, InAppNotificationService inAppNotification,
        IMessageCacheService messageCache, IEncryptionService encryptionService, IFileCacheService fileCacheService)
    {
        _apiService = apiService;
        _signalRService = signalRService;
        _authService = authService;
        _audioRecorderService = audioRecorderService;
        _inAppNotification = inAppNotification;
        _messageCache = messageCache;
        _encryption = encryptionService;
        _fileCache = fileCacheService;

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

            // Initialize E2E encryption keys
            await InitializeEncryptionAsync();

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
                var cachedOrig = cached.ToList();
                cached = DecryptMessages(cached);
                cached = await ResolveMediaFilesAsync(cachedOrig, cached);
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
                // Save all to cache (encrypted form)
                await _messageCache.SaveMessagesAsync(ChatId, serverMessages);

                // Decrypt for display
                var serverOrig = serverMessages.ToList();
                serverMessages = DecryptMessages(serverMessages);
                serverMessages = await ResolveMediaFilesAsync(serverOrig, serverMessages);

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
                var editContent = await EncryptContentAsync(MessageText.Trim());
                await _signalRService.EditMessage(EditingMessageId.Value, new EditMessageDto(editContent));
                IsEditing = false;
                EditingMessageId = null;
            }
            else
            {
                var content = MessageText.Trim();

                // E2E encrypt for private chats
                content = await EncryptContentAsync(content);

                var dto = new SendMessageDto(
                    ChatId: ChatId,
                    Content: content,
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

            var (url, fileName, size, encContent) = await EncryptAndUploadFileAsync(
                await photo.OpenReadAsync(), photo.FileName, null);

            var dto = new SendMessageDto(
                ChatId: ChatId,
                Content: encContent,
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

            var messageType = GetMessageTypeFromFileName(result.FileName);

            var (url, fileName, size, encContent) = await EncryptAndUploadFileAsync(
                await result.OpenReadAsync(), result.FileName, null);

            var dto = new SendMessageDto(
                ChatId: ChatId,
                Content: encContent,
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

                // Upload the audio file (encrypt for private chats)
                var voiceCaption = $"Voice message ({RecordingDurationText})";
                await using var stream = File.OpenRead(filePath);
                var fileName = Path.GetFileName(filePath);
                var (url, uploadedName, size, encContent) = await EncryptAndUploadFileAsync(
                    stream, fileName, voiceCaption);

                // Send as Voice message
                var dto = new SendMessageDto(
                    ChatId: ChatId,
                    Content: encContent ?? voiceCaption,
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

    private async void OnMessageReceived(MessageDto message)
    {
        if (message.ChatId != ChatId)
            return;

        var originalContent = message.Content;
        var decrypted = DecryptIfNeeded(message);

        // Resolve encrypted files (download + decrypt + cache)
        if (!string.IsNullOrEmpty(decrypted.MediaUrl) && !decrypted.MediaUrl.StartsWith("emote:"))
            decrypted = await ResolveEncryptedFileAsync(decrypted, originalContent);

        MainThread.BeginInvokeOnMainThread(() =>
        {
            Messages.Add(decrypted);
        });

        // Cache the new message (await to guarantee write before chat switch)
        try
        {
            await _messageCache.AddMessageAsync(ChatId, message);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Cache write failed: {ex.Message}");
        }

        // If this message is from someone else, mark it as read since we have the chat open
        if (message.SenderId != _authService.CurrentUser?.Id)
        {
            try
            {
                await _signalRService.MarkAsRead(ChatId);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MarkAsRead failed: {ex.Message}");
            }
        }
    }

    private void OnMessageEdited(MessageDto message)
    {
        if (message.ChatId != ChatId) return;

        var decrypted = DecryptIfNeeded(message);

        MainThread.BeginInvokeOnMainThread(() =>
        {
            for (int i = 0; i < Messages.Count; i++)
            {
                if (Messages[i].Id == decrypted.Id)
                {
                    Messages[i] = decrypted;
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

    // ──────────────────────────────────────────────
    //  E2E Encryption
    // ──────────────────────────────────────────────

    private async Task InitializeEncryptionAsync()
    {
        try
        {
            await _encryption.EnsureKeysLoadedAsync();

            // Upload public key once per session
            if (!_keysUploadedThisSession && _encryption.MyPublicKey is not null && _authService.CurrentUser is not null)
            {
                var userId = _authService.CurrentUser.Id;
                var pubKeyBase64 = Convert.ToBase64String(_encryption.MyPublicKey);
                await _apiService.PostAsync(ApiRoutes.Keys, new PublicKeyDto(userId, pubKeyBase64));
                _keysUploadedThisSession = true;
                DebugLog.Log("E2E: Public key uploaded to server");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[E2E] Key init failed: {ex.Message}");
        }
    }

    private async Task<byte[]?> GetPeerPublicKeyAsync(Guid userId)
    {
        if (PeerPublicKeys.TryGetValue(userId, out var cached))
            return cached;

        try
        {
            var dto = await _apiService.GetAsync<PublicKeyDto>($"{ApiRoutes.Keys}/{userId}");
            if (dto is not null)
            {
                var key = Convert.FromBase64String(dto.PublicKeyBase64);
                PeerPublicKeys[userId] = key;
                return key;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[E2E] Failed to fetch public key for {userId}: {ex.Message}");
        }

        return null;
    }

    private async Task<string> EncryptContentAsync(string plaintext)
    {
        if (IsGroupChat || _encryption.MyPrivateKey is null || _encryption.MyPublicKey is null || !_otherUserId.HasValue)
            return plaintext;

        try
        {
            var peerKey = await GetPeerPublicKeyAsync(_otherUserId.Value);
            if (peerKey is null)
                return plaintext; // Fallback to plaintext if peer has no key

            var sharedSecret = _encryption.DeriveSharedSecret(_encryption.MyPrivateKey, peerKey);
            var (ciphertext, nonce, tag) = _encryption.Encrypt(plaintext, sharedSecret);
            var packed = _encryption.PackEncryptedContent(ciphertext, nonce, tag, _encryption.MyPublicKey);
            return packed;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[E2E] Encryption failed, sending plaintext: {ex.Message}");
            return plaintext;
        }
    }

    private MessageDto DecryptIfNeeded(MessageDto message)
    {
        if (_encryption.MyPrivateKey is null)
            return message;

        // Decrypt text content
        if (_encryption.IsEncryptedContent(message.Content))
        {
            var packed = _encryption.TryUnpackEncryptedContent(message.Content);
            if (packed is not null)
            {
                try
                {
                    var (ciphertext, nonce, tag, senderPubKey) = packed.Value;
                    var sharedSecret = _encryption.DeriveSharedSecret(_encryption.MyPrivateKey, senderPubKey);
                    var plaintext = _encryption.Decrypt(ciphertext, nonce, tag, sharedSecret);
                    return message with { Content = plaintext };
                }
                catch
                {
                    return message with { Content = "\ud83d\udd12 Encrypted message" };
                }
            }
        }

        // Decrypt encrypted file metadata — restore caption, resolve file later
        if (_encryption.IsEncryptedFileContent(message.Content))
        {
            var fileMeta = _encryption.TryUnpackEncryptedFileContent(message.Content);
            if (fileMeta is not null)
                return message with { Content = fileMeta.Value.caption };
        }

        return message;
    }

    private List<MessageDto> DecryptMessages(List<MessageDto> messages)
    {
        return messages.Select(DecryptIfNeeded).ToList();
    }

    // ──────────────────────────────────────────────
    //  E2E File Encryption
    // ──────────────────────────────────────────────

    /// <summary>
    /// Encrypt file bytes and upload. Returns (url, fileName, size, encryptedContentMetadata).
    /// For group chats or when encryption unavailable, uploads plaintext and returns null metadata.
    /// </summary>
    private async Task<(string url, string fileName, long size, string? encContent)> EncryptAndUploadFileAsync(
        Stream fileStream, string fileName, string? caption)
    {
        using var ms = new MemoryStream();
        await fileStream.CopyToAsync(ms);
        var fileBytes = ms.ToArray();

        // Try to encrypt for private chats
        if (!IsGroupChat && _encryption.MyPrivateKey is not null && _encryption.MyPublicKey is not null && _otherUserId.HasValue)
        {
            try
            {
                var peerKey = await GetPeerPublicKeyAsync(_otherUserId.Value);
                if (peerKey is not null)
                {
                    var sharedSecret = _encryption.DeriveSharedSecret(_encryption.MyPrivateKey, peerKey);
                    var (ciphertext, nonce, tag) = _encryption.EncryptBytes(fileBytes, sharedSecret);

                    // Upload encrypted bytes
                    using var encStream = new MemoryStream(ciphertext);
                    var (url, uploadedName, size) = await _apiService.UploadFileAsync(encStream, fileName);

                    // Cache the original (decrypted) file locally
                    await _fileCache.CacheBytesAsync(url, fileBytes);

                    var encContent = _encryption.PackEncryptedFileContent(nonce, tag, _encryption.MyPublicKey, caption);
                    return (url, uploadedName, size, encContent);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[E2E] File encryption failed, uploading plaintext: {ex.Message}");
            }
        }

        // Fallback: upload unencrypted
        using var plainStream = new MemoryStream(fileBytes);
        var (pUrl, pName, pSize) = await _apiService.UploadFileAsync(plainStream, fileName);

        // Cache locally
        await _fileCache.CacheBytesAsync(pUrl, fileBytes);

        return (pUrl, pName, pSize, null);
    }

    /// <summary>
    /// For messages with encrypted files, download + decrypt + cache, then replace MediaUrl with local path.
    /// </summary>
    private async Task<MessageDto> ResolveEncryptedFileAsync(MessageDto message, string? originalContent)
    {
        if (string.IsNullOrEmpty(message.MediaUrl) || _encryption.MyPrivateKey is null)
            return message;

        // Check if already cached locally
        var cached = _fileCache.GetCachedPath(message.MediaUrl);
        if (cached is not null)
            return message with { MediaUrl = cached };

        // If the original content had file encryption metadata, decrypt the file
        if (originalContent is not null && _encryption.IsEncryptedFileContent(originalContent))
        {
            var fileMeta = _encryption.TryUnpackEncryptedFileContent(originalContent);
            if (fileMeta is not null)
            {
                try
                {
                    var encBytes = await _apiService.DownloadBytesAsync(message.MediaUrl);
                    if (encBytes is not null)
                    {
                        var (nonce, tag, senderPubKey, _) = fileMeta.Value;
                        var sharedSecret = _encryption.DeriveSharedSecret(_encryption.MyPrivateKey, senderPubKey);
                        var decrypted = _encryption.DecryptBytes(encBytes, nonce, tag, sharedSecret);
                        var localPath = await _fileCache.CacheBytesAsync(message.MediaUrl, decrypted);
                        return message with { MediaUrl = localPath };
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[E2E] File decryption failed: {ex.Message}");
                }
            }
        }

        // Non-encrypted file: download and cache as-is
        try
        {
            var localPath = await _fileCache.DownloadAndCacheAsync(message.MediaUrl);
            return message with { MediaUrl = localPath };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FileCache] Download failed: {ex.Message}");
        }

        return message;
    }

    /// <summary>
    /// Resolve all media files in a message list (download + decrypt + cache).
    /// </summary>
    private async Task<List<MessageDto>> ResolveMediaFilesAsync(List<MessageDto> originalMessages, List<MessageDto> decryptedMessages)
    {
        var tasks = new List<Task<MessageDto>>();
        for (int i = 0; i < decryptedMessages.Count; i++)
        {
            var msg = decryptedMessages[i];
            if (!string.IsNullOrEmpty(msg.MediaUrl) && !msg.MediaUrl.StartsWith("emote:"))
            {
                var origContent = originalMessages[i].Content;
                tasks.Add(ResolveEncryptedFileAsync(msg, origContent));
            }
            else
            {
                tasks.Add(Task.FromResult(msg));
            }
        }
        return (await Task.WhenAll(tasks)).ToList();
    }
}
