using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http.Features;
using AIChat.Data;
using AIChat.Services;

var builder = WebApplication.CreateBuilder(args);
const string CorsPolicyName = "AllowedOrigins";
const long BytesPerMegabyte = 1024 * 1024;

var maxRequestBodySizeMb = builder.Configuration.GetValue<long?>("RequestLimits:MaxRequestBodySizeMb") ?? 160;
if (maxRequestBodySizeMb is < 1 or > 1024)
{
    throw new InvalidOperationException("RequestLimits:MaxRequestBodySizeMb must be between 1 and 1024.");
}

var maxRequestBodySize = maxRequestBodySizeMb * BytesPerMegabyte;
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins")
    .Get<string[]>()?
    .Where(static origin => !string.IsNullOrWhiteSpace(origin))
    .Select(static origin => origin.Trim().TrimEnd('/'))
    .Where(static origin => Uri.TryCreate(origin, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray() ?? [];

if (allowedOrigins.Length == 0 && builder.Environment.IsDevelopment())
{
    allowedOrigins = ["http://localhost:5173"];
}

if (allowedOrigins.Length == 0)
{
    throw new InvalidOperationException("Cors:AllowedOrigins must contain at least one HTTP or HTTPS origin outside Development.");
}

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = maxRequestBodySize;
});

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = maxRequestBodySize;
});

// Add services to the container.
builder.Services.AddControllers();

// Allow only explicitly configured browser origins.
builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicyName, policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// 配置 SQLite 数据库
var dbPath = Path.Combine(builder.Environment.ContentRootPath, "Data", "chat.db");
var dbDirectory = Path.GetDirectoryName(dbPath);
if (!string.IsNullOrEmpty(dbDirectory) && !Directory.Exists(dbDirectory))
{
    Directory.CreateDirectory(dbDirectory);
}

builder.Services.AddDbContext<ChatDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

// 注册服务
builder.Services.AddScoped<ChatDataService>();
builder.Services.AddScoped<IFileExtractionService, FileExtractionService>();
builder.Services.AddSingleton<IFileHashCacheService, FileHashCacheService>();

var app = builder.Build();

// 自动应用数据库迁移
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
    dbContext.Database.Migrate();
}

app.UseCors(CorsPolicyName);

app.UseAuthorization();

app.MapControllers();

app.Run();
