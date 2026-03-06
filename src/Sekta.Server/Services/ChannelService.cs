using Microsoft.EntityFrameworkCore;
using Sekta.Server.Data;
using Sekta.Server.Models;
using Sekta.Shared.DTOs;

namespace Sekta.Server.Services;

public interface IChannelService
{
    Task<ChannelDto> CreateChannel(Guid userId, CreateChannelDto dto);
    Task<ChannelDto> GetChannel(Guid channelId);
    Task<List<ChannelDto>> GetUserChannels(Guid userId);
    Task Subscribe(Guid channelId, Guid userId);
    Task Unsubscribe(Guid channelId, Guid userId);
    Task<ChannelPostDto> CreatePost(Guid channelId, Guid userId, CreateChannelPostDto dto);
    Task<List<ChannelPostDto>> GetPosts(Guid channelId, int page, int pageSize);
}

public class ChannelService : IChannelService
{
    private readonly AppDbContext _db;

    public ChannelService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<ChannelDto> CreateChannel(Guid userId, CreateChannelDto dto)
    {
        var channel = new Channel
        {
            Id = Guid.NewGuid(),
            Title = dto.Title,
            Description = dto.Description,
            OwnerId = userId,
            CreatedAt = DateTime.UtcNow
        };

        _db.Channels.Add(channel);

        _db.ChannelSubscribers.Add(new ChannelSubscriber
        {
            Id = Guid.NewGuid(),
            ChannelId = channel.Id,
            UserId = userId,
            JoinedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        return new ChannelDto(
            channel.Id,
            channel.Title,
            channel.Description,
            channel.AvatarUrl,
            channel.OwnerId,
            1,
            channel.CreatedAt
        );
    }

    public async Task<ChannelDto> GetChannel(Guid channelId)
    {
        var channel = await _db.Channels
            .Include(c => c.Subscribers)
            .FirstOrDefaultAsync(c => c.Id == channelId);

        if (channel is null)
            throw new Exception("Channel not found.");

        return new ChannelDto(
            channel.Id,
            channel.Title,
            channel.Description,
            channel.AvatarUrl,
            channel.OwnerId,
            channel.Subscribers.Count,
            channel.CreatedAt
        );
    }

    public async Task<List<ChannelDto>> GetUserChannels(Guid userId)
    {
        var channelIds = await _db.ChannelSubscribers
            .Where(cs => cs.UserId == userId)
            .Select(cs => cs.ChannelId)
            .ToListAsync();

        var channels = await _db.Channels
            .Where(c => channelIds.Contains(c.Id))
            .Include(c => c.Subscribers)
            .OrderBy(c => c.Title)
            .ToListAsync();

        return channels.Select(c => new ChannelDto(
            c.Id,
            c.Title,
            c.Description,
            c.AvatarUrl,
            c.OwnerId,
            c.Subscribers.Count,
            c.CreatedAt
        )).ToList();
    }

    public async Task Subscribe(Guid channelId, Guid userId)
    {
        var exists = await _db.Channels.AnyAsync(c => c.Id == channelId);
        if (!exists)
            throw new Exception("Channel not found.");

        var alreadySubscribed = await _db.ChannelSubscribers
            .AnyAsync(cs => cs.ChannelId == channelId && cs.UserId == userId);
        if (alreadySubscribed)
            throw new Exception("Already subscribed to this channel.");

        _db.ChannelSubscribers.Add(new ChannelSubscriber
        {
            Id = Guid.NewGuid(),
            ChannelId = channelId,
            UserId = userId,
            JoinedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
    }

    public async Task Unsubscribe(Guid channelId, Guid userId)
    {
        var subscription = await _db.ChannelSubscribers
            .FirstOrDefaultAsync(cs => cs.ChannelId == channelId && cs.UserId == userId);

        if (subscription is null)
            throw new Exception("Not subscribed to this channel.");

        var channel = await _db.Channels.FindAsync(channelId);
        if (channel is not null && channel.OwnerId == userId)
            throw new Exception("Channel owner cannot unsubscribe. Transfer ownership first.");

        _db.ChannelSubscribers.Remove(subscription);
        await _db.SaveChangesAsync();
    }

    public async Task<ChannelPostDto> CreatePost(Guid channelId, Guid userId, CreateChannelPostDto dto)
    {
        var channel = await _db.Channels.FindAsync(channelId);
        if (channel is null)
            throw new Exception("Channel not found.");

        if (channel.OwnerId != userId)
            throw new Exception("Only the channel owner can create posts.");

        var post = new ChannelPost
        {
            Id = Guid.NewGuid(),
            ChannelId = channelId,
            Content = dto.Content,
            MediaUrl = dto.MediaUrl,
            CreatedAt = DateTime.UtcNow
        };

        _db.ChannelPosts.Add(post);
        await _db.SaveChangesAsync();

        return new ChannelPostDto(
            post.Id,
            post.ChannelId,
            post.Content,
            post.MediaUrl,
            post.CreatedAt
        );
    }

    public async Task<List<ChannelPostDto>> GetPosts(Guid channelId, int page, int pageSize)
    {
        var exists = await _db.Channels.AnyAsync(c => c.Id == channelId);
        if (!exists)
            throw new Exception("Channel not found.");

        var posts = await _db.ChannelPosts
            .Where(p => p.ChannelId == channelId)
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return posts.Select(p => new ChannelPostDto(
            p.Id,
            p.ChannelId,
            p.Content,
            p.MediaUrl,
            p.CreatedAt
        )).ToList();
    }
}
