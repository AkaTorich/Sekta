using Microsoft.EntityFrameworkCore;
using Sekta.Server.Data;
using Sekta.Server.Models;
using Sekta.Shared.DTOs;
using Sekta.Shared.Enums;

namespace Sekta.Server.Services;

public interface IUserService
{
    Task<UserDto> GetUser(Guid userId);
    Task<List<UserSearchResultDto>> SearchUsers(string query);
    Task<UserDto> UpdateProfile(Guid userId, UpdateProfileDto dto);
    Task<List<UserDto>> GetContacts(Guid userId);
    Task AddContact(Guid userId, Guid contactId);
    Task RemoveContact(Guid userId, Guid contactId);
    Task SetOnline(Guid userId);
    Task SetOffline(Guid userId);
}

public class UserService : IUserService
{
    private readonly AppDbContext _db;

    public UserService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<UserDto> GetUser(Guid userId)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user is null)
            throw new Exception("User not found.");

        return new UserDto(
            user.Id,
            user.Username,
            user.DisplayName,
            user.AvatarUrl,
            user.Bio,
            user.Status,
            user.LastSeen
        );
    }

    public async Task<List<UserSearchResultDto>> SearchUsers(string query)
    {
        var normalizedQuery = query.ToLower();

        var users = await _db.Users
            .Where(u => u.Username.ToLower().Contains(normalizedQuery)
                     || (u.DisplayName != null && u.DisplayName.ToLower().Contains(normalizedQuery)))
            .Take(20)
            .ToListAsync();

        return users.Select(u => new UserSearchResultDto(
            u.Id,
            u.Username,
            u.DisplayName,
            u.AvatarUrl
        )).ToList();
    }

    public async Task<UserDto> UpdateProfile(Guid userId, UpdateProfileDto dto)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user is null)
            throw new Exception("User not found.");

        if (dto.DisplayName is not null)
            user.DisplayName = dto.DisplayName;

        if (dto.Bio is not null)
            user.Bio = dto.Bio;

        if (dto.AvatarUrl is not null)
            user.AvatarUrl = dto.AvatarUrl;

        await _db.SaveChangesAsync();

        return new UserDto(
            user.Id,
            user.Username,
            user.DisplayName,
            user.AvatarUrl,
            user.Bio,
            user.Status,
            user.LastSeen
        );
    }

    public async Task<List<UserDto>> GetContacts(Guid userId)
    {
        var contacts = await _db.Contacts
            .Where(c => c.UserId == userId)
            .Include(c => c.ContactUser)
            .ToListAsync();

        return contacts.Select(c => new UserDto(
            c.ContactUser.Id,
            c.ContactUser.Username,
            c.ContactUser.DisplayName,
            c.ContactUser.AvatarUrl,
            c.ContactUser.Bio,
            c.ContactUser.Status,
            c.ContactUser.LastSeen
        )).ToList();
    }

    public async Task AddContact(Guid userId, Guid contactId)
    {
        if (userId == contactId)
            throw new Exception("Cannot add yourself as a contact.");

        var contactUser = await _db.Users.FindAsync(contactId);
        if (contactUser is null)
            throw new Exception("User not found.");

        var alreadyExists = await _db.Contacts
            .AnyAsync(c => c.UserId == userId && c.ContactUserId == contactId);

        if (!alreadyExists)
        {
            _db.Contacts.Add(new Contact
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                ContactUserId = contactId,
                CreatedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();
        }
    }

    public async Task RemoveContact(Guid userId, Guid contactId)
    {
        var contact = await _db.Contacts
            .FirstOrDefaultAsync(c => c.UserId == userId && c.ContactUserId == contactId);

        if (contact is null)
            throw new Exception("Contact not found.");

        _db.Contacts.Remove(contact);
        await _db.SaveChangesAsync();
    }

    public async Task SetOnline(Guid userId)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user is null) return;

        user.Status = UserStatus.Online;
        user.LastSeen = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task SetOffline(Guid userId)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user is null) return;

        user.Status = UserStatus.Offline;
        user.LastSeen = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }
}
