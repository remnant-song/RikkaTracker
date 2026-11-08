using System;
using System.Text.Json;
using Fleck;
using RikkaTracker.Core.Librarys;
using RikkaTracker.Core.Models;
using RikkaTracker.Core.Models.WebSentry;
using RikkaTracker.Models;

namespace RikkaTracker.Services
{
    public class WebMonitorService : IWebMonitorService
    {
        private readonly IWebDataService _webDataService;
        private readonly ILoggerService _logger;
        private readonly IConfigService _configService;

        private WebSocketServer? _server;
        private bool _isRunning;
        private readonly List<IWebSocketConnection> _clients = new();
        private readonly object _lock = new();

        public event Action<WebsiteUsage>? WebUsageReceived;

        // @Author: trae + deepseek-V4-pro
        // @Date: 2026-06-23
        // @Desc: 连接状态变更事件，用于设置页显示插件连接状态
        public event Action<bool>? ConnectionStatusChanged;

        // @Author: trae + deepseek-V4-pro
        // @Date: 2026-06-23
        // @Desc: 当前是否有浏览器扩展连接
        public bool IsConnected
        {
            get
            {
                lock (_lock)
                {
                    return _clients.Count > 0;
                }
            }
        }

        public WebMonitorService(IWebDataService webDataService, ILoggerService logger, IConfigService configService)
        {
            _webDataService = webDataService;
            _logger = logger;
            _configService = configService;
        }

        public void Start()
        {
            if (_isRunning) return;

            try
            {
                int port = _configService.Config.WebSocketPort > 0
                    ? _configService.Config.WebSocketPort
                    : 8910;

                _server = new WebSocketServer($"ws://127.0.0.1:{port}");

                _server.Start(socket =>
                {
                    socket.OnOpen = () =>
                    {
                        lock (_lock) { _clients.Add(socket); }
                        _logger.Info($"[WebMonitor] Client connected ({_clients.Count} total)", LogCategory.Web);
                        ConnectionStatusChanged?.Invoke(true);
                    };

                    socket.OnClose = () =>
                    {
                        lock (_lock) { _clients.Remove(socket); }
                        _logger.Info($"[WebMonitor] Client disconnected ({_clients.Count} total)", LogCategory.Web);
                        ConnectionStatusChanged?.Invoke(false);
                    };

                    socket.OnMessage = message =>
                    {
                        if (message == "ping") return;
                        HandleMessage(message);
                    };
                });

                _isRunning = true;
                _logger.Info($"[WebMonitor] WebSocket server started on ws://127.0.0.1:{port}", LogCategory.Web);

                // Actual path is determined by the extension connecting to the root
                // Fleck doesn't support path-based routing natively, so the extension
                // connects to ws://127.0.0.1:8910 directly.
            }
            catch (Exception ex)
            {
                _logger.Error("[WebMonitor] Failed to start WebSocket server", ex, LogCategory.Web);
            }
        }

        public void Stop()
        {
            if (!_isRunning) return;

            try
            {
                lock (_lock)
                {
                    foreach (var client in _clients)
                    {
                        try { client.Close(); } catch { }
                    }
                    _clients.Clear();
                }

                _server?.Dispose();
                _server = null;
                _isRunning = false;
                _logger.Info("[WebMonitor] WebSocket server stopped", LogCategory.Web);
            }
            catch (Exception ex)
            {
                _logger.Error("[WebMonitor] Error stopping WebSocket server", ex, LogCategory.Web);
            }
        }

        private async void HandleMessage(string rawJson)
        {
            try
            {
                var data = JsonSerializer.Deserialize<NotifyWeb>(rawJson);
                if (data == null || string.IsNullOrWhiteSpace(data.Url))
                    return;

                if (UrlHelper.IsDefaultIgnore(data.Url))
                    return;

                var activeTime = DateTimeOffset.FromUnixTimeSeconds(data.ActiveTime).LocalDateTime;

                var segment = new WebBrowseSegment
                {
                    Url = data.Url,
                    Domain = UrlHelper.GetDomain(data.Url),
                    Title = data.Title ?? string.Empty,
                    Icon = data.Icon ?? string.Empty,
                    StartTime = activeTime,
                    EndTime = activeTime.AddSeconds(data.Duration)
                };

                await _webDataService.SaveSegmentAsync(segment);

                _ = Task.Run(async () =>
                {
                    string localPath = await FaviconDownloader.DownloadAsync(
                        data.Icon, segment.Domain, _configService.Config.DataStoragePath);
                    if (!string.IsNullOrEmpty(localPath))
                    {
                        await _webDataService.UpdateDomainFaviconAsync(segment.Domain, localPath);
                        _logger.Info($"[Favicon] Updated DB for domain '{segment.Domain}' -> {localPath}", LogCategory.Web);
                    }
                });

                var usage = new WebsiteUsage
                {
                    Url = segment.Url,
                    Domain = segment.Domain,
                    Title = segment.Title,
                    StartTime = segment.StartTime,
                    EndTime = segment.EndTime
                };

                _logger.Info($"[Web] {usage.Domain} | {usage.Duration.TotalSeconds:F0}s", LogCategory.Web);
                WebUsageReceived?.Invoke(usage);
            }
            catch (JsonException)
            {
                // Ignore malformed messages
            }
            catch (Exception ex)
            {
                _logger.Error("[WebMonitor] Error processing message", ex, LogCategory.Web);
            }
        }
    }
}
