using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Sekta.Server.Services;
using Sekta.Shared.DTOs;
using Sekta.Shared.Enums;

namespace Sekta.Server.Hubs;

[Authorize]
public class ChatHub : Hub
{
    private readonly IChatService _chatService;
    private readonly IUserService _userService;
    private readonly IPushNotificationService _pushNotificationService;

    private static readonly Dictionary<string, HashSet<string>> _userConnections = new();
    private static readonly object _lock = new();

    public ChatHub(
        IChatService chatService,
        IUserService userService,
        IPushNotificationService pushNotificationService)
    {
        _chatService = chatService;
        _userService = userService;
        _pushNotificationService = pushNotificationService;
    }

    private Guid GetUserId() =>
        Guid.Parse(Context.User!.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public override async Task OnConnectedAsync()
    {
        var userId = GetUserId();
        var userIdStr = userId.ToString();

        lock (_lock)
        {
            if (!_userConnections.ContainsKey(userIdStr))
                _userConnections[userIdStr] = new HashSet<string>();
            _userConnections[userIdStr].Add(Context.ConnectionId);
        }

        await _userService.SetOnline(userId);

        // Join all chat groups
        var chats = await _chatService.GetUserChats(userId);
        foreach (var chat in chats)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, chat.Id.ToString());
        }

        // Notify contacts about online status
        await Clients.Others.SendAsync("UserStatusChanged", userId, UserStatus.Online);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = GetUserId();
        var userIdStr = userId.ToString();
        bool isLastConnection = false;

        lock (_lock)
        {
            if (_userConnections.ContainsKey(userIdStr))
            {
                _userConnections[userIdStr].Remove(Context.ConnectionId);
                if (_userConnections[userIdStr].Count == 0)
                {
                    _userConnections.Remove(userIdStr);
                    isLastConnection = true;
                }
            }
        }

        if (isLastConnection)
        {
            await _userService.SetOffline(userId);
            await Clients.Others.SendAsync("UserStatusChanged", userId, UserStatus.Offline);
        }

        await base.OnDisconnectedAsync(exception);
    }

    public async Task SendMessage(SendMessageDto dto)
    {
        var userId = GetUserId();
        var message = await _chatService.SendMessage(userId, dto);

        await Clients.Group(dto.ChatId.ToString()).SendAsync("ReceiveMessage", message);

        // Send push notifications to offline members of the chat
        var members = await _chatService.GetChatMembers(dto.ChatId);
        var sender = await _userService.GetUser(userId);
        var senderName = sender.DisplayName ?? sender.Username;

        foreach (var member in members)
        {
            // Skip the sender
            if (member.UserId == userId)
                continue;

            // Only push to users who are not currently connected
            if (!IsUserOnline(member.UserId.ToString()))
            {
                var notificationBody = message.Content?.Length > 100
                    ? message.Content[..100] + "..."
                    : message.Content ?? "Sent a message";

                await _pushNotificationService.SendPushNotification(
                    member.UserId,
                    senderName,
                    notificationBody);
            }
        }
    }

    public async Task EditMessage(Guid messageId, EditMessageDto dto)
    {
        var userId = GetUserId();
        var message = await _chatService.EditMessage(messageId, userId, dto);

        await Clients.Group(message.ChatId.ToString()).SendAsync("MessageEdited", message);
    }

    public async Task DeleteMessage(Guid messageId, Guid chatId)
    {
        var userId = GetUserId();
        await _chatService.DeleteMessage(messageId, userId);

        await Clients.Group(chatId.ToString()).SendAsync("MessageDeleted", messageId, chatId);
    }

    public async Task MarkMessagesDelivered(Guid chatId)
    {
        var userId = GetUserId();
        var messageIds = await _chatService.MarkMessagesDelivered(chatId, userId);

        if (messageIds.Count > 0)
        {
            await Clients.OthersInGroup(chatId.ToString())
                .SendAsync("MessagesDelivered", chatId, messageIds);
        }
    }

    public async Task MarkAsRead(Guid chatId)
    {
        var userId = GetUserId();

        // Update message statuses to Read
        var messageIds = await _chatService.MarkMessagesRead(chatId, userId);

        // Update membership last read timestamp
        await _chatService.MarkAsRead(chatId, userId);

        if (messageIds.Count > 0)
        {
            await Clients.OthersInGroup(chatId.ToString())
                .SendAsync("MessagesStatusRead", chatId, messageIds);
        }

        // Keep existing event for unread count updates
        await Clients.Group(chatId.ToString()).SendAsync("MessagesRead", chatId, userId);
    }

    public async Task StartTyping(Guid chatId)
    {
        var userId = GetUserId();
        var user = await _userService.GetUser(userId);

        await Clients.OthersInGroup(chatId.ToString())
            .SendAsync("UserTyping", chatId, userId, user.DisplayName ?? user.Username);
    }

    public async Task StopTyping(Guid chatId)
    {
        var userId = GetUserId();
        await Clients.OthersInGroup(chatId.ToString())
            .SendAsync("UserStoppedTyping", chatId, userId);
    }

    public async Task JoinChat(Guid chatId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, chatId.ToString());
    }

    public static bool IsUserOnline(string userId)
    {
        lock (_lock)
        {
            return _userConnections.ContainsKey(userId) && _userConnections[userId].Count > 0;
        }
    }

    public static IEnumerable<string> GetUserConnections(string userId)
    {
        lock (_lock)
        {
            return _userConnections.TryGetValue(userId, out var connections)
                ? connections.ToList()
                : Enumerable.Empty<string>();
        }
    }
}
