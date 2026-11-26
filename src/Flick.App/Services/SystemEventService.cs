using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Flick.Core.Models;

namespace Flick.App.Services;

/// <summary>
/// Listens for OS, power, session, shell, and application lifecycle events
/// (AppOpen, AppClose, AppMinimized, AppFocused, OnLogin, SessionLock/Unlock, DisplayOff/On, StartMenuOpen, SystemTrayClick).
/// </summary>
public sealed class SystemEventService : IDisposable
{
    private const uint WmWtsSessionChange = 0x02B1;
    private const uint WmPowerBroadcast = 0x0218;
    private const uint WtsSessionLock = 0x7;
    private const uint WtsSessionUnlock = 0x8;
    private const uint PbtPowerSettingChange = 0x8013;

    private const uint EventSystemForeground = 0x0003;
    private const uint EventSystemMinimizeStart = 0x0016;
    private const uint EventObjectDestroy = 0x8001;
    private const uint EventObjectShow = 0x8002;
    private const uint WineventOutOfContext = 0x0000;
    private const uint WineventSkipOwnProcess = 0x0002;
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080L;

    private static readonly Guid GuidConsoleDisplayState = new("6fe69556-704a-47a0-8f24-c28d936fda47");

    private delegate nint WndProcDelegate(nint hWnd, uint msg, nint wParam, nint lParam);
    private delegate void WinEventDelegate(nint hWinEventHook, uint eventType, nint hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);
    private delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public WndProcDelegate lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public NativeMethods.POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POWERBROADCAST_SETTING
    {
        public Guid PowerSetting;
        public uint DataLength;
        public byte Data;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight, nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint idThread, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern nint SetWinEventHook(
        uint eventMin, uint eventMax, nint hmodWinEventProc, WinEventDelegate lpfnWinEventProc,
        uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(nint hWinEventHook);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    private static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("wtsapi32.dll")]
    private static extern bool WTSRegisterSessionNotification(nint hWnd, uint dwFlags);

    [DllImport("wtsapi32.dll")]
    private static extern bool WTSUnRegisterSessionNotification(nint hWnd);

    [DllImport("user32.dll")]
    private static extern nint RegisterPowerSettingNotification(nint hRecipient, ref Guid powerSettingGuid, uint flags);

    [DllImport("user32.dll")]
    private static extern bool UnregisterPowerSettingNotification(nint handle);

    private readonly Dictionary<nint, string> _trackedWindows = [];
    private WndProcDelegate? _wndProc;
    private WinEventDelegate? _winEventProc;
    private Thread? _thread;
    private uint _threadId;
    private nint _hwnd;
    private nint _powerNotifyHandle;
    private nint _hookForeground;
    private nint _hookMinimize;
    private nint _hookShowDestroy;
    private bool _hasInitialDisplayState;
    private byte _lastDisplayState = 1;
    private long _lastStartMenuTick;
    private long _lastTrayTick;
    private bool _disposed;

    public bool IsEnabled { get; set; } = true;

    public event Action<SystemEventKind, ForegroundAppInfo>? EventTriggered;

    public void Start()
    {
        if (_thread is not null || _disposed) return;

        _thread = new Thread(MessageLoop)
        {
            IsBackground = true,
            Name = "Flick.SystemEvents"
        };
        _thread.Start();

        // Fire OnLogin once shortly after Flick starts.
        _ = Task.Run(async () =>
        {
            await Task.Delay(1200).ConfigureAwait(false);
            if (!_disposed && IsEnabled)
            {
                Emit(SystemEventKind.OnLogin, new ForegroundAppInfo(0, "Flick", string.Empty));
            }
        });
    }

    private void MessageLoop()
    {
        _threadId = NativeMethods.GetCurrentThreadId();
        _wndProc = WndProc;
        _winEventProc = OnWinEvent;

        const string className = "Flick.SystemEventHost";
        nint hInst = NativeMethods.GetModuleHandle(null);
        var wc = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = _wndProc,
            hInstance = hInst,
            lpszClassName = className
        };
        RegisterClassEx(ref wc);

        _hwnd = CreateWindowEx(0, className, className, 0, 0, 0, 0, 0, 0, 0, hInst, 0);
        if (_hwnd != 0)
        {
            WTSRegisterSessionNotification(_hwnd, 0);
            var displayGuid = GuidConsoleDisplayState;
            _powerNotifyHandle = RegisterPowerSettingNotification(_hwnd, ref displayGuid, 0);
        }

        SeedOpenWindows();

        uint flags = WineventOutOfContext | WineventSkipOwnProcess;
        _hookForeground = SetWinEventHook(EventSystemForeground, EventSystemForeground, 0, _winEventProc, 0, 0, flags);
        _hookMinimize = SetWinEventHook(EventSystemMinimizeStart, EventSystemMinimizeStart, 0, _winEventProc, 0, 0, flags);
        _hookShowDestroy = SetWinEventHook(EventObjectDestroy, EventObjectShow, 0, _winEventProc, 0, 0, flags);

        while (GetMessage(out var msg, 0, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        if (_hookForeground != 0) UnhookWinEvent(_hookForeground);
        if (_hookMinimize != 0) UnhookWinEvent(_hookMinimize);
        if (_hookShowDestroy != 0) UnhookWinEvent(_hookShowDestroy);

        if (_powerNotifyHandle != 0) UnregisterPowerSettingNotification(_powerNotifyHandle);
        if (_hwnd != 0)
        {
            WTSUnRegisterSessionNotification(_hwnd);
            DestroyWindow(_hwnd);
            _hwnd = 0;
        }
    }

    private void SeedOpenWindows()
    {
        EnumWindows((hwnd, lParam) =>
        {
            if (IsTrackableTopLevel(hwnd, out string proc, out _))
            {
                _trackedWindows[hwnd] = proc;
            }
            return true;
        }, 0);
    }

    private nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == WmWtsSessionChange && IsEnabled)
        {
            uint evt = (uint)wParam;
            if (evt == WtsSessionLock)
            {
                Emit(SystemEventKind.SessionLock, new ForegroundAppInfo(0, "Windows", string.Empty));
            }
            else if (evt == WtsSessionUnlock)
            {
                Emit(SystemEventKind.SessionUnlock, new ForegroundAppInfo(0, "Windows", string.Empty));
            }
        }
        else if (msg == WmPowerBroadcast && (uint)wParam == PbtPowerSettingChange && lParam != 0)
        {
            var ps = Marshal.PtrToStructure<POWERBROADCAST_SETTING>(lParam);
            if (ps.PowerSetting == GuidConsoleDisplayState)
            {
                byte state = ps.Data;
                if (!_hasInitialDisplayState)
                {
                    _hasInitialDisplayState = true;
                    _lastDisplayState = state;
                }
                else if (state != _lastDisplayState)
                {
                    _lastDisplayState = state;
                    if (IsEnabled)
                    {
                        if (state == 0)
                        {
                            Emit(SystemEventKind.DisplayOff, new ForegroundAppInfo(0, "Display", string.Empty));
                        }
                        else if (state == 1)
                        {
                            Emit(SystemEventKind.DisplayOn, new ForegroundAppInfo(0, "Display", string.Empty));
                        }
                    }
                }
            }
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void OnWinEvent(nint hWinEventHook, uint eventType, nint hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (!IsEnabled || hwnd == 0 || idObject != 0 || idChild != 0)
        {
            return;
        }

        if (eventType == EventObjectDestroy)
        {
            if (_trackedWindows.Remove(hwnd, out string? closedProc))
            {
                bool anyLeft = _trackedWindows.Values.Any(p => string.Equals(p, closedProc, StringComparison.OrdinalIgnoreCase));
                if (!anyLeft)
                {
                    Emit(SystemEventKind.AppClose, new ForegroundAppInfo(0, closedProc, string.Empty));
                }
            }
            return;
        }

        if (eventType == EventSystemMinimizeStart)
        {
            if (TryGetWindowInfo(hwnd, out string minProc, out string minTitle))
            {
                Emit(SystemEventKind.AppMinimized, new ForegroundAppInfo(hwnd, minProc, minTitle));
            }
            return;
        }

        if (eventType == EventSystemForeground)
        {
            string cls = GetWindowClass(hwnd);
            if (TryGetWindowInfo(hwnd, out string fgProc, out string fgTitle))
            {
                long now = Environment.TickCount64;
                if (string.Equals(fgProc, "StartMenuExperienceHost", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(fgProc, "SearchHost", StringComparison.OrdinalIgnoreCase))
                {
                    if (now - _lastStartMenuTick >= 500)
                    {
                        _lastStartMenuTick = now;
                        Emit(SystemEventKind.StartMenuOpen, new ForegroundAppInfo(hwnd, fgProc, fgTitle));
                    }
                    return;
                }

                if (cls is "Shell_TrayWnd" or "NotifyIconOverflowWindow" or "TopLevelWindowForOverflowXamlIsland" or "ControlCenterWindow")
                {
                    if (now - _lastTrayTick >= 500)
                    {
                        _lastTrayTick = now;
                        Emit(SystemEventKind.SystemTrayClick, new ForegroundAppInfo(hwnd, fgProc, fgTitle));
                    }
                    return;
                }

                if (IsTrackableTopLevel(hwnd, out _, out _))
                {
                    TrackShowOrOpen(hwnd, fgProc, fgTitle);
                    Emit(SystemEventKind.AppFocused, new ForegroundAppInfo(hwnd, fgProc, fgTitle));
                }
            }
            return;
        }

        if (eventType == EventObjectShow)
        {
            if (IsTrackableTopLevel(hwnd, out string showProc, out string showTitle))
            {
                TrackShowOrOpen(hwnd, showProc, showTitle);
            }
        }
    }

    private void TrackShowOrOpen(nint hwnd, string processName, string title)
    {
        if (_trackedWindows.ContainsKey(hwnd)) return;

        bool firstForProcess = !_trackedWindows.Values.Any(p => string.Equals(p, processName, StringComparison.OrdinalIgnoreCase));
        _trackedWindows[hwnd] = processName;

        if (firstForProcess)
        {
            Emit(SystemEventKind.AppOpen, new ForegroundAppInfo(hwnd, processName, title));
        }
    }

    private void Emit(SystemEventKind kind, ForegroundAppInfo info)
    {
        var handler = EventTriggered;
        if (handler is not null)
        {
            _ = Task.Run(() => handler(kind, info));
        }
    }

    private static bool IsTrackableTopLevel(nint hwnd, out string processName, out string title)
    {
        processName = string.Empty;
        title = string.Empty;

        if (!IsWindowVisible(hwnd)) return false;
        if (NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT) != hwnd) return false;
        if ((GetWindowLongPtr(hwnd, GwlExStyle).ToInt64() & WsExToolWindow) != 0) return false;

        string cls = GetWindowClass(hwnd);
        if (cls is "Shell_TrayWnd" or "Progman" or "WorkerW" or "NotifyIconOverflowWindow" or "ControlCenterWindow")
        {
            return false;
        }

        if (!TryGetWindowInfo(hwnd, out processName, out title)) return false;
        if (string.Equals(processName, "Flick", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(processName, "StartMenuExperienceHost", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(processName, "SearchHost", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static bool TryGetWindowInfo(nint hwnd, out string processName, out string title)
    {
        processName = string.Empty;
        title = string.Empty;
        try
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return false;
            using var proc = Process.GetProcessById((int)pid);
            processName = proc.ProcessName;

            char[] buf = new char[256];
            int len = NativeMethods.GetWindowText(hwnd, buf, buf.Length);
            title = len > 0 ? new string(buf, 0, len) : string.Empty;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string GetWindowClass(nint hwnd)
    {
        var sb = new StringBuilder(128);
        int len = GetClassName(hwnd, sb, sb.Capacity);
        return len > 0 ? sb.ToString(0, len) : string.Empty;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_threadId != 0)
        {
            PostThreadMessage(_threadId, 0x0012 /* WM_QUIT */, 0, 0);
        }
    }
}
