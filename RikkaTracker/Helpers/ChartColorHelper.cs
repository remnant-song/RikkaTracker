/*
 * @Author: trae + deepseek-v4-pro
 * @Date:   2026-06-30
 * @Desc:   图表颜色工具类。
 *          从 DashboardViewModel / UsageStatisticsViewModel 中提取
 *          重复的颜色计算逻辑，集中管理 Windows 强调色读取、
 *          增亮、色相偏移、主题明暗判断等方法。
 *
 * @Note:   原两个 ViewModel 中 UpdateChartColors 内部使用 Dispose+重建模式
 *          管理 SolidColorPaint，调用方需自行管理 Paint 生命周期。
 */
using System;
using SkiaSharp;

namespace RikkaTracker.Helpers
{
    /// <summary>
    /// 图表颜色工具类。提供从 Windows 调色板读取强调色、
    /// 颜色变换以及明暗主题判断的静态方法。
    /// </summary>
    public static class ChartColorHelper
    {
        /// <summary>
        /// 读取 Windows 系统强调色并转为 SKColor。
        /// 若读取失败则回退到默认蓝色 (#0EA5E9)。
        /// </summary>
        public static SKColor GetAccentSkColor()
        {
            try
            {
                var accent = Wpf.Ui.Appearance.ApplicationAccentColorManager
                    .GetColorizationColor();
                return new SKColor(accent.R, accent.G, accent.B);
            }
            catch
            {
                // fallback blue – matches the app's default accent
                return new SKColor(14, 165, 233);
            }
        }

        /// <summary>
        /// 将 SKColor 朝向白色增亮，factor 0=不变，1=纯白。
        /// </summary>
        public static SKColor BrightenSkColor(SKColor c, float factor)
        {
            factor = Math.Clamp(factor, 0f, 1f);
            return new SKColor(
                (byte)(c.Red   + (255 - c.Red)   * factor),
                (byte)(c.Green + (255 - c.Green) * factor),
                (byte)(c.Blue  + (255 - c.Blue)  * factor));
        }

        /// <summary>
        /// 对 SKColor 做简单 RGB 色相偏移，产生与强调色有视觉区分的衍生色。
        /// 用于网页柱体颜色，使其与应用的强调色柱体区分开来。
        /// </summary>
        public static SKColor ShiftHueSkColor(SKColor c, float amount)
        {
            return new SKColor(
                (byte)Math.Clamp(c.Red   + c.Blue  * amount, 0, 255),
                (byte)Math.Clamp(c.Green - c.Red   * amount, 0, 255),
                (byte)Math.Clamp(c.Blue  - c.Green * amount, 0, 255));
        }

        /// <summary>
        /// 对 SKColor 做线性去饱和处理，factor 0=不变，1=完全灰度。
        /// 用于暗色模式下弱化网页柱体视觉强度。
        /// </summary>
        /// <param name="c">原始颜色</param>
        /// <param name="factor">去饱和系数，0~1</param>
        public static SKColor DesaturateSkColor(SKColor c, float factor)
        {
            factor = Math.Clamp(factor, 0f, 1f);
            // 使用 luminosity 法：基于人眼感知的加权灰度值
            float gray = c.Red * 0.299f
                       + c.Green * 0.587f
                       + c.Blue * 0.114f;
            return new SKColor(
                (byte)(c.Red   + (gray - c.Red)   * factor),
                (byte)(c.Green + (gray - c.Green) * factor),
                (byte)(c.Blue  + (gray - c.Blue)  * factor));
        }

        /// <summary>
        /// 判断当前主题是否实际为深色模式。
        /// 正确解析 "System" 模式下的实际 WPF-UI 主题状态。
        /// </summary>
        public static bool IsThemeEffectivelyDark(string theme)
        {
            if (theme.Equals("Dark", StringComparison.OrdinalIgnoreCase))
                return true;
            if (theme.Equals("Light", StringComparison.OrdinalIgnoreCase))
                return false;

            // "System" or unknown: check WPF-UI's current effective theme
            try
            {
                return Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme()
                    == Wpf.Ui.Appearance.ApplicationTheme.Dark;
            }
            catch
            {
                return false;
            }
        }
    }
}
