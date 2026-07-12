using System;
using System.Linq;
using System.Windows.Controls;

namespace RikkaTracker.Views
{
    public partial class StatisticsView : UserControl
    {
        private bool _isSyncing;

        public StatisticsView()
        {
            InitializeComponent();
            this.DataContextChanged += StatisticsView_DataContextChanged;
            this.Loaded += (s, e) => 
            {
                Dispatcher.BeginInvoke(new Action(() => ScrollToLatest()), System.Windows.Threading.DispatcherPriority.Background);
            };
        }

        private void StatisticsView_DataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is ViewModels.StatisticsViewModel oldVm)
            {
                oldVm.PropertyChanged -= ViewModel_PropertyChanged;
            }
            if (e.NewValue is ViewModels.StatisticsViewModel newVm)
            {
                newVm.PropertyChanged += ViewModel_PropertyChanged;
            }
        }

        private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewModels.StatisticsViewModel.RefreshTrigger))
            {
                Dispatcher.BeginInvoke(new Action(() => 
                {
                    ScrollToLatest();
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }

        private void ScrollToLatest()
        {
            if (TimelineScroller == null || DataContext is not ViewModels.StatisticsViewModel vm) return;
            if (vm.GanttSegments == null || !vm.GanttSegments.Any()) return;

            TimelineScroller.UpdateLayout();

            var segments = vm.GanttSegments.ToList();
            var latestEnd = segments.Max(s => s.End);
            var baseTime = segments.First().Start.Date;
            var pixelsPerHour = ZoomSlider.Value;

            double x = (latestEnd - baseTime).TotalHours * pixelsPerHour;
            double targetOffset = x - (TimelineScroller.ViewportWidth / 2);
            if (targetOffset < 0) targetOffset = 0;
            
            TimelineScroller.ScrollToHorizontalOffset(targetOffset);
        }

        private void TimelineScroller_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_isSyncing) return;
            _isSyncing = true;
            try
            {
                if (e.VerticalChange != 0)
                {
                    HeaderScroller.ScrollToVerticalOffset(e.VerticalOffset);
                }
                if (e.HorizontalChange != 0)
                {
                    TimeHeaderScroller.ScrollToHorizontalOffset(e.HorizontalOffset);
                }
            }
            finally
            {
                _isSyncing = false;
            }
        }

        private void HeaderScroller_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_isSyncing) return;
            _isSyncing = true;
            try
            {
                if (e.VerticalChange != 0)
                {
                    TimelineScroller.ScrollToVerticalOffset(e.VerticalOffset);
                }
            }
            finally
            {
                _isSyncing = false;
            }
        }

        private void TimelineScroller_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            TimelineScroller.ScrollToHorizontalOffset(TimelineScroller.HorizontalOffset - e.Delta);
            e.Handled = true;
        }
    }
}
