using System;
using System.Windows;
using System.Windows.Media;
using RikkaTracker.Helpers;
using Wpf.Ui.Controls;

namespace RikkaTracker.Controls
{
    public partial class RikkaMessageBoxWindow : FluentWindow
    {
        public System.Windows.MessageBoxResult Result { get; private set; } = System.Windows.MessageBoxResult.None;

        public RikkaMessageBoxWindow(string message, string title, System.Windows.MessageBoxButton button, MessageBoxImage image)
        {
            InitializeComponent();

            // Fall back to solid themed background on Windows 10
            // (Mica/Acrylic legacy fallbacks are unreliable across builds).
            if (!PlatformHelper.IsWindows11OrNewer && WindowBackdropType == WindowBackdropType.Mica)
            {
                WindowBackdropType = WindowBackdropType.None;
                SetResourceReference(BackgroundProperty, "ApplicationBackgroundBrush");
            }

            Title = title;
            MessageText.Text = message;

            SetupIcon(image);
            SetupButtons(button);
        }

        private void SetupIcon(MessageBoxImage image)
        {
            StatusIcon.Visibility = Visibility.Visible;
            switch (image)
            {
                case MessageBoxImage.Information:
                    StatusIcon.Symbol = SymbolRegular.Info24;
                    StatusIcon.Foreground = (Brush)Application.Current.Resources["SystemAccentColorBrush"] ?? Brushes.DeepSkyBlue;
                    break;
                case MessageBoxImage.Question:
                    StatusIcon.Symbol = SymbolRegular.QuestionCircle24;
                    StatusIcon.Foreground = (Brush)Application.Current.Resources["SystemAccentColorBrush"] ?? Brushes.DeepSkyBlue;
                    break;
                case MessageBoxImage.Warning:
                    StatusIcon.Symbol = SymbolRegular.Alert24;
                    StatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(230, 162, 44)); // #E6A23C
                    break;
                case MessageBoxImage.Error:
                    StatusIcon.Symbol = SymbolRegular.DismissCircle24;
                    StatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(245, 108, 108)); // #F56C6C
                    break;
                default:
                    StatusIcon.Visibility = Visibility.Collapsed;
                    break;
            }
        }

        private string GetLocalizedString(string key, string fallback)
        {
            if (Application.Current?.Resources != null && Application.Current.Resources.Contains(key))
            {
                return Application.Current.Resources[key] as string ?? fallback;
            }
            return fallback;
        }

        private void SetupButtons(System.Windows.MessageBoxButton button)
        {
            ButtonsPanel.Children.Clear();

            string okText = GetLocalizedString("StrOk", "确定");
            string cancelText = GetLocalizedString("StrCancel", "取消");
            string yesText = GetLocalizedString("StrYes", "是");
            string noText = GetLocalizedString("StrNoBtn", "否");

            switch (button)
            {
                case System.Windows.MessageBoxButton.OK:
                    AddButton(okText, System.Windows.MessageBoxResult.OK, isDefault: true);
                    break;
                case System.Windows.MessageBoxButton.OKCancel:
                    AddButton(okText, System.Windows.MessageBoxResult.OK, isDefault: true);
                    AddButton(cancelText, System.Windows.MessageBoxResult.Cancel, isCancel: true);
                    break;
                case System.Windows.MessageBoxButton.YesNo:
                    AddButton(yesText, System.Windows.MessageBoxResult.Yes, isDefault: true);
                    AddButton(noText, System.Windows.MessageBoxResult.No);
                    break;
                case System.Windows.MessageBoxButton.YesNoCancel:
                    AddButton(yesText, System.Windows.MessageBoxResult.Yes, isDefault: true);
                    AddButton(noText, System.Windows.MessageBoxResult.No);
                    AddButton(cancelText, System.Windows.MessageBoxResult.Cancel, isCancel: true);
                    break;
            }
        }

        private void AddButton(string text, System.Windows.MessageBoxResult result, bool isDefault = false, bool isCancel = false)
        {
            var btn = new Wpf.Ui.Controls.Button
            {
                Content = text,
                MinWidth = 85,
                Height = 32,
                IsDefault = isDefault,
                IsCancel = isCancel
            };

            if (ButtonsPanel.Children.Count > 0)
            {
                btn.Margin = new Thickness(10, 0, 0, 0);
            }

            if (isDefault)
            {
                btn.Appearance = ControlAppearance.Primary;
                btn.Focus();
            }
            else
            {
                btn.Appearance = ControlAppearance.Secondary;
            }

            btn.Click += (s, e) =>
            {
                Result = result;
                try
                {
                    DialogResult = true;
                }
                catch
                {
                    // 防止在非 ShowDialog 状态下设置 DialogResult 抛错
                }
                Close();
            };

            ButtonsPanel.Children.Add(btn);
        }
    }
}
