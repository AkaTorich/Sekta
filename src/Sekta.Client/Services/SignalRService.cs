using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.SignalR.Client;
using Sekta.Shared;
using Sekta.Shared.DTOs;
using Sekta.Shared.Enums;

namespace Sekta.Client.Services;

public interface ISignalRService
{
    bool IsConnected { get; }
    event Action<MessageDto>? MessageReceived;
    event Action<MessageDto>? MessageEdited;
    event Action<Guid, Guid>? MessageDeleted;
    event Action<Guid, Guid>? MessagesRead;
    event Action<Guid, List<Guid>>? MessagesDelivered;
    event Action<Guid, List<Guid>>? MessagesStatusRead;
    event Action<Guid, UserStatus>? UserStatusChanged;
    event Action<Guid, Guid, string>? UserTyping;
    event Action<Guid, Guid>? UserStoppedTyping;
    event Action<CallOfferDto>? IncomingCall;
    event Action<Guid>? CallEnded;
    event Action<Guid>? CallRejected;
    event Action<Guid, string?, string?>? ChatUpdated;
    event Action<ChatDto>? AddedToChat;
    event Action<bool>? ConnectionStateChanged;
    Task ConnectAsync(string accessToken);
    Task DisconnectAsync();
    Task SendMessage(SendMessageDto dto);
    Task EditMessage(Guid messageId, EditMessageDto dto);
    Task DeleteMessage(Guid messageId, Guid chatId);
    Task MarkAsRead(Guid chatId);
    Task MarkDelivered(Guid chatId);
    Task StartTyping(Guid chatId);
    Task StopTyping(Guid chatId);
    Task JoinChat(Guid chatId);
}

public class SignalRService : ISignalRService, IAsyncDisposable
{
    private readonly ISettingsService _settingsService;
    private HubConnection? _hubConnection;

    public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

    public event Action<MessageDto>? MessageReceived;
    public event Action<MessageDto>? MessageEdited;
    public event Action<Guid, Guid>? MessageDeleted;
    public event Action<Guid, Guid>? MessagesRead;
    public event Action<Guid, List<Guid>>? MessagesDelivered;
    public event Action<Guid, List<Guid>>? MessagesStatusRead;
    public event Action<Guid, UserStatus>? UserStatusChanged;
    public event Action<Guid, Guid, string>? UserTyping;
    public event Action<Guid, Guid>? UserStoppedTyping;
    public event Action<CallOfferDto>? IncomingCall;
    public event Action<Guid>? CallEnded;
    public event Action<Guid>? CallRejected;
    public event Action<Guid, string?, string?>? ChatUpdated;
    public event Action<ChatDto>? AddedToChat;
    public event Action<bool>? ConnectionStateChanged;

    private string? _accessToken;
    private bool _intentionalDisconnect;
    private CancellationTokenSource? _reconnectCts;

    public SignalRService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public async Task ConnectAsync(string accessToken)
    {
        _intentionalDisconnect = false;
        _accessToken = accessToken;

        if (_hubConnection is not null)
        {
            await DisconnectAsync();
        }

        _reconnectCts = new CancellationTokenSource();

        var hubUrl = $"{_settingsService.ServerUrl.TrimEnd('/')}{HubRoutes.Chat}";

        _hubConnection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
            })
            .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30) })
            .AddJsonProtocol(options =>
            {
                options.PayloadSerializerOptions.PropertyNameCaseInsensitive = true;
                options.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                options.PayloadSerializerOptions.TypeInfoResolver = new DefaultJsonTypeInfoResolver();
            })
            .Build();

        _hubConnection.Reconnecting += _ =>
        {
            ConnectionStateChanged?.Invoke(false);
            return Task.CompletedTask;
        };

        _hubConnection.Reconnected += _ =>
        {
            ConnectionStateChanged?.Invoke(true);
            return Task.CompletedTask;
        };

        // If automatic reconnect gives up, keep trying manually
        _hubConnection.Closed += async _ =>
        {
            ConnectionStateChanged?.Invoke(false);
            if (!_intentionalDisconnect)
            {
                await ReconnectLoopAsync(_reconnectCts.Token);
            }
        };

        RegisterHandlers();

        await _hubConnection.StartAsync();
        ConnectionStateChanged?.Invoke(true);
    }

    public async Task DisconnectAsync()
    {
        _intentionalDisconnect = true;
        _reconnectCts?.Cancel();
        _reconnectCts?.Dispose();
        _reconnectCts = null;

        if (_hubConnection is not null)
        {
            await _hubConnection.StopAsync();
            await _hubConnection.DisposeAsync();
            _hubConnection = null;
        }
    }

    private async Task ReconnectLoopAsync(CancellationToken ct)
    {
        var delays = new[] { 5, 10, 15, 30, 60 };
        var attempt = 0;

        while (!ct.IsCancellationRequested)
        {
            var delaySec = delays[Math.Min(attempt, delays.Length - 1)];
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySec), ct);
            }
            catch (TaskCanceledException)
            {
                break;
            }

            try
            {
                if (_hubConnection is not null)
                {
                    await _hubConnection.StartAsync(ct);
                    ConnectionStateChanged?.Invoke(true);
                    return;
                }
                else if (_accessToken is not null)
                {
                    await ConnectAsync(_accessToken);
                    return;
                }
            }
            catch when (!ct.IsCancellationRequested)
            {
                attempt++;
            }
        }
    }

    public async Task SendMessage(SendMessageDto dto)
    {
        await EnsureConnectedAsync();
        await _hubConnection!.InvokeAsync("SendMessage", dto);
    }

    public async Task EditMessage(Guid messageId, EditMessageDto dto)
    {
        await EnsureConnectedAsync();
        await _hubConnection!.InvokeAsync("EditMessage", messageId, dto);
    }

    public async Task DeleteMessage(Guid messageId, Guid chatId)
    {
        await EnsureConnectedAsync();
        await _hubConnection!.InvokeAsync("DeleteMessage", messageId, chatId);
    }

    public async Task MarkAsRead(Guid chatId)
    {
        await EnsureConnectedAsync();
        await _hubConnection!.InvokeAsync("MarkAsRead", chatId);
    }

    public async Task MarkDelivered(Guid chatId)
    {
        await EnsureConnectedAsync();
        await _hubConnection!.InvokeAsync("MarkMessagesDelivered", chatId);
    }

    public async Task StartTyping(Guid chatId)
    {
        await EnsureConnectedAsync();
        await _hubConnection!.InvokeAsync("StartTyping", chatId);
    }

    public async Task StopTyping(Guid chatId)
    {
        await EnsureConnectedAsync();
        await _hubConnection!.InvokeAsync("StopTyping", chatId);
    }

    public async Task JoinChat(Guid chatId)
    {
        await EnsureConnectedAsync();
        await _hubConnection!.InvokeAsync("JoinChat", chatId);
    }

    private void RegisterHandlers()
    {
        if (_hubConnection is null) return;

        _hubConnection.On<MessageDto>("ReceiveMessage", message =>
        {
            MessageReceived?.Invoke(message);
        });

        _hubConnection.On<MessageDto>("MessageEdited", message =>
        {
            MessageEdited?.Invoke(message);
        });

        _hubConnection.On<Guid, Guid>("MessageDeleted", (messageId, chatId) =>
        {
            MessageDeleted?.Invoke(messageId, chatId);
        });

        _hubConnection.On<Guid, Guid>("MessagesRead", (chatId, userId) =>
        {
            MessagesRead?.Invoke(chatId, userId);
        });

        _hubConnection.On<Guid, List<Guid>>("MessagesDelivered", (chatId, messageIds) =>
        {
            MessagesDelivered?.Invoke(chatId, messageIds);
        });

        _hubConnection.On<Guid, List<Guid>>("MessagesStatusRead", (chatId, messageIds) =>
        {
            MessagesStatusRead?.Invoke(chatId, messageIds);
        });

        _hubConnection.On<Guid, UserStatus>("UserStatusChanged", (userId, status) =>
        {
            UserStatusChanged?.Invoke(userId, status);
        });

        _hubConnection.On<Guid, Guid, string>("UserTyping", (userId, chatId, userName) =>
        {
            UserTyping?.Invoke(userId, chatId, userName);
        });

        _hubConnection.On<Guid, Guid>("UserStoppedTyping", (userId, chatId) =>
        {
            UserStoppedTyping?.Invoke(userId, chatId);
        });

        _hubConnection.On<CallOfferDto>("IncomingCall", offer =>
        {
            IncomingCall?.Invoke(offer);
        });

        _hubConnection.On<Guid>("CallEnded", callId =>
        {
            CallEnded?.Invoke(callId);
        });

        _hubConnection.On<Guid>("CallRejected", callId =>
        {
            CallRejected?.Invoke(callId);
        });

        _hubConnection.On<Guid, string?, string?>("ChatUpdated", (chatId, title, avatarUrl) =>
        {
            ChatUpdated?.Invoke(chatId, title, avatarUrl);
        });

        _hubConnection.On<ChatDto>("AddedToChat", chat =>
        {
            AddedToChat?.Invoke(chat);
        });
    }

    private async Task EnsureConnectedAsync()
    {
        if (_hubConnection is null)
            throw new InvalidOperationException("SignalR connection is not established.");

        // If reconnecting, wait up to 5 seconds
        if (_hubConnection.State == HubConnectionState.Reconnecting)
        {
            for (var i = 0; i < 50; i++)
            {
                await Task.Delay(100);
                if (_hubConnection.State == HubConnectionState.Connected)
                    return;
            }
        }

        if (_hubConnection.State != HubConnectionState.Connected)
            throw new InvalidOperationException("SignalR connection is not established.");
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        GC.SuppressFinalize(this);
    }
}
