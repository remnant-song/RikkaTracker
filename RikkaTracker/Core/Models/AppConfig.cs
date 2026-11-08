using System;
using System.Collections.Generic;

namespace RikkaTracker.Models
{
    public class AppConfig
    {
        public int IdleTimeoutMinutes { get; set; } = 5;
        public bool StartWithWindows { get; set; } = false;
        public string DataStoragePath { get; set; } = string.Empty;
        public bool GitSyncEnabled { get; set; } = false;
        public string GitRepositoryPath { get; set; } = string.Empty;
        public string Theme { get; set; } = "System"; // "System", "Light", or "Dark"
        public string TimelineZoomMode { get; set; } = "Center"; // "Center" or "Latest"
        public string Language { get; set; } = "Auto"; // "Auto", "zh-CN", "en-US"
        public bool WebMonitorEnabled { get; set; } = true;
        public int WebSocketPort { get; set; } = 8910;
        public string DisplayMode { get; set; } = "Combined";
        public string TimelineSortMode { get; set; } = "Duration";
        public bool ShowRowBadges { get; set; } = true;

        /*
         * @Author: trae + GLM-5.2
         * @Date: 2026-06-30
         * @Desc: 日志系统配置字段。
         *        - LogLevel：全局最低日志级别，低于此级别的日志不会被记录，默认 Info
         *        - LogRetentionDays：日志文件保留天数，超过天数的文件将被清理，默认 30
         *        - LogMaxFileSizeMb：单个日志文件最大大小（MB），达到上限后当日文件追加写入
         *          （按日期轮转为主策略，大小限制为辅助）, 默认 10
         */
        /// <summary>全局最低日志级别。Trace=0, Debug=1, Info=2, Warning=3, Error=4, Fatal=5</summary>
        public int LogLevel { get; set; } = (int)RikkaTracker.Core.Models.LogLevel.Info;
        /// <summary>日志文件保留天数，默认 30 天</summary>
        public int LogRetentionDays { get; set; } = 30;
        /// <summary>单个日志文件最大大小（MB），默认 10 MB</summary>
        public int LogMaxFileSizeMb { get; set; } = 10;
        public List<FilterRule> FilterRules { get; set; } = new List<FilterRule>
        {
            // 默认免空闲检测（多媒体应用）
            new FilterRule { ProcessPattern = "vlc*", DisableIdleDetection = true },
            new FilterRule { ProcessPattern = "spotify*", DisableIdleDetection = true },
            new FilterRule { ProcessPattern = "wmplayer*", DisableIdleDetection = true },
            // 默认忽略（系统组件）
            new FilterRule { ProcessPattern = "ShellExperienceHost", Ignore = true }
        };
    }

    public class FilterRule
    {
        public string ProcessPattern { get; set; } = string.Empty;
        public bool Ignore { get; set; } = false;
        public bool DisableIdleDetection { get; set; } = false;
    }
}
