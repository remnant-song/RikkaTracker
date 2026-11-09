using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using RikkaTracker.Core.Models;
using RikkaTracker.Core.Monitor;
using RikkaTracker.Services;

namespace RikkaTracker.Core.Data
{
    public interface IActivityLogStore
    {
        void RecordTransition(string processName, string processPath, string windowTitle, ActivityStatus newStatus, DateTime timestamp, string alias = "");
        void CloseProcess(string processName, DateTime timestamp);
    }

    public class SqliteLogStore : IActivityLogStore, IDisposable
    {
        private readonly SqliteDbContext _dbContext;
        private readonly ILoggerService _logger;
        private readonly ConcurrentQueue<ActivitySegment> _writeQueue = new();
        private readonly System.Threading.Timer _flushTimer;
        private readonly Dictionary<string, ActivitySegment> _openSegments = new();
        private readonly object _syncLock = new();
        private bool _isDisposing = false;

        public SqliteLogStore(SqliteDbContext dbContext, ILoggerService logger)
        {
            _dbContext = dbContext;
            _logger = logger;
            _flushTimer = new System.Threading.Timer(async _ => await FlushQueueAsync(), null, 2000, 2000);
            _logger.Info("SqliteLogStore initialized with batch flush every 2 seconds.", LogCategory.DB);
        }

        public void RecordTransition(string processName, string processPath, string windowTitle, ActivityStatus newStatus, DateTime timestamp, string alias = "")
        {
            if (_isDisposing) return;

            lock (_syncLock)
            {
                if (_openSegments.TryGetValue(processName, out var oldSeg))
                {
                    if (oldSeg.Status == newStatus)
                    {
                        if (!string.IsNullOrEmpty(windowTitle)) oldSeg.WindowTitle = windowTitle;
                        if (!string.IsNullOrEmpty(processPath)) oldSeg.ProcessPath = processPath;
                        if (!string.IsNullOrEmpty(alias)) oldSeg.Alias = alias;
                        return;
                    }

                    oldSeg.EndTime = timestamp;
                    QueueSegments(oldSeg);
                    _openSegments.Remove(processName);
                }

                _openSegments[processName] = new ActivitySegment
                {
                    ProcessName = processName,
                    ProcessPath = !string.IsNullOrEmpty(processPath) ? processPath : oldSeg?.ProcessPath ?? string.Empty,
                    WindowTitle = !string.IsNullOrEmpty(windowTitle) ? windowTitle : oldSeg?.WindowTitle ?? string.Empty,
                    Alias = !string.IsNullOrEmpty(alias) ? alias : oldSeg?.Alias ?? string.Empty,
                    Status = newStatus,
                    StartTime = timestamp
                };
            }
        }

        public void CloseProcess(string processName, DateTime timestamp)
        {
            lock (_syncLock)
            {
                if (_openSegments.TryGetValue(processName, out var seg))
                {
                    seg.EndTime = timestamp;
                    QueueSegments(seg);
                    _openSegments.Remove(processName);
                    _logger.Info($"Closed log segment for process: {processName}", LogCategory.DB);
                }
            }
        }

        private void QueueSegments(ActivitySegment segment)
        {
            if (segment.StartTime.Date != segment.EndTime.Date)
            {
                DateTime endOfDay = segment.StartTime.Date.AddDays(1).AddTicks(-1);
                var firstPart = new ActivitySegment
                {
                    ProcessName = segment.ProcessName,
                    ProcessPath = segment.ProcessPath,
                    WindowTitle = segment.WindowTitle,
                    Alias = segment.Alias,
                    Status = segment.Status,
                    StartTime = segment.StartTime,
                    EndTime = endOfDay
                };
                _writeQueue.Enqueue(firstPart);

                var secondPart = new ActivitySegment
                {
                    ProcessName = segment.ProcessName,
                    ProcessPath = segment.ProcessPath,
                    WindowTitle = segment.WindowTitle,
                    Alias = segment.Alias,
                    Status = segment.Status,
                    StartTime = segment.StartTime.Date.AddDays(1),
                    EndTime = segment.EndTime
                };
                QueueSegments(secondPart);
            }
            else
            {
                if (segment.Duration.TotalMilliseconds > 100)
                {
                    _writeQueue.Enqueue(segment);
                }
            }
        }

        private async Task FlushQueueAsync()
        {
            if (_writeQueue.IsEmpty) return;

            var segmentsToWrite = new List<ActivitySegment>();
            while (_writeQueue.TryDequeue(out var segment))
            {
                segmentsToWrite.Add(segment);
            }

            if (segmentsToWrite.Count == 0) return;

            try
            {
                using var connection = _dbContext.CreateConnection();
                // 确保连接已打开
                if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();
                
                using var transaction = connection.BeginTransaction();
                try 
                {
                    foreach (var seg in segmentsToWrite)
                    {
                        using var command = connection.CreateCommand();
                        command.Transaction = transaction;
                        command.CommandText = @"
                            INSERT INTO ActivityLog (ProcessName, ProcessPath, WindowTitle, Alias, Status, StartTime, EndTime)
                            VALUES ($proc, $path, $title, $alias, $status, $start, $end)
                        ";
                        command.Parameters.AddWithValue("$proc", seg.ProcessName);
                        command.Parameters.AddWithValue("$path", seg.ProcessPath ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("$title", seg.WindowTitle ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("$alias", seg.Alias ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("$status", (int)seg.Status);
                        command.Parameters.AddWithValue("$start", seg.StartTime.ToString("o"));
                        command.Parameters.AddWithValue("$end", seg.EndTime.ToString("o"));
                        await command.ExecuteNonQueryAsync();
                    }
                    await transaction.CommitAsync();
                }
                catch (Exception)
                {
                    await transaction.RollbackAsync();
                    throw; // 重新抛出以进入外部重试/日志逻辑
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to flush {segmentsToWrite.Count} segments to database. Re-enqueuing data.", ex, LogCategory.DB);
                // 失败保护：将数据放回队列，等待下次尝试
                foreach (var seg in segmentsToWrite)
                {
                    _writeQueue.Enqueue(seg);
                }
            }
        }

        public void Dispose()
        {
            if (_isDisposing) return;
            _isDisposing = true;

            _logger.Info("Disposing SqliteLogStore, performing final flush...", LogCategory.DB);
            lock (_syncLock)
            {
                var now = DateTime.Now;
                foreach (var seg in _openSegments.Values)
                {
                    seg.EndTime = now;
                    QueueSegments(seg);
                }
                _openSegments.Clear();
            }
            
            _flushTimer.Dispose();
            // 同步等待最后一批数据写入
            try 
            {
                FlushQueueAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.Error("Final flush failed during Dispose.", ex, LogCategory.DB);
            }
        }
    }
}
