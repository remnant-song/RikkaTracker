/*
 * @Author: trae + deepseek-v4-pro
 * @Date:   2026-06-30
 * @Desc:   Windows 系统版本检测工具类。
 *          集中管理版本判断逻辑，避免多窗口重复定义 IsWindows11OrNewer。
 */
using System;

namespace RikkaTracker.Helpers
{
    /// <summary>
    /// 平台相关辅助方法，统一管理 Windows 版本检测等逻辑。
    /// </summary>
    public static class PlatformHelper
    {
        /// <summary>
        /// Win11 最低 Build 号为 22000。Win10 最高 Build 为 19045。
        /// 注意：Windows Server 2025 的 Build 也 >= 22000，这里不做区分。
        /// </summary>
        public static bool IsWindows11OrNewer =>
            Environment.OSVersion.Version.Build >= 22000;
    }
}
