using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace AIChat.Services;

/// <summary>
/// 文件缓存条目
/// </summary>
public class FileCacheEntry
{
    /// <summary>
    /// 文件唯一标识
    /// </summary>
    public string FileId { get; set; } = string.Empty;

    /// <summary>
    /// 文件哈希值
    /// </summary>
    public string Hash { get; set; } = string.Empty;

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

    /// <summary>
    /// Base64 数据
    /// </summary>
    public string Base64Data { get; set; } = string.Empty;

    /// <summary>
    /// 缩略图 Base64 数据（如果是图片）
    /// </summary>
    public string? ThumbnailBase64 { get; set; }

    /// <summary>
    /// 最后访问时间（用于 LRU 淘汰）
    /// </summary>
    public DateTime LastAccessTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 会话文件缓存
/// </summary>
public class SessionFileCache
{
    /// <summary>
    /// 文件缓存字典 (Hash -> Entry)
    /// </summary>
    public ConcurrentDictionary<string, FileCacheEntry> Files { get; } = new();

    /// <summary>
    /// 文件ID到Hash的映射
    /// </summary>
    public ConcurrentDictionary<string, string> FileIdToHash { get; } = new();

    /// <summary>
    /// 最后活跃时间
    /// </summary>
    public DateTime LastActiveTime { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 文件哈希缓存服务
/// 使用 LRU 策略管理每个会话的文件缓存
/// </summary>
public interface IFileHashCacheService
{
    /// <summary>
    /// 尝试从缓存获取文件
    /// </summary>
    FileCacheEntry? TryGetFile(string sessionId, string hash);

    /// <summary>
    /// 通过文件ID获取文件
    /// </summary>
    FileCacheEntry? TryGetFileById(string sessionId, string fileId);

    /// <summary>
    /// 添加文件到缓存
    /// </summary>
    FileCacheEntry AddFile(string sessionId, string fileName, string contentType, long size, string base64Data, string? thumbnailBase64 = null);

    /// <summary>
    /// 计算文件哈希
    /// </summary>
    string ComputeHash(string base64Data);

    /// <summary>
    /// 检查文件是否已存在
    /// </summary>
    bool FileExists(string sessionId, string hash);

    /// <summary>
    /// 清理过期的会话缓存
    /// </summary>
    void CleanupExpiredSessions(TimeSpan maxAge);
}

/// <summary>
/// 文件哈希缓存服务实现
/// </summary>
public class FileHashCacheService : IFileHashCacheService
{
    private readonly ConcurrentDictionary<string, SessionFileCache> _sessionCaches = new();
    private readonly ILogger<FileHashCacheService> _logger;
    private readonly int _maxFilesPerSession;
    private readonly TimeSpan _sessionTimeout;

    public FileHashCacheService(ILogger<FileHashCacheService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _maxFilesPerSession = configuration.GetValue("FileCache:MaxFilesPerSession", 50);
        _sessionTimeout = TimeSpan.FromHours(configuration.GetValue("FileCache:SessionTimeoutHours", 24));
    }

    /// <summary>
    /// 计算 Base64 数据的 SHA256 哈希
    /// </summary>
    public string ComputeHash(string base64Data)
    {
        var bytes = Convert.FromBase64String(base64Data);
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// 检查文件是否已存在
    /// </summary>
    public bool FileExists(string sessionId, string hash)
    {
        if (_sessionCaches.TryGetValue(sessionId, out var cache))
        {
            return cache.Files.ContainsKey(hash);
        }
        return false;
    }

    /// <summary>
    /// 尝试从缓存获取文件
    /// </summary>
    public FileCacheEntry? TryGetFile(string sessionId, string hash)
    {
        if (_sessionCaches.TryGetValue(sessionId, out var cache))
        {
            if (cache.Files.TryGetValue(hash, out var entry))
            {
                // 更新访问时间
                entry.LastAccessTime = DateTime.UtcNow;
                cache.LastActiveTime = DateTime.UtcNow;
                _logger.LogDebug("Cache hit for file hash: {Hash} in session: {SessionId}", hash, sessionId);
                return entry;
            }
        }
        _logger.LogDebug("Cache miss for file hash: {Hash} in session: {SessionId}", hash, sessionId);
        return null;
    }

    /// <summary>
    /// 通过文件ID获取文件
    /// </summary>
    public FileCacheEntry? TryGetFileById(string sessionId, string fileId)
    {
        if (_sessionCaches.TryGetValue(sessionId, out var cache))
        {
            if (cache.FileIdToHash.TryGetValue(fileId, out var hash))
            {
                return TryGetFile(sessionId, hash);
            }
        }
        return null;
    }

    /// <summary>
    /// 添加文件到缓存
    /// </summary>
    public FileCacheEntry AddFile(string sessionId, string fileName, string contentType, long size, string base64Data, string? thumbnailBase64 = null)
    {
        var hash = ComputeHash(base64Data);
        var cache = _sessionCaches.GetOrAdd(sessionId, _ => new SessionFileCache());

        // 检查是否已存在相同哈希的文件
        if (cache.Files.TryGetValue(hash, out var existingEntry))
        {
            existingEntry.LastAccessTime = DateTime.UtcNow;
            cache.LastActiveTime = DateTime.UtcNow;
            _logger.LogInformation("File already cached: {FileName}, Hash: {Hash}, FileId: {FileId}",
                fileName, hash, existingEntry.FileId);
            return existingEntry;
        }

        // 检查是否需要淘汰旧文件
        if (cache.Files.Count >= _maxFilesPerSession)
        {
            EvictLeastRecentlyUsed(cache);
        }

        // 创建新的缓存条目
        var fileId = Guid.NewGuid().ToString("N")[..16];
        var entry = new FileCacheEntry
        {
            FileId = fileId,
            Hash = hash,
            FileName = fileName,
            ContentType = contentType,
            Size = size,
            Base64Data = base64Data,
            ThumbnailBase64 = thumbnailBase64,
            LastAccessTime = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        cache.Files[hash] = entry;
        cache.FileIdToHash[fileId] = hash;
        cache.LastActiveTime = DateTime.UtcNow;

        _logger.LogInformation("File cached: {FileName}, Hash: {Hash}, FileId: {FileId}, Session: {SessionId}, CacheSize: {CacheSize}",
            fileName, hash, fileId, sessionId, cache.Files.Count);

        return entry;
    }

    /// <summary>
    /// 淘汰最近最少使用的文件
    /// </summary>
    private void EvictLeastRecentlyUsed(SessionFileCache cache)
    {
        // 找到最近最少使用的文件
        var oldestEntry = cache.Files.Values
            .OrderBy(e => e.LastAccessTime)
            .FirstOrDefault();

        if (oldestEntry != null)
        {
            cache.Files.TryRemove(oldestEntry.Hash, out _);
            cache.FileIdToHash.TryRemove(oldestEntry.FileId, out _);
            _logger.LogDebug("Evicted LRU file: {FileName}, Hash: {Hash}",
                oldestEntry.FileName, oldestEntry.Hash);
        }
    }

    /// <summary>
    /// 清理过期的会话缓存
    /// </summary>
    public void CleanupExpiredSessions(TimeSpan maxAge)
    {
        var cutoffTime = DateTime.UtcNow - maxAge;
        var expiredSessions = _sessionCaches
            .Where(kvp => kvp.Value.LastActiveTime < cutoffTime)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var sessionId in expiredSessions)
        {
            if (_sessionCaches.TryRemove(sessionId, out var cache))
            {
                _logger.LogInformation("Cleaned up expired session cache: {SessionId}, Files: {FileCount}",
                    sessionId, cache.Files.Count);
            }
        }
    }
}
