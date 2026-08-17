using Microsoft.AspNetCore.Mvc;
using AIChat.Models.Dto;
using AIChat.Services;

namespace AIChat.Controllers;

/// <summary>
/// 聊天历史记录控制器
/// 管理会话、消息和文件
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class HistoryController : ControllerBase
{
    private readonly ChatDataService _chatDataService;
    private readonly ILogger<HistoryController> _logger;

    public HistoryController(ChatDataService chatDataService, ILogger<HistoryController> logger)
    {
        _chatDataService = chatDataService;
        _logger = logger;
    }

    /// <summary>
    /// 获取客户端ID（从请求头或查询参数）
    /// </summary>
    private string GetClientId()
    {
        // 优先从请求头获取
        var clientId = Request.Headers["X-Client-Id"].FirstOrDefault();
        if (string.IsNullOrEmpty(clientId))
        {
            clientId = Request.Query["clientId"].FirstOrDefault();
        }
        // 如果都没有，生成一个新的
        return clientId ?? Guid.NewGuid().ToString("N");
    }

    #region 会话管理

    /// <summary>
    /// 获取会话列表
    /// </summary>
    [HttpGet("sessions")]
    public async Task<ActionResult<List<ChatSessionListDto>>> GetSessions()
    {
        try
        {
            var clientId = GetClientId();
            var sessions = await _chatDataService.GetSessionsAsync(clientId);
            return Ok(sessions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get sessions");
            return StatusCode(500, new ApiResponse { Success = false, Message = "获取会话列表失败" });
        }
    }

    /// <summary>
    /// 获取会话详情
    /// </summary>
    [HttpGet("sessions/{sessionId}")]
    public async Task<ActionResult<ChatSessionDto>> GetSession(string sessionId)
    {
        try
        {
            var session = await _chatDataService.GetSessionAsync(sessionId);
            if (session == null)
            {
                return NotFound(new ApiResponse { Success = false, Message = "会话不存在" });
            }
            return Ok(session);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get session {SessionId}", sessionId);
            return StatusCode(500, new ApiResponse { Success = false, Message = "获取会话详情失败" });
        }
    }

    /// <summary>
    /// 创建新会话
    /// </summary>
    [HttpPost("sessions")]
    public async Task<ActionResult<CreateSessionResponse>> CreateSession([FromBody] CreateSessionRequest? request)
    {
        try
        {
            var clientId = request?.ClientId ?? GetClientId();
            var sessionId = await _chatDataService.CreateSessionAsync(clientId, request?.Title);

            return Ok(new CreateSessionResponse
            {
                Success = true,
                SessionId = sessionId,
                Message = "会话创建成功"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create session");
            return StatusCode(500, new CreateSessionResponse { Success = false, Message = "创建会话失败" });
        }
    }

    /// <summary>
    /// 更新会话标题
    /// </summary>
    [HttpPut("sessions/{sessionId}/title")]
    public async Task<ActionResult<ApiResponse>> UpdateSessionTitle(string sessionId, [FromBody] UpdateSessionTitleRequest request)
    {
        try
        {
            var success = await _chatDataService.UpdateSessionTitleAsync(sessionId, request.Title);
            if (!success)
            {
                return NotFound(new ApiResponse { Success = false, Message = "会话不存在" });
            }
            return Ok(new ApiResponse { Success = true, Message = "标题更新成功" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update session title");
            return StatusCode(500, new ApiResponse { Success = false, Message = "更新标题失败" });
        }
    }

    /// <summary>
    /// 删除会话
    /// </summary>
    [HttpDelete("sessions/{sessionId}")]
    public async Task<ActionResult<ApiResponse>> DeleteSession(string sessionId)
    {
        try
        {
            var success = await _chatDataService.DeleteSessionAsync(sessionId);
            if (!success)
            {
                return NotFound(new ApiResponse { Success = false, Message = "会话不存在" });
            }
            return Ok(new ApiResponse { Success = true, Message = "会话删除成功" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete session");
            return StatusCode(500, new ApiResponse { Success = false, Message = "删除会话失败" });
        }
    }

    #endregion

    #region 消息管理

    /// <summary>
    /// 保存消息
    /// </summary>
    [HttpPost("messages")]
    public async Task<ActionResult<SaveMessageResponse>> SaveMessage([FromBody] SaveMessageRequest request)
    {
        try
        {
            var messageId = await _chatDataService.SaveMessageAsync(
                request.SessionId,
                request.Role,
                request.Content,
                request.FileId
            );

            if (messageId == null)
            {
                return NotFound(new SaveMessageResponse { Success = false, Message = "会话不存在" });
            }

            return Ok(new SaveMessageResponse
            {
                Success = true,
                MessageId = messageId,
                Message = "消息保存成功"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save message");
            return StatusCode(500, new SaveMessageResponse { Success = false, Message = "保存消息失败" });
        }
    }

    #endregion

    #region 文件管理

    /// <summary>
    /// 上传文件
    /// </summary>
    [HttpPost("files")]
    public async Task<ActionResult<UploadFileResponse>> UploadFile([FromBody] UploadFileRequest request)
    {
        try
        {
            var clientId = request.ClientId ?? GetClientId();
            var (fileId, thumbnailUrl) = await _chatDataService.SaveFileAsync(
                clientId,
                request.FileName,
                request.ContentType,
                request.Size,
                request.Base64Data
            );

            if (fileId == null)
            {
                return StatusCode(500, new UploadFileResponse { Success = false, Message = "文件保存失败" });
            }

            return Ok(new UploadFileResponse
            {
                Success = true,
                FileId = fileId,
                ThumbnailUrl = thumbnailUrl,
                Message = "文件上传成功"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload file");
            return StatusCode(500, new UploadFileResponse { Success = false, Message = "文件上传失败" });
        }
    }

    /// <summary>
    /// 获取文件
    /// </summary>
    [HttpGet("files/{fileId}")]
    public async Task<ActionResult<FileDataResponse>> GetFile(string fileId)
    {
        try
        {
            var file = await _chatDataService.GetFileAsync(fileId);
            if (file == null)
            {
                return NotFound(new FileDataResponse { Success = false, Message = "文件不存在" });
            }
            return Ok(file);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get file");
            return StatusCode(500, new FileDataResponse { Success = false, Message = "获取文件失败" });
        }
    }

    #endregion

    #region 数据同步

    /// <summary>
    /// 从本地存储同步数据到服务器
    /// </summary>
    [HttpPost("sync")]
    public async Task<ActionResult<SyncResponse>> SyncFromLocal([FromBody] SyncDataRequest request)
    {
        try
        {
            var clientId = request.ClientId ?? GetClientId();
            var result = await _chatDataService.SyncFromLocalAsync(clientId, request.Sessions);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync data");
            return StatusCode(500, new SyncResponse { Success = false, Message = "数据同步失败：" + ex.Message });
        }
    }

    #endregion
}
