using Microsoft.EntityFrameworkCore;
using AIChat.Data;
using AIChat.Data.Entities;
using AIChat.Models.Dto;
using SkiaSharp;

namespace AIChat.Services;

/// <summary>
/// 聊天数据服务
/// </summary>
public class ChatDataService
{
    private readonly ChatDbContext _context;
    private readonly ILogger<ChatDataService> _logger;

    public ChatDataService(ChatDbContext context, ILogger<ChatDataService> logger)
    {
        _context = context;
        _logger = logger;
    }

    #region 用户管理

    /// <summary>
    /// 获取或创建用户
    /// </summary>
    public async Task<User> GetOrCreateUserAsync(string clientId)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.ClientId == clientId);
        if (user == null)
        {
            user = new User
            {
                ClientId = clientId,
                CreatedAt = DateTime.UtcNow,
                LastActiveAt = DateTime.UtcNow
            };
            _context.Users.Add(user);
            await _context.SaveChangesAsync();
        }
        else
        {
            user.LastActiveAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
        return user;
    }

    #endregion

    #region 会话管理

    /// <summary>
    /// 获取用户的所有会话列表
    /// </summary>
    public async Task<List<ChatSessionListDto>> GetSessionsAsync(string clientId)
    {
        var user = await GetOrCreateUserAsync(clientId);

        return await _context.ChatSessions
            .Where(s => s.UserId == user.Id && !s.IsDeleted)
            .OrderByDescending(s => s.UpdatedAt)
            .Select(s => new ChatSessionListDto
            {
                Id = s.SessionId,
                Title = s.Title,
                CreatedAt = new DateTimeOffset(s.CreatedAt).ToUnixTimeMilliseconds(),
                UpdatedAt = new DateTimeOffset(s.UpdatedAt).ToUnixTimeMilliseconds(),
                MessageCount = s.Messages.Count
            })
            .ToListAsync();
    }

    /// <summary>
    /// 获取会话详情（包含消息）
    /// </summary>
    public async Task<ChatSessionDto?> GetSessionAsync(string sessionId)
    {
        var session = await _context.ChatSessions
            .Include(s => s.Messages.OrderBy(m => m.OrderIndex))
            .ThenInclude(m => m.File)
            .FirstOrDefaultAsync(s => s.SessionId == sessionId && !s.IsDeleted);

        if (session == null) return null;

        return new ChatSessionDto
        {
            Id = session.SessionId,
            Title = session.Title,
            CreatedAt = new DateTimeOffset(session.CreatedAt).ToUnixTimeMilliseconds(),
            UpdatedAt = new DateTimeOffset(session.UpdatedAt).ToUnixTimeMilliseconds(),
            Messages = session.Messages.Select(m => new MessageDto
            {
                Id = m.MessageId,
                Role = m.Role,
                Content = m.Content,
                FileId = m.File?.FileId,
                ThumbnailUrl = m.File?.ThumbnailBase64 != null
                    ? $"data:{m.File.ContentType};base64,{m.File.ThumbnailBase64}"
                    : null,
                Timestamp = new DateTimeOffset(m.Timestamp).ToUnixTimeMilliseconds()
            }).ToList()
        };
    }

    /// <summary>
    /// 创建新会话
    /// </summary>
    public async Task<string> CreateSessionAsync(string clientId, string? title = null)
    {
        var user = await GetOrCreateUserAsync(clientId);

        var session = new ChatSession
        {
            SessionId = Guid.NewGuid().ToString("N"),
            UserId = user.Id,
            Title = title ?? "新对话",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.ChatSessions.Add(session);

        // 添加欢迎消息
        var welcomeMessage = new ChatMessage
        {
            MessageId = Guid.NewGuid().ToString("N"),
            SessionId = session.Id,
            Role = "assistant",
            Content = "你好！我是 AI 助手，有什么可以帮助你的吗？",
            Timestamp = DateTime.UtcNow,
            OrderIndex = 0
        };
        session.Messages.Add(welcomeMessage);

        await _context.SaveChangesAsync();

        return session.SessionId;
    }

    /// <summary>
    /// 更新会话标题
    /// </summary>
    public async Task<bool> UpdateSessionTitleAsync(string sessionId, string title)
    {
        var session = await _context.ChatSessions
            .FirstOrDefaultAsync(s => s.SessionId == sessionId && !s.IsDeleted);

        if (session == null) return false;

        session.Title = title;
        session.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return true;
    }

    /// <summary>
    /// 删除会话（软删除）
    /// </summary>
    public async Task<bool> DeleteSessionAsync(string sessionId)
    {
        var session = await _context.ChatSessions
            .FirstOrDefaultAsync(s => s.SessionId == sessionId);

        if (session == null) return false;

        session.IsDeleted = true;
        await _context.SaveChangesAsync();

        return true;
    }

    #endregion

    #region 消息管理

    /// <summary>
    /// 保存消息
    /// </summary>
    public async Task<string?> SaveMessageAsync(string sessionId, string role, string content, string? fileId = null)
    {
        var session = await _context.ChatSessions
            .Include(s => s.Messages)
            .FirstOrDefaultAsync(s => s.SessionId == sessionId && !s.IsDeleted);

        if (session == null) return null;

        int? chatFileId = null;
        if (!string.IsNullOrEmpty(fileId))
        {
            var file = await _context.ChatFiles.FirstOrDefaultAsync(f => f.FileId == fileId);
            chatFileId = file?.Id;
        }

        var message = new ChatMessage
        {
            MessageId = Guid.NewGuid().ToString("N"),
            SessionId = session.Id,
            Role = role,
            Content = content,
            FileId = chatFileId,
            Timestamp = DateTime.UtcNow,
            OrderIndex = session.Messages.Count
        };

        _context.ChatMessages.Add(message);

        // 更新会话时间和标题
        session.UpdatedAt = DateTime.UtcNow;

        // 如果是第一条用户消息且标题是默认的，自动更新标题
        if (role == "user" && session.Title == "新对话")
        {
            var plainText = System.Text.RegularExpressions.Regex.Replace(content, "<[^>]*>", "");
            session.Title = plainText.Length > 20 ? plainText[..20] + "..." : plainText;
        }

        await _context.SaveChangesAsync();

        return message.MessageId;
    }

    #endregion

    #region 文件管理

    /// <summary>
    /// 保存文件并生成缩略图
    /// </summary>
    public async Task<(string? fileId, string? thumbnailUrl)> SaveFileAsync(
        string clientId,
        string fileName,
        string contentType,
        long size,
        string base64Data)
    {
        try
        {
            var user = await GetOrCreateUserAsync(clientId);

            // 生成缩略图
            string? thumbnailBase64 = null;
            if (contentType.StartsWith("image/"))
            {
                thumbnailBase64 = GenerateThumbnail(base64Data, 200, 200);
            }

            var file = new ChatFile
            {
                FileId = Guid.NewGuid().ToString("N"),
                UserId = user.Id,
                FileName = fileName,
                ContentType = contentType,
                Size = size,
                Base64Data = base64Data,
                ThumbnailBase64 = thumbnailBase64,
                UploadedAt = DateTime.UtcNow
            };

            _context.ChatFiles.Add(file);
            await _context.SaveChangesAsync();

            var thumbnailUrl = thumbnailBase64 != null
                ? $"data:{contentType};base64,{thumbnailBase64}"
                : null;

            return (file.FileId, thumbnailUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save file");
            return (null, null);
        }
    }

    /// <summary>
    /// 获取文件数据
    /// </summary>
    public async Task<FileDataResponse?> GetFileAsync(string fileId)
    {
        var file = await _context.ChatFiles
            .FirstOrDefaultAsync(f => f.FileId == fileId);

        if (file == null) return null;

        return new FileDataResponse
        {
            Success = true,
            FileId = file.FileId,
            FileName = file.FileName,
            ContentType = file.ContentType,
            Size = file.Size,
            Base64Data = file.Base64Data
        };
    }

    /// <summary>
    /// 生成图片缩略图
    /// </summary>
    private string? GenerateThumbnail(string base64Data, int maxWidth, int maxHeight)
    {
        try
        {
            var imageBytes = Convert.FromBase64String(base64Data);
            using var inputStream = new MemoryStream(imageBytes);
            using var original = SKBitmap.Decode(inputStream);

            if (original == null) return null;

            // 计算缩放比例
            float scale = Math.Min(
                (float)maxWidth / original.Width,
                (float)maxHeight / original.Height
            );
            scale = Math.Min(scale, 1f); // 不放大

            int newWidth = (int)(original.Width * scale);
            int newHeight = (int)(original.Height * scale);

            using var resized = original.Resize(
                new SKImageInfo(newWidth, newHeight),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
            if (resized == null) return null;

            using var image = SKImage.FromBitmap(resized);
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 70);

            return Convert.ToBase64String(data.ToArray());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate thumbnail");
            return null;
        }
    }

    #endregion

    #region 数据同步

    /// <summary>
    /// 从本地存储同步数据
    /// </summary>
    public async Task<SyncResponse> SyncFromLocalAsync(string clientId, List<SyncSessionData> sessions)
    {
        var user = await GetOrCreateUserAsync(clientId);
        int sessionsImported = 0;
        int messagesImported = 0;
        int filesImported = 0;

        foreach (var sessionData in sessions)
        {
            // 检查会话是否已存在
            var existingSession = await _context.ChatSessions
                .FirstOrDefaultAsync(s => s.SessionId == sessionData.Id);

            if (existingSession != null) continue;

            var session = new ChatSession
            {
                SessionId = sessionData.Id,
                UserId = user.Id,
                Title = sessionData.Title,
                CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(sessionData.CreatedAt).UtcDateTime,
                UpdatedAt = DateTimeOffset.FromUnixTimeMilliseconds(sessionData.UpdatedAt).UtcDateTime
            };

            _context.ChatSessions.Add(session);
            await _context.SaveChangesAsync();
            sessionsImported++;

            int orderIndex = 0;
            foreach (var msgData in sessionData.Messages)
            {
                int? fileId = null;

                // 如果消息有图片，先保存文件
                if (!string.IsNullOrEmpty(msgData.ImageBase64))
                {
                    var file = new ChatFile
                    {
                        FileId = Guid.NewGuid().ToString("N"),
                        UserId = user.Id,
                        FileName = "imported_image.jpg",
                        ContentType = "image/jpeg",
                        Size = msgData.ImageBase64.Length * 3 / 4,
                        Base64Data = msgData.ImageBase64,
                        ThumbnailBase64 = GenerateThumbnail(msgData.ImageBase64, 200, 200),
                        UploadedAt = DateTimeOffset.FromUnixTimeMilliseconds(msgData.Timestamp).UtcDateTime
                    };
                    _context.ChatFiles.Add(file);
                    await _context.SaveChangesAsync();
                    fileId = file.Id;
                    filesImported++;
                }

                var message = new ChatMessage
                {
                    MessageId = Guid.NewGuid().ToString("N"),
                    SessionId = session.Id,
                    Role = msgData.Role,
                    Content = msgData.Content,
                    FileId = fileId,
                    Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(msgData.Timestamp).UtcDateTime,
                    OrderIndex = orderIndex++
                };
                _context.ChatMessages.Add(message);
                messagesImported++;
            }
        }

        await _context.SaveChangesAsync();

        return new SyncResponse
        {
            Success = true,
            SessionsImported = sessionsImported,
            MessagesImported = messagesImported,
            FilesImported = filesImported,
            Message = $"成功导入 {sessionsImported} 个会话，{messagesImported} 条消息，{filesImported} 个文件"
        };
    }

    #endregion
}
