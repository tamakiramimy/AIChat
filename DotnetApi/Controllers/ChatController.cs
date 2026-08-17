using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;
using OpenAI;
using AIChat.Models;
using AIChat.Services;
using System.ClientModel;
using OpenAI.Chat;

namespace AIChat.Controllers;

/// <summary>
/// 聊天控制器
/// 处理聊天消息和文件上传
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ChatController : ControllerBase
{
    private const int MaxFileSizeBytes = 20 * 1024 * 1024;
    private const int MaxBase64FileLength = ((MaxFileSizeBytes + 2) / 3) * 4;

    private static readonly HashSet<string> AllowedUploadContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/gif", "image/webp", "image/bmp",
        "application/pdf",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.ms-powerpoint",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        "text/plain", "text/csv", "text/markdown"
    };

    private readonly IConfiguration _configuration;
    private readonly ILogger<ChatController> _logger;
    private readonly IFileExtractionService _fileExtractionService;
    private readonly IFileHashCacheService _fileHashCacheService;
    private readonly OpenAIClient _textClient;
    private readonly OpenAIClient _visionClient;
    private readonly string _textModel;
    private readonly string _visionModel;
    private readonly bool _visionEnabled;

    /// <summary>
    /// 构造函数
    /// </summary>
    public ChatController(
        IConfiguration configuration,
        ILogger<ChatController> logger,
        IFileExtractionService fileExtractionService,
        IFileHashCacheService fileHashCacheService)
    {
        _configuration = configuration;
        _logger = logger;
        _fileExtractionService = fileExtractionService;
        _fileHashCacheService = fileHashCacheService;

        // 初始化文本模型客户端
        var textApiKey = _configuration["TextModel:ApiKey"] ?? "";
        var textApiEndpoint = _configuration["TextModel:ApiEndpoint"] ?? "https://api.siliconflow.cn/v1";
        _textModel = _configuration["TextModel:Model"] ?? "";

        _textClient = new OpenAIClient(new ApiKeyCredential(textApiKey), new OpenAIClientOptions
        {
            Endpoint = new Uri(textApiEndpoint)
        });

        // 初始化视觉模型客户端
        _visionEnabled = _configuration.GetValue<bool>("VisionModel:Enabled", false);
        var visionApiKey = _configuration["VisionModel:ApiKey"] ?? textApiKey;
        var visionApiEndpoint = _configuration["VisionModel:ApiEndpoint"] ?? textApiEndpoint;
        _visionModel = _configuration["VisionModel:Model"] ?? _textModel;

        _visionClient = new OpenAIClient(new ApiKeyCredential(visionApiKey), new OpenAIClientOptions
        {
            Endpoint = new Uri(visionApiEndpoint)
        });
    }

    /// <summary>
    /// 获取ChatClient，根据是否有图片选择不同的模型
    /// </summary>
    private ChatClient GetChatClient(bool hasImage)
    {
        if (hasImage && _visionEnabled)
        {
            _logger.LogInformation("Using vision model: {Model}", _visionModel);
            return _visionClient.GetChatClient(_visionModel);
        }
        else
        {
            _logger.LogInformation("Using text model: {Model}", _textModel);
            return _textClient.GetChatClient(_textModel);
        }
    }

    /// <summary>
    /// 流式聊天接口
    /// 支持文本消息、图片和文档（Base64格式）
    /// </summary>
    /// <param name="request">聊天请求</param>
    [HttpPost("stream")]
    public async Task StreamChat([FromBody] ChatRequest request)
    {
        try
        {
            // 合并单图片和多图片（向后兼容）
            var allImages = new List<string>();
            if (!string.IsNullOrEmpty(request.Image))
            {
                allImages.Add(request.Image);
            }
            if (request.Images != null && request.Images.Count > 0)
            {
                allImages.AddRange(request.Images);
            }

            // 处理上传的文件（PDF、Word、Excel、PPT等）
            var documentContents = new List<DocumentContent>();
            var documentImages = new List<EmbeddedImage>();
            var documentTexts = new List<string>();

            if (request.Files != null && request.Files.Count > 0)
            {
                foreach (var file in request.Files)
                {
                    var docContent = await _fileExtractionService.ExtractContentAsync(
                        file.Base64Data, file.FileType, file.FileName);

                    if (docContent.Success)
                    {
                        documentContents.Add(docContent);

                        _logger.LogInformation(
                            "File processed: {FileName}, Strategy: {Strategy}, Text: {TextLength}, EmbeddedImages: {ImageCount}, PageImages: {PageImageCount}, PageCount: {PageCount}",
                            file.FileName, docContent.Strategy, docContent.Text.Length, docContent.EmbeddedImages.Count, docContent.PageImages.Count, docContent.PageCount);

                        // 根据策略处理
                        switch (docContent.Strategy)
                        {
                            case DocumentStrategy.TextOnly:
                                // 纯文本策略：直接添加文本
                                documentTexts.Add($"[文件: {file.FileName}]\n{docContent.Text}");
                                break;

                            case DocumentStrategy.TextWithImages:
                                // 文本+图片策略：添加文本和嵌入图片
                                documentTexts.Add($"[文件: {file.FileName}]\n{docContent.Text}");
                                documentImages.AddRange(docContent.EmbeddedImages);
                                break;

                            case DocumentStrategy.PageAsImages:
                                // 整页图片策略：使用页面图片
                                if (docContent.PageImages.Count > 0)
                                {
                                    _logger.LogInformation(
                                        "PageAsImages: Adding {Count} page images from {FileName}",
                                        docContent.PageImages.Count, file.FileName);
                                    documentImages.AddRange(docContent.PageImages);
                                }
                                else if (docContent.EmbeddedImages.Count > 0)
                                {
                                    // 备选：使用嵌入图片
                                    _logger.LogInformation(
                                        "PageAsImages: No page images, using {Count} embedded images from {FileName}",
                                        docContent.EmbeddedImages.Count, file.FileName);
                                    documentImages.AddRange(docContent.EmbeddedImages);
                                }
                                // 同时保留文本作为补充信息
                                if (!string.IsNullOrEmpty(docContent.Text))
                                {
                                    documentTexts.Add($"[文件: {file.FileName} - 辅助文本]\n{docContent.Text}");
                                }
                                break;
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Failed to process file: {FileName}, Error: {Error}",
                            file.FileName, docContent.Error);
                        documentTexts.Add($"[文件 {file.FileName} 处理失败: {docContent.Error}]");
                    }
                }
            }

            // 将文档中的图片添加到图片列表
            foreach (var docImage in documentImages)
            {
                allImages.Add(docImage.Base64Data);
            }

            _logger.LogInformation("Total images collected: {Count} (from documents: {DocCount})",
                allImages.Count, documentImages.Count);

            bool hasImage = allImages.Count > 0;

            // 检查图片功能是否启用
            if (hasImage && !_visionEnabled)
            {
                // 图片功能未启用，返回提示信息
                Response.ContentType = "text/event-stream; charset=utf-8";
                var errorData = new
                {
                    choices = new[]
                    {
                        new
                        {
                            delta = new { content = "抱歉，图片识别功能当前未启用。请联系管理员开启 VisionEnabled 配置。\n\n如果您上传了文档，以下是提取的文本内容：\n\n" + string.Join("\n\n", documentTexts) },
                            index = 0,
                            finish_reason = (string?)null
                        }
                    }
                };
                var json = JsonSerializer.Serialize(errorData);
                var sseMessage = $"data: {json}\n\n";
                var bytes = Encoding.UTF8.GetBytes(sseMessage);
                await Response.Body.WriteAsync(bytes);
                await Response.Body.FlushAsync();
                var errorDoneBytes = Encoding.UTF8.GetBytes("data: [DONE]\n\n");
                await Response.Body.WriteAsync(errorDoneBytes);
                await Response.Body.FlushAsync();
                return;
            }

            // 禁用响应缓冲，确保流式输出
            var bufferingFeature = HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>();
            bufferingFeature?.DisableBuffering();

            Response.ContentType = "text/event-stream; charset=utf-8";
            Response.Headers.Append("Cache-Control", "no-cache, no-store, must-revalidate");
            Response.Headers.Append("Pragma", "no-cache");
            Response.Headers.Append("Expires", "0");
            Response.Headers.Append("Connection", "keep-alive");
            Response.Headers.Append("X-Accel-Buffering", "no");
            Response.Headers.Append("X-Content-Type-Options", "nosniff");

            // 构建聊天消息列表
            var messages = new List<OpenAI.Chat.ChatMessage>();

            // 添加历史消息
            foreach (var msg in request.History)
            {
                if (msg.Role == "user")
                {
                    messages.Add(new UserChatMessage(msg.Content));
                }
                else
                {
                    messages.Add(new AssistantChatMessage(msg.Content));
                }
            }

            // 构建用户消息内容
            var userMessageText = request.Message;

            // 如果有文档文本，添加到消息中
            if (documentTexts.Count > 0)
            {
                var documentContext = string.Join("\n\n---\n\n", documentTexts);
                userMessageText = $"以下是上传的文档内容：\n\n{documentContext}\n\n---\n\n用户问题：{request.Message}";
            }

            // 判断是否需要分页处理（PDF 多页图片场景）
            // 对于任何多页 PDF（无论 TextWithImages 还是 PageAsImages 策略），均使用整页渲染图进行逐页处理
            var multiPagePdfContents = documentContents
                .Where(d => d.FileType == "pdf" && d.PageImages.Count > 1)
                .ToList();
            var isPdfMultiPage = multiPagePdfContents.Count > 0;

            if (isPdfMultiPage)
            {
                // 使用 PageImages（整页渲染图片）而非 EmbeddedImages，确保每页内容完整
                var pdfPageImages = multiPagePdfContents
                    .SelectMany(d => d.PageImages)
                    .ToList();
                _logger.LogInformation(
                    "PDF multi-page mode triggered: {PageCount} pages, strategy(s): {Strategies}",
                    pdfPageImages.Count,
                    string.Join(", ", multiPagePdfContents.Select(d => d.Strategy)));
                await ProcessPdfPagesSequentially(
                    messages, pdfPageImages, userMessageText, request.Message, documentTexts);
            }
            else
            {
                // 常规处理模式：所有图片一次性发送
                if (allImages.Count > 0)
                {
                    var contentParts = new List<ChatMessageContentPart>
                    {
                        ChatMessageContentPart.CreateTextPart(userMessageText)
                    };

                    var imagesToAdd = allImages.Take(20).ToList();
                    _logger.LogInformation("Sending {SendCount} images to VL model (total collected: {TotalCount})",
                        imagesToAdd.Count, allImages.Count);

                    foreach (var imageBase64 in imagesToAdd)
                    {
                        contentParts.Add(ChatMessageContentPart.CreateImagePart(
                            new BinaryData(Convert.FromBase64String(imageBase64)),
                            "image/png"));
                    }

                    messages.Add(new UserChatMessage(contentParts));
                    _logger.LogInformation("Processing message with {ImageCount} image(s) attached", imagesToAdd.Count);
                }
                else
                {
                    messages.Add(new UserChatMessage(userMessageText));
                }

                await StreamSingleResponse(messages, hasImage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in stream chat");
            var errorData = new { error = ex.Message };
            var errorBytes = Encoding.UTF8.GetBytes($"data: {JsonSerializer.Serialize(errorData)}\n\n");
            await Response.Body.WriteAsync(errorBytes);
            await Response.Body.FlushAsync();
        }
    }

    /// <summary>
    /// 检测用户指令是否要求 JSON 输出
    /// </summary>
    private static bool IsJsonExtractionRequest(string question)
    {
        var lower = question.ToLower();
        return lower.Contains("json") || lower.Contains("提取") || lower.Contains("结构化") || lower.Contains("字段");
    }

    /// <summary>
    /// PDF 多页分批处理：每页单独调用 VL 模型，最后汇总结果
    /// </summary>
    private async Task ProcessPdfPagesSequentially(
        List<OpenAI.Chat.ChatMessage> historyMessages,
        List<EmbeddedImage> pageImages,
        string userMessageText,
        string originalQuestion,
        List<string> documentTexts)
    {
        var pageResults = new List<string>();
        var totalPages = pageImages.Count;
        var isJsonMode = IsJsonExtractionRequest(originalQuestion);

        _logger.LogInformation("Starting PDF multi-page processing: {TotalPages} pages, JsonMode: {JsonMode}", totalPages, isJsonMode);

        await SendSseMessage($"📄 正在分析 PDF 文档（共 {totalPages} 页）...\n\n");

        var chatClient = GetChatClient(true); // 使用 VL 模型

        // 逐页处理
        for (int i = 0; i < totalPages; i++)
        {
            var pageImage = pageImages[i];
            var pageNumber = i + 1;

            _logger.LogInformation("Processing page {PageNumber}/{TotalPages}", pageNumber, totalPages);
            await SendSseMessage($"**【第 {pageNumber}/{totalPages} 页】**\n");

            try
            {
                // 每页独立消息，不携带历史，避免干扰模型对当前页内容的判断
                var pageMessages = new List<OpenAI.Chat.ChatMessage>();

                string pagePrompt;
                if (isJsonMode)
                {
                    // JSON 提取模式：严格的结构化提取指令
                    pagePrompt = $@"你是专业的文档信息提取助手。
这是文档第 {pageNumber}/{totalPages} 页。

任务：将此页中所有可见文字和表格内容，完整提取为 JSON 格式。

严格规则：
1. 只输出纯 JSON 对象，不输出任何解释、说明或 Markdown 代码块（不要 ```json）
2. 使用文档原始字段名作为 key，保留原始中文
3. 不遗漏任何字段，空白字段值输出 null
4. 表格内容按行提取，多值字段用数组
5. 所有文字原样保留，不改写、不缩写
6. 如果此页无内容，输出 {{}}";
                }
                else
                {
                    // 普通分析模式：使用用户原始问题
                    pagePrompt = $"这是文档第 {pageNumber}/{totalPages} 页。\n\n{originalQuestion}";
                }

                var contentParts = new List<ChatMessageContentPart>
                {
                    ChatMessageContentPart.CreateTextPart(pagePrompt),
                    ChatMessageContentPart.CreateImagePart(
                        new BinaryData(Convert.FromBase64String(pageImage.Base64Data)),
                        "image/png")
                };

                pageMessages.Add(new UserChatMessage(contentParts));

                var pageResponse = new StringBuilder();
                await foreach (var update in chatClient.CompleteChatStreamingAsync(pageMessages))
                {
                    foreach (var contentPart in update.ContentUpdate)
                    {
                        if (contentPart.Text is { Length: > 0 } text)
                        {
                            pageResponse.Append(text);
                            await SendSseMessage(text);
                        }
                    }
                }

                var pageResult = pageResponse.ToString();
                pageResults.Add(pageResult);

                _logger.LogInformation("Page {PageNumber} completed, response length: {Length}", pageNumber, pageResult.Length);

                if (i < totalPages - 1)
                {
                    await SendSseMessage("\n\n---\n\n");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing page {PageNumber}", pageNumber);
                await SendSseMessage($"\n⚠️ 第 {pageNumber} 页处理失败: {ex.Message}\n");
                pageResults.Add(isJsonMode ? "{}" : $"【第 {pageNumber} 页】处理失败: {ex.Message}");
            }
        }

        // 汇总阶段
        if (totalPages > 1)
        {
            await SendSseMessage("\n\n---\n\n📋 **汇总**\n\n");

            try
            {
                var summaryMessages = new List<OpenAI.Chat.ChatMessage>();

                string summaryPrompt;
                if (isJsonMode)
                {
                    // JSON 模式：将各页 JSON 合并为统一结构
                    summaryPrompt = $@"以下是一份 {totalPages} 页 PDF 文档的逐页 JSON 提取结果：

{string.Join("\n\n---页分隔---\n\n", pageResults.Select((r, idx) => $"第{idx + 1}页:\n{r}"))}

---

任务：将以上各页的 JSON 内容合并为一个完整、结构清晰的 JSON 对象。
规则：
1. 只输出纯 JSON，不输出任何解释或 Markdown 代码块
2. 相同字段合并，多页重复的字段取最完整的值
3. 不同页特有的字段都要保留
4. 列表类字段合并所有页的数组项并去重
5. 保持原始中文字段名";
                }
                else
                {
                    summaryPrompt = $@"以下是一份 {totalPages} 页 PDF 文档的逐页分析结果：

{string.Join("\n\n", pageResults.Select((r, idx) => $"【第{idx + 1}页】\n{r}"))}

---

请根据以上各页内容，针对用户问题提供完整汇总答案。

用户问题：{originalQuestion}";
                }

                summaryMessages.Add(new UserChatMessage(summaryPrompt));

                var textClient = GetChatClient(false); // 汇总用文本模型
                await foreach (var update in textClient.CompleteChatStreamingAsync(summaryMessages))
                {
                    foreach (var contentPart in update.ContentUpdate)
                    {
                        if (contentPart.Text is { Length: > 0 } text)
                        {
                            await SendSseMessage(text);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating summary");
                await SendSseMessage($"\n⚠️ 汇总生成失败: {ex.Message}");
            }
        }

        // 发送完成信号
        var doneBytes = Encoding.UTF8.GetBytes("data: [DONE]\n\n");
        await Response.Body.WriteAsync(doneBytes);
        await Response.Body.FlushAsync();

        _logger.LogInformation("PDF multi-page processing completed. Total pages: {TotalPages}", totalPages);
    }

    /// <summary>
    /// 发送 SSE 消息
    /// </summary>
    private async Task SendSseMessage(string content)
    {
        var sseData = new
        {
            choices = new[]
            {
                new
                {
                    delta = new { content },
                    index = 0,
                    finish_reason = (string?)null
                }
            }
        };

        var json = JsonSerializer.Serialize(sseData);
        var sseMessage = $"data: {json}\n\n";
        var bytes = Encoding.UTF8.GetBytes(sseMessage);
        await Response.Body.WriteAsync(bytes);
        await Response.Body.FlushAsync();
    }

    /// <summary>
    /// 单次流式响应处理
    /// </summary>
    private async Task StreamSingleResponse(List<OpenAI.Chat.ChatMessage> messages, bool hasImage)
    {
        _logger.LogInformation("Starting streaming chat with {MessageCount} messages, hasImage: {HasImage}",
            messages.Count, hasImage);

        var chunkCount = 0;
        var responseBuilder = new StringBuilder();
        var streamingStarted = false;

        var chatClient = GetChatClient(hasImage);

        await foreach (var update in chatClient.CompleteChatStreamingAsync(messages))
        {
            foreach (var contentPart in update.ContentUpdate)
            {
                if (contentPart.Text is { Length: > 0 } text)
                {
                    chunkCount++;

                    if (!streamingStarted)
                    {
                        streamingStarted = true;
                        _logger.LogInformation("Streaming response started, receiving chunks...");
                    }

                    responseBuilder.Append(text);
                    await SendSseMessage(text);
                }
            }
        }

        // 发送完成信号
        var doneBytes = Encoding.UTF8.GetBytes("data: [DONE]\n\n");
        await Response.Body.WriteAsync(doneBytes);
        await Response.Body.FlushAsync();

        _logger.LogInformation("Streaming completed. Total chunks: {ChunkCount}, Response length: {Length} chars",
            chunkCount, responseBuilder.Length);
        _logger.LogDebug("Full response content: {Response}", responseBuilder.ToString());
    }

    /// <summary>
    /// 文件上传接口（Base64格式）
    /// 支持图片、PDF、Word、Excel、PPT等格式
    /// 使用文件哈希缓存避免重复上传
    /// </summary>
    /// <param name="request">文件上传请求</param>
    /// <returns>上传结果</returns>
    [HttpPost("upload")]
    public IActionResult UploadFile([FromBody] FileUploadRequest request)
    {
        try
        {
            // 验证请求
            if (request == null || string.IsNullOrWhiteSpace(request.Base64Data))
            {
                return BadRequest(new FileUploadResponse
                {
                    Success = false,
                    Message = "未提供文件数据"
                });
            }

            if (request.Base64Data.Length > MaxBase64FileLength)
            {
                return BadRequest(new FileUploadResponse
                {
                    Success = false,
                    Message = "文件大小超过限制（最大 20MB）"
                });
            }

            var contentType = request.ContentType?.Trim();
            if (string.IsNullOrWhiteSpace(contentType) || !AllowedUploadContentTypes.Contains(contentType))
            {
                return BadRequest(new FileUploadResponse
                {
                    Success = false,
                    Message = "不支持的文件类型。支持的类型：图片、PDF、Word、Excel、PPT、TXT、CSV、Markdown"
                });
            }

            var fileName = Path.GetFileName(request.FileName);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return BadRequest(new FileUploadResponse
                {
                    Success = false,
                    Message = "文件名无效"
                });
            }

            // 验证 Base64 数据
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(request.Base64Data);
                _logger.LogInformation("Received file upload: {FileName}, Size: {Size} bytes, Type: {ContentType}",
                    request.FileName, bytes.Length, request.ContentType);
            }
            catch (FormatException)
            {
                return BadRequest(new FileUploadResponse
                {
                    Success = false,
                    Message = "无效的 Base64 数据格式"
                });
            }

            // Trust the decoded payload rather than the client-provided size field.
            if (bytes.Length > MaxFileSizeBytes)
            {
                return BadRequest(new FileUploadResponse
                {
                    Success = false,
                    Message = "文件大小超过限制（最大 20MB）"
                });
            }

            if (request.Size != bytes.Length)
            {
                _logger.LogWarning("Client file size did not match decoded payload: {FileName}, ClaimedSize: {ClaimedSize}, ActualSize: {ActualSize}",
                    fileName, request.Size, bytes.Length);
            }

            // 使用缓存服务
            var sessionId = request.SessionId ?? "default";
            var cacheEntry = _fileHashCacheService.AddFile(
                sessionId,
                fileName,
                contentType,
                bytes.Length,
                request.Base64Data
            );

            // 检查是否命中缓存（通过比较创建时间判断）
            var fromCache = (DateTime.UtcNow - cacheEntry.CreatedAt).TotalSeconds > 1;

            // 返回成功响应
            return Ok(new FileUploadResponse
            {
                Success = true,
                Message = fromCache ? "文件已在缓存中" : "文件上传成功",
                FileId = cacheEntry.FileId,
                Hash = cacheEntry.Hash,
                FileName = cacheEntry.FileName,
                Base64Data = request.Base64Data,
                FromCache = fromCache
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in file upload");
            return StatusCode(500, new FileUploadResponse
            {
                Success = false,
                Message = "文件上传失败：" + ex.Message
            });
        }
    }

    /// <summary>
    /// 检查文件缓存
    /// 用于前端快速判断文件是否已上传
    /// </summary>
    /// <param name="request">检查请求</param>
    /// <returns>缓存状态</returns>
    [HttpPost("file/check")]
    public IActionResult CheckFileCache([FromBody] FileCheckRequest request)
    {
        try
        {
            var response = new FileCheckResponse
            {
                Success = true,
                CachedFiles = new Dictionary<string, CachedFileInfo>()
            };

            foreach (var hash in request.Hashes)
            {
                var entry = _fileHashCacheService.TryGetFile(request.SessionId, hash);
                if (entry != null)
                {
                    response.CachedFiles[hash] = new CachedFileInfo
                    {
                        FileId = entry.FileId,
                        FileName = entry.FileName,
                        ContentType = entry.ContentType,
                        Size = entry.Size
                    };
                }
            }

            _logger.LogInformation("File cache check: {SessionId}, Requested: {Count}, Found: {Found}",
                request.SessionId, request.Hashes.Count, response.CachedFiles.Count);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in file cache check");
            return StatusCode(500, new FileCheckResponse
            {
                Success = false
            });
        }
    }

    /// <summary>
    /// 通过文件ID获取文件数据
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    /// <param name="fileId">文件ID</param>
    /// <returns>文件数据</returns>
    [HttpGet("file/{sessionId}/{fileId}")]
    public IActionResult GetFile(string sessionId, string fileId)
    {
        try
        {
            var entry = _fileHashCacheService.TryGetFileById(sessionId, fileId);
            if (entry == null)
            {
                return NotFound(new { Success = false, Message = "文件不存在或已过期" });
            }

            return Ok(new
            {
                Success = true,
                FileId = entry.FileId,
                FileName = entry.FileName,
                ContentType = entry.ContentType,
                Size = entry.Size,
                Base64Data = entry.Base64Data
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in get file");
            return StatusCode(500, new { Success = false, Message = "获取文件失败：" + ex.Message });
        }
    }
}
