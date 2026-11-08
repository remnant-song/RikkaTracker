using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using RikkaTracker.Core.Models;
using RikkaTracker.Services;

namespace RikkaTracker.Core.Monitor
{
    public enum ActivityStatus
    {
        Background = 0,
        ForegroundInactive = 1,
        ForegroundActive = 2
    }

    public class AppActivityChangedEventArgs : EventArgs
    {
        public string ProcessName { get; set; } = string.Empty;
        public string Alias { get; set; } = string.Empty;
        public string ProcessPath { get; set; } = string.Empty;
        public string WindowTitle { get; set; } = string.Empty;
        public int ProcessId { get; set; }
        public ActivityStatus OldStatus { get; set; }
        public ActivityStatus NewStatus { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public interface IAppActivityTracker
    {
        event EventHandler<AppActivityChangedEventArgs>? AppActivityChanged;
        void Start();
        void Stop();
    }

    public class AppActivityTracker : IAppActivityTracker
    {
        private readonly IConfigService _configService;
        private readonly Strategies.IFilterEngine _filterEngine;
        private readonly Data.IActivityLogStore _logStore;
        private readonly ILoggerService _logger;
        private readonly DispatcherTimer _idleTimer;
        private Win32Api.WinEventDelegate? _winEventDelegate;
        private IntPtr _hHook;

        private class ProcessState
        {
            public string ProcessName { get; set; } = string.Empty;
            public string Alias { get; set; } = string.Empty;
            public string LastTitle { get; set; } = string.Empty;
            public ActivityStatus LastStatus { get; set; } = ActivityStatus.Background;
            public IntPtr LastHwnd { get; set; } = IntPtr.Zero;
        }

        private readonly Dictionary<int, ProcessState> _processStates = new();
        private int _currentForegroundPid;
        private IntPtr _currentForegroundHwnd;
        private bool _isIdleDemoted;

        public event EventHandler<AppActivityChangedEventArgs>? AppActivityChanged;

        public AppActivityTracker(
            IConfigService configService, 
            Strategies.IFilterEngine filterEngine, 
            Data.IActivityLogStore logStore,
            ILoggerService logger)
        {
            _configService = configService;
            _filterEngine = filterEngine;
            _logStore = logStore;
            _logger = logger;
            _idleTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _idleTimer.Tick += OnIdleTimerTick;
        }

        public void Start()
        {
            try 
            {
                _logger.Info("Initializing AppActivityTracker...", LogCategory.Monitor);
                _winEventDelegate = new Win32Api.WinEventDelegate(WinEventProc);
                _hHook = Win32Api.SetWinEventHook(
                    Win32Api.EVENT_SYSTEM_FOREGROUND,
                    Win32Api.EVENT_SYSTEM_MINIMIZEEND,
                    IntPtr.Zero,
                    _winEventDelegate,
                    0, 0,
                    Win32Api.WINEVENT_OUTOFCONTEXT);

                if (_hHook == IntPtr.Zero)
                {
                    _logger.Error("Failed to set WinEventHook. Monitoring will not work.", null, LogCategory.Monitor);
                    return;
                }

                _logger.Info("WinEventHook successfully established.", LogCategory.Monitor);
                UpdateForegroundStatus();
            }
            catch (Exception ex)
            {
                _logger.Error("Critical error during AppActivityTracker startup.", ex, LogCategory.Monitor);
            }
        }

        public void Stop()
        {
            _logger.Info("Stopping AppActivityTracker...", LogCategory.Monitor);
            if (_hHook != IntPtr.Zero)
            {
                Win32Api.UnhookWinEvent(_hHook);
                _hHook = IntPtr.Zero;
            }
            _idleTimer.Stop();
        }

        private void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            try 
            {
                // 仅处理关键事件
                if (eventType != Win32Api.EVENT_SYSTEM_FOREGROUND &&
                    eventType != Win32Api.EVENT_SYSTEM_MINIMIZESTART &&
                    eventType != Win32Api.EVENT_SYSTEM_MINIMIZEEND)
                {
                    return;
                }

                if (hwnd == IntPtr.Zero) return;

                uint pid;
                Win32Api.GetWindowThreadProcessId(hwnd, out pid);
                int actualPid = (int)pid;
                if (actualPid == 0) return;

                string processName = GetProcessName(actualPid);
                if (processName == "ApplicationFrameHost")
                {
                    actualPid = Win32Api.ResolveUwpProcessId(hwnd, actualPid);
                }

                string title = Win32Api.GetWindowTitle(hwnd);
                bool isIconic = Win32Api.IsIconic(hwnd);
                bool isVisible = Win32Api.IsWindowVisible(hwnd);

                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    try 
                    {
                        switch (eventType)
                        {
                            case Win32Api.EVENT_SYSTEM_FOREGROUND:
                                HandleForegroundChange(hwnd, actualPid, title, isIconic, isVisible);
                                break;
                            case Win32Api.EVENT_SYSTEM_MINIMIZESTART:
                                HandleMinimize(hwnd, actualPid);
                                break;
                            case Win32Api.EVENT_SYSTEM_MINIMIZEEND:
                                HandleRestore(hwnd, actualPid, title);
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"Error in UI dispatcher during WinEvent {eventType}.", ex, LogCategory.Monitor);
                    }
                });
            }
            catch (Exception ex)
            {
                // 此处必须极度稳健，不能抛出任何异常
                _logger.Error("Fatal error in WinEventProc callback.", ex, LogCategory.Monitor);
            }
        }

        private void UpdateForegroundStatus()
        {
            IntPtr hwnd = Win32Api.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return;

            Win32Api.GetWindowThreadProcessId(hwnd, out uint pid);
            int actualPid = (int)pid;
            if (actualPid == 0) return;

            if (GetProcessName(actualPid) == "ApplicationFrameHost")
            {
                actualPid = Win32Api.ResolveUwpProcessId(hwnd, actualPid);
            }
            HandleForegroundChange(hwnd, actualPid, Win32Api.GetWindowTitle(hwnd), Win32Api.IsIconic(hwnd), Win32Api.IsWindowVisible(hwnd));
        }

        private void HandleForegroundChange(IntPtr hwnd, int newPid, string title, bool isIconic, bool isVisible)
        {
            string processName = GetProcessName(newPid);
            
            // 记录焦点切换日志
            _logger.Info($"Focus changed to: {processName} (PID: {newPid}) | Title: {title}", LogCategory.Monitor);

            // 过滤系统组件和非应用窗口（如桌面、任务栏、锁屏等）
            if (!Win32Api.IsAppWindow(hwnd))
            {
                _logger.Info($"Ignoring non-app window for focus tracking: {processName} (HWND: {hwnd})", LogCategory.Monitor);
                if (_currentForegroundPid != 0)
                {
                    UpdateProcessStatus(_currentForegroundPid, string.Empty, ActivityStatus.ForegroundInactive);
                    _currentForegroundPid = 0;
                    _currentForegroundHwnd = IntPtr.Zero;
                    _idleTimer.Stop();
                    _isIdleDemoted = false;
                }
                return;
            }

            if (IsSystemUI(newPid, title) || _filterEngine.ShouldIgnore(processName))
            {
                _logger.Info($"Ignoring system UI or filtered process: {processName}", LogCategory.Monitor);
                return;
            }

            ActivityStatus newStatus = (isIconic || !isVisible)
                ? ActivityStatus.Background
                : ActivityStatus.ForegroundActive;

            if (_currentForegroundPid != 0 && _currentForegroundPid != newPid)
            {
                ActivityStatus oldProcessNewStatus = ActivityStatus.ForegroundInactive;
                if (_currentForegroundHwnd != IntPtr.Zero)
                {
                    if (Win32Api.IsIconic(_currentForegroundHwnd) || !Win32Api.IsWindowVisible(_currentForegroundHwnd))
                    {
                        oldProcessNewStatus = ActivityStatus.Background;
                    }
                }
                UpdateProcessStatus(_currentForegroundPid, string.Empty, oldProcessNewStatus);
            }

            UpdateProcessStatus(newPid, title, newStatus);
            _currentForegroundPid = newPid;
            _currentForegroundHwnd = hwnd;

            if (_processStates.TryGetValue(newPid, out var state))
            {
                state.LastHwnd = hwnd;
            }

            _isIdleDemoted = false;
            if (newStatus == ActivityStatus.ForegroundActive)
            {
                if (!_idleTimer.IsEnabled) _idleTimer.Start();
            }
            else
            {
                _idleTimer.Stop();
            }
        }

        private void HandleMinimize(IntPtr hwnd, int pid)
        {
            UpdateProcessStatus(pid, string.Empty, ActivityStatus.Background);
            if (pid == _currentForegroundPid)
            {
                _currentForegroundPid = 0;
                _currentForegroundHwnd = IntPtr.Zero;
                _idleTimer.Stop();
                _isIdleDemoted = false;
            }
        }

        private void HandleRestore(IntPtr hwnd, int pid, string title)
        {
            IntPtr fgHwnd = Win32Api.GetForegroundWindow();
            if (fgHwnd == hwnd)
            {
                HandleForegroundChange(hwnd, pid, title, false, true);
            }
            else
            {
                UpdateProcessStatus(pid, title, ActivityStatus.ForegroundInactive);
                if (_processStates.TryGetValue(pid, out var state))
                {
                    state.LastHwnd = hwnd;
                }
            }
        }

        private bool IsSystemUI(int pid, string title)
        {
            string processName = GetProcessName(pid);
            if (processName.Equals("explorer", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(title) || 
                    title.Contains("系统托盘") || title.Contains("开始") || 
                    title.Contains("任务栏") || title.Contains("OverflowWindow"))
                {
                    return true;
                }
            }
            return false;
        }

        private void UpdateProcessStatus(int pid, string title, ActivityStatus newStatus)
        {
            if (!_processStates.TryGetValue(pid, out var state))
            {
                state = new ProcessState 
                { 
                    ProcessName = GetProcessName(pid),
                    Alias = Win32Api.GetProcessAlias(pid)
                };
                _processStates[pid] = state;
            }

            if (!string.IsNullOrEmpty(title))
            {
                state.LastTitle = title;
            }

            if (newStatus == ActivityStatus.ForegroundActive)
            {
                var otherActiveProcesses = _processStates
                    .Where(p => p.Key != pid && p.Value.LastStatus == ActivityStatus.ForegroundActive)
                    .ToList();

                foreach (var other in otherActiveProcesses)
                {
                    UpdateProcessStatus(other.Key, string.Empty, ActivityStatus.ForegroundInactive);
                }
            }

            if (state.LastStatus != newStatus)
            {
                var oldStatus = state.LastStatus;
                state.LastStatus = newStatus;
                string processPath = Win32Api.GetProcessPath(pid);

                try 
                {
                    _logStore.RecordTransition(state.ProcessName, processPath, state.LastTitle, newStatus, DateTime.Now, state.Alias);
                }
                catch (Exception ex)
                {
                    _logger.Error($"Failed to record transition to database for {state.ProcessName}.", ex, LogCategory.Monitor);
                }

                AppActivityChanged?.Invoke(this, new AppActivityChangedEventArgs
                {
                    ProcessId = pid,
                    ProcessName = state.ProcessName,
                    Alias = state.Alias,
                    ProcessPath = processPath,
                    WindowTitle = state.LastTitle,
                    OldStatus = oldStatus,
                    NewStatus = newStatus,
                    Timestamp = DateTime.Now
                });
            }
        }

        private void OnIdleTimerTick(object? sender, EventArgs e)
        {
            try 
            {
                if (_currentForegroundPid == 0) return;

                var lii = new Win32Api.LASTINPUTINFO();
                lii.cbSize = (uint)Marshal.SizeOf(lii);
                if (!Win32Api.GetLastInputInfo(ref lii)) return;

                uint idleTimeMs = (uint)Environment.TickCount - lii.dwTime;
                double idleMinutes = idleTimeMs / 60000.0;

                string processName = GetProcessName(_currentForegroundPid);

                if (idleMinutes >= _configService.Config.IdleTimeoutMinutes)
                {
                    if (!_isIdleDemoted)
                    {
                        if (_filterEngine.ShouldDisableIdleDetection(processName)) return;
                        UpdateProcessStatus(_currentForegroundPid, string.Empty, ActivityStatus.ForegroundInactive);
                        _isIdleDemoted = true;
                    }
                }
                else
                {
                    if (_isIdleDemoted)
                    {
                        UpdateProcessStatus(_currentForegroundPid, string.Empty, ActivityStatus.ForegroundActive);
                        _isIdleDemoted = false;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Error in IdleTimerTick.", ex, LogCategory.Monitor);
            }
        }

        private string GetProcessName(int pid) => Win32Api.GetInternalProcessName(pid);
    }
}
