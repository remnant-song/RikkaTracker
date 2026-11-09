using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using RikkaTracker.Core.Models;
using RikkaTracker.Core.Monitor;

namespace RikkaTracker.Controls
{
    public enum GanttDisplayMode
    {
        Full,
        Header,
        Timeline,
        TimeHeader
    }

    public class GanttChart : FrameworkElement
    {
        private readonly VisualCollection _children;
        private readonly DrawingVisual _mainVisual;
        private System.Windows.Controls.ToolTip _internalToolTip;
        
        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register("ItemsSource", typeof(IEnumerable<GanttSegment>), typeof(GanttChart),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender, OnItemsSourceChanged));

        public IEnumerable<GanttSegment> ItemsSource
        {
            get => (IEnumerable<GanttSegment>)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        public static readonly DependencyProperty PixelsPerHourProperty =
            DependencyProperty.Register("PixelsPerHour", typeof(double), typeof(GanttChart),
                new FrameworkPropertyMetadata(200.0, 
                    FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
                    OnPixelsPerHourChanged));

        public double PixelsPerHour
        {
            get => (double)GetValue(PixelsPerHourProperty);
            set => SetValue(PixelsPerHourProperty, value);
        }

        private static void OnPixelsPerHourChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not GanttChart control) return;
            double newVal = (double)e.NewValue;

            if (control._parentScrollViewer == null || newVal <= 0) return;

            // 如果还没有稳定中心点（比如刚加载），先初始化一个
            if (control._lastStableCenterInHours < 0)
            {
                control.UpdateStableCenter();
            }

            // 使用锁定的中心点计算新的偏移量
            double viewportWidth = control._parentScrollViewer.ViewportWidth;
            double newOffset = control._lastStableCenterInHours * newVal - control._stableCenterViewportRelativeX;

            // 标记当前为内部滚动，防止 OnParentScrollChanged 修改我们的锚点
            control._isInternalScrolling = true;
            control._parentScrollViewer.ScrollToHorizontalOffset(Math.Max(0, newOffset));

            // 在布局刷新后解除锁定
            control.Dispatcher.BeginInvoke(new Action(() =>
            {
                control._isInternalScrolling = false;
            }), System.Windows.Threading.DispatcherPriority.DataBind);
        }

        public static readonly DependencyProperty DisplayModeProperty =
            DependencyProperty.Register("DisplayMode", typeof(GanttDisplayMode), typeof(GanttChart),
                new FrameworkPropertyMetadata(GanttDisplayMode.Full, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

        public GanttDisplayMode DisplayMode
        {
            get => (GanttDisplayMode)GetValue(DisplayModeProperty);
            set => SetValue(DisplayModeProperty, value);
        }

        public static readonly DependencyProperty SegmentClickedCommandProperty =
            DependencyProperty.Register("SegmentClickedCommand", typeof(System.Windows.Input.ICommand), typeof(GanttChart), new PropertyMetadata(null));

        public System.Windows.Input.ICommand SegmentClickedCommand
        {
            get => (System.Windows.Input.ICommand)GetValue(SegmentClickedCommandProperty);
            set => SetValue(SegmentClickedCommandProperty, value);
        }

        public static readonly DependencyProperty ZoomModeProperty =
            DependencyProperty.Register("ZoomMode", typeof(string), typeof(GanttChart),
                new PropertyMetadata("Center", OnZoomModeChanged));

        public static readonly DependencyProperty ShowRowBadgesProperty =
            DependencyProperty.Register("ShowRowBadges", typeof(bool), typeof(GanttChart),
                new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

        public bool ShowRowBadges
        {
            get => (bool)GetValue(ShowRowBadgesProperty);
            set => SetValue(ShowRowBadgesProperty, value);
        }

        private static void OnZoomModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is GanttChart control)
            {
                control.UpdateStableCenter();
            }
        }

        public string ZoomMode
        {
            get => (string)GetValue(ZoomModeProperty);
            set => SetValue(ZoomModeProperty, value);
        }

        public static readonly DependencyProperty RequestFocusLatestProperty =
            DependencyProperty.Register("RequestFocusLatest", typeof(bool), typeof(GanttChart),
                new PropertyMetadata(false, OnRequestFocusLatestChanged));

        public bool RequestFocusLatest
        {
            get => (bool)GetValue(RequestFocusLatestProperty);
            set => SetValue(RequestFocusLatestProperty, value);
        }

        private static void OnRequestFocusLatestChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is GanttChart control && (bool)e.NewValue)
            {
                control.FocusLatest();
            }
        }

        public void FocusLatest()
        {
            if (_parentScrollViewer == null || ItemsSource == null || !ItemsSource.Any()) return;

            // 获取最新记录的时间点（小时）
            DateTime baseTime = ItemsSource.First().Start.Date;
            DateTime latestEnd = ItemsSource.Max(s => s.End);
            double latestTimeInHours = (latestEnd - baseTime).TotalHours;
            double latestX = latestTimeInHours * PixelsPerHour;

            double viewportWidth = _parentScrollViewer.ViewportWidth;
            double targetOffset = latestX - viewportWidth / 2;

            _isInternalScrolling = true;
            _parentScrollViewer.ScrollToHorizontalOffset(Math.Max(0, targetOffset));

            // 聚焦后立即更新稳定中心点，防止用户接下来缩放时跳回原来的锚点
            UpdateStableCenter();

            Dispatcher.BeginInvoke(new Action(() =>
            {
                _isInternalScrolling = false;
            }), System.Windows.Threading.DispatcherPriority.DataBind);
        }

        private System.Windows.Controls.ScrollViewer? _parentScrollViewer;
        private double _lastStableCenterInHours = -1;
        private bool _isInternalScrolling = false;

        public GanttChart()
        {
            _children = new VisualCollection(this);
            _mainVisual = new DrawingVisual();
            _children.Add(_mainVisual);

            _internalToolTip = new System.Windows.Controls.ToolTip();
            // RelativePoint + PlacementTarget：PlacementMode.Mouse 的 ToolTip 打开后
            // 位置固定，无法在保持打开时跟随鼠标更新，导致详细信息与色块错位
            _internalToolTip.Placement = System.Windows.Controls.Primitives.PlacementMode.RelativePoint;
            _internalToolTip.PlacementTarget = this;
            _internalToolTip.Background = Brushes.Transparent;
            _internalToolTip.BorderThickness = new Thickness(0);
            _internalToolTip.Padding = new Thickness(0);

            this.Loaded += GanttChart_Loaded;
        }

        private void GanttChart_Loaded(object sender, RoutedEventArgs e)
        {
            DependencyObject parent = VisualTreeHelper.GetParent(this);
            while (parent != null && !(parent is System.Windows.Controls.ScrollViewer))
            {
                parent = VisualTreeHelper.GetParent(parent);
            }
            _parentScrollViewer = parent as System.Windows.Controls.ScrollViewer;
            if (_parentScrollViewer != null)
            {
                _parentScrollViewer.ScrollChanged += OnParentScrollChanged;
            }
        }

        private void OnParentScrollChanged(object sender, System.Windows.Controls.ScrollChangedEventArgs e)
        {
            // 只有当不是因为我们内部缩放导致的滚动时，才更新“稳定中心点”
            // 这样在连续拖动滑块缩放时，中心点会锁定在缩放开始时的那个位置
            if (!_isInternalScrolling && e.HorizontalChange != 0)
            {
                UpdateStableCenter();
            }

            if (DisplayMode != GanttDisplayMode.Header)
            {
                this.InvalidateVisual();
            }
        }

        private void UpdateStableCenter()
        {
            if (_parentScrollViewer == null || PixelsPerHour <= 0) return;

            if (ZoomMode == "Latest" && ItemsSource != null && ItemsSource.Any())
            {
                // 锚定到最新的一条线段的右端点
                DateTime baseTime = ItemsSource.First().Start.Date;
                DateTime latestEnd = ItemsSource.Max(s => s.End);
                
                double latestTimeInHours = (latestEnd - baseTime).TotalHours;
                double latestX = latestTimeInHours * PixelsPerHour;
                
                // 记录该点相对于视口左侧的像素偏移
                double relativeX = latestX - _parentScrollViewer.HorizontalOffset;
                
                // 将相对位置存入 _lastStableCenterInHours (这里复用变量名，但含义变为 [逻辑时间, 像素相对偏移])
                // 为了简单起见，我们用两个变量分开存储，或者这里做一个约定
                _lastStableCenterInHours = latestTimeInHours;
                _stableCenterViewportRelativeX = relativeX;
            }
            else
            {
                // 默认：锚定到视口中心
                _lastStableCenterInHours = (_parentScrollViewer.HorizontalOffset + _parentScrollViewer.ViewportWidth / 2) / PixelsPerHour;
                _stableCenterViewportRelativeX = _parentScrollViewer.ViewportWidth / 2;
            }
        }

        private double _stableCenterViewportRelativeX = 0;

        private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (GanttChart)d;
            control.InvalidateMeasure();
            control.InvalidateVisual();
        }

        private readonly Brush[] _palette = {
            new SolidColorBrush(Color.FromRgb(255, 121, 198)), // Pink
            new SolidColorBrush(Color.FromRgb(139, 233, 253)), // Cyan
            new SolidColorBrush(Color.FromRgb(80, 250, 123)),  // Green
            new SolidColorBrush(Color.FromRgb(255, 184, 108)), // Orange
            new SolidColorBrush(Color.FromRgb(189, 147, 249)), // Purple
            new SolidColorBrush(Color.FromRgb(255, 85, 85)),   // Red
            new SolidColorBrush(Color.FromRgb(241, 250, 140))  // Yellow
        };

        private double _rowHeaderWidth = 160;

        protected override int VisualChildrenCount => _children.Count;
        protected override Visual GetVisualChild(int index) => _children[index];

        protected override Size MeasureOverride(Size availableSize)
        {
            if (ItemsSource == null || !ItemsSource.Any())
            {
                return new Size(DisplayMode == GanttDisplayMode.Header ? _rowHeaderWidth : 24 * PixelsPerHour, 100);
            }

            double rowHeight = 40;
            double rowSpacing = 10;
            int maxRow = ItemsSource.Max(s => s.RowIndex);
            double bottomPad = (DisplayMode == GanttDisplayMode.Header || DisplayMode == GanttDisplayMode.Timeline) ? 20 : 60;
            double height = (maxRow + 1) * (rowHeight + rowSpacing) + bottomPad;

            double width = DisplayMode switch
            {
                GanttDisplayMode.Header => _rowHeaderWidth,
                GanttDisplayMode.Timeline => 24 * PixelsPerHour + 50,
                GanttDisplayMode.TimeHeader => 24 * PixelsPerHour + 50,
                _ => 24 * PixelsPerHour + _rowHeaderWidth + 50
            };

            if (DisplayMode == GanttDisplayMode.TimeHeader)
                return new Size(width, 40);
            
            return new Size(width, height);
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, RenderSize.Width, RenderSize.Height));
            base.OnRender(drawingContext);
            RenderChart();
        }

        private void RenderChart()
        {
            using (var dc = _mainVisual.RenderOpen())
            {
                DateTime baseTime = ItemsSource != null && ItemsSource.Any() 
                    ? ItemsSource.First().Start.Date 
                    : DateTime.Today;

                double rowHeight = 40;
                double rowSpacing = 10;
                double topOffset = (DisplayMode == GanttDisplayMode.Header || DisplayMode == GanttDisplayMode.Timeline) ? 0 : 40;
                double xOffset = DisplayMode == GanttDisplayMode.Timeline ? 0 : _rowHeaderWidth;

                // 绘制背景与辅助线
                DrawGrid(dc, baseTime, rowHeight, rowSpacing, topOffset, xOffset);

                if (DisplayMode == GanttDisplayMode.TimeHeader) return;

                if (ItemsSource == null || !ItemsSource.Any()) return;

                // 只有在非 Header 模式下才绘制条带
                if (DisplayMode != GanttDisplayMode.Header)
                {
                    // 视图剔除 (虚拟化)
                    double visibleTop = _parentScrollViewer?.VerticalOffset ?? 0;
                    double visibleBottom = visibleTop + (_parentScrollViewer?.ViewportHeight ?? RenderSize.Height);

                    int startRow = Math.Max(0, (int)((visibleTop - topOffset) / (rowHeight + rowSpacing)));
                    int endRow = (int)((visibleBottom - topOffset) / (rowHeight + rowSpacing)) + 1;

                    foreach (var segment in ItemsSource)
                    {
                        if (segment.RowIndex < startRow || segment.RowIndex > endRow) continue;

                        DateTime start = segment.Start < baseTime ? baseTime : segment.Start;
                        DateTime end = segment.End > baseTime.AddDays(1) ? baseTime.AddDays(1) : segment.End;
                        if (start >= end) continue;

                        double xStart = xOffset + (start - baseTime).TotalHours * PixelsPerHour;
                        double xEnd = xOffset + (end - baseTime).TotalHours * PixelsPerHour;
                        double width = Math.Max(2, xEnd - xStart);
                        
                        // 三档粗细
                        double segmentHeight = segment.Status switch
                        {
                            ActivityStatus.ForegroundActive => 32,
                            ActivityStatus.ForegroundInactive => 20,
                            ActivityStatus.Background => 10,
                            _ => 20
                        };

                        double y = topOffset + segment.RowIndex * (rowHeight + rowSpacing) + (rowHeight - segmentHeight) / 2;

                        Brush brush = GetBrushForSegment(segment);
                        dc.DrawRoundedRectangle(brush, null, new Rect(xStart, y, width, segmentHeight), 4, 4);
                    }
                }
            }
        }

        private void DrawGrid(DrawingContext dc, DateTime baseTime, double rowHeight, double rowSpacing, double topOffset, double xOffset)
        {
            double height = RenderSize.Height;
            double width = RenderSize.Width;

            // 1. 绘制纵向时间线 (Full 或 TimeHeader 模式)
            if (DisplayMode == GanttDisplayMode.Full || DisplayMode == GanttDisplayMode.TimeHeader)
            {
                Pen timePen = new Pen(new SolidColorBrush(Color.FromArgb(30, 128, 128, 128)), 1);
                timePen.DashStyle = DashStyles.Dash;

                Pen tickPen = new Pen(new SolidColorBrush(Color.FromArgb(80, 128, 128, 128)), 1); // 刻度短实线
                Brush labelBrush = (Brush)FindResource("TextFillColorPrimaryBrush") ?? Brushes.White;

                // 动态计算时间步长 (单位：小时)
                double minPixelInterval = 100.0;
                double[] candidateSteps = new double[] {
                    1.0 / 60.0,   // 1分钟
                    2.0 / 60.0,   // 2分钟
                    5.0 / 60.0,   // 5分钟
                    10.0 / 60.0,  // 10分钟
                    15.0 / 60.0,  // 15分钟
                    30.0 / 60.0,  // 30分钟
                    1.0,          // 1小时
                    2.0,          // 2小时
                    3.0,          // 3小时
                    4.0,          // 4小时
                    6.0,          // 6小时
                    12.0          // 12小时
                };

                double step = 1.0;
                foreach (var s in candidateSteps)
                {
                    if (s * PixelsPerHour >= minPixelInterval)
                    {
                        step = s;
                        break;
                    }
                }

                for (double h = 0; h <= 24.0 + 1e-9; h += step)
                {
                    double x = xOffset + h * PixelsPerHour;
                    
                    int totalMinutes = (int)Math.Round(h * 60.0);
                    int hours = totalMinutes / 60;
                    int minutes = totalMinutes % 60;

                    // 判断是否绘制全屏虚线：
                    // 如果 step >= 1.0，则全部绘制全屏虚线。
                    // 如果 step < 1.0 且 minutes == 0（即整点小时），绘制全屏虚线。
                    // 其他情况下只绘制顶部短刻度线。
                    bool drawFullLine = (step >= 1.0) || (minutes == 0);

                    if (drawFullLine)
                    {
                        dc.DrawLine(timePen, new Point(x, topOffset - 10), new Point(x, height));
                    }
                    else
                    {
                        // 绘制顶部短刻度指示线
                        dc.DrawLine(tickPen, new Point(x, topOffset - 10), new Point(x, topOffset - 4));
                    }

                    // 格式化时间字符串
                    string timeStr = step >= 1.0 ? $"{hours}:00" : $"{hours:D2}:{minutes:D2}";

                    var text = new FormattedText(
                        timeStr,
                        CultureInfo.CurrentUICulture,
                        FlowDirection.LeftToRight,
                        new Typeface("Segoe UI"),
                        11,
                        labelBrush,
                        VisualTreeHelper.GetDpi(this).PixelsPerDip);
                    
                    dc.DrawText(text, new Point(x - text.Width / 2, topOffset - 30));
                }
            }

            // 2. 绘制横向行线与应用名称
            if (DisplayMode != GanttDisplayMode.TimeHeader && ItemsSource != null && ItemsSource.Any())
            {
                Pen rowPen = new Pen(new SolidColorBrush(Color.FromArgb(15, 128, 128, 128)), 1);
                var rowGroups = ItemsSource.GroupBy(s => s.RowIndex).Select(g => g.First()).OrderBy(s => s.RowIndex);

                foreach (var group in rowGroups)
                {
                    double y = topOffset + group.RowIndex * (rowHeight + rowSpacing);
                    
                    // 行底线
                    dc.DrawLine(rowPen, new Point(0, y + rowHeight + rowSpacing / 2), new Point(width, y + rowHeight + rowSpacing / 2));

                    // 应用名称 (仅在 Header 模式)
                    if (DisplayMode != GanttDisplayMode.Timeline)
                    {
                        double badgeLabelX = 4;
                        double badgeWidth = 120;
                        double badgePad = 8;
                        double iconSize = 20;
                        double iconPadding = 6;
                        double iconX = badgeLabelX + badgePad;
                        double textX = iconX;

                        string label = group.DisplayName;
                        if (group.Icon != null)
                            textX += iconSize + iconPadding;

                        double maxTextWidth = (badgeLabelX + badgeWidth) - textX - badgePad;

                        var text = new FormattedText(
                            label,
                            CultureInfo.CurrentUICulture,
                            FlowDirection.LeftToRight,
                            new Typeface("Segoe UI SemiBold"),
                            12,
                            (Brush)FindResource("TextFillColorPrimaryBrush") ?? Brushes.White,
                            VisualTreeHelper.GetDpi(this).PixelsPerDip);
                        text.MaxTextWidth = maxTextWidth;
                        text.MaxTextHeight = rowHeight - 2;
                        text.Trimming = TextTrimming.CharacterEllipsis;

                        // 1. 背景矩形（先画，在下层）
                        if (ShowRowBadges)
                        {
                            Color badgeColor = group.SegmentType == "Web"
                                ? Color.FromArgb(45, 244, 114, 182)
                                : Color.FromArgb(45, 14, 165, 233);
                            Brush badgeBrush = new SolidColorBrush(badgeColor);
                            dc.DrawRoundedRectangle(badgeBrush, null, new Rect(badgeLabelX, y + 1, badgeWidth, rowHeight - 2), 6, 6);
                        }

                        // 2. 图标（在背景上方）
                        if (group.Icon != null)
                            dc.DrawImage(group.Icon, new Rect(iconX, y + (rowHeight - iconSize) / 2, iconSize, iconSize));

                        // 3. 文字（最上层）
                        dc.DrawText(text, new Point(textX, y + (rowHeight - text.Height) / 2));
                    }
                }
            }
        }

        protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (ItemsSource == null || !ItemsSource.Any() || DisplayMode == GanttDisplayMode.Header || DisplayMode == GanttDisplayMode.TimeHeader) return;

            Point pos = e.GetPosition(this);
            DateTime baseTime = ItemsSource.First().Start.Date;

            double rowHeight = 40;
            double rowSpacing = 10;
            double topOffset = 40;
            double xOffset = DisplayMode == GanttDisplayMode.Timeline ? 0 : _rowHeaderWidth;

            int rowIndex = (int)((pos.Y - topOffset) / (rowHeight + rowSpacing));
            
            GanttSegment hit = null;
            if (rowIndex >= 0 && pos.X >= xOffset)
            {
                double timeInHours = (pos.X - xOffset) / PixelsPerHour;
                DateTime timeAtMouse = baseTime.AddHours(timeInHours);
                hit = ItemsSource.FirstOrDefault(s => s.RowIndex == rowIndex && s.Start <= timeAtMouse && s.End >= timeAtMouse);
            }

            if (hit != null)
            {
                string tooltipContent;
                if (hit.SegmentType == "Web")
                {
                    tooltipContent = $"【网页浏览】\n" +
                                     $"站点: {hit.Domain}\n" +
                                     $"标题: {hit.WindowTitle}\n" +
                                     $"时间: {hit.Start:HH:mm:ss} - {hit.End:HH:mm:ss}\n" +
                                     $"持续: {hit.Duration:hh\\:mm\\:ss}\n" +
                                     (string.IsNullOrEmpty(hit.Browser) ? "" : $"浏览器: {hit.Browser}\n") +
                                     $"网址: {hit.Url}";
                }
                else
                {
                    string statusText = hit.Status switch
                    {
                        ActivityStatus.ForegroundActive => "前台活动",
                        ActivityStatus.ForegroundInactive => "前台非活动",
                        ActivityStatus.Background => "后台运行",
                        _ => hit.Status.ToString()
                    };

                    tooltipContent = $"【应用详情】\n" +
                                     $"名称: {hit.DisplayName}\n" +
                                     $"标题: {hit.WindowTitle}\n" +
                                     $"时间: {hit.Start:HH:mm:ss} - {hit.End:HH:mm:ss}\n" +
                                     $"持续: {hit.Duration:hh\\:mm\\:ss}\n" +
                                     $"状态: {statusText}";
                }

                // 使用单独的 ToolTip 控件以确保跟随鼠标且实时更新
                if (_internalToolTip.Parent == null)
                {
                    this.ToolTip = _internalToolTip;
                }
                
                _internalToolTip.Content = tooltipContent;
                _internalToolTip.IsOpen = true;

                // 每次移动都按当前鼠标位置重设偏移，使打开中的 ToolTip 跟随鼠标；
                // 靠近右/下边缘时翻转到鼠标另一侧，避免被截断。
                // 首次打开时 ActualWidth/Height 尚为 0，用估算值兜底
                double tipW = _internalToolTip.ActualWidth > 0 ? _internalToolTip.ActualWidth : 340;
                double tipH = _internalToolTip.ActualHeight > 0 ? _internalToolTip.ActualHeight : 150;
                double x = pos.X + 16;
                double y = pos.Y + 18;
                if (x + tipW > ActualWidth) x = pos.X - tipW - 12;
                if (y + tipH > ActualHeight) y = pos.Y - tipH - 12;
                _internalToolTip.HorizontalOffset = x;
                _internalToolTip.VerticalOffset = y;
            }
            else
            {
                if (_internalToolTip != null)
                {
                    _internalToolTip.IsOpen = false;
                }
            }
        }

        protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            if (_internalToolTip != null)
            {
                _internalToolTip.IsOpen = false;
            }
        }

        protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (ItemsSource == null || !ItemsSource.Any() || DisplayMode == GanttDisplayMode.Header || DisplayMode == GanttDisplayMode.TimeHeader) return;

            Point pos = e.GetPosition(this);
            DateTime baseTime = ItemsSource.First().Start.Date;

            double rowHeight = 40;
            double rowSpacing = 10;
            double topOffset = 40;
            double xOffset = DisplayMode == GanttDisplayMode.Timeline ? 0 : _rowHeaderWidth;

            int rowIndex = (int)((pos.Y - topOffset) / (rowHeight + rowSpacing));
            if (rowIndex < 0 || pos.X < xOffset) return;

            double timeInHours = (pos.X - xOffset) / PixelsPerHour;
            DateTime timeAtMouse = baseTime.AddHours(timeInHours);

            var hit = ItemsSource.FirstOrDefault(s => s.RowIndex == rowIndex && s.Start <= timeAtMouse && s.End >= timeAtMouse);

            if (hit != null)
            {
                // TODO: 占位操作 - 查看该应用的使用统计
                System.Diagnostics.Debug.WriteLine($"点击了片段: {hit.DisplayName}, 将跳转到应用统计详情...");
                
                if (SegmentClickedCommand != null && SegmentClickedCommand.CanExecute(hit))
                {
                    SegmentClickedCommand.Execute(hit);
                }
            }
        }

        private Brush GetBrushForSegment(GanttSegment segment)
        {
            var solidBrush = _palette[segment.RowIndex % _palette.Length] as SolidColorBrush;
            var baseColor = solidBrush?.Color ?? Colors.Gray;

            if (segment.Status == ActivityStatus.ForegroundInactive)
            {
                return new SolidColorBrush(Color.FromArgb(100, baseColor.R, baseColor.G, baseColor.B)); 
            }
            else if (segment.Status == ActivityStatus.Background)
            {
                return new SolidColorBrush(Color.FromRgb(240, 240, 240)); 
            }

            return new SolidColorBrush(baseColor);
        }
    }
}
