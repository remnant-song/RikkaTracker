using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RikkaTracker.Core.Librarys
{
    public static class GlobeIcon
    {
        private static readonly ImageSource _source;

        static GlobeIcon()
        {
            try
            {
                var typeface = new Typeface(
                    new FontFamily(new Uri("pack://application:,,,/Assets/SegoeFluentIcons.ttf"), "Segoe Fluent Icons"),
                    FontStyles.Normal,
                    FontWeights.Normal,
                    FontStretches.Normal);

                if (!typeface.TryGetGlyphTypeface(out var _))
                    return;

                var glyph = new FormattedText(
                    "\uE774",
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    14,
                    Brushes.Gray,
                    VisualTreeHelper.GetDpi(new DrawingVisual()).PixelsPerDip);

                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    dc.DrawText(glyph, new Point(3, 3));
                }

                var bitmap = new RenderTargetBitmap(20, 20, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(visual);
                bitmap.Freeze();
                _source = bitmap;
            }
            catch
            {
                // fallback: empty image
                _source = new RenderTargetBitmap(20, 20, 96, 96, PixelFormats.Pbgra32);
                _source.Freeze();
            }
        }

        public static ImageSource Source => _source;
    }
}
