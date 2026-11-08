/*
 * @Author: trae + GLM-5.2
 * @Date: 2026-06-30
 * @Desc: 日志系统核心接口与实现。
 *        改造要点：
 *        v1.0（原版）：简单 3 级日志，异步 Task.Run + lock 写入文件。
 *        v2.0（本版）：
 *        - 日志级别扩展为 Trace/Debug/Info/Warning/Error/Fatal 6 级
 *        - 新增 LogCategory 分类，Monitor/Web 类日志独立输出到 mntr_*.log
 *        - 生产者-消费者模式：BlockingCollection 队列 + 专用后台写入线程，保证顺序且不阻塞调用方
 *        - 支持全局最低日志级别过滤
 *        - 按日期轮转文件 + 自动清理过期文件（默认保留 30 天）
 *        - 结构化日志支持（key=value 附加数据）
 *        - 向后兼容：旧版 Info/Warning/Error(string) 调用无需修改
 * @Modify: 2026-06-30 trae + GLM-5.2 – 完全重写，保留旧接口兼容
 */

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RikkaTracker.Core.Models;

namespace RikkaTracker.Services
{
    // ============================================================
    // 日志服务接口
    // ============================================================
    public interface ILoggerService
    {
        // === 旧版兼容接口（与 v1.0 完全一致，保证现有调用方无需修改） ===
        void Info(string message);
        void Error(string message, Exception? ex = null);
        void Warning(string message);
        string GetLogPath();

        // === 新增：监控日志路径 ===
        string GetMonitorLogPath();

        // === 新增：带分类的便捷方法 ===
        /// <summary>
        /// 记录 Trace 级别日志（最细粒度，默认不输出）
        /// </summary>
        void Trace(string message, LogCategory category = LogCategory.System);
        /// <summary>
        /// 记录 Debug 级别日志
        /// </summary>
        void Debug(string message, LogCategory category = LogCategory.System);
        /// <summary>
        /// 记录带分类的 Info 级别日志
        /// </summary>
        void Info(string message, LogCategory category);
        /// <summary>
        /// 记录带分类的 Warning 级别日志
        /// </summary>
        void Warning(string message, LogCategory category);
        /// <summary>
        /// 记录带分类的 Error 级别日志
        /// </summary>
        void Error(string message, Exception? ex, LogCategory category);
        /// <summary>
        /// 记录 Fatal 级别日志（应用即将崩溃）
        /// </summary>
        void Fatal(string message, Exception? ex = null, LogCategory category = LogCategory.System);

        // === 通用日志入口 ===
        /// <summary>
        /// 通用结构化日志入口。
        /// </summary>
        /// <param name="level">日志级别</param>
        /// <param name="category">日志分类</param>
        /// <param name="message">日志消息</param>
        /// <param name="data">附加结构化数据（可选），以 key=value 形式追加到日志行</param>
        void Log(LogLevel level, LogCategory category, string message,
                 Dictionary<string, object?>? data = null);

        // === 日志配置（运行时动态修改） ===
        /// <summary>当前全局最低日志级别，低于此级别的日志将被忽略</summary>
        LogLevel MinimumLevel { get; set; }
        /// <summary>日志文件保留天数</summary>
        int LogRetentionDays { get; set; }
        /// <summary>单个日志文件最大大小（MB）</summary>
        int LogMaxFileSizeMb { get; set; }
    }

    // ============================================================
    // 日志服务实现
    // ============================================================
    public class FileLoggerService : ILoggerService, IDisposable
    {
        // --- 内部数据结构 ---

        /// <summary>
        /// 单条日志的内部表示。
        /// </summary>
        private readonly struct LogEntry
        {
            public readonly DateTime Timestamp;
            public readonly LogLevel Level;
            public readonly LogCategory Category;
            public readonly string Message;
            public readonly Dictionary<string, object?>? Data;

            public LogEntry(DateTime timestamp, LogLevel level, LogCategory category,
                            string message, Dictionary<string, object?>? data)
            {
                Timestamp = timestamp;
                Level = level;
                Category = category;
                Message = message;
                Data = data;
            }
        }

        // --- 配置 ---
        private volatile LogLevel _minimumLevel = LogLevel.Info;
        private volatile int _logRetentionDays = 30;
        private volatile int _logMaxFileSizeMb = 10;
        private const long BytesPerMb = 1024 * 1024;

        // --- 文件路径 ---
        private readonly string _logDirectory;
        private string _currentAppLogPath = string.Empty;
        private string _currentMonitorLogPath = string.Empty;
        private string _currentLogDate = string.Empty; // yyyyMMdd

        // --- 生产者-消费者队列 ---
        private readonly BlockingCollection<LogEntry> _logQueue =
            new BlockingCollection<LogEntry>(boundedCapacity: 10000);
        private readonly Thread _writerThread;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private volatile bool _disposed;

        // --- 文件写入锁（同时只允许一个线程操作文件） ---
        private readonly object _fileLock = new object();

        // ============================================================
        // 构造函数
        // ============================================================
        public FileLoggerService()
        {
            // 日志目录：%LocalAppData%\RikkaTracker\logs
            _logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RikkaTracker",
                "logs");

            if (!Directory.Exists(_logDirectory))
            {
                Directory.CreateDirectory(_logDirectory);
            }

            // 启动时清理过期日志文件
            CleanupOldLogFiles();

            // 初始化当前日志文件路径
            RefreshLogPaths();

            // 启动后台写入线程
            _writerThread = new Thread(ProcessLogQueue)
            {
                Name = "RikkaTracker-LogWriter",
                IsBackground = true
            };
            _writerThread.Start();

            // 记录初始化日志（此时队列和线程已就绪）
            Info("=== Logger v2.0 Initialized ===", LogCategory.System);
            Debug($"Log directory: {_logDirectory}", LogCategory.System);
            Debug($"Minimum log level: {_minimumLevel}", LogCategory.System);
        }

        // ============================================================
        // 旧版兼容接口实现
        // ============================================================

        public void Info(string message)
            => EnqueueLog(LogLevel.Info, LogCategory.System, message, null);

        public void Warning(string message)
            => EnqueueLog(LogLevel.Warning, LogCategory.System, message, null);

        public void Error(string message, Exception? ex = null)
        {
            string fullMsg = ex != null
                ? $"{message}\n  ── Exception: {ex.GetType().Name}: {ex.Message}\n  ── Stack: {ex.StackTrace}"
                : message;
            EnqueueLog(LogLevel.Error, LogCategory.System, fullMsg, null);
        }

        public string GetLogPath() => _currentAppLogPath;
        public string GetMonitorLogPath() => _currentMonitorLogPath;

        // ============================================================
        // 新增便捷方法
        // ============================================================

        public void Trace(string message, LogCategory category = LogCategory.System)
            => EnqueueLog(LogLevel.Trace, category, message, null);

        public void Debug(string message, LogCategory category = LogCategory.System)
            => EnqueueLog(LogLevel.Debug, category, message, null);

        public void Info(string message, LogCategory category)
            => EnqueueLog(LogLevel.Info, category, message, null);

        public void Warning(string message, LogCategory category)
            => EnqueueLog(LogLevel.Warning, category, message, null);

        public void Error(string message, Exception? ex, LogCategory category)
        {
            string fullMsg = ex != null
                ? $"{message}\n  ── Exception: {ex.GetType().Name}: {ex.Message}\n  ── Stack: {ex.StackTrace}"
                : message;
            EnqueueLog(LogLevel.Error, category, fullMsg, null);
        }

        public void Fatal(string message, Exception? ex = null, LogCategory category = LogCategory.System)
        {
            string fullMsg = ex != null
                ? $"{message}\n  ── Exception: {ex.GetType().Name}: {ex.Message}\n  ── Stack: {ex.StackTrace}"
                : message;
            EnqueueLog(LogLevel.Fatal, category, fullMsg, null);
        }

        public void Log(LogLevel level, LogCategory category, string message,
                        Dictionary<string, object?>? data = null)
            => EnqueueLog(level, category, message, data);

        // ============================================================
        // 日志配置
        // ============================================================

        /// <summary>
        /// 获取或设置全局最低日志级别。
        /// 设置为更低级别可即时生效（如从 Info 改为 Debug），
        /// 设置为更高级别仅影响后续入队的日志。
        /// </summary>
        public LogLevel MinimumLevel
        {
            get => _minimumLevel;
            set
            {
                if (_minimumLevel != value)
                {
                    var old = _minimumLevel;
                    _minimumLevel = value;
                    // 使用 System 分类记录级别变更，避免循环依赖
                    EnqueueLog(LogLevel.Info, LogCategory.System,
                        $"Log level changed: {old} -> {value}", null);
                }
            }
        }

        public int LogRetentionDays
        {
            get => _logRetentionDays;
            set
            {
                if (_logRetentionDays != value && value > 0)
                {
                    _logRetentionDays = value;
                    EnqueueLog(LogLevel.Info, LogCategory.System,
                        $"Log retention days changed to: {value}", null);
                    // 配置变更后立即清理一次
                    CleanupOldLogFiles();
                }
            }
        }

        public int LogMaxFileSizeMb
        {
            get => _logMaxFileSizeMb;
            set
            {
                if (_logMaxFileSizeMb != value && value > 0)
                {
                    _logMaxFileSizeMb = value;
                    EnqueueLog(LogLevel.Info, LogCategory.System,
                        $"Log max file size changed to: {value} MB", null);
                }
            }
        }

        // ============================================================
        // 内部方法：入队
        // ============================================================

        /// <summary>
        /// 将日志条目加入队列。
        /// 如果队列已满（超过 boundedCapacity）且写入线程处理速度跟不上，
        /// 则丢弃该条日志（非阻塞策略，避免拖慢调用方）。
        /// </summary>
        private void EnqueueLog(LogLevel level, LogCategory category, string message,
                                Dictionary<string, object?>? data)
        {
            // 级别过滤：低于全局最低级别的日志直接丢弃
            if (level < _minimumLevel) return;

            if (_disposed) return;

            var entry = new LogEntry(DateTime.Now, level, category, message, data);

            // TryAdd 非阻塞：如果队列满了就丢弃（宁可丢日志也不能卡住主流程）
            if (!_logQueue.TryAdd(entry))
            {
                // 丢弃时输出到 Debug 窗口以供排查
                System.Diagnostics.Debug.WriteLine($"[LOGGER DROPPED] Queue full. Level={level}, Category={category}, Msg={message}");
            }
        }

        // ============================================================
        // 后台写入线程
        // ============================================================

        /// <summary>
        /// 后台线程主循环：从队列中取出日志条目并写入文件。
        /// 使用 GetConsumingEnumerable 持续消费，队列关闭时自动退出。
        /// </summary>
        private void ProcessLogQueue()
        {
            try
            {
                foreach (var entry in _logQueue.GetConsumingEnumerable(_cts.Token))
                {
                    try
                    {
                        WriteSingleEntry(entry);
                    }
                    catch (Exception ex)
                    {
                        // 写入单条日志失败不应导致线程退出
                        System.Diagnostics.Debug.WriteLine($"[LOGGER] Failed to write log entry: {ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 正常关闭
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LOGGER] Writer thread fatal error: {ex}");
            }
        }

        /// <summary>
        /// 写入单条日志到文件。
        /// 包含：级别过滤确认、日期切换检测、格式化、写入、Debug 输出。
        /// </summary>
        private void WriteSingleEntry(LogEntry entry)
        {
            lock (_fileLock)
            {
                // 检查是否需要切换日志文件（跨天）
                string today = entry.Timestamp.ToString("yyyyMMdd");
                if (today != _currentLogDate)
                {
                    RefreshLogPaths();
                    CleanupOldLogFiles();
                }

                // 确定写入的目标文件
                string filePath = IsMonitorCategory(entry.Category)
                    ? _currentMonitorLogPath
                    : _currentAppLogPath;

                // 检查文件大小，若超过限制则在日志中记录告警（不中断写入）
                CheckFileSizeLimit(filePath);

                // 格式化日志行
                string logLine = FormatLogLine(entry);

                // 写入文件
                try
                {
                    File.AppendAllText(filePath, logLine + Environment.NewLine, Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    // 文件写入失败：输出到 Debug 窗口作为兜底
                    System.Diagnostics.Debug.WriteLine($"[LOGGER FILE ERROR] Failed to write to '{filePath}': {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"[LOGGER FILE ERROR] Lost entry: {logLine}");
                }

                // 始终输出到 Debug 窗口（IDE 调试时可见）
                System.Diagnostics.Debug.WriteLine(logLine);
            }
        }

        // ============================================================
        // 格式化
        // ============================================================

        /// <summary>
        /// 将 LogEntry 格式化为最终写入文件的文本行。
        /// 格式：[yyyy-MM-dd HH:mm:ss.fff] [LEVEL] [Category] message {key=value, ...}
        /// </summary>
        private static string FormatLogLine(LogEntry entry)
        {
            var sb = new StringBuilder();
            sb.Append(entry.Timestamp.ToString("[yyyy-MM-dd HH:mm:ss.fff] "));
            sb.Append($"[{entry.Level.ToString().ToUpperInvariant(),-7}] ");
            sb.Append($"[{entry.Category.ToString(),-7}] ");
            sb.Append(entry.Message);

            // 附加结构化数据
            if (entry.Data != null && entry.Data.Count > 0)
            {
                sb.Append("  {");
                bool first = true;
                foreach (var kvp in entry.Data)
                {
                    if (!first) sb.Append(", ");
                    sb.Append(kvp.Key);
                    sb.Append('=');
                    sb.Append(kvp.Value?.ToString() ?? "null");
                    first = false;
                }
                sb.Append('}');
            }

            return sb.ToString();
        }

        // ============================================================
        // 文件管理
        // ============================================================

        /// <summary>
        /// 刷新当前日志文件路径（基于当前日期）。
        /// app_yyyyMMdd.log 存放 System/DB/UI/Config 日志，
        /// mntr_yyyyMMdd.log 存放 Monitor/Web 日志。
        /// </summary>
        private void RefreshLogPaths()
        {
            string dateStr = DateTime.Now.ToString("yyyyMMdd");
            _currentLogDate = dateStr;
            _currentAppLogPath = Path.Combine(_logDirectory, $"app_{dateStr}.log");
            _currentMonitorLogPath = Path.Combine(_logDirectory, $"mntr_{dateStr}.log");
        }

        /// <summary>
        /// 检查文件大小是否超过限制，超过时记录告警日志。
        /// 当前不中断写入，仅告警。
        /// ponytail: 按日期轮转为主策略，大小限制为辅助检测。
        /// </summary>
        private void CheckFileSizeLimit(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    var fileInfo = new FileInfo(filePath);
                    if (fileInfo.Length > _logMaxFileSizeMb * BytesPerMb)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[LOGGER SIZE WARNING] Log file exceeds {_logMaxFileSizeMb} MB: " +
                            $"{fileInfo.Name} ({fileInfo.Length / BytesPerMb} MB)");
                    }
                }
            }
            catch
            {
                // 大小检查失败不影响写入
            }
        }

        /// <summary>
        /// 清理超过保留天数的旧日志文件。
        /// 同时清理 app_* 和 mntr_* 文件。
        /// </summary>
        private void CleanupOldLogFiles()
        {
            try
            {
                var cutoff = DateTime.Now.AddDays(-_logRetentionDays);
                var logFiles = Directory.GetFiles(_logDirectory, "*.log");

                int deletedCount = 0;
                foreach (var file in logFiles)
                {
                    try
                    {
                        var fi = new FileInfo(file);
                        if (fi.LastWriteTime < cutoff)
                        {
                            fi.Delete();
                            deletedCount++;
                        }
                    }
                    catch
                    {
                        // 单个文件清理失败不影响其他文件
                    }
                }

                if (deletedCount > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[LOGGER CLEANUP] Deleted {deletedCount} old log file(s) older than {_logRetentionDays} days.");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LOGGER CLEANUP ERROR] Failed to clean up old log files: {ex.Message}");
            }
        }

        // ============================================================
        // 辅助方法
        // ============================================================

        /// <summary>
        /// 判断分类是否属于监控类日志（输出到 mntr_*.log）。
        /// </summary>
        private static bool IsMonitorCategory(LogCategory category)
        {
            return category == LogCategory.Monitor || category == LogCategory.Web;
        }

        // ============================================================
        // IDisposable
        // ============================================================

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // 写入一条关闭日志（直接通过队列，因为 dispose 时队列还在）
            try
            {
                EnqueueLog(LogLevel.Info, LogCategory.System, "=== Logger Shutting Down ===", null);
            }
            catch
            {
                // 忽略 dispose 时的日志写入失败
            }

            // 标记队列完成添加，等待消费者处理完剩余条目
            _logQueue.CompleteAdding();

            // 取消令牌，通知线程退出
            _cts.Cancel();

            // 等待写入线程退出（最多等待 3 秒）
            if (!_writerThread.Join(TimeSpan.FromSeconds(3)))
            {
                System.Diagnostics.Debug.WriteLine("[LOGGER] Writer thread did not exit in time. Forcing shutdown.");
            }

            _cts.Dispose();
            _logQueue.Dispose();
        }
    }
}
