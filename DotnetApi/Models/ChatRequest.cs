namespace AIChat.Models;

/// <summary>
/// 聊天请求模型
/// </summary>
public class ChatRequest
{
    /// <summary>
    /// 用户消息内容
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// 历史消息记录
    /// </summary>
    public List<ChatMessage> History { get; set; } = new();

    /// <summary>
    /// 单张图片数据（Base64格式，可选，向后兼容）
    /// </summary>
    public string? Image { get; set; }

    /// <summary>
    /// 多张图片数据（Base64格式，可选）
    /// </summary>
    public List<string>? Images { get; set; }

    /// <summary>
    /// 文件数据列表（PDF、Word、Excel、PPT等）
    /// </summary>
    public List<FileData>? Files { get; set; }
}

/// <summary>
/// 文件数据模型
/// </summary>
public class FileData
{
    /// <summary>
    /// 文件名
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// 文件类型（MIME类型）
    /// </summary>
    public string FileType { get; set; } = string.Empty;

    /// <summary>
    /// 文件内容（Base64编码）
    /// </summary>
    public string Base64Data { get; set; } = string.Empty;
}

/// <summary>
/// 聊天消息模型
/// </summary>
public class ChatMessage
{
    /// <summary>
    /// 消息角色（user 或 assistant）
    /// </summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// 消息内容
    /// </summary>
    public string Content { get; set; } = string.Empty;
}

/// <summary>
/// 文件上传请求模型（Base64格式）
/// </summary>
public class FileUploadRequest
{
    /// <summary>
    /// 会话ID（用于缓存隔离）
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// 文件名
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// 文件类型（MIME类型）
    /// </summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>
    /// 文件大小（字节）
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// 文件内容（Base64编码）
    /// </summary>
    public string Base64Data { get; set; } = string.Empty;
}

/// <summary>
/// 文件上传响应模型
/// </summary>
public class FileUploadResponse
{
    /// <summary>
    /// 是否成功
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 响应消息
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// 文件ID（用于后续引用）
    /// </summary>
    public string? FileId { get; set; }

    /// <summary>
    /// 文件哈希（用于去重检查）
    /// </summary>
    public string? Hash { get; set; }

    /// <summary>
    /// 文件名
    /// </summary>
    public string? FileName { get; set; }

    /// <summary>
    /// Base64数据（用于前端显示）
    /// </summary>
    public string? Base64Data { get; set; }

    /// <summary>
    /// 是否命中缓存
    /// </summary>
    public bool FromCache { get; set; }
}

/// <summary>
/// 文件缓存检查请求
/// </summary>
public class FileCheckRequest
{
    /// <summary>
    /// 会话ID
    /// </summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// 文件哈希值列表
    /// </summary>
    public List<string> Hashes { get; set; } = new();
}

/// <summary>
/// 文件缓存检查响应
/// </summary>
public class FileCheckResponse
{
    /// <summary>
    /// 是否成功
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 已缓存的文件信息（Hash -> FileInfo）
    /// </summary>
    public Dictionary<string, CachedFileInfo> CachedFiles { get; set; } = new();
}

/// <summary>
/// 缓存文件信息
/// </summary>
public class CachedFileInfo
{
    /// <summary>
    /// 文件ID
    /// </summary>
    public string FileId { get; set; } = string.Empty;

    /// <summary>
    /// 文件名
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// 内容类型
    /// </summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>
    /// 文件大小
    /// </summary>
    public long Size { get; set; }
}
