using Microsoft.AspNetCore.Mvc;
using System.Collections.Concurrent;
using System.Text.Json;

namespace AIChat.Controllers;

/// <summary>
/// 音频控制器
/// 处理语音转文字(STT)和文字转语音(TTS)
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AudioController : ControllerBase
{
    private const long MaxAudioBytes = 10 * 1024 * 1024;
    private const int MaxTtsTextLength = 10_000;

    private static readonly IReadOnlyDictionary<string, string> SupportedAudioTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["audio/webm"] = "webm",
            ["audio/ogg"] = "ogg",
            ["audio/mp3"] = "mp3",
            ["audio/mpeg"] = "mp3",
            ["audio/wav"] = "wav"
        };

    private readonly IConfiguration _configuration;
    private readonly ILogger<AudioController> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _sttApiEndpoint;
    private readonly string _sttApiKey;
    private readonly string _sttModel;
    private readonly string _ttsApiEndpoint;
    private readonly string _ttsApiKey;
    private readonly string _ttsModel;
    private readonly string _ttsVoice;

    // 静态字典：管理每个session的最新请求ID和CancellationTokenSource
    private static readonly ConcurrentDictionary<string, TtsSessionState> _sessionStates = new();

    public AudioController(IConfiguration configuration, ILogger<AudioController> logger)
    {
        _configuration = configuration;
        _logger = logger;

        // 配置 STT
        _sttApiEndpoint = _configuration["AudioModel:STT:ApiEndpoint"] ?? "https://api.siliconflow.cn/v1/audio/transcriptions";
        _sttApiKey = _configuration["AudioModel:STT:ApiKey"] ?? "";
        _sttModel = _configuration["AudioModel:STT:Model"] ?? "FunAudioLLM/SenseVoiceSmall";

        // 配置 TTS
        _ttsApiEndpoint = _configuration["AudioModel:TTS:ApiEndpoint"] ?? "https://api.siliconflow.cn/v1/audio/speech";
        _ttsApiKey = _configuration["AudioModel:TTS:ApiKey"] ?? "";
        _ttsModel = _configuration["AudioModel:TTS:Model"] ?? "FunAudioLLM/CosyVoice2-0.5B";
        _ttsVoice = _configuration["AudioModel:TTS:Voice"] ?? "FunAudioLLM/CosyVoice2-0.5B:diana";

        _httpClient = new HttpClient();
    }

    /// <summary>
    /// 语音转文字接口 (STT)
    /// 接收音频文件并返回转录的文字
    /// </summary>
    [HttpPost("stt")]
    public async Task<IActionResult> SpeechToText(IFormFile? audio)
    {
        try
        {
            if (audio == null || audio.Length == 0)
            {
                return BadRequest(new { success = false, message = "未提供音频文件" });
            }

            if (audio.Length > MaxAudioBytes)
            {
                return BadRequest(new { success = false, message = "音频文件超过大小限制（最大 10MB）" });
            }

            if (!TryGetAudioInfo(audio.ContentType, out var contentType, out var extension))
            {
                return BadRequest(new { success = false, message = "不支持的音频格式" });
            }

            _logger.LogInformation("Received audio file for STT: {FileName}, Size: {Size} bytes",
                audio.FileName, audio.Length);

            using var content = new MultipartFormDataContent();
            using var fileStream = audio.OpenReadStream();
            using var streamContent = new StreamContent(fileStream);

            streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
            content.Add(streamContent, "file", $"audio.{extension}");
            content.Add(new StringContent(_sttModel), "model");

            var request = new HttpRequestMessage(HttpMethod.Post, _sttApiEndpoint)
            {
                Content = content
            };
            request.Headers.Add("Authorization", $"Bearer {_sttApiKey}");

            var response = await _httpClient.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("STT API error: {StatusCode} - {Content}", response.StatusCode, responseContent);
                return StatusCode(StatusCodes.Status502BadGateway, new { success = false, message = "语音转文字服务暂不可用" });
            }

            // 解析响应
            var result = JsonSerializer.Deserialize<JsonElement>(responseContent);
            var text = result.GetProperty("text").GetString();

            _logger.LogInformation("STT completed with {TextLength} characters", text?.Length ?? 0);

            return Ok(new { success = true, text = text });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in STT");
            return StatusCode(500, new { success = false, message = "语音转文字失败，请稍后重试" });
        }
    }

    /// <summary>
    /// 语音转文字接口 (STT) - Base64格式
    /// </summary>
    [HttpPost("stt/base64")]
    public async Task<IActionResult> SpeechToTextBase64([FromBody] SttBase64Request request)
    {
        try
        {
            if (request == null || string.IsNullOrWhiteSpace(request.AudioData))
            {
                return BadRequest(new { success = false, message = "未提供音频数据" });
            }

            if (request.AudioData.Length > GetMaxBase64Length(MaxAudioBytes))
            {
                return BadRequest(new { success = false, message = "音频数据超过大小限制（最大 10MB）" });
            }

            if (!TryGetAudioInfo(request.ContentType, out var contentType, out var extension))
            {
                return BadRequest(new { success = false, message = "不支持的音频格式" });
            }

            _logger.LogInformation("Received base64 audio for STT, ContentType: {ContentType}", request.ContentType);

            byte[] audioBytes;
            try
            {
                audioBytes = Convert.FromBase64String(request.AudioData);
            }
            catch (FormatException)
            {
                return BadRequest(new { success = false, message = "无效的 Base64 音频数据" });
            }

            if (audioBytes.LongLength > MaxAudioBytes)
            {
                return BadRequest(new { success = false, message = "音频数据超过大小限制（最大 10MB）" });
            }

            using var content = new MultipartFormDataContent();
            using var streamContent = new ByteArrayContent(audioBytes);

            streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
            content.Add(streamContent, "file", $"audio.{extension}");
            content.Add(new StringContent(_sttModel), "model");

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, _sttApiEndpoint)
            {
                Content = content
            };
            httpRequest.Headers.Add("Authorization", $"Bearer {_sttApiKey}");

            var response = await _httpClient.SendAsync(httpRequest);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("STT API error: {StatusCode} - {Content}", response.StatusCode, responseContent);
                return StatusCode(StatusCodes.Status502BadGateway, new { success = false, message = "语音转文字服务暂不可用" });
            }

            var result = JsonSerializer.Deserialize<JsonElement>(responseContent);
            var text = result.GetProperty("text").GetString();

            _logger.LogInformation("STT completed with {TextLength} characters", text?.Length ?? 0);

            return Ok(new { success = true, text = text });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in STT");
            return StatusCode(500, new { success = false, message = "语音转文字失败，请稍后重试" });
        }
    }

    /// <summary>
    /// 文字转语音接口 (TTS)
    /// 接收文字并返回音频文件
    /// 支持基于sessionId的请求取消机制
    /// </summary>
    [HttpPost("tts")]
    public async Task<IActionResult> TextToSpeech([FromBody] TtsRequest request)
    {
        try
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Text))
            {
                return BadRequest(new { success = false, message = "未提供文字内容" });
            }

            if (request.Text.Length > MaxTtsTextLength)
            {
                return BadRequest(new { success = false, message = "文字内容超过长度限制（最大 10000 个字符）" });
            }

            var sessionId = request.SessionId ?? "default";
            var requestId = request.RequestId ?? Guid.NewGuid().ToString();

            _logger.LogInformation("TTS request: SessionId={SessionId}, RequestId={RequestId}, TextLength={Length}",
                sessionId, requestId, request.Text.Length);

            // 创建新的CancellationTokenSource
            var cts = new CancellationTokenSource();

            // 获取或创建session状态，并取消之前的请求
            var sessionState = _sessionStates.AddOrUpdate(
                sessionId,
                _ => new TtsSessionState { CurrentRequestId = requestId, CancellationTokenSource = cts },
                (_, oldState) =>
                {
                    // 取消之前的请求
                    try
                    {
                        oldState.CancellationTokenSource?.Cancel();
                        oldState.CancellationTokenSource?.Dispose();
                    }
                    catch { }
                    return new TtsSessionState { CurrentRequestId = requestId, CancellationTokenSource = cts };
                }
            );

            // 检查是否已被取消（在添加到字典后可能被其他请求取消）
            if (cts.Token.IsCancellationRequested)
            {
                _logger.LogInformation("TTS request cancelled before start: RequestId={RequestId}", requestId);
                return Ok(new { success = false, cancelled = true, message = "请求已被取消", requestId = requestId });
            }

            var ttsRequest = new
            {
                model = _ttsModel,
                input = request.Text,
                voice = request.Voice ?? _ttsVoice,
                response_format = "mp3"
            };

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, _ttsApiEndpoint)
            {
                Content = new StringContent(JsonSerializer.Serialize(ttsRequest), System.Text.Encoding.UTF8, "application/json")
            };
            httpRequest.Headers.Add("Authorization", $"Bearer {_ttsApiKey}");

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(httpRequest, cts.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("TTS request cancelled during API call: RequestId={RequestId}", requestId);
                return Ok(new { success = false, cancelled = true, message = "请求已被取消", requestId = requestId });
            }

            // 再次检查是否被取消
            if (cts.Token.IsCancellationRequested)
            {
                _logger.LogInformation("TTS request cancelled after API call: RequestId={RequestId}", requestId);
                return Ok(new { success = false, cancelled = true, message = "请求已被取消", requestId = requestId });
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("TTS API error: {StatusCode} - {Content}", response.StatusCode, errorContent);
                return StatusCode(StatusCodes.Status502BadGateway, new { success = false, message = "文字转语音服务暂不可用", requestId = requestId });
            }

            var audioBytes = await response.Content.ReadAsByteArrayAsync(cts.Token);

            // 最终检查：确保这个请求仍然是该session的最新请求
            if (_sessionStates.TryGetValue(sessionId, out var currentState) && currentState.CurrentRequestId != requestId)
            {
                _logger.LogInformation("TTS request superseded by newer request: RequestId={RequestId}", requestId);
                return Ok(new { success = false, cancelled = true, message = "请求已被新请求取代", requestId = requestId });
            }

            var base64Audio = Convert.ToBase64String(audioBytes);

            _logger.LogInformation("TTS completed: RequestId={RequestId}, AudioSize={Size} bytes", requestId, audioBytes.Length);

            return Ok(new { success = true, audioData = base64Audio, contentType = "audio/mp3", requestId = requestId });
        }
        catch (OperationCanceledException)
        {
            return Ok(new { success = false, cancelled = true, message = "请求已被取消" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TTS");
            return StatusCode(500, new { success = false, message = "文字转语音失败，请稍后重试" });
        }
    }

    private static long GetMaxBase64Length(long maxBytes)
    {
        return ((maxBytes + 2) / 3) * 4;
    }

    private static bool TryGetAudioInfo(string? rawContentType, out string contentType, out string extension)
    {
        contentType = rawContentType?.Split(';', 2)[0].Trim().ToLowerInvariant() ?? string.Empty;
        if (SupportedAudioTypes.TryGetValue(contentType, out var resolvedExtension))
        {
            extension = resolvedExtension;
            return true;
        }

        extension = string.Empty;
        return false;
    }
}

/// <summary>
/// STT Base64请求模型
/// </summary>
public class SttBase64Request
{
    /// <summary>
    /// 音频数据(Base64编码)
    /// </summary>
    public string AudioData { get; set; } = string.Empty;

    /// <summary>
    /// 音频内容类型
    /// </summary>
    public string? ContentType { get; set; }
}

/// <summary>
/// TTS请求模型
/// </summary>
public class TtsRequest
{
    /// <summary>
    /// 要转换的文字
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// 语音类型（可选）
    /// </summary>
    public string? Voice { get; set; }

    /// <summary>
    /// 会话ID，用于标识同一个播放会话
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// 请求ID，用于标识单次请求
    /// </summary>
    public string? RequestId { get; set; }
}

/// <summary>
/// TTS会话状态，用于管理请求取消
/// </summary>
public class TtsSessionState
{
    /// <summary>
    /// 当前请求ID
    /// </summary>
    public string CurrentRequestId { get; set; } = string.Empty;

    /// <summary>
    /// 当前请求的取消令牌源
    /// </summary>
    public CancellationTokenSource? CancellationTokenSource { get; set; }
}
