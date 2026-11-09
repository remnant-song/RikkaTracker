using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RikkaTracker.Models;
using RikkaTracker.Services;
using RikkaTracker.Core.Strategies;
using RikkaTracker.Controls;

namespace RikkaTracker.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly IThemeService _themeService;
        private readonly IConfigService _configService;
        private readonly ILocalizationService _localizationService;
        private readonly IUpdateService _updateService;
        private readonly ILoggerService _logger;
        private readonly IWebMonitorService _webMonitorService;

        public SettingsViewModel(
            IThemeService themeService, 
            IConfigService configService, 
            ILocalizationService localizationService, 
            IUpdateService updateService,
            ILoggerService logger,
            IWebMonitorService webMonitorService)
        {
            _themeService = themeService;
            _configService = configService;
            _localizationService = localizationService;
            _updateService = updateService;
            _logger = logger;
            _webMonitorService = webMonitorService;
            
            _theme = _themeService.GetCurrentTheme();
            _idleTimeoutMinutes = _configService.Config.IdleTimeoutMinutes;
            _storagePath = _configService.Config.DataStoragePath;
            _timelineZoomMode = _configService.Config.TimelineZoomMode;
            _language = _configService.Config.Language;
            _currentVersion = _updateService.GetCurrentVersion();
            _isAutoStart = _configService.Config.StartWithWindows;
            _webMonitorEnabled = _configService.Config.WebMonitorEnabled;
            _displayMode = _configService.Config.DisplayMode;
            _timelineSortMode = _configService.Config.TimelineSortMode;
            _showRowBadges = _configService.Config.ShowRowBadges;

            // @Author: trae + deepseek-V4-pro
            // @Date: 2026-06-23
            // @Desc: 初始化扩展连接状态并订阅状态变更
            _isExtensionConnected = _webMonitorService.IsConnected;
            _webMonitorService.ConnectionStatusChanged += OnExtensionConnectionStatusChanged;
        }

        // @Author: trae + deepseek-V4-pro
        // @Date: 2026-06-23
        // @Desc: 浏览器扩展连接状态变更回调
        private void OnExtensionConnectionStatusChanged(bool connected)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                IsExtensionConnected = connected;
            });
        }

        [ObservableProperty]
        private string _currentVersion;

        [ObservableProperty]
        private string _updateStatus = string.Empty;

        [ObservableProperty]
        private bool _isUpdating;

        [ObservableProperty]
        private double _updateProgress;

        // 从资源中获取字符串的便捷方法
        private static string? GetResource(string key)
        {
            return System.Windows.Application.Current.Resources[key] as string;
        }

        [RelayCommand]
        private async Task CheckForUpdate()
        {
            _logger.Info("User clicked 'Check for Updates'.");
            UpdateStatus = GetResource("StrCheckUpdate") + "...";
            
            try
            {
                var result = await _updateService.CheckForUpdatesAsync();

                switch (result.Status)
                {
                    case UpdateCheckStatus.UpdateAvailable:
                        _logger.Info($"Update found: {result.LatestVersion}");
                        HandleUpdateFound(result);
                        break;

                    case UpdateCheckStatus.NoUpdate:
                        _logger.Info("No update available.");
                        RikkaMessageBox.Show(
                            GetResource("StrAlreadyLatest"),
                            GetResource("StrUpdate"));
                        UpdateStatus = string.Empty;
                        break;

                    case UpdateCheckStatus.NetworkError:
                        _logger.Warning($"Update check failed due to network error: {result.ErrorMessage}");
                        RikkaMessageBox.Show(
                            string.Format(GetResource("StrNetworkErrorMsg") ?? "Network Error: {0}", result.ErrorMessage),
                            GetResource("StrUpdateCheckFailed"),
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Warning);
                        UpdateStatus = string.Empty;
                        break;

                    case UpdateCheckStatus.AssetMissing:
                        _logger.Warning($"New version {result.LatestVersion} found, but no matching asset for this installation type.");
                        RikkaMessageBox.Show(
                            string.Format(GetResource("StrAssetMissingMsg") ?? "Version {0} is available, but no matching asset was found.", result.LatestVersion),
                            GetResource("StrUpdateCheckFailed"),
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Warning);
                        UpdateStatus = string.Empty;
                        break;

                    case UpdateCheckStatus.InternalError:
                    default:
                        _logger.Error($"Internal error during update check: {result.ErrorMessage}");
                        RikkaMessageBox.Show(
                            string.Format(GetResource("StrInternalErrorMsg") ?? "An internal error occurred: {0}", result.ErrorMessage),
                            GetResource("StrError"),
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Error);
                        UpdateStatus = string.Empty;
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Unhandled exception in CheckForUpdate command.", ex);
                UpdateStatus = string.Empty;
            }
        }

        private void HandleUpdateFound(UpdateCheckResult result)
        {
            string msg = GetResource("StrUpdateAvailable") ?? string.Empty;
            string versionLabel = GetResource("StrVersion") ?? "Version";
            var choice = RikkaMessageBox.Show(
                $"{msg}\n\n{versionLabel}: {result.LatestVersion}\n\n{result.ReleaseNotes}",
                GetResource("StrUpdate"),
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Information);

            if (choice == System.Windows.MessageBoxResult.Yes)
            {
                _logger.Info("User accepted update. Starting download...");
                IsUpdating = true;
                
                // 执行异步下载安装
                _ = RunDownloadAndInstall(result);
            }
            else
            {
                _logger.Info("User declined update.");
                UpdateStatus = string.Empty;
            }
        }

        private async Task RunDownloadAndInstall(UpdateCheckResult result)
        {
            try
            {
                await _updateService.DownloadAndInstallAsync(result, (progress, status) =>
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        UpdateStatus = status;
                        UpdateProgress = progress * 100;
                    });
                });
            }
            catch (Exception ex)
            {
                _logger.Error("Download and Install failed.", ex);
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    RikkaMessageBox.Show(
                        string.Format(GetResource("StrUpdateDownloadFailed") ?? "Update download failed: {0}", ex.Message, _logger.GetLogPath()),
                        GetResource("StrError"),
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                    IsUpdating = false;
                    UpdateStatus = string.Empty;
                });
            }
        }

        [ObservableProperty]
        private int _idleTimeoutMinutes;

        partial void OnIdleTimeoutMinutesChanged(int value)
        {
            _configService.Config.IdleTimeoutMinutes = value;
            _configService.Save();
        }


        [RelayCommand]
        private void ChangeStoragePath()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = GetResource("StrSelectDbLocationTitle"),
                Filter = GetResource("StrDbFilter"),
                FileName = GetResource("StrDbDefaultFileName")
            };

            if (dialog.ShowDialog() == true)
            {
                string oldPath = _configService.Config.DataStoragePath;
                string newPath = System.IO.Path.GetDirectoryName(dialog.FileName);
                
                if (oldPath == newPath) return;

                try
                {
                    if (!System.IO.Directory.Exists(newPath))
                    {
                        System.IO.Directory.CreateDirectory(newPath);
                    }

                    string oldFile = System.IO.Path.Combine(oldPath, "tracker.db");
                    string newFile = System.IO.Path.Combine(newPath, "tracker.db");

                    if (System.IO.File.Exists(oldFile))
                    {
                        System.IO.File.Copy(oldFile, newFile, true);
                    }

                    StoragePath = newPath;
                    _configService.Config.DataStoragePath = StoragePath;
                    _configService.Save();

                    RikkaMessageBox.Show(
                        GetResource("StrDbMigratedSuccess"),
                        GetResource("StrChangeSuccess"),
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    _logger.Error("Failed to migrate data path.", ex);
                    RikkaMessageBox.Show(
                        string.Format(GetResource("StrDbMigrateFailed") ?? "Migrate failed: {0}", ex.Message),
                        GetResource("StrError"),
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                }
            }
        }

        [ObservableProperty]
        private bool _isAutoStart;

        // 监听自启动状态改变
        partial void OnIsAutoStartChanged(bool value)
        {
            _configService.Config.StartWithWindows = value;
            _configService.Save();

            try
            {
                Helpers.StartupHelper.SetStartup(value);
                _logger.Info($"已同步自启动注册表项，新值: {value}");
            }
            catch (Exception ex)
            {
                _logger.Error($"同步自启动注册表项失败: {ex.Message}", ex);
            }
        }

        [ObservableProperty]
        private string _storagePath;

        [ObservableProperty]
        private string _timelineZoomMode;

        partial void OnTimelineZoomModeChanged(string value)
        {
            _configService.Config.TimelineZoomMode = value;
            _configService.Save();
        }

        [RelayCommand]
        private void OpenLogFolder()
        {
            try
            {
                string logPath = _logger.GetLogPath();
                string? logDir = System.IO.Path.GetDirectoryName(logPath);

                if (!string.IsNullOrEmpty(logDir) && System.IO.Directory.Exists(logDir))
                {
                    _logger.Info($"User opening log folder: {logDir}");
                    System.Diagnostics.Process.Start("explorer.exe", logDir);
                }
                else
                {
                    RikkaMessageBox.Show(
                        GetResource("StrLogDirNotExist"),
                        GetResource("StrPrompt"),
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to open log folder.", ex);
            }
        }

        [RelayCommand]
        private void OpenDiagnosticConsole()
        {
            App.Current.ShowDiagnosticWindow();
        }

        [ObservableProperty]
        private string _language;

        partial void OnLanguageChanged(string value)
        {
            _configService.Config.Language = value;
            _configService.Save();
            
            if (value == "Auto")
            {
                _localizationService.Initialize("Auto");
            }
            else
            {
                _localizationService.SetLanguage(value);
            }
        }

        private string _theme = "System";
        public string Theme
        {
            get => _theme;
            set
            {
                if (SetProperty(ref _theme, value))
                {
                    _themeService.ApplyTheme(value);
                }
            }
        }

        // @Author: trae + deepseek-V4-pro
        // @Date: 2026-06-23
        // @Desc: 浏览器扩展连接状态（用于设置页绿色/灰色圆点）
        [ObservableProperty]
        private bool _isExtensionConnected;

        [ObservableProperty]
        private bool _webMonitorEnabled;

        partial void OnWebMonitorEnabledChanged(bool value)
        {
            _configService.Config.WebMonitorEnabled = value;
            _configService.Save();
        }

        // @Author: trae + deepseek-V4-pro
        // @Date: 2026-06-23
        // @Desc: 浏览器扩展商店及 GitHub Release URL 常量
        private const string ChromeExtensionStoreUrl = "https://chrome.google.com/webstore/detail/rikkatracker-sentry/...";
        private const string EdgeExtensionStoreUrl = "https://microsoftedge.microsoft.com/addons/detail/rikkatracker-sentry/...";
        private const string FirefoxExtensionStoreUrl = "https://addons.mozilla.org/firefox/addon/rikkatracker-sentry/";
        private const string GitHubReleaseUrl = "https://github.com/remnant-song/RikkaTrack/releases/latest";

        // @Author: trae + deepseek-V4-pro
        // @Date: 2026-06-23
        // @Desc: 安装指引展开/折叠状态
        [ObservableProperty]
        private bool _isInstallGuideExpanded;

        // @Author: trae + deepseek-V4-pro
        // @Date: 2026-06-23
        // @Desc: 打开 Chrome 网上应用店
        [RelayCommand]
        private void OpenChromeStore()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = ChromeExtensionStoreUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to open Chrome Web Store.", ex);
            }
        }

        // @Author: trae + deepseek-V4-pro
        // @Date: 2026-06-23
        // @Desc: 打开 Edge 加载项商店
        [RelayCommand]
        private void OpenEdgeStore()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = EdgeExtensionStoreUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to open Edge Add-ons Store.", ex);
            }
        }

        // @Desc: 打开 Firefox 附加组件商店（AMO）
        [RelayCommand]
        private void OpenFirefoxStore()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = FirefoxExtensionStoreUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to open Firefox Add-ons Store.", ex);
            }
        }

        // @Author: trae + deepseek-V4-pro
        // @Date: 2026-06-23
        // @Desc: 打开 GitHub Releases 下载页
        [RelayCommand]
        private void OpenGitHubRelease()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = GitHubReleaseUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to open GitHub Releases page.", ex);
            }
        }

        // @Author: trae + deepseek-V4-pro
        // @Date: 2026-06-23
        // @Desc: 切换安装指引展开/折叠
        [RelayCommand]
        private void ToggleInstallGuide()
        {
            IsInstallGuideExpanded = !IsInstallGuideExpanded;
        }

        [ObservableProperty]
        private string _displayMode = "Combined";

        partial void OnDisplayModeChanged(string value)
        {
            _configService.Config.DisplayMode = value;
            _configService.Save();
        }

        [ObservableProperty]
        private string _timelineSortMode = "Duration";

        partial void OnTimelineSortModeChanged(string value)
        {
            _configService.Config.TimelineSortMode = value;
            _configService.Save();
        }

        [ObservableProperty]
        private bool _showRowBadges = true;

        partial void OnShowRowBadgesChanged(bool value)
        {
            _configService.Config.ShowRowBadges = value;
            _configService.Save();
        }
    }
}
