using System.Runtime.InteropServices;

namespace Flick.App.Services;

/// <summary>
/// Manages the Windows system tray icon and context menu for background operation.
/// </summary>
public sealed unsafe class TrayIconService : IDisposable
{
    private const uint WmTrayCallback = NativeMethods.WM_USER + 201;
    private const uint TrayIconId = 1;
    private const uint CmdOpen = 1001;
    private const uint CmdToggle = 1002;
    private const uint CmdExit = 1003;

    private Thread? _thread;
    private uint _threadId;
    private nint _hwnd;
    private NativeMethods.WndProcDelegate? _wndProc;
    private bool _disposed;

    public Func<bool> IsEngineEnabled { get; set; } = () => true;
    public event Action? OpenRequested;
    public event Action? ToggleEnabledRequested;
    public event Action? ExitRequested;

    public void Start()
    {
        if (_thread is not null)
        {
            return;
        }

        _thread = new Thread(TrayThreadLoop)
        {
            IsBackground = true,
            Name = "Flick.TrayIcon"
        };
        _thread.Start();
    }

    private void TrayThreadLoop()
    {
        _threadId = NativeMethods.GetCurrentThreadId();
        _wndProc = WndProc;

        string className = "FlickTrayWindowClass";
        nint hInstance = NativeMethods.GetModuleHandle(null);

        var wc = new NativeMethods.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = hInstance,
            lpszClassName = className
        };

        NativeMethods.RegisterClassEx(ref wc);

        _hwnd = NativeMethods.CreateWindowEx(
            0, className, "FlickTray", 0,
            0, 0, 0, 0,
            0, 0, hInstance, 0);

        AddTrayIcon();

        while (NativeMethods.GetMessage(out var msg, 0, 0, 0) > 0)
        {
            NativeMethods.TranslateMessage(ref msg);
            NativeMethods.DispatchMessage(ref msg);
        }

        RemoveTrayIcon();

        if (_hwnd != 0)
        {
            NativeMethods.DestroyWindow(_hwnd);
            _hwnd = 0;
        }
    }

    private void AddTrayIcon()
    {
        string icoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "flick.ico");
        nint hIcon = File.Exists(icoPath)
            ? NativeMethods.LoadImage(0, icoPath, NativeMethods.IMAGE_ICON, 32, 32, NativeMethods.LR_LOADFROMFILE)
            : 0;
        if (hIcon == 0)
        {
            hIcon = NativeMethods.LoadIcon(0, 32512); // IDI_APPLICATION
        }

        var nid = new NativeMethods.NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = TrayIconId,
            uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP,
            uCallbackMessage = WmTrayCallback,
            hIcon = hIcon
        };

        string tip = "Flick — Gesture & Trigger Automation";
        for (int i = 0; i < tip.Length && i < 127; i++)
        {
            nid.szTip[i] = tip[i];
        }

        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref nid);
    }

    private void RemoveTrayIcon()
    {
        if (_hwnd == 0)
        {
            return;
        }

        var nid = new NativeMethods.NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = TrayIconId
        };
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref nid);
    }

    private nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == WmTrayCallback)
        {
            uint mouseMsg = (uint)lParam;
            if (mouseMsg == NativeMethods.WM_LBUTTONDBLCLK)
            {
                OpenRequested?.Invoke();
            }
            else if (mouseMsg == NativeMethods.WM_RBUTTONUP_TRAY)
            {
                ShowContextMenu(hWnd);
            }
            return 0;
        }

        return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void ShowContextMenu(nint hWnd)
    {
        nint menu = NativeMethods.CreatePopupMenu();
        NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, CmdOpen, "Open Flick");
        string toggleLabel = IsEngineEnabled() ? "Pause Gestures" : "Resume Gestures";
        NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, CmdToggle, toggleLabel);
        NativeMethods.AppendMenu(menu, NativeMethods.MF_SEPARATOR, 0, string.Empty);
        NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, CmdExit, "Exit Flick");

        NativeMethods.GetCursorPos(out var pt);
        NativeMethods.SetForegroundWindow(hWnd);

        uint cmd = NativeMethods.TrackPopupMenu(
            menu,
            NativeMethods.TPM_RETURNCMD | NativeMethods.TPM_RIGHTBUTTON,
            pt.X, pt.Y, 0, hWnd, 0);

        NativeMethods.DestroyMenu(menu);

        switch (cmd)
        {
            case CmdOpen:
                OpenRequested?.Invoke();
                break;
            case CmdToggle:
                ToggleEnabledRequested?.Invoke();
                break;
            case CmdExit:
                ExitRequested?.Invoke();
                break;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        if (_threadId != 0)
        {
            NativeMethods.PostThreadMessage(_threadId, NativeMethods.WM_QUIT, 0, 0);
        }
    }
}
