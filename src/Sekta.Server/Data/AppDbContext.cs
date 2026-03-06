using Microsoft.EntityFrameworkCore;
using Sekta.Server.Models;

namespace Sekta.Server.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Chat> Chats => Set<Chat>();
    public DbSet<ChatMember> ChatMembers => Set<ChatMember>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<ChannelSubscriber> ChannelSubscribers => Set<ChannelSubscriber>();
    public DbSet<ChannelPost> ChannelPosts => Set<ChannelPost>();
    public DbSet<StickerPack> StickerPacks => Set<StickerPack>();
    public DbSet<Sticker> Stickers => Set<Sticker>();
    public DbSet<ChatFolder> ChatFolders => Set<ChatFolder>();
    public DbSet<ChatFolderChat> ChatFolderChats => Set<ChatFolderChat>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── User ──
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Username).IsUnique();
            entity.HasIndex(e => e.Email).IsUnique();

            entity.Property(e => e.Username).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(256);
            entity.Property(e => e.PasswordHash).IsRequired();
            entity.Property(e => e.DisplayName).HasMaxLength(100);
            entity.Property(e => e.AvatarUrl).HasMaxLength(512);
            entity.Property(e => e.Bio).HasMaxLength(500);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.DeviceToken).HasMaxLength(512);
            entity.Property(e => e.Platform).HasMaxLength(20);
        });

        // ── Chat ──
        modelBuilder.Entity<Chat>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Type).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.Title).HasMaxLength(200);
            entity.Property(e => e.AvatarUrl).HasMaxLength(512);
        });

        // ── ChatMember ──
        modelBuilder.Entity<ChatMember>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ChatId, e.UserId }).IsUnique();
            entity.Property(e => e.Role).HasConversion<string>().HasMaxLength(20);

            entity.HasOne(e => e.Chat)
                .WithMany(c => c.Members)
                .HasForeignKey(e => e.ChatId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.User)
                .WithMany(u => u.ChatMembers)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Message ──
        modelBuilder.Entity<Message>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ChatId);
            entity.HasIndex(e => e.SenderId);

            entity.Property(e => e.Type).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.MediaUrl).HasMaxLength(512);
            entity.Property(e => e.FileName).HasMaxLength(256);

            entity.Property(e => e.LinkPreviewUrl).HasMaxLength(2048);
            entity.Property(e => e.LinkPreviewTitle).HasMaxLength(500);
            entity.Property(e => e.LinkPreviewDescription).HasMaxLength(1000);
            entity.Property(e => e.LinkPreviewImageUrl).HasMaxLength(2048);
            entity.Property(e => e.LinkPreviewDomain).HasMaxLength(256);

            entity.HasOne(e => e.Chat)
                .WithMany(c => c.Messages)
                .HasForeignKey(e => e.ChatId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Sender)
                .WithMany(u => u.SentMessages)
                .HasForeignKey(e => e.SenderId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.ReplyTo)
                .WithMany()
                .HasForeignKey(e => e.ReplyToId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ── Contact ──
        modelBuilder.Entity<Contact>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.ContactUserId }).IsUnique();

            entity.HasOne(e => e.User)
                .WithMany(u => u.Contacts)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.ContactUser)
                .WithMany()
                .HasForeignKey(e => e.ContactUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── Channel ──
        modelBuilder.Entity<Channel>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Description).HasMaxLength(1000);
            entity.Property(e => e.AvatarUrl).HasMaxLength(512);

            entity.HasOne(e => e.Owner)
                .WithMany(u => u.OwnedChannels)
                .HasForeignKey(e => e.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── ChannelSubscriber ──
        modelBuilder.Entity<ChannelSubscriber>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ChannelId, e.UserId }).IsUnique();

            entity.HasOne(e => e.Channel)
                .WithMany(c => c.Subscribers)
                .HasForeignKey(e => e.ChannelId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── ChannelPost ──
        modelBuilder.Entity<ChannelPost>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ChannelId);
            entity.Property(e => e.MediaUrl).HasMaxLength(512);

            entity.HasOne(e => e.Channel)
                .WithMany(c => c.Posts)
                .HasForeignKey(e => e.ChannelId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── StickerPack ──
        modelBuilder.Entity<StickerPack>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(100);

            entity.HasOne(e => e.Author)
                .WithMany()
                .HasForeignKey(e => e.AuthorId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── Sticker ──
        modelBuilder.Entity<Sticker>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ImageUrl).IsRequired().HasMaxLength(512);

            entity.HasOne(e => e.Pack)
                .WithMany(p => p.Stickers)
                .HasForeignKey(e => e.PackId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── ChatFolder ──
        modelBuilder.Entity<ChatFolder>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(10);
            entity.Property(e => e.Icon).HasMaxLength(50);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── ChatFolderChat ──
        modelBuilder.Entity<ChatFolderChat>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.FolderId, e.ChatId }).IsUnique();

            entity.HasOne(e => e.Folder)
                .WithMany(f => f.Chats)
                .HasForeignKey(e => e.FolderId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Chat)
                .WithMany()
                .HasForeignKey(e => e.ChatId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
