using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Sekta.Server.Hubs;
using Sekta.Server.Services;
using Sekta.Shared.DTOs;

namespace Sekta.Server.Controllers;

[ApiController]
[Route("api/chats")]
[Authorize]
public class ChatsController : ControllerBase
{
    private readonly IChatService _chatService;
    private readonly IHubContext<ChatHub> _hubContext;

    public ChatsController(IChatService chatService, IHubContext<ChatHub> hubContext)
    {
        _chatService = chatService;
        _hubContext = hubContext;
    }

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<ActionResult<List<ChatDto>>> GetChats()
    {
        var chats = await _chatService.GetUserChats(GetUserId());
        return Ok(chats);
    }

    [HttpGet("{chatId:guid}")]
    public async Task<ActionResult<ChatDto>> GetChat(Guid chatId)
    {
        var chat = await _chatService.GetChatById(chatId, GetUserId());
        if (chat is null) return NotFound();
        return Ok(chat);
    }

    [HttpPut("{chatId:guid}")]
    public async Task<IActionResult> UpdateGroupChat(Guid chatId, UpdateGroupChatDto dto)
    {
        await _chatService.UpdateGroupChat(chatId, GetUserId(), dto);

        // Notify all members about chat info change
        await _hubContext.Clients.Group(chatId.ToString())
            .SendAsync("ChatUpdated", chatId, dto.Title, dto.AvatarUrl);

        return Ok();
    }

    [HttpPost("private/{targetUserId:guid}")]
    public async Task<ActionResult<ChatDto>> GetOrCreatePrivateChat(Guid targetUserId)
    {
        var chat = await _chatService.GetOrCreatePrivateChat(GetUserId(), targetUserId);
        return Ok(chat);
    }

    [HttpPost("group")]
    public async Task<ActionResult<ChatDto>> CreateGroupChat(CreateGroupChatDto dto)
    {
        var chat = await _chatService.CreateGroupChat(GetUserId(), dto);

        // Notify all members (except creator) about the new group
        foreach (var memberId in dto.MemberIds.Where(id => id != GetUserId()))
        {
            foreach (var connId in ChatHub.GetUserConnections(memberId.ToString()))
            {
                await _hubContext.Groups.AddToGroupAsync(connId, chat.Id.ToString());
                await _hubContext.Clients.Client(connId).SendAsync("AddedToChat", chat);
            }
        }

        // Add creator's connections to the SignalR group
        foreach (var connId in ChatHub.GetUserConnections(GetUserId().ToString()))
        {
            await _hubContext.Groups.AddToGroupAsync(connId, chat.Id.ToString());
        }

        return Ok(chat);
    }

    [HttpGet("{chatId:guid}/messages")]
    public async Task<ActionResult<List<MessageDto>>> GetMessages(
        Guid chatId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var messages = await _chatService.GetChatMessages(chatId, GetUserId(), page, pageSize);
        return Ok(messages);
    }

    [HttpGet("{chatId:guid}/members")]
    public async Task<ActionResult<List<ChatMemberDto>>> GetMembers(Guid chatId)
    {
        var members = await _chatService.GetChatMembers(chatId);
        return Ok(members);
    }

    [HttpPost("{chatId:guid}/members/{userId:guid}")]
    public async Task<IActionResult> AddMember(Guid chatId, Guid userId)
    {
        await _chatService.AddMemberToGroup(chatId, GetUserId(), userId);

        // Get chat info to send to the new member
        var chat = await _chatService.GetChatById(chatId, userId);
        if (chat is not null)
        {
            // Notify the added user on all their connections
            foreach (var connId in ChatHub.GetUserConnections(userId.ToString()))
            {
                await _hubContext.Groups.AddToGroupAsync(connId, chatId.ToString());
                await _hubContext.Clients.Client(connId).SendAsync("AddedToChat", chat);
            }
        }

        return Ok();
    }

    [HttpDelete("{chatId:guid}/members/{userId:guid}")]
    public async Task<IActionResult> RemoveMember(Guid chatId, Guid userId)
    {
        await _chatService.RemoveMemberFromGroup(chatId, GetUserId(), userId);
        return Ok();
    }

    [HttpPost("{chatId:guid}/read")]
    public async Task<IActionResult> MarkAsRead(Guid chatId)
    {
        await _chatService.MarkAsRead(chatId, GetUserId());
        return Ok();
    }

    [HttpPost("{chatId:guid}/pin")]
    public async Task<ActionResult<bool>> TogglePin(Guid chatId)
    {
        var isPinned = await _chatService.TogglePinChat(chatId, GetUserId());
        return Ok(isPinned);
    }
}
