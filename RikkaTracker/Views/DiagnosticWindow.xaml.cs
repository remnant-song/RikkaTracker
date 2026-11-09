using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RikkaTracker.Core.Monitor;
using RikkaTracker.Helpers;
using Wpf.Ui.Controls;

namespace RikkaTracker.Views
{
    public class DiagnosticLogEntry
    {
        public DateTime Timestamp { get; set; }
        public string TimestampString => Timestamp.ToString("HH:mm:ss.fff");
        public string ProcessName { get; set; } = string.Empty;
        public int ProcessId { get; set; }
        public string ProcessIdString => $"({ProcessId})";
        public string OldStatusString { get; set; } = string.Empty;
        public string NewStatusString { get; set; } = string.Empty;
        public string WindowTitle { get; set; } = string.Empty;
        public Brush StatusBrush { get; set; } = Brushes.Gray;
    }

    public partial class DiagnosticWindow : FluentWindow
    {
        private readonly IAppActivityTracker _activityTracker;
        private readonly List<DiagnosticLogEntry> _allEntries = new();
        private readonly ObservableCollection<DiagnosticLogEntry> _filteredEntries = new();
        private const int MaxLogCount = 1000;

        public DiagnosticWindow(IAppActivityTracker activityTracker)
        {
            InitializeComponent();
            _activityTracker = activityTracker;

            // Fall back to solid themed background on Windows 10
            // (Mica/Acrylic legacy fallbacks are unreliable across builds).
            if (!PlatformHelper.IsWindows11OrNewer && WindowBackdropType == WindowBackdropType.Mica)
            {
                WindowBackdropType = WindowBackdropType.None;
                SetResourceReference(BackgroundProperty, "ApplicationBackgroundBrush");
            }

            // Apply current theme to ensure DWM dark mode adaptation
            Wpf.Ui.Appearance.ApplicationThemeManager.Apply(this);
            
            LstLogs.ItemsSource = _filteredEntries;
            
            // Subscribe to activity changes
            _activityTracker.AppActivityChanged += OnAppActivityChanged;
            
            // Auto scroll to bottom when items are added
            ((INotifyCollectionChanged)LstLogs.Items).CollectionChanged += OnLogsCollectionChanged;
            
            // Initial Topmost state button style
            UpdateTopmostButtonAppearance();
        }


        private void OnAppActivityChanged(object? sender, AppActivityChangedEventArgs e)
        {
            // Ensure UI updates run on the Dispatcher thread
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var entry = new DiagnosticLogEntry
                {
                    Timestamp = e.Timestamp,
                    ProcessName = e.ProcessName,
                    ProcessId = e.ProcessId,
                    OldStatusString = FormatStatus(e.OldStatus),
                    NewStatusString = FormatStatus(e.NewStatus),
                    WindowTitle = string.IsNullOrEmpty(e.WindowTitle) ? "<Empty Title>" : e.WindowTitle,
                    StatusBrush = GetStatusBrush(e.NewStatus)
                };

                _allEntries.Add(entry);
                if (_allEntries.Count > MaxLogCount)
                {
                    _allEntries.RemoveAt(0);
                }

                if (MatchesFilter(entry))
                {
                    _filteredEntries.Add(entry);
                    if (_filteredEntries.Count > MaxLogCount)
                    {
                        _filteredEntries.RemoveAt(0);
                    }
                }
            }));
        }

        private void OnLogsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add && _filteredEntries.Count > 0)
            {
                // Scroll to the last item
                LstLogs.ScrollIntoView(_filteredEntries[_filteredEntries.Count - 1]);
            }
        }

        private bool MatchesFilter(DiagnosticLogEntry entry)
        {
            string filter = TxtFilter.Text.Trim();
            if (string.IsNullOrEmpty(filter))
                return true;

            return entry.ProcessName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                   entry.WindowTitle.Contains(filter, StringComparison.OrdinalIgnoreCase);
        }

        private void ApplyFilter()
        {
            _filteredEntries.Clear();
            var matched = _allEntries.Where(MatchesFilter).TakeLast(MaxLogCount);
            foreach (var entry in matched)
            {
                _filteredEntries.Add(entry);
            }
        }

        private void TxtFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void BtnTopmost_Click(object sender, RoutedEventArgs e)
        {
            Topmost = !Topmost;
            UpdateTopmostButtonAppearance();
        }

        private void UpdateTopmostButtonAppearance()
        {
            if (Topmost)
            {
                BtnTopmost.Appearance = Wpf.Ui.Controls.ControlAppearance.Primary;
            }
            else
            {
                BtnTopmost.Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary;
            }
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            _allEntries.Clear();
            _filteredEntries.Clear();
        }

        protected override void OnClosed(EventArgs e)
        {
            // Unsubscribe to avoid memory leaks
            _activityTracker.AppActivityChanged -= OnAppActivityChanged;
            ((INotifyCollectionChanged)LstLogs.Items).CollectionChanged -= OnLogsCollectionChanged;
            base.OnClosed(e);
        }

        private static string FormatStatus(ActivityStatus status)
        {
            return status switch
            {
                ActivityStatus.Background => "Background",
                ActivityStatus.ForegroundInactive => "Inactive",
                ActivityStatus.ForegroundActive => "Active",
                _ => status.ToString()
            };
        }

        private static Brush GetStatusBrush(ActivityStatus status)
        {
            string hex = status switch
            {
                ActivityStatus.ForegroundActive => "#4CAF50",      // Green
                ActivityStatus.ForegroundInactive => "#FFC107",    // Amber/Yellow
                ActivityStatus.Background => "#9E9E9E",            // Gray
                _ => "#9E9E9E"
            };
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }
    }
}
