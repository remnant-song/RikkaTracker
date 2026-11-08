using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Threading;
using RikkaTracker.Core.Models;
using RikkaTracker.Services;

namespace RikkaTracker.Core.Monitor
{
    public class ProcessEventArgs : EventArgs
    {
        public int ProcessId { get; set; }
        public string ProcessName { get; set; } = string.Empty;
    }

    public interface IProcessMonitor
    {
        event EventHandler<ProcessEventArgs> ProcessStarted;
        event EventHandler<ProcessEventArgs> ProcessExited;
        void Start();
        void Stop();
    }

    public class ProcessMonitor : IProcessMonitor
    {
        private readonly DispatcherTimer _scanTimer;
        private readonly ILoggerService _logger;
        private Dictionary<int, string> _pidNames = new();

        public event EventHandler<ProcessEventArgs>? ProcessStarted;
        public event EventHandler<ProcessEventArgs>? ProcessExited;

        public ProcessMonitor(ILoggerService logger)
        {
            _logger = logger;
            _scanTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _scanTimer.Tick += OnScanTimerTick;
        }

        public void Start()
        {
            _logger.Info("Starting ProcessMonitor scan loop...", LogCategory.Monitor);
            try 
            {
                _pidNames = GetCurrentPidNames();
                _scanTimer.Start();
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to perform initial process scan.", ex, LogCategory.Monitor);
            }
        }

        public void Stop()
        {
            _logger.Info("Stopping ProcessMonitor...", LogCategory.Monitor);
            _scanTimer.Stop();
        }

        private void OnScanTimerTick(object? sender, EventArgs e)
        {
            try 
            {
                var currentPidNames = GetCurrentPidNames();

                // 检测新启动的进程
                foreach (var kvp in currentPidNames)
                {
                    if (!_pidNames.ContainsKey(kvp.Key))
                    {
                        ProcessStarted?.Invoke(this, new ProcessEventArgs
                        {
                            ProcessId = kvp.Key,
                            ProcessName = kvp.Value
                        });
                    }
                }

                // 检测已退出的进程
                foreach (var kvp in _pidNames)
                {
                    if (!currentPidNames.ContainsKey(kvp.Key))
                    {
                        // 清理已退出的 PID 在 Win32Api 内部缓存的名称
                        Win32Api.RemoveCachedProcessName(kvp.Key);

                        ProcessExited?.Invoke(this, new ProcessEventArgs
                        {
                            ProcessId = kvp.Key,
                            ProcessName = kvp.Value
                        });
                    }
                }

                _pidNames = currentPidNames;
            }
            catch (Exception ex)
            {
                _logger.Error("Error during ProcessMonitor scan tick.", ex, LogCategory.Monitor);
            }
        }

        private Dictionary<int, string> GetCurrentPidNames()
        {
            var result = new Dictionary<int, string>();
            
            try
            {
                // 获取当前拥有应用级可见窗口的进程 ID
                var appPids = Win32Api.GetAppProcessIds();
                foreach (var pid in appPids)
                {
                    try
                    {
                        string name = Win32Api.GetInternalProcessName(pid);
                        if (!string.IsNullOrEmpty(name) && name != "Unknown")
                        {
                            result[pid] = name;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Unexpected error scanning process {pid}: {ex.Message}", LogCategory.Monitor);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Critical failure in GetCurrentPidNames.", ex, LogCategory.Monitor);
            }
            
            return result;
        }
    }
}
