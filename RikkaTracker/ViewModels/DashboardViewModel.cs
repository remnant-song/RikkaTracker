using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.Defaults;
using SkiaSharp;
using RikkaTracker.Helpers;
using RikkaTracker.Services;

namespace RikkaTracker.ViewModels
{
    public partial class DashboardViewModel : ObservableObject
    {
        private readonly IDataService _dataService;
        private readonly IIconService _iconService;
        private readonly IThemeService _themeService;

        private SolidColorPaint? _axisLabelPaint;
        private SolidColorPaint? _axisSeparatorPaint;
        private SolidColorPaint? _columnFillPaint;
        private SolidColorPaint? _webColumnFillPaint;

        [ObservableProperty]
        private string _totalTimeText = "0h 0m";

        [ObservableProperty]
        private int _appCount = 0;

        [ObservableProperty]
        private string _topAppName = "N/A";

        [ObservableProperty]
        private string _topAppTimeText = "0h 0m";

        [ObservableProperty]
        private ImageSource? _topAppIcon;

        [ObservableProperty]
        private string _mostActiveHour = "-";

        [ObservableProperty]
        private string _webTotalTimeText = "0h 0m";

        [ObservableProperty]
        private string _topSiteDomain = "N/A";

        [ObservableProperty]
        private string _topSiteTimeText = "0h 0m";

        [ObservableProperty]
        private ISeries[] _series;

        [ObservableProperty]
        private Axis[] _xAxes;

        [ObservableProperty]
        private Axis[] _yAxes;

        [ObservableProperty]
        private ISeries[] _webSeries;

        [ObservableProperty]
        private Axis[] _webXAxes;

        [ObservableProperty]
        private Axis[] _webYAxes;

        public DashboardViewModel(IDataService dataService, IIconService iconService, IThemeService themeService)
        {
            _dataService = dataService;
            _iconService = iconService;
            _themeService = themeService;

            _themeService.ThemeChanged += OnThemeChanged;
            UpdateChartColors(_themeService.GetCurrentTheme());

            var emptySeries = new ISeries[]
            {
                new ColumnSeries<ObservablePoint>
                {
                    Values = new ObservablePoint[0],
                    Fill = _columnFillPaint,
                    MaxBarWidth = 40,
                    Rx = 4,
                    Ry = 4
                }
            };

            Series = emptySeries;
            WebSeries = emptySeries;

            var emptyAxis = new Axis[] { new Axis { Labels = new string[0], LabelsPaint = _axisLabelPaint, SeparatorsPaint = _axisSeparatorPaint } };
            XAxes = emptyAxis;
            YAxes = new Axis[] { new Axis { Labeler = value => TimeSpan.FromSeconds(value).ToString(@"hh\:mm\:ss"), LabelsPaint = _axisLabelPaint, SeparatorsPaint = _axisSeparatorPaint } };
            WebXAxes = emptyAxis;
            WebYAxes = new Axis[] { new Axis { Labeler = value => TimeSpan.FromSeconds(value).ToString(@"hh\:mm\:ss"), LabelsPaint = _axisLabelPaint, SeparatorsPaint = _axisSeparatorPaint } };

            InitializeAsync();
        }

        private async void InitializeAsync()
        {
            await RefreshDataAsync();
        }

        public async Task RefreshDataAsync()
        {
            var today = DateTime.Today;
            var tomorrow = today.AddDays(1);

            var appSummary = await _dataService.GetStatsSummaryAsync(today, tomorrow);
            TotalTimeText = FormatTimeSpan(appSummary.TotalTime);
            AppCount = appSummary.AppCount;
            TopAppName = appSummary.TopAppName;
            TopAppTimeText = FormatTimeSpan(appSummary.TopAppTime);
            TopAppIcon = _iconService.GetIcon(appSummary.TopAppName, appSummary.TopAppPath);

            var webSummary = await _dataService.GetWebStatsSummaryAsync(today, tomorrow);
            WebTotalTimeText = FormatTimeSpan(webSummary.TotalTime);
            TopSiteDomain = webSummary.TopDomain;
            TopSiteTimeText = FormatTimeSpan(webSummary.TopDomainTime);

            var appHourly = await _dataService.GetHourlyUsageAsync(today);
            var hourlyList = appHourly.ToList();
            var maxHour = hourlyList.OrderByDescending(h => h.TotalTime).FirstOrDefault();
            MostActiveHour = maxHour.TotalTime.TotalSeconds > 0
                ? $"{maxHour.Hour:D2}:00 - {maxHour.Hour + 1:D2}:00"
                : "-";

            var appValues = new List<ObservablePoint>();
            var labels = new List<string>();
            for (int i = 0; i < hourlyList.Count; i++)
            {
                appValues.Add(new ObservablePoint(i, hourlyList[i].TotalTime.TotalSeconds));
                labels.Add($"{hourlyList[i].Hour}:00");
            }

            XAxes = new Axis[] { new Axis { Labels = labels, LabelsPaint = _axisLabelPaint, SeparatorsPaint = _axisSeparatorPaint } };
            YAxes = new Axis[] { new Axis { MinLimit = 0, Labeler = value => TimeSpan.FromSeconds(value).ToString(@"hh\:mm\:ss"), LabelsPaint = _axisLabelPaint, SeparatorsPaint = _axisSeparatorPaint } };
            Series = new ISeries[]
            {
                new ColumnSeries<ObservablePoint>
                {
                    Values = appValues,
                    Fill = _columnFillPaint,
                    MaxBarWidth = 40,
                    Rx = 4,
                    Ry = 4
                }
            };

            var webHourly = await _dataService.GetWebHourlyUsageAsync(today);
            var webHourlyList = webHourly.ToList();
            var webValues = new List<ObservablePoint>();
            var webLabels = new List<string>();
            for (int i = 0; i < webHourlyList.Count; i++)
            {
                webValues.Add(new ObservablePoint(i, webHourlyList[i].TotalTime.TotalSeconds));
                webLabels.Add($"{webHourlyList[i].Hour}:00");
            }

            WebXAxes = new Axis[] { new Axis { Labels = webLabels, LabelsPaint = _axisLabelPaint, SeparatorsPaint = _axisSeparatorPaint } };
            WebYAxes = new Axis[] { new Axis { MinLimit = 0, Labeler = value => TimeSpan.FromSeconds(value).ToString(@"hh\:mm\:ss"), LabelsPaint = _axisLabelPaint, SeparatorsPaint = _axisSeparatorPaint } };
            WebSeries = new ISeries[]
            {
                new ColumnSeries<ObservablePoint>
                {
                    Values = webValues,
                    Fill = _webColumnFillPaint,
                    MaxBarWidth = 40,
                    Rx = 4,
                    Ry = 4
                }
            };
        }

        /*
         * @Author: trae + default-model
         * @Date:   2026-06-30
         * @Modify: 2026-06-30 – 重构：提取共享工具方法至 ChartColorHelper；
         *          暗色模式下网页柱体增加去饱和处理（DesaturateSkColor），
         *          使其与强调色应用柱体有明显视觉区分。
         */
        private void UpdateChartColors(string theme)
        {
            var isDark = ChartColorHelper.IsThemeEffectivelyDark(theme);
            var labelColor = isDark
                ? new SKColor(220, 220, 220)
                : new SKColor(80, 80, 80);
            var separatorColor = isDark
                ? new SKColor(255, 255, 255, 30)
                : new SKColor(0, 0, 0, 15);

            // Derive bar colors from the Windows accent color so charts follow the palette
            var accent = ChartColorHelper.GetAccentSkColor();
            // App bars: accent itself (brightened a touch in dark mode for contrast)
            var barColor = isDark
                ? ChartColorHelper.BrightenSkColor(accent, 0.20f)
                : accent;
            // Web bars: shift hue (light mode) or desaturate accent (dark mode)
            // for visual distinction from app bars.
            var webBarColor = isDark
                ? ChartColorHelper.DesaturateSkColor(accent, 0.40f)
                : ChartColorHelper.ShiftHueSkColor(accent, 0.15f);

            (_axisLabelPaint as IDisposable)?.Dispose();
            (_axisSeparatorPaint as IDisposable)?.Dispose();
            (_columnFillPaint as IDisposable)?.Dispose();
            (_webColumnFillPaint as IDisposable)?.Dispose();

            _axisLabelPaint = new SolidColorPaint(labelColor);
            _axisSeparatorPaint = new SolidColorPaint(separatorColor)
                { StrokeThickness = 1 };
            _columnFillPaint = new SolidColorPaint(barColor);
            _webColumnFillPaint = new SolidColorPaint(webBarColor);
        }

        private void OnThemeChanged(string newTheme)
        {
            UpdateChartColors(newTheme);
        }

        private static string FormatTimeSpan(TimeSpan ts)
        {
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours}h {ts.Minutes}m";
            return $"{ts.Minutes}m {ts.Seconds}s";
        }
    }
}
