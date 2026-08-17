namespace AIChat.Models.Dto;

/// <summary>
/// 聊天会话列表项 DTO
/// </summary>
public class ChatSessionListDto
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }
    public int MessageCount { get; set; }
}

/// <summary>
/// 聊天会话详情 DTO
/// </summary>
public class ChatSessionDto
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }
    public List<MessageDto> Messages { get; set; } = new();
}

/// <summary>
/// 消息 DTO
/// </summary>
public class MessageDto
{
    public string Id { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? FileId { get; set; }
    public string? ThumbnailUrl { get; set; }
    public long Timestamp { get; set; }
}

/// <summary>
/// 创建会话请求
/// </summary>
public class CreateSessionRequest
{
    public string? ClientId { get; set; }
    public string? Title { get; set; }
}

/// <summary>
/// 创建会话响应
/// </summary>
public class CreateSessionResponse
{
    public bool Success { get; set; }
    public string? SessionId { get; set; }
    public string? Message { get; set; }
}

/// <summary>
/// 保存消息请求
/// </summary>
public class SaveMessageRequest
{
    public string SessionId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? FileId { get; set; }
}

/// <summary>
/// 保存消息响应
/// </summary>
public class SaveMessageResponse
{
    public bool Success { get; set; }
    public string? MessageId { get; set; }
    public string? Message { get; set; }
}

/// <summary>
/// 上传文件请求
/// </summary>
public class UploadFileRequest
{
    public string? ClientId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long Size { get; set; }
    public string Base64Data { get; set; } = string.Empty;
}

/// <summary>
/// 上传文件响应
/// </summary>
public class UploadFileResponse
{
    public bool Success { get; set; }
    public string? FileId { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? Message { get; set; }
}

/// <summary>
/// 获取文件响应
/// </summary>
public class FileDataResponse
{
    public bool Success { get; set; }
    public string? FileId { get; set; }
    public string? FileName { get; set; }
    public string? ContentType { get; set; }
    public long Size { get; set; }
    public string? Base64Data { get; set; }
    public string? Message { get; set; }
}

/// <summary>
/// 更新会话标题请求
/// </summary>
public class UpdateSessionTitleRequest
{
    public string SessionId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
}

/// <summary>
/// 通用API响应
/// </summary>
public class ApiResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
}

/// <summary>
/// 同步数据请求（用于迁移本地数据到服务器）
/// </summary>
public class SyncDataRequest
{
    public string? ClientId { get; set; }
    public List<SyncSessionData> Sessions { get; set; } = new();
}

/// <summary>
/// 同步会话数据
/// </summary>
public class SyncSessionData
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }
    public List<SyncMessageData> Messages { get; set; } = new();
}

/// <summary>
/// 同步消息数据
/// </summary>
public class SyncMessageData
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? ImageBase64 { get; set; }
    public long Timestamp { get; set; }
}

/// <summary>
/// 同步响应
/// </summary>
public class SyncResponse
{
    public bool Success { get; set; }
    public int SessionsImported { get; set; }
    public int MessagesImported { get; set; }
    public int FilesImported { get; set; }
    public string? Message { get; set; }
}
