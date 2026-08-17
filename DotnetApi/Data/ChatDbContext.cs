using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using AIChat.Data.Entities;

namespace AIChat.Data;

/// <summary>
/// 聊天应用数据库上下文
/// </summary>
public class ChatDbContext : DbContext
{
    public ChatDbContext(DbContextOptions<ChatDbContext> options) : base(options)
    {
    }

    /// <summary>
    /// 配置所有 DateTime 属性，确保从数据库读取时指定为 UTC Kind
    /// SQLite 读取 DateTime 后 Kind 为 Unspecified，需要明确指定为 UTC
    /// </summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>()
            .HaveConversion<UtcDateTimeConverter>();
    }

    /// <summary>
    /// UTC DateTime 转换器
    /// </summary>
    private class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
    {
        public UtcDateTimeConverter() : base(
            v => v.Kind == DateTimeKind.Utc ? v : DateTime.SpecifyKind(v, DateTimeKind.Utc),
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
        {
        }
    }

    /// <summary>
    /// 用户表
    /// </summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>
    /// 聊天会话表
    /// </summary>
    public DbSet<ChatSession> ChatSessions => Set<ChatSession>();

    /// <summary>
    /// 聊天消息表
    /// </summary>
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();

    /// <summary>
    /// 文件表
    /// </summary>
    public DbSet<ChatFile> ChatFiles => Set<ChatFile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // User 配置
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(e => e.ClientId).IsUnique();
        });

        // ChatSession 配置
        modelBuilder.Entity<ChatSession>(entity =>
        {
            entity.HasIndex(e => e.SessionId).IsUnique();
            entity.HasIndex(e => new { e.UserId, e.IsDeleted, e.UpdatedAt });

            entity.HasOne(e => e.User)
                .WithMany(u => u.ChatSessions)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ChatMessage 配置
        modelBuilder.Entity<ChatMessage>(entity =>
        {
            entity.HasIndex(e => e.MessageId).IsUnique();
            entity.HasIndex(e => new { e.SessionId, e.OrderIndex });

            entity.HasOne(e => e.Session)
                .WithMany(s => s.Messages)
                .HasForeignKey(e => e.SessionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.File)
                .WithMany()
                .HasForeignKey(e => e.FileId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ChatFile 配置
        modelBuilder.Entity<ChatFile>(entity =>
        {
            entity.HasIndex(e => e.FileId).IsUnique();
            entity.HasIndex(e => e.UserId);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
