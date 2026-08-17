# 部署指南

本文面向负责 Linux 主机、反向代理和密钥管理的部署人员。示例使用 systemd 和 Nginx；可替换为等价的平台服务，但必须保留同样的环境变量、持久卷和 SSE 代理行为。

## 部署前准备

准备以下内容：

- .NET 10 Runtime。
- Node.js 22 或更高版本，仅在构建前端时需要。
- 一个可写、可备份的目录用于 SQLite 数据库。
- OpenAI 兼容模型服务的端点、模型名和密钥。
- 一个受 HTTPS 保护的公网域名，或一个已认证的内部访问层。

应用没有内建登录或资源所有权校验。不要将 API 直接暴露到互联网；在其前方使用公司 SSO、VPN、身份感知代理或其他认证网关。

## 构建发布物

在受控构建机上执行：

```bash
git clone https://github.com/tamakiramimy/AIChat.git
cd AIChat

dotnet publish DotnetApi/AIChat.csproj --configuration Release --output artifacts/api
npm --prefix Vue3 ci
npm --prefix Vue3 run build
```

部署时复制 `artifacts/api/` 到 API 运行目录，并复制 `Vue3/dist/` 到 Web 服务器静态目录。不要复制 `node_modules/`、本地 `.env`、`appsettings*.json`、`Data/*.db*` 或开发目录。

## 运行配置

使用部署平台的密钥存储或 systemd 环境文件，不要把密钥写入仓库或镜像。最少需要设置：

```ini
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://127.0.0.1:8080
Cors__AllowedOrigins__0=https://chat.example.com
TextModel__ApiKey=replace-with-a-secret
TextModel__ApiEndpoint=https://api.example.com/v1
TextModel__Model=replace-with-a-model-name
```

可选视觉与语音功能使用 `VisionModel__*`、`AudioModel__STT__*` 和 `AudioModel__TTS__*` 键。所有 `VITE_*` 值会写入浏览器构建产物，绝不能存放密钥。

`RequestLimits__MaxRequestBodySizeMb` 默认值为 `160`。该值必须与反向代理的请求体限制一致；单个文件仍由 API 限制为 20 MB，单个语音请求限制为 10 MB。

## systemd 示例

创建 `/etc/systemd/system/aichat-api.service`：

```ini
[Unit]
Description=AIChat API
After=network.target

[Service]
WorkingDirectory=/opt/aichat/api
ExecStart=/usr/bin/dotnet /opt/aichat/api/AIChat.dll
Restart=always
RestartSec=5
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://127.0.0.1:8080
Environment=Cors__AllowedOrigins__0=https://chat.example.com
EnvironmentFile=/etc/aichat/api.env

[Install]
WantedBy=multi-user.target
```

将模型密钥放入权限为 `600` 的 `/etc/aichat/api.env`，然后执行：

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now aichat-api
sudo systemctl status aichat-api
```

首次启动会在 API 工作目录创建 `Data/chat.db` 并自动执行 EF Core 迁移。SQLite 只适合单实例写入；多副本部署应由一个发布任务执行迁移并改用集中式数据库。

## Nginx 示例

将前端构建产物放在 `/var/www/aichat`，并将 API 保持在回环端口：

```nginx
server {
    listen 443 ssl http2;
    server_name chat.example.com;

    root /var/www/aichat;
    index index.html;
    client_max_body_size 160m;

    location /api/ {
        proxy_pass http://127.0.0.1:8080;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_buffering off;
        proxy_cache off;
        proxy_read_timeout 3600s;
        add_header X-Accel-Buffering no;
    }

    location / {
        try_files $uri $uri/ /index.html;
    }
}
```

`proxy_buffering off` 和较长的 `proxy_read_timeout` 是流式回复正常输出的必要条件。部署完成后从浏览器打开站点并确认 `/api/chat/stream` 的回复会逐段出现。

## 数据、备份与回滚

- 持久化 `Data/chat.db`、`Data/chat.db-wal` 和 `Data/chat.db-shm` 所在目录；不要只备份主数据库文件。
- 在停止 API 或使用 SQLite 在线备份机制后备份数据库。聊天记录与附件可能包含敏感信息，应加密备份并按组织保留策略清理。
- 发布前保留上一个 API 目录和前端 `dist` 目录。回滚时恢复前一版本文件并重启 systemd 服务；数据库迁移不自动回滚，需要先验证兼容性。
- 每次密钥疑似泄露时，立即在模型服务商侧轮换密钥，再更新部署环境文件并重启服务。

## 发布后检查

```bash
curl -i -X OPTIONS https://chat.example.com/api/chat/stream \
  -H 'Origin: https://chat.example.com' \
  -H 'Access-Control-Request-Method: POST'
```

响应应包含匹配的 `Access-Control-Allow-Origin`。还应验证普通聊天、图片/文档附件、历史记录和语音功能，并检查 Nginx 和 systemd 日志中没有模型连接或 SQLite 错误。