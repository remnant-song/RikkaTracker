using System.Windows;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using RikkaTracker.Helpers;
using RikkaTracker.Services;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace RikkaTracker
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : FluentWindow
    {
        /*
         * @Author: trae + GLM-5.2
         * @Date: 2026-06-30
         * @Desc: 修复 PR #23 引入的问题：构造函数中重复设置 DataContext 会创建
         *        两个 MainViewModel（每个又创建 DashboardViewModel 并订阅事件），
         *        导致事件泄漏与不必要的资源开销。
         *        原 PR 代码：DataContext = App.Current.ServiceProvider.GetService<MainViewModel>();
         *        ShowMainWindow() 已通过对象初始化器设置 DataContext，此处无需再设。
         * @Modify: 2026-06-30 trae + GLM-5.2 – 移除重复 DataContext 赋值，增加构造日志
         */
        public MainWindow()
        {
            // 记录构造过程，便于诊断窗口创建失败问题
            App.Current?.ServiceProvider?.GetService<ILoggerService>()?.Info("MainWindow constructor started.");

            App.EnsureLiveChartsConfigured();
            InitializeComponent();

            if (!PlatformHelper.IsWindows11OrNewer)
            {
                // Windows 10 does not support Mica. Acrylic's legacy fallback is
                // unreliable across builds. Use no backdrop with a solid themed
                // background so the window never goes transparent/white.
                WindowBackdropType = WindowBackdropType.None;
                SetResourceReference(BackgroundProperty, "ApplicationBackgroundBrush");
            }

            // Seed WPF-UI control dictionaries into this window.
            ApplicationThemeManager.Apply(this);

            // When the theme changes globally, re-sync this window's local
            // dictionaries so controls follow the new theme immediately.
            ApplicationThemeManager.Changed += OnThemeChanged;

            // PR #23 原在此处设置 DataContext，但 ShowMainWindow() 的对象初始化器
            // 会再次设置，导致 MainViewModel 被创建两次。现已移除此行，
            // DataContext 统一由 ShowMainWindow() 设置。
            // 保留原注释说明：
            // DataContext = App.Current.ServiceProvider.GetService<ViewModels.MainViewModel>();

            App.Current?.ServiceProvider?.GetService<ILoggerService>()?.Info("MainWindow constructor completed.");
        }

        private void OnThemeChanged(ApplicationTheme currentTheme, Color systemAccent)
        {
            ApplicationThemeManager.Apply(this);
        }

        protected override void OnClosed(EventArgs e)
        {
            ApplicationThemeManager.Changed -= OnThemeChanged;
            base.OnClosed(e);
        }
    }
}