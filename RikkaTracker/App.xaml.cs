using System;
using System.Diagnostics;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using RikkaTracker.ViewModels;
using RikkaTracker.Services;
using Hardcodet.Wpf.TaskbarNotification;
using System.Windows.Controls;
using RikkaTracker.Core.Monitor;
using RikkaTracker.Core.Data;
using RikkaTracker.Core.Models;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using RikkaTracker.Controls;

namespace RikkaTracker
{
    public partial class App : Application
    {
        public IServiceProvider ServiceProvider { get; private set; }
        private TaskbarIcon? _notifyIcon;
        private ILoggerService? _logger;
        private static Mutex? _appMutex;
        private Views.DiagnosticWindow? _diagnosticWindow;
        private static bool _isLiveChartsConfigured = false;
        private static readonly object _liveChartsLock = new object();

        public App()
        {
            ServiceProvider = ConfigureServices();
            _logger = ServiceProvider.GetRequiredService<ILoggerService>();
            
            // 最后的防线：全局异常捕获
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            _logger?.Error("FATAL: Unhandled Dispatcher Exception", e.Exception);
            System.Console.WriteLine($"[FATAL Dispatcher Exception] {e.Exception}");
            System.Diagnostics.Debug.WriteLine($"[FATAL Dispatcher Exception] {e.Exception}");
            
            var localizationService = ServiceProvider?.GetService<ILocalizationService>();
            string title = localizationService?.GetString("StrFatalErrorTitle", "致命错误") ?? "致命错误";
            string msg = localizationService?.GetString("StrFatalErrorUiMessage", "应用遇到了严重的 UI 线程错误，即将记录并尝试关闭。详情请见日志。") ?? "应用遇到了严重的 UI 线程错误，即将记录并尝试关闭。详情请见日志。";
            
            RikkaMessageBox.Show(msg, title, MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true; // 防止立即崩溃，尝试优雅退出
            ExitApplication();
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            _logger?.Error($"FATAL: Unhandled Domain Exception. IsTerminating: {e.IsTerminating}", e.ExceptionObject as Exception);
            System.Console.WriteLine($"[FATAL Domain Exception] IsTerminating: {e.IsTerminating}, Exception Object: {e.ExceptionObject}");
            System.Diagnostics.Debug.WriteLine($"[FATAL Domain Exception] IsTerminating: {e.IsTerminating}, Exception Object: {e.ExceptionObject}");
            if (!e.IsTerminating)
            {
                var localizationService = ServiceProvider?.GetService<ILocalizationService>();
                string title = localizationService?.GetString("StrFatalErrorTitle", "致命错误") ?? "致命错误";
                string msg = localizationService?.GetString("StrFatalErrorNonUiMessage", "应用遇到了严重的非 UI 线程错误，详情请见日志。") ?? "应用遇到了严重的非 UI 线程错误，详情请见日志。";
                
                RikkaMessageBox.Show(msg, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            // 确保单实例运行之前先初始化语言系统以供本地化弹窗使用
            var configService = ServiceProvider.GetRequiredService<IConfigService>();
            var localizationService = ServiceProvider.GetRequiredService<ILocalizationService>();
            localizationService.Initialize(configService.Config.Language);

            // 确保单实例运行
            bool createdNew;
            try
            {
                _appMutex = new Mutex(true, "Global\\RikkaTracker_Mutex_Unique_ID", out createdNew);
            }
            catch (UnauthorizedAccessException)
            {
                createdNew = false;
            }

            if (!createdNew)
            {
                // 使用 Windows 原生 MessageBox，避免在 WPF 资源和主题尚未加载时使用自定义 FluentWindow 导致样式缺失或白屏
                string title = localizationService.GetString("StrDuplicateInstanceTitle", "RikkaTracker");
                string msg = localizationService.GetString("StrDuplicateInstanceMessage", "RikkaTracker 已经在运行中。");
                
                System.Console.WriteLine("Duplicate instance detected: RikkaTracker is already running. Showing native MessageBox...");
                System.Diagnostics.Debug.WriteLine("Duplicate instance detected: RikkaTracker is already running. Showing native MessageBox...");
                System.Windows.MessageBox.Show(msg, title, MessageBoxButton.OK, MessageBoxImage.Information);
                
                // 立即退出，避免进入 WPF 的 OnStartup 生命周期造成残留进程和白色空窗口挂起
                System.Console.WriteLine("Exiting application process immediately.");
                System.Diagnostics.Debug.WriteLine("Exiting application process immediately.");
                Environment.Exit(0);
                return;
            }

            base.OnStartup(e);
            _logger?.Info("--- RikkaTracker Startup ---");

            /*
             * @Author: trae + GLM-5.2
             * @Date: 2026-06-30
             * @Desc: 从配置文件加载日志系统配置，覆盖默认值。
             *        在 ConfigService 初始化后立即应用，确保后续所有日志
             *        都遵从用户配置的级别和策略。
             */
            ApplyLogConfiguration(configService.Config);

            // Initialize Tray Icon
            _notifyIcon = new TaskbarIcon();
            _notifyIcon.IconSource = new System.Windows.Media.Imaging.BitmapImage(
                new Uri("pack://application:,,,/assets/app-icon.png")
            );
            _notifyIcon.ToolTipText = (Current.Resources["StrAppName"] as string) ?? "RikkaTracker";
            _notifyIcon.DoubleClickCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(ShowMainWindow);

            bool startMinimized = false;
            foreach (var arg in e.Args)
            {
                if (arg.Equals("/minimized", StringComparison.OrdinalIgnoreCase) || 
                    arg.Equals("/hide", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("/silent", StringComparison.OrdinalIgnoreCase))
                {
                    startMinimized = true;
                }
            }

            try 
            {
                InitializeCoreServices();
            }
            catch (Exception ex)
            {
                _logger?.Error("Failed to initialize core services during startup.", ex);
                System.Console.WriteLine($"[Core Services Init Failed] Exception: {ex}");
                System.Diagnostics.Debug.WriteLine($"[Core Services Init Failed] Exception: {ex}");
                
                string title = localizationService.GetString("StrInitFailedTitle", "初始化失败");
                string msg = localizationService.GetString("StrInitFailedMessage", "启动核心服务失败，应用可能无法正常工作。请检查日志。");
                RikkaMessageBox.Show(msg, title, MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            if (!startMinimized)
            {
                ShowMainWindow();
            }
            else
            {
                // 静默启动到后台托盘，执行一次工作集最小化
                Win32Api.MinimizeMemory();
            }
        }

        private void InitializeCoreServices()
        {
            var activityTracker = ServiceProvider.GetRequiredService<IAppActivityTracker>();
            var logStore = ServiceProvider.GetRequiredService<IActivityLogStore>();

            activityTracker.AppActivityChanged += (s, args) =>
            {
                // 日志记录级别调整为 Info 以便追踪，但在生产环境下可以根据需要调低
                _logger?.Info($"[Activity] {args.ProcessName} -> {args.NewStatus} | {args.WindowTitle}", LogCategory.Monitor);
            };
            activityTracker.Start();

            var processMonitor = ServiceProvider.GetRequiredService<IProcessMonitor>();
            processMonitor.ProcessStarted += (s, args) => _logger?.Info($"[Process] Started: {args.ProcessName}", LogCategory.Monitor);
            processMonitor.ProcessExited += (s, args) =>
            {
                _logger?.Info($"[Process] Exited: {args.ProcessName}", LogCategory.Monitor);
                logStore.CloseProcess(args.ProcessName, DateTime.Now);
            };
            processMonitor.Start();

            var themeService = ServiceProvider.GetRequiredService<IThemeService>();
            themeService.ApplyTheme(themeService.GetCurrentTheme());

            try
            {
                var configService = ServiceProvider.GetRequiredService<IConfigService>();
                if (configService.Config.WebMonitorEnabled)
                {
                    var webMonitor = ServiceProvider.GetRequiredService<IWebMonitorService>();
                    webMonitor.WebUsageReceived += (usage) =>
                        _logger?.Info($"[Web] {usage.Domain} | {usage.Title} | {usage.Duration.TotalSeconds:F0}s", LogCategory.Web);
                    webMonitor.Start();
                }
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Failed to start web monitor: {ex.Message}");
            }

            // 确保开机自启动路径的正确性（如果在配置中启用，则重新写入当前路径，应对程序移动或更新等情况）
            try
            {
                var configService = ServiceProvider.GetRequiredService<IConfigService>();
                if (configService.Config.StartWithWindows)
                {
                    Helpers.StartupHelper.SetStartup(true);
                }
            }
            catch (Exception ex)
            {
                _logger?.Warning($"启动时同步自启动注册表项失败: {ex.Message}");
            }

            UpdateTrayMenu();
        }

        public void ShowMainWindow()
        {
            /*
             * @Author: trae + GLM-5.2
             * @Date: 2026-06-30
             * @Desc: 修复 PR #23 合并后任务栏"概览"无法打开窗口的问题。
             *        根因：原实现将 MainWindow = mainWindow 置于 Show() 之前，
             *        若 Show() 抛异常（主题/Mica 初始化失败），MainWindow 会卡在
             *        非 null 状态，后续点击均命中 Activate 分支但窗口实际不可见。
             *        修复：将赋值移至 Show() 成功之后，并在 Activate 分支增加
             *        可见性检查与日志，确保窗口真正显示。
             * @Modify: 2026-06-30 trae + GLM-5.2 – 增加调试日志与异常保护
             */
            _logger?.Info($"ShowMainWindow called. MainWindow == null: {MainWindow == null}");

            if (MainWindow != null)
            {
                // 检查窗口是否真正可见。若窗口处于异常状态（已关闭但 Closed 未触发，
                // 或 Visibility 非 Visible），则清除引用并走创建分支。
                // 注意：仅检查 IsVisible 而非 IsLoaded，因为 new 后 IsLoaded 即为 true，
                // 但 Show() 失败的窗口 IsVisible 仍为 false。
                if (MainWindow.IsVisible)
                {
                    _logger?.Info($"Activating existing MainWindow. IsVisible={MainWindow.IsVisible}, WindowState={MainWindow.WindowState}");
                    MainWindow.Activate();
                    if (MainWindow.WindowState == WindowState.Minimized)
                        MainWindow.WindowState = WindowState.Normal;
                    return;
                }
                else
                {
                    // 窗口引用存在但不可见，说明之前的 Show() 可能失败或窗口已异常关闭。
                    // 清除陈旧引用，重新创建窗口。
                    _logger?.Warning($"MainWindow reference exists but not visible (IsVisible={MainWindow.IsVisible}). Clearing stale reference and recreating.");
                    MainWindow = null;
                }
            }

            try
            {
                _logger?.Info("Creating new MainWindow instance...");
                var mainWindow = new MainWindow
                {
                    DataContext = ServiceProvider.GetRequiredService<MainViewModel>()
                };
                _logger?.Info("MainWindow instance created successfully.");

                // Re-stamp accent resources so the fresh window picks up the
                // Windows-palette-derived colors (not B/W defaults).
                try
                {
                    ServiceProvider.GetRequiredService<IThemeService>().RefreshAccent();
                }
                catch (Exception ex)
                {
                    // @Author: trae + deepseek-v4-pro
                    // @Date: 2026-06-30
                    // @Desc: 记录 RefreshAccent 失败日志，避免静默吞没异常导致
                    //        窗口丢失强调色而用户不知情。
                    _logger?.Warning($"RefreshAccent failed in ShowMainWindow: {ex.Message}");
                }

                mainWindow.Closed += (s, e) =>
                {
                    _logger?.Info("MainWindow Closed event fired. Setting MainWindow = null.");
                    MainWindow = null;
                    // 执行主动垃圾收集与物理内存换出
                    Win32Api.MinimizeMemory();
                };

                // 关键修复：先 Show() 成功后再赋值 MainWindow。
                // 这样若 Show() 抛异常，MainWindow 保持 null，下次点击会重新创建，
                // 而不是卡在 Activate 分支无法显示窗口。
                mainWindow.Show();
                _logger?.Info("MainWindow.Show() completed successfully.");
                MainWindow = mainWindow;
            }
            catch (Exception ex)
            {
                // Show() 或构造过程中抛异常时记录日志。
                // 不设置 MainWindow，下次点击会重新尝试创建。
                _logger?.Error("Failed to create or show MainWindow.", ex);
                System.Console.WriteLine($"[ShowMainWindow Failed] {ex}");
                System.Diagnostics.Debug.WriteLine($"[ShowMainWindow Failed] {ex}");
            }
        }

        private void UpdateTrayMenu()
        {
            if (_notifyIcon == null) return;
            var contextMenu = new ContextMenu();
            var showItem = new MenuItem { Header = GetResourceString("StrDashboard", "Show") };
            showItem.Click += (s, ex) => ShowMainWindow();
            var diagnosticItem = new MenuItem { Header = GetResourceString("StrDiagnosticConsole", "Diagnostic Console") };
            diagnosticItem.Click += (s, ex) => ShowDiagnosticWindow();
            var exitItem = new MenuItem { Header = GetResourceString("StrExit", "Exit") };
            exitItem.Click += (s, ex) => ExitApplication();

            contextMenu.Items.Add(showItem);
            contextMenu.Items.Add(diagnosticItem);
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(exitItem);
            _notifyIcon.ContextMenu = contextMenu;
        }

        public void ShowDiagnosticWindow()
        {
            if (_diagnosticWindow != null)
            {
                _diagnosticWindow.Activate();
                if (_diagnosticWindow.WindowState == WindowState.Minimized)
                    _diagnosticWindow.WindowState = WindowState.Normal;
                return;
            }

            _diagnosticWindow = ServiceProvider.GetRequiredService<Views.DiagnosticWindow>();
            _diagnosticWindow.Closed += (s, e) => _diagnosticWindow = null;
            _diagnosticWindow.Show();
        }

        private string GetResourceString(string key, string fallback) => Application.Current.Resources[key] as string ?? fallback;
        private string CurrentLanguage => ServiceProvider?.GetService<ILocalizationService>()?.CurrentLanguage ?? "zh-CN";

        private void ExitApplication()
        {
            _logger?.Info("Shutting down application...");
            try 
            {
                if (ServiceProvider != null)
                {
                    var webMonitor = ServiceProvider.GetService<IWebMonitorService>();
                    webMonitor?.Stop();
                    var logStore = ServiceProvider.GetService<IActivityLogStore>() as IDisposable;
                    logStore?.Dispose();
                }
                _diagnosticWindow?.Close();
                _notifyIcon?.Dispose();
                _appMutex?.ReleaseMutex();
                _appMutex?.Dispose();
            }
            catch (Exception ex)
            {
                _logger?.Error("Error during shutdown cleanup.", ex);
            }
            Shutdown();
        }

        private IServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();
            services.AddSingleton<ILoggerService, FileLoggerService>();
            services.AddSingleton<IConfigService, ConfigService>();
            services.AddSingleton<IThemeService, ThemeService>();
            services.AddSingleton<SqliteDbContext>();
            services.AddSingleton<IActivityLogStore, SqliteLogStore>();
            services.AddSingleton<IDataService, SqliteDataService>();
            services.AddSingleton<RikkaTracker.Core.Strategies.IFilterEngine, RikkaTracker.Core.Strategies.FilterEngine>();
            services.AddSingleton<IAppActivityTracker, AppActivityTracker>();
            services.AddSingleton<IProcessMonitor, ProcessMonitor>();
            services.AddSingleton<IIconService, IconService>();
            services.AddSingleton<ILocalizationService, LocalizationService>();
            services.AddSingleton<IUpdateService, UpdateService>();
            services.AddSingleton<IWebDataService, WebDataService>();
            services.AddSingleton<IWebMonitorService, WebMonitorService>();
            services.AddTransient<Views.DiagnosticWindow>();
            
            services.AddTransient<MainViewModel>();
            services.AddTransient<DashboardViewModel>();
            services.AddTransient<SettingsViewModel>();
            services.AddTransient<ActivityListViewModel>();
            services.AddTransient<StatisticsViewModel>();
            services.AddTransient<FilterSettingsViewModel>();
            services.AddTransient<UsageStatisticsViewModel>();
            services.AddTransient<ExportViewModel>();

            return services.BuildServiceProvider();
        }

        public static void EnsureLiveChartsConfigured()
        {
            if (_isLiveChartsConfigured) return;
            lock (_liveChartsLock)
            {
                if (_isLiveChartsConfigured) return;
                LiveCharts.Configure(config => 
                    config
                        .AddDefaultMappers()
                        .AddSkiaSharp()
                        .AddLightTheme());
                _isLiveChartsConfigured = true;
            }
        }

        /*
         * @Author: trae + GLM-5.2
         * @Date: 2026-06-30
         * @Desc: 将 AppConfig 中的日志配置应用到 ILoggerService 实例。
         *        在 OnStartup 中配置加载完成后调用。
         */
        private void ApplyLogConfiguration(Models.AppConfig config)
        {
            if (_logger == null) return;

            try
            {
                // 应用日志级别（配置中存的是 int）
                var level = (LogLevel)config.LogLevel;
                if (Enum.IsDefined(typeof(LogLevel), level))
                {
                    _logger.MinimumLevel = level;
                }
                else
                {
                    _logger.Warning($"Invalid LogLevel in config: {config.LogLevel}, using default Info.");
                    _logger.MinimumLevel = LogLevel.Info;
                }

                // 应用日志保留天数
                if (config.LogRetentionDays > 0)
                {
                    _logger.LogRetentionDays = config.LogRetentionDays;
                }

                // 应用日志文件大小上限
                if (config.LogMaxFileSizeMb > 0)
                {
                    _logger.LogMaxFileSizeMb = config.LogMaxFileSizeMb;
                }

                _logger.Debug($"Log configuration applied: Level={_logger.MinimumLevel}, " +
                              $"Retention={_logger.LogRetentionDays}d, MaxSize={_logger.LogMaxFileSizeMb}MB",
                              LogCategory.System);
            }
            catch (Exception ex)
            {
                // 日志配置应用失败不应阻止启动
                Debug.WriteLine($"[App] Failed to apply log configuration: {ex.Message}");
            }
        }

        public static new App Current => (App)Application.Current;
    }
}
