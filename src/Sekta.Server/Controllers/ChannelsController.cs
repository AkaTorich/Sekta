using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sekta.Server.Services;
using Sekta.Shared.DTOs;

namespace Sekta.Server.Controllers;

[ApiController]
[Route("api/channels")]
[Authorize]
public class ChannelsController : ControllerBase
{
    private readonly IChannelService _channelService;

    public ChannelsController(IChannelService channelService)
    {
        _channelService = channelService;
    }

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpPost]
    public async Task<ActionResult<ChannelDto>> Create(CreateChannelDto dto)
    {
        var channel = await _channelService.CreateChannel(GetUserId(), dto);
        return Ok(channel);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ChannelDto>> Get(Guid id)
    {
        var channel = await _channelService.GetChannel(id);
        return Ok(channel);
    }

    [HttpGet]
    public async Task<ActionResult<List<ChannelDto>>> GetUserChannels()
    {
        var channels = await _channelService.GetUserChannels(GetUserId());
        return Ok(channels);
    }

    [HttpPost("{id:guid}/subscribe")]
    public async Task<IActionResult> Subscribe(Guid id)
    {
        await _channelService.Subscribe(id, GetUserId());
        return Ok();
    }

    [HttpDelete("{id:guid}/subscribe")]
    public async Task<IActionResult> Unsubscribe(Guid id)
    {
        await _channelService.Unsubscribe(id, GetUserId());
        return Ok();
    }

    [HttpPost("{id:guid}/posts")]
    public async Task<ActionResult<ChannelPostDto>> CreatePost(Guid id, CreateChannelPostDto dto)
    {
        var post = await _channelService.CreatePost(id, GetUserId(), dto);
        return Ok(post);
    }

    [HttpGet("{id:guid}/posts")]
    public async Task<ActionResult<List<ChannelPostDto>>> GetPosts(
        Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var posts = await _channelService.GetPosts(id, page, pageSize);
        return Ok(posts);
    }
}
