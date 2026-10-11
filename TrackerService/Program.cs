using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using SharedData.Data;
using SharedData.Models;
using TrackerService;

namespace TrackerService;

internal static class Program
{
    // ════════════════════════════════════════════════════════════════════
    // RULE 2: Assign the WinEventDelegate to a static field to prevent
    // unmanaged P/Invoke garbage collection crashes.
    // ════════════════════════════════════════════════════════════════════
    private static NativeMethods.WinEventDelegate? _winEventDelegate;
    private static IntPtr _hookHandle = IntPtr.Zero;

    // ── Database and State ──────────────────────────────────────────────
    private static readonly SessionRepository Repository = new();
    private static readonly object SessionLock = new();
    private static ActiveSession? _currentSession;
    private static bool _isIdle = false;

    // ── Intervals and Thresholds ────────────────────────────────────────
    private const int IdleThresholdMs = 60_000;    // 60 seconds (Pause session if idle > 60s)
    private const int IdlePollIntervalMs = 10_000; // 10 seconds (Poll GetLastInputInfo every 10s)
    private const int FlushIntervalMs = 300_000;   // 5 minutes (Flush to DB every 5 minutes)

    private static System.Threading.Timer? _idleTimer;
    private static System.Threading.Timer? _flushTimer;

    public static void Main(string[] args)
    {
        Console.WriteLine("================================================");
        Console.WriteLine(" ScreenPi Tracker Service (.NET 8)");
        Console.WriteLine(" Zero-CPU Foreground Monitor & Idle Detector");
        Console.WriteLine("================================================");

        // 1. Initialize SQLite Database (WAL mode enabled)
        DatabaseInitializer.EnsureCreated();
        Console.WriteLine($"[ScreenPi] Database initialized at: {DatabaseInitializer.DbPath}");

        // 2. Assign delegate to static field to prevent GC collection
        _winEventDelegate = OnForegroundWindowChanged;

        // 3. Set WinEvent Hook for EVENT_SYSTEM_FOREGROUND (Zero CPU: no polling!)
        _hookHandle = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero,
            _winEventDelegate,
            0,
            0,
            NativeMethods.WINEVENT_OUTOFCONTEXT);

        if (_hookHandle == IntPtr.Zero)
        {
            Console.WriteLine("[ScreenPi] FATAL: Failed to register WinEvent hook.");
            return;
        }

        Console.WriteLine("[ScreenPi] SetWinEventHook active. Monitoring foreground switches...");

        // 4. Capture current foreground window at startup
        CaptureCurrentForeground();

        // 5. System timer for idle detection (poll every 10 seconds)
        _idleTimer = new System.Threading.Timer(_ => CheckIdle(), null, IdlePollIntervalMs, IdlePollIntervalMs);

        // 6. Periodic flush timer (every 5 minutes)
        _flushTimer = new System.Threading.Timer(_ => PeriodicFlush(), null, FlushIntervalMs, FlushIntervalMs);

        // 7. Graceful shutdown registration
        Console.CancelKeyPress += OnCancelKeyPress;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

        Console.WriteLine("[ScreenPi] Background service running. Press Ctrl+C to terminate.");

        // 8. Win32 message pump (GetMessage blocks with ~0% CPU until WinEvent message arrives)
        RunMessageLoop();
    }

    private static void OnForegroundWindowChanged(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        try
        {
            var (processName, windowTitle) = GetWindowInfo(hwnd);
            if (string.IsNullOrEmpty(processName)) return;

            lock (SessionLock)
            {
                // If same process, update window title in RAM buffer without flushing to DB
                if (_currentSession != null &&
                    _currentSession.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase))
                {
                    _currentSession.WindowTitle = windowTitle;
                    return;
                }

                // Window switch triggered: flush previous session to DB and start new one
                FlushCurrentSessionInternal(DateTime.UtcNow);
                StartNewSession(processName, windowTitle);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ScreenPi] Error in OnForegroundWindowChanged: {ex.Message}");
        }
    }

    private static void CaptureCurrentForeground()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return;

        var (processName, windowTitle) = GetWindowInfo(hwnd);
        if (!string.IsNullOrEmpty(processName))
        {
            lock (SessionLock)
            {
                StartNewSession(processName, windowTitle);
            }
        }
    }

    private static (string processName, string windowTitle) GetWindowInfo(IntPtr hwnd)
    {
        try
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return (string.Empty, string.Empty);

            using var process = Process.GetProcessById((int)pid);
            var processName = process.ProcessName;

            int titleLength = NativeMethods.GetWindowTextLength(hwnd);
            var sb = new StringBuilder(titleLength + 1);
            NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);

            return (processName, sb.ToString());
        }
        catch
        {
            return (string.Empty, string.Empty);
        }
    }

    /// <summary>
    /// Starts a new session buffer in RAM. No immediate DB write is made.
    /// </summary>
    private static void StartNewSession(string processName, string windowTitle)
    {
        var now = DateTime.UtcNow;
        _currentSession = new ActiveSession
        {
            DbId = null, // In RAM only until flush
            ProcessName = processName,
            WindowTitle = windowTitle,
            StartTime = now,
            LastFlushTime = now,
            FlushedDurationSeconds = 0
        };

        Console.WriteLine($"[ScreenPi] Active: {processName} | \"{windowTitle}\"");
    }

    /// <summary>
    /// Flushes the active in-memory session to SQLite.
    /// Updates or inserts depending on whether this session was already flushed before.
    /// </summary>
    private static void FlushCurrentSessionInternal(DateTime upToTime)
    {
        if (_currentSession == null) return;

        var elapsed = (upToTime - _currentSession.LastFlushTime).TotalSeconds;
        if (elapsed > 0)
        {
            _currentSession.FlushedDurationSeconds += elapsed;
            _currentSession.LastFlushTime = upToTime;
        }

        // Only persist if duration is greater than 0
        if (_currentSession.FlushedDurationSeconds <= 0) return;

        if (_currentSession.DbId == null)
        {
            // First flush: Insert new row into SQLite and save returned Id
            var log = new SessionLog
            {
                ProcessName = _currentSession.ProcessName,
                WindowTitle = _currentSession.WindowTitle,
                StartTime = _currentSession.StartTime,
                EndTime = upToTime,
                DurationSeconds = _currentSession.FlushedDurationSeconds
            };
            _currentSession.DbId = Repository.InsertAndGetId(log);
        }
        else
        {
            // Subsequent flush (5-minute timer or switch): update existing row
            var log = new SessionLog
            {
                Id = _currentSession.DbId.Value,
                ProcessName = _currentSession.ProcessName,
                WindowTitle = _currentSession.WindowTitle,
                StartTime = _currentSession.StartTime,
                EndTime = upToTime,
                DurationSeconds = _currentSession.FlushedDurationSeconds
            };
            Repository.Update(log);
        }
    }

    /// <summary>
    /// Periodic flush every 5 minutes (Rule 4: Flush to DB ONLY on: window switch, idle trigger, or every 5 minutes).
    /// </summary>
    private static void PeriodicFlush()
    {
        lock (SessionLock)
        {
            if (!_isIdle && _currentSession != null)
            {
                FlushCurrentSessionInternal(DateTime.UtcNow);
                Console.WriteLine($"[ScreenPi] 5-minute periodic flush completed for: {_currentSession.ProcessName}");
            }
        }
    }

    /// <summary>
    /// Polls GetLastInputInfo every 10s. Pauses session tracking if idle > 60s (Rule 3).
    /// </summary>
    private static void CheckIdle()
    {
        var lii = new NativeMethods.LASTINPUTINFO
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.LASTINPUTINFO>()
        };

        if (!NativeMethods.GetLastInputInfo(ref lii)) return;

        uint currentTick = NativeMethods.GetTickCount();
        uint idleMs = currentTick - lii.dwTime;

        lock (SessionLock)
        {
            if (idleMs >= IdleThresholdMs && !_isIdle)
            {
                // User went idle (> 60s without input).
                // Flush the session up to the last user input time so idle time is NOT counted.
                _isIdle = true;
                DateTime idleStartTime = DateTime.UtcNow.AddMilliseconds(-idleMs);
                if (_currentSession != null && idleStartTime > _currentSession.LastFlushTime)
                {
                    FlushCurrentSessionInternal(idleStartTime);
                }
                Console.WriteLine($"[ScreenPi] User idle ({idleMs / 1000}s). Pausing tracking.");
            }
            else if (idleMs < IdleThresholdMs && _isIdle)
            {
                // User resumed activity
                _isIdle = false;
                if (_currentSession != null)
                {
                    _currentSession.LastFlushTime = DateTime.UtcNow;
                }
                Console.WriteLine("[ScreenPi] User active. Resuming tracking.");
            }
        }
    }

    private static void RunMessageLoop()
    {
        // GetMessageW blocks with zero CPU until a message is dispatched to the thread
        while (NativeMethods.GetMessageW(out NativeMethods.MSG msg, IntPtr.Zero, 0, 0) > 0)
        {
            NativeMethods.TranslateMessage(ref msg);
            NativeMethods.DispatchMessageW(ref msg);
        }
    }

    private static void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true;
        Shutdown();
    }

    private static void OnProcessExit(object? sender, EventArgs e)
    {
        Shutdown();
    }

    private static void Shutdown()
    {
        Console.WriteLine("\n[ScreenPi] Shutting down service...");

        lock (SessionLock)
        {
            if (_currentSession != null && !_isIdle)
            {
                FlushCurrentSessionInternal(DateTime.UtcNow);
                _currentSession = null;
            }
        }

        if (_hookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }

        _idleTimer?.Dispose();
        _flushTimer?.Dispose();
        Environment.Exit(0);
    }
}
