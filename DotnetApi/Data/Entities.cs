using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AIChat.Data.Entities;

/// <summary>
/// 用户实体
/// </summary>
public class User
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// 用户唯一标识（用于前端识别，可以是设备ID或UUID）
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// 用户名（可选）
    /// </summary>
    [MaxLength(50)]
    public string? Username { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 最后活跃时间
    /// </summary>
    public DateTime LastActiveAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 用户的聊天会话列表
    /// </summary>
    public virtual ICollection<ChatSession> ChatSessions { get; set; } = new List<ChatSession>();
}

/// <summary>
/// 聊天会话实体
/// </summary>
public class ChatSession
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// 会话唯一标识（供前端使用）
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// 所属用户ID
    /// </summary>
    public int UserId { get; set; }

    /// <summary>
    /// 会话标题
    /// </summary>
    [MaxLength(200)]
    public string Title { get; set; } = "新对话";

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 最后更新时间
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 是否已删除（软删除）
    /// </summary>
    public bool IsDeleted { get; set; } = false;

    /// <summary>
    /// 所属用户
    /// </summary>
    [ForeignKey("UserId")]
    public virtual User? User { get; set; }

    /// <summary>
    /// 会话消息列表
    /// </summary>
    public virtual ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
}

/// <summary>
/// 聊天消息实体
/// </summary>
public class ChatMessage
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// 消息唯一标识（供前端使用）
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string MessageId { get; set; } = string.Empty;

    /// <summary>
    /// 所属会话ID
    /// </summary>
    public int SessionId { get; set; }

    /// <summary>
    /// 消息角色（user/assistant）
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// 消息文本内容
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 关联的文件ID（如果有附件）
    /// </summary>
    public int? FileId { get; set; }

    /// <summary>
    /// 消息时间戳
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 消息顺序（用于排序）
    /// </summary>
    public int OrderIndex { get; set; }

    /// <summary>
    /// 所属会话
    /// </summary>
    [ForeignKey("SessionId")]
    public virtual ChatSession? Session { get; set; }

    /// <summary>
    /// 关联的文件
    /// </summary>
    [ForeignKey("FileId")]
    public virtual ChatFile? File { get; set; }
}

/// <summary>
/// 文件实体（用于存储图片等附件）
/// </summary>
public class ChatFile
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// 文件唯一标识（供前端使用）
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string FileId { get; set; } = string.Empty;

    /// <summary>
    /// 所属用户ID
    /// </summary>
    public int UserId { get; set; }

    /// <summary>
    /// 原始文件名
    /// </summary>
    [MaxLength(255)]
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// 文件MIME类型
    /// </summary>
    [MaxLength(100)]
    public string ContentType { get; set; } = string.Empty;

    /// <summary>
    /// 文件大小（字节）
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// 文件内容（Base64编码）
    /// </summary>
    public string Base64Data { get; set; } = string.Empty;

    /// <summary>
    /// 缩略图（Base64编码，用于列表显示）
    /// </summary>
    public string? ThumbnailBase64 { get; set; }

    /// <summary>
    /// 上传时间
    /// </summary>
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 所属用户
    /// </summary>
    [ForeignKey("UserId")]
    public virtual User? User { get; set; }
}
