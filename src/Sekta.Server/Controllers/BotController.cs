using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Sekta.Server.Hubs;
using Sekta.Server.Services;
using Sekta.Shared.DTOs;
using Sekta.Shared.Enums;

namespace Sekta.Server.Controllers;

[ApiController]
[Route("api/bot")]
public class BotController : ControllerBase
{
    private readonly IChatService _chatService;
    private readonly IHubContext<ChatHub> _chatHub;

    public BotController(IChatService chatService, IHubContext<ChatHub> chatHub)
    {
        _chatService = chatService;
        _chatHub = chatHub;
    }

    [HttpPost("send")]
    public async Task<ActionResult<MessageDto>> SendMessage(
        [FromHeader(Name = "X-Bot-Token")] string botToken,
        [FromBody] BotSendMessageRequest request)
    {
        // Simple bot token validation — in production, use DB-stored bot tokens
        if (string.IsNullOrEmpty(botToken))
            return Unauthorized(new { message = "Bot token required" });

        var dto = new SendMessageDto(
            request.ChatId,
            request.Text,
            MessageType.Text,
            null, null, null, null
        );

        var message = await _chatService.SendMessage(request.BotUserId, dto);
        await _chatHub.Clients.Group(request.ChatId.ToString()).SendAsync("ReceiveMessage", message);

        return Ok(message);
    }
}

public record BotSendMessageRequest(Guid ChatId, Guid BotUserId, string Text);
