using System;
using System.Windows.Media;
using Wpf.Ui.Appearance;
using RikkaTracker.Services;

namespace RikkaTracker.Services
{
    public interface IThemeService
    {
        event Action<string> ThemeChanged;
        void ApplyTheme(string themeName);
        /// <summary>
        /// Call after a new window is created so WPF-UI re-syncs system accent
        /// resources onto the fresh window.
        /// </summary>
        void RefreshAccent();
        string GetCurrentTheme();
    }

    public class ThemeService : IThemeService
    {
        private readonly IConfigService _configService;
        private bool _subscribedToWpfUi;

        public event Action<string>? ThemeChanged;

        public ThemeService(IConfigService configService)
        {
            _configService = configService;
        }

        public void ApplyTheme(string themeName)
        {
            switch (themeName.ToLowerInvariant())
            {
                case "system":
                    ApplicationThemeManager.ApplySystemTheme(updateAccent: true);
                    break;
                case "light":
                    ApplicationThemeManager.Apply(ApplicationTheme.Light, updateAccent: true);
                    break;
                case "dark":
                default:
                    ApplicationThemeManager.Apply(ApplicationTheme.Dark, updateAccent: true);
                    break;
            }

            _configService.Config.Theme = themeName;
            _configService.Save();

            if (!_subscribedToWpfUi)
            {
                ApplicationThemeManager.Changed += OnWpfUiThemeChanged;
                _subscribedToWpfUi = true;
            }

            ThemeChanged?.Invoke(themeName);
        }

        /// <summary>
        /// Re-syncs the system accent through WPF-UI's own native mechanism.
        /// Call after a new FluentWindow is created so controls pick up the
        /// accent color instead of falling back to monochrome.
        /// </summary>
        public void RefreshAccent()
        {
            try
            {
                ApplicationAccentColorManager.ApplySystemAccent();
            }
            catch
            {
                // Ignore – WPF-UI will use its defaults.
            }
        }

        /// <summary>
        /// Relays WPF-UI theme-change events to our own subscribers
        /// when the user has chosen "System" mode.
        /// </summary>
        private void OnWpfUiThemeChanged(ApplicationTheme currentTheme, Color systemAccent)
        {
            if (_configService.Config.Theme.Equals("System", StringComparison.OrdinalIgnoreCase))
            {
                ThemeChanged?.Invoke("System");
            }
        }

        public string GetCurrentTheme()
        {
            return _configService.Config.Theme;
        }
    }
}
