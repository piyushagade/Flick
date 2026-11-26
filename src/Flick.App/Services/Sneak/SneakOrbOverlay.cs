using System.Runtime.InteropServices;

namespace Flick.App.Services.Sneak;

/// <summary>
/// Renders a feathered, fade-out circular portal that follows the cursor and peeks
/// through the active foreground window to show windows underneath.
/// Capture runs asynchronously on a background thread while cursor tracking and
/// compositing run at full 60+ fps using a precomputed radial alpha LUT.
/// </summary>
public sealed unsafe class SneakOrbOverlay : IDisposable
{
    private const uint WmSneakShow = SneakNativeMethods.WM_USER + 201;
    private const uint WmSneakHide = SneakNativeMethods.WM_USER + 202;
    private const uint WmSneakToggle = SneakNativeMethods.WM_USER + 203;
    private const uint WmSneakResize = SneakNativeMethods.WM_USER + 204;
    private const uint WmSneakCycle = SneakNativeMethods.WM_USER + 205;
    private const uint WmSneakMove = SneakNativeMethods.WM_USER + 206;
    private const uint WmSneakFrameReady = SneakNativeMethods.WM_USER + 207;

    private const nuint TimerId = 1;
    private const uint TimerIntervalMs = 16; // ~60 fps fade & fallback refresh
    private const int CaptureIntervalMs = 110; // ~9 fps background window content refresh

    private const int MinOrbDiameter = 160;
    private const int MaxOrbDiameter = 1200;
    private const int DefaultOrbDiameter = 380;
    private const int ResizeStep = 40;
    private const int LabelFooter = 40;

    private sealed class CapturedWindowFrame
    {
        public nint Handle;
        public int Left;
        public int Top;
        public int Width;
        public int Height;
        public string ProcessName = string.Empty;
        public uint[] Pixels = [];
    }

    private Thread? _thread;
    private Thread? _captureThread;
    private readonly AutoResetEvent _captureWake = new(false);
    private nint _hwnd;
    private uint _threadId;
    private nuint _gdipToken;
    private SneakNativeMethods.WndProcDelegate? _wndProc;
    private SneakNativeMethods.LowLevelMouseProc? _mouseProc;
    private SneakNativeMethods.LowLevelKeyboardProc? _keyboardProc;
    private nint _mouseHook;
    private nint _keyboardHook;

    private volatile bool _isActive;
    private volatile bool _isFadingOut;
    private volatile bool _disposed;
    private volatile bool _showAllWindows;
    private bool _windowShown;
    private float _opacity;
    private int _orbDiameter = DefaultOrbDiameter;

    // Coalesced cursor & frame-ready flags
    private int _movePending;
    private int _framePending;
    private int _cursorX;
    private int _cursorY;
    private int _lastRenderedX = int.MinValue;
    private int _lastRenderedY = int.MinValue;

    // Shared window selection / snapshot state between render & capture threads
    private readonly object _snapshotGate = new();
    private nint _lockedTargetHwnd;
    private int _pendingCycleDelta;
    private List<CapturedWindowFrame> _latestFrames = [];
    private int _latestActiveIdx;
    private int _latestTotalCount;

    // Precomputed radial fade-out alpha LUT (0..255) for current _orbDiameter
    private byte[] _alphaLut = [];
    private int _alphaLutDiameter;

    // Stationary virtual-screen orb DIB (eliminates DWM move-vs-surface desync vibration)
    private int _virtX;
    private int _virtY;
    private int _virtW;
    private int _virtH;
    private nint _screenDc;
    private nint _orbDc;
    private nint _orbBmp;
    private nint _orbOldBmp;
    private uint* _orbPixels;
    private SneakNativeMethods.RECT _prevDirtyRect;

    // Background-thread-only PrintWindow capture DIB
    private nint _capScreenDc;
    private nint _capDc;
    private nint _capBmp;
    private nint _capOldBmp;
    private uint* _capPixels;
    private int _capW;
    private int _capH;
    private readonly Dictionary<nint, uint[]> _pixelPool = [];

    public bool IsVisible => _isActive;

    public void Start()
    {
        if (_thread is not null || _disposed)
        {
            return;
        }

        _thread = new Thread(OverlayLoop)
        {
            IsBackground = true,
            Name = "Flick.SneakOrb"
        };
        _thread.Start();

        _captureThread = new Thread(CaptureLoop)
        {
            IsBackground = true,
            Name = "Flick.SneakCapture"
        };
        _captureThread.Start();
    }

    public void Toggle(bool showAllWindows = false)
    {
        if (_hwnd != 0 && !_disposed)
        {
            SneakNativeMethods.PostMessage(_hwnd, WmSneakToggle, showAllWindows ? 1 : 0, 0);
        }
    }

    public void Show(bool showAllWindows = false)
    {
        if (_hwnd != 0 && !_disposed)
        {
            SneakNativeMethods.PostMessage(_hwnd, WmSneakShow, showAllWindows ? 1 : 0, 0);
        }
    }

    public void Hide()
    {
        if (_hwnd != 0 && !_disposed)
        {
            SneakNativeMethods.PostMessage(_hwnd, WmSneakHide, 0, 0);
        }
    }

    private void OverlayLoop()
    {
        _threadId = SneakNativeMethods.GetCurrentThreadId();

        var gdipInput = new SneakNativeMethods.GdiplusStartupInput { GdiplusVersion = 1 };
        _ = SneakNativeMethods.GdiplusStartup(out _gdipToken, ref gdipInput, 0);

        _wndProc = WndProc;
        _mouseProc = MouseHookCallback;
        _keyboardProc = KeyboardHookCallback;

        const string className = "FlickSneakOrbClass";
        nint hInstance = SneakNativeMethods.GetModuleHandle(null);

        var wc = new SneakNativeMethods.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<SneakNativeMethods.WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = hInstance,
            lpszClassName = className
        };

        SneakNativeMethods.RegisterClassEx(ref wc);

        uint exStyle = SneakNativeMethods.WS_EX_LAYERED |
                       SneakNativeMethods.WS_EX_TRANSPARENT |
                       SneakNativeMethods.WS_EX_TOPMOST |
                       SneakNativeMethods.WS_EX_TOOLWINDOW |
                       SneakNativeMethods.WS_EX_NOACTIVATE;

        _virtX = SneakNativeMethods.GetSystemMetrics(SneakNativeMethods.SM_XVIRTUALSCREEN);
        _virtY = SneakNativeMethods.GetSystemMetrics(SneakNativeMethods.SM_YVIRTUALSCREEN);
        _virtW = Math.Max(1, SneakNativeMethods.GetSystemMetrics(SneakNativeMethods.SM_CXVIRTUALSCREEN));
        _virtH = Math.Max(1, SneakNativeMethods.GetSystemMetrics(SneakNativeMethods.SM_CYVIRTUALSCREEN));

        _hwnd = SneakNativeMethods.CreateWindowEx(
            exStyle,
            className,
            "FlickSneakOrb",
            SneakNativeMethods.WS_POPUP,
            _virtX, _virtY, _virtW, _virtH,
            0, 0, hInstance, 0);

        _screenDc = SneakNativeMethods.GetDC(0);
        InitVirtualOrbBuffer();
        EnsureAlphaLut(_orbDiameter);

        while (SneakNativeMethods.GetMessage(out var msg, 0, 0, 0) > 0)
        {
            SneakNativeMethods.TranslateMessage(ref msg);
            SneakNativeMethods.DispatchMessage(ref msg);
        }

        UninstallRuntimeHooks();
        ReleaseOrbBuffer();

        if (_screenDc != 0)
        {
            SneakNativeMethods.ReleaseDC(0, _screenDc);
            _screenDc = 0;
        }

        if (_hwnd != 0)
        {
            SneakNativeMethods.DestroyWindow(_hwnd);
            _hwnd = 0;
        }

        if (_gdipToken != 0)
        {
            SneakNativeMethods.GdiplusShutdown(_gdipToken);
            _gdipToken = 0;
        }
    }

    private void CaptureLoop()
    {
        _capScreenDc = SneakNativeMethods.GetDC(0);

        while (!_disposed)
        {
            if (!_isActive)
            {
                _captureWake.WaitOne(500);
                continue;
            }

            try
            {
                RefreshCaptureSnapshot();
            }
            catch
            {
                // Ignore transient Win32 errors if a window closes mid-capture.
            }

            if (_isActive && !_disposed && _hwnd != 0 &&
                Interlocked.Exchange(ref _framePending, 1) == 0)
            {
                SneakNativeMethods.PostMessage(_hwnd, WmSneakFrameReady, 0, 0);
            }

            _captureWake.WaitOne(CaptureIntervalMs);
        }

        ReleaseCaptureBuffer();
        if (_capScreenDc != 0)
        {
            SneakNativeMethods.ReleaseDC(0, _capScreenDc);
            _capScreenDc = 0;
        }
        _captureWake.Dispose();
    }

    private void RefreshCaptureSnapshot()
    {
        nint foreground = SneakNativeMethods.GetForegroundWindow();
        var windows = SneakWindowFinder.FindAllWindowsUnderneath(foreground, _hwnd);

        if (windows.Count == 0)
        {
            lock (_snapshotGate)
            {
                _latestFrames = [];
                _latestActiveIdx = 0;
                _latestTotalCount = 0;
            }
            return;
        }

        bool showAll = _showAllWindows;
        if (showAll)
        {
            var frames = new List<CapturedWindowFrame>(windows.Count);
            // Capture back-to-front so compositing order matches desktop Z-order.
            for (int i = windows.Count - 1; i >= 0; i--)
            {
                var win = windows[i];
                var frame = CaptureWindowToFrame(win);
                if (frame is not null)
                {
                    frames.Add(frame);
                }
            }

            lock (_snapshotGate)
            {
                _latestFrames = frames;
                _latestActiveIdx = 0;
                _latestTotalCount = windows.Count;
            }
            return;
        }

        int curX = Volatile.Read(ref _cursorX);
        int curY = Volatile.Read(ref _cursorY);
        int activeIdx = 0;

        lock (_snapshotGate)
        {
            if (_lockedTargetHwnd != 0)
            {
                bool found = false;
                for (int i = 0; i < windows.Count; i++)
                {
                    if (windows[i].Handle == _lockedTargetHwnd)
                    {
                        activeIdx = i;
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    activeIdx = 0;
                    _lockedTargetHwnd = windows[0].Handle;
                }
            }
            else
            {
                for (int i = 0; i < windows.Count; i++)
                {
                    if (windows[i].ExtendedBounds.Contains(curX, curY))
                    {
                        activeIdx = i;
                        break;
                    }
                }
                _lockedTargetHwnd = windows[activeIdx].Handle;
            }

            if (_pendingCycleDelta != 0)
            {
                activeIdx = ((activeIdx + _pendingCycleDelta) % windows.Count + windows.Count) % windows.Count;
                _lockedTargetHwnd = windows[activeIdx].Handle;
                _pendingCycleDelta = 0;
            }
        }

        var target = windows[activeIdx];
        var singleFrame = CaptureWindowToFrame(target);

        lock (_snapshotGate)
        {
            _latestFrames = singleFrame is not null ? [singleFrame] : [];
            _latestActiveIdx = activeIdx;
            _latestTotalCount = windows.Count;
        }
    }

    private CapturedWindowFrame? CaptureWindowToFrame(SneakTargetWindow win)
    {
        int w = win.WindowRect.Width;
        int h = win.WindowRect.Height;
        if (w <= 0 || h <= 0)
        {
            return null;
        }

        if (!CaptureTargetWindow(win.Handle, w, h) || _capPixels == null)
        {
            return null;
        }

        int len = w * h;
        if (!_pixelPool.TryGetValue(win.Handle, out uint[]? pixels) || pixels.Length != len)
        {
            pixels = new uint[len];
            _pixelPool[win.Handle] = pixels;
        }

        // Copy raw PrintWindow pixels to managed buffer (still on background capture thread).
        fixed (uint* dst = pixels)
        {
            Buffer.MemoryCopy(_capPixels, dst, (long)len * 4, (long)len * 4);
        }

        return new CapturedWindowFrame
        {
            Handle = win.Handle,
            Left = win.WindowRect.Left,
            Top = win.WindowRect.Top,
            Width = w,
            Height = h,
            ProcessName = win.ProcessName,
            Pixels = pixels
        };
    }

    private nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case WmSneakToggle:
            {
                bool showAll = wParam != 0;
                if (_isActive && !_isFadingOut)
                {
                    if (_showAllWindows != showAll)
                    {
                        _showAllWindows = showAll;
                        lock (_snapshotGate)
                        {
                            _lockedTargetHwnd = 0;
                            _latestFrames = [];
                        }
                        _captureWake.Set();
                        RenderFrame();
                    }
                    else
                    {
                        BeginFadeOut();
                    }
                }
                else
                {
                    _showAllWindows = showAll;
                    Activate();
                }
                return 0;
            }

            case WmSneakShow:
                _showAllWindows = wParam != 0;
                Activate();
                return 0;

            case WmSneakHide:
                if (_isActive && !_isFadingOut) BeginFadeOut();
                return 0;

            case WmSneakResize:
                if (_isActive)
                {
                    int delta = unchecked((int)wParam);
                    _orbDiameter = Math.Clamp(_orbDiameter + delta, MinOrbDiameter, MaxOrbDiameter);
                    EnsureAlphaLut(_orbDiameter);
                    RenderFrame();
                }
                return 0;

            case WmSneakCycle:
                if (_isActive && !_showAllWindows)
                {
                    lock (_snapshotGate)
                    {
                        _pendingCycleDelta += unchecked((int)wParam);
                    }
                    _captureWake.Set();
                }
                return 0;

            case WmSneakMove:
                Interlocked.Exchange(ref _movePending, 0);
                if (_isActive)
                {
                    RenderFrame();
                }
                return 0;

            case WmSneakFrameReady:
                Interlocked.Exchange(ref _framePending, 0);
                if (_isActive)
                {
                    RenderFrame();
                }
                return 0;

            case SneakNativeMethods.WM_TIMER when (nuint)wParam == TimerId:
                RenderTick();
                return 0;
        }

        return SneakNativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void Activate()
    {
        if (_isActive && !_isFadingOut)
        {
            return;
        }

        if (SneakNativeMethods.GetCursorPos(out var pt))
        {
            Volatile.Write(ref _cursorX, pt.X);
            Volatile.Write(ref _cursorY, pt.Y);
        }

        lock (_snapshotGate)
        {
            _lockedTargetHwnd = 0;
            _pendingCycleDelta = 0;
            _latestFrames = [];
            _latestActiveIdx = 0;
            _latestTotalCount = 0;
        }

        _isActive = true;
        _isFadingOut = false;
        _opacity = Math.Max(0.2f, _opacity);
        _prevDirtyRect = default;

        EnsureAlphaLut(_orbDiameter);
        InstallRuntimeHooks();
        _captureWake.Set();
        RenderFrame();
        SneakNativeMethods.SetTimer(_hwnd, TimerId, TimerIntervalMs, 0);
    }

    private void BeginFadeOut()
    {
        if (!_isActive || _isFadingOut)
        {
            return;
        }

        _isFadingOut = true;
        UninstallRuntimeHooks();
    }

    private void FinishHide()
    {
        _isActive = false;
        _isFadingOut = false;
        _windowShown = false;
        _opacity = 0f;
        _lastRenderedX = int.MinValue;
        _lastRenderedY = int.MinValue;
        UninstallRuntimeHooks();
        SneakNativeMethods.KillTimer(_hwnd, TimerId);
        ClearRect(_prevDirtyRect);
        _prevDirtyRect = default;
        SneakNativeMethods.ShowWindow(_hwnd, SneakNativeMethods.SW_HIDE);

        lock (_snapshotGate)
        {
            _latestFrames = [];
        }
    }

    private void InstallRuntimeHooks()
    {
        nint mod = SneakNativeMethods.GetModuleHandle(null);
        if (_mouseHook == 0 && _mouseProc is not null)
        {
            _mouseHook = SneakNativeMethods.SetWindowsHookEx(SneakNativeMethods.WH_MOUSE_LL, _mouseProc, mod, 0);
        }

        if (_keyboardHook == 0 && _keyboardProc is not null)
        {
            _keyboardHook = SneakNativeMethods.SetWindowsHookEx(SneakNativeMethods.WH_KEYBOARD_LL, _keyboardProc, mod, 0);
        }
    }

    private void UninstallRuntimeHooks()
    {
        if (_mouseHook != 0)
        {
            SneakNativeMethods.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = 0;
        }

        if (_keyboardHook != 0)
        {
            SneakNativeMethods.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = 0;
        }
    }

    private nint MouseHookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0 && _isActive && !_isFadingOut)
        {
            int msg = (int)wParam;
            if (msg == SneakNativeMethods.WM_MOUSEMOVE)
            {
                if (Interlocked.Exchange(ref _movePending, 1) == 0)
                {
                    SneakNativeMethods.PostMessage(_hwnd, WmSneakMove, 0, 0);
                }
            }
            else if (msg == SneakNativeMethods.WM_MOUSEWHEEL)
            {
                var ms = Marshal.PtrToStructure<SneakNativeMethods.MSLLHOOKSTRUCT>(lParam);
                short wheelDelta = unchecked((short)((ms.mouseData >> 16) & 0xFFFF));
                if (wheelDelta != 0)
                {
                    int step = wheelDelta > 0 ? ResizeStep : -ResizeStep;
                    SneakNativeMethods.PostMessage(_hwnd, WmSneakResize, step, 0);
                    return 1;
                }
            }
            else if (msg == SneakNativeMethods.WM_MBUTTONDOWN)
            {
                bool shift = (SneakNativeMethods.GetAsyncKeyState(SneakNativeMethods.VK_SHIFT) & 0x8000) != 0;
                SneakNativeMethods.PostMessage(_hwnd, WmSneakCycle, shift ? -1 : 1, 0);
                return 1;
            }
        }

        return SneakNativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private nint KeyboardHookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0 && _isActive && !_isFadingOut)
        {
            int msg = (int)wParam;
            if (msg == SneakNativeMethods.WM_KEYDOWN || msg == SneakNativeMethods.WM_SYSKEYDOWN)
            {
                var kbd = Marshal.PtrToStructure<SneakNativeMethods.KBDLLHOOKSTRUCT>(lParam);
                int vk = (int)kbd.vkCode;

                switch (vk)
                {
                    case SneakNativeMethods.VK_ESCAPE:
                        SneakNativeMethods.PostMessage(_hwnd, WmSneakHide, 0, 0);
                        return 1;

                    case SneakNativeMethods.VK_TAB:
                    {
                        bool shift = (SneakNativeMethods.GetAsyncKeyState(SneakNativeMethods.VK_SHIFT) & 0x8000) != 0;
                        SneakNativeMethods.PostMessage(_hwnd, WmSneakCycle, shift ? -1 : 1, 0);
                        return 1;
                    }

                    case SneakNativeMethods.VK_RIGHT:
                        SneakNativeMethods.PostMessage(_hwnd, WmSneakCycle, 1, 0);
                        return 1;

                    case SneakNativeMethods.VK_LEFT:
                        SneakNativeMethods.PostMessage(_hwnd, WmSneakCycle, -1, 0);
                        return 1;

                    case SneakNativeMethods.VK_OEM_PLUS:
                    case SneakNativeMethods.VK_ADD:
                        SneakNativeMethods.PostMessage(_hwnd, WmSneakResize, ResizeStep, 0);
                        return 1;

                    case SneakNativeMethods.VK_OEM_MINUS:
                    case SneakNativeMethods.VK_SUBTRACT:
                        SneakNativeMethods.PostMessage(_hwnd, WmSneakResize, -ResizeStep, 0);
                        return 1;
                }
            }
        }

        return SneakNativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private void RenderTick()
    {
        if (!_isActive)
        {
            return;
        }

        bool animating = false;
        if (_isFadingOut)
        {
            _opacity -= 0.14f;
            if (_opacity <= 0.01f)
            {
                FinishHide();
                return;
            }
            animating = true;
        }
        else if (_opacity < 1.0f)
        {
            _opacity = Math.Min(1.0f, _opacity + 0.14f);
            animating = true;
        }

        if (SneakNativeMethods.GetCursorPos(out var pt))
        {
            if (pt.X != _lastRenderedX || pt.Y != _lastRenderedY || animating)
            {
                RenderFrame();
            }
        }
        else if (animating)
        {
            RenderFrame();
        }
    }

    private void RenderFrame()
    {
        if (!_isActive || _orbPixels == null || _hwnd == 0)
        {
            return;
        }

        if (!SneakNativeMethods.GetCursorPos(out var pt))
        {
            return;
        }

        int cursorX = pt.X;
        int cursorY = pt.Y;
        Volatile.Write(ref _cursorX, cursorX);
        Volatile.Write(ref _cursorY, cursorY);
        _lastRenderedX = cursorX;
        _lastRenderedY = cursorY;

        List<CapturedWindowFrame> frames;
        int activeIdx;
        int totalCount;
        lock (_snapshotGate)
        {
            frames = _latestFrames;
            activeIdx = _latestActiveIdx;
            totalCount = _latestTotalCount;
        }

        int d = _orbDiameter;
        int half = d / 2;
        int localX = cursorX - _virtX;
        int localY = cursorY - _virtY;

        var currRect = new SneakNativeMethods.RECT
        {
            Left = Math.Max(0, localX - half),
            Top = Math.Max(0, localY - half),
            Right = Math.Min(_virtW, localX + half),
            Bottom = Math.Min(_virtH, localY + half + LabelFooter)
        };

        ClearRect(_prevDirtyRect);
        ComposeOrbFromFrames(cursorX, cursorY, frames);

        string label;
        if (totalCount == 0 || frames.Count == 0)
        {
            label = "No window underneath";
        }
        else if (_showAllWindows)
        {
            label = $"All Behind ({totalCount})";
        }
        else
        {
            var f = frames[0];
            string name = string.IsNullOrWhiteSpace(f.ProcessName) ? "Window" : f.ProcessName;
            label = totalCount > 1 ? $"{name} ({activeIdx + 1}/{totalCount})" : name;
        }

        DrawLabelPill(label, localX, localY + half + 2f);

        var dirtyRect = UnionRect(_prevDirtyRect, currRect);
        _prevDirtyRect = currRect;
        PresentOrbDirty(dirtyRect);
    }

    private void ClearRect(SneakNativeMethods.RECT rect)
    {
        if (_orbPixels == null || rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        int x0 = Math.Max(0, rect.Left);
        int y0 = Math.Max(0, rect.Top);
        int x1 = Math.Min(_virtW, rect.Right);
        int y1 = Math.Min(_virtH, rect.Bottom);
        int w = x1 - x0;
        if (w <= 0 || y0 >= y1)
        {
            return;
        }

        for (int y = y0; y < y1; y++)
        {
            new Span<uint>(_orbPixels + y * _virtW + x0, w).Clear();
        }
    }

    private static SneakNativeMethods.RECT UnionRect(SneakNativeMethods.RECT a, SneakNativeMethods.RECT b)
    {
        if (a.Width <= 0 || a.Height <= 0) return b;
        if (b.Width <= 0 || b.Height <= 0) return a;
        return new SneakNativeMethods.RECT
        {
            Left = Math.Min(a.Left, b.Left),
            Top = Math.Min(a.Top, b.Top),
            Right = Math.Max(a.Right, b.Right),
            Bottom = Math.Max(a.Bottom, b.Bottom)
        };
    }

    private void ComposeOrbFromFrames(int cursorX, int cursorY, List<CapturedWindowFrame> frames)
    {
        if (frames.Count == 0 || _alphaLut.Length == 0)
        {
            return;
        }

        int d = _orbDiameter;
        int half = d / 2;
        int orbScreenLeft = cursorX - half;
        int orbScreenTop = cursorY - half;
        int orbSurfLeft = orbScreenLeft - _virtX;
        int orbSurfTop = orbScreenTop - _virtY;
        uint opacityScale = (uint)Math.Clamp((int)(_opacity * 256f), 0, 256);
        if (opacityScale == 0)
        {
            return;
        }

        // Paint back-to-front using precomputed radial alpha LUT and integer bitshifts (zero Sqrt/float math).
        for (int f = 0; f < frames.Count; f++)
        {
            var win = frames[f];
            int winLeft = win.Left;
            int winTop = win.Top;
            int winW = win.Width;
            int winH = win.Height;

            int x0 = Math.Max(0, Math.Max(winLeft - orbScreenLeft, -orbSurfLeft));
            int y0 = Math.Max(0, Math.Max(winTop - orbScreenTop, -orbSurfTop));
            int x1 = Math.Min(d, Math.Min(winLeft + winW - orbScreenLeft, _virtW - orbSurfLeft));
            int y1 = Math.Min(d, Math.Min(winTop + winH - orbScreenTop, _virtH - orbSurfTop));

            if (x0 >= x1 || y0 >= y1)
            {
                continue;
            }

            fixed (uint* srcPtr = win.Pixels)
            fixed (byte* lutPtr = _alphaLut)
            {
                for (int y = y0; y < y1; y++)
                {
                    int srcY = orbScreenTop + y - winTop;
                    int lutRow = y * d;
                    int dstRow = (orbSurfTop + y) * _virtW + orbSurfLeft;
                    int srcRow = srcY * winW;
                    int srcXBase = orbScreenLeft - winLeft;

                    for (int x = x0; x < x1; x++)
                    {
                        byte lutA = lutPtr[lutRow + x];
                        if (lutA == 0)
                        {
                            continue;
                        }

                        uint ba = (lutA * opacityScale) >> 8;
                        if (ba == 0)
                        {
                            continue;
                        }

                        uint src = srcPtr[srcRow + (srcXBase + x)];
                        if (ba == 255)
                        {
                            _orbPixels[dstRow + x] = 0xFF000000u | (src & 0x00FFFFFFu);
                        }
                        else
                        {
                            uint r = (((src >> 16) & 0xFFu) * ba) >> 8;
                            uint g = (((src >> 8) & 0xFFu) * ba) >> 8;
                            uint b = ((src & 0xFFu) * ba) >> 8;
                            _orbPixels[dstRow + x] = (ba << 24) | (r << 16) | (g << 8) | b;
                        }
                    }
                }
            }
        }
    }

    private void DrawLabelPill(string text, float centerX, float pillY)
    {
        if (string.IsNullOrWhiteSpace(text) || _orbDc == 0 || pillY + 32f > _virtH)
        {
            return;
        }

        if (SneakNativeMethods.GdipCreateFromHDC(_orbDc, out nint graphics) != 0)
        {
            return;
        }

        try
        {
            SneakNativeMethods.GdipSetSmoothingMode(graphics, 4);

            if (SneakNativeMethods.GdipCreateFontFamilyFromName("Segoe UI", 0, out nint family) != 0)
            {
                return;
            }

            try
            {
                if (SneakNativeMethods.GdipCreateFont(family, 15.5f, 1, 2, out nint font) != 0)
                {
                    return;
                }

                try
                {
                    var layout = new SneakNativeMethods.RectF { X = 0, Y = 0, Width = _orbDiameter - 12, Height = 34 };
                    SneakNativeMethods.GdipMeasureString(graphics, text, text.Length, font, ref layout, 0, out var bounds, out _, out _);

                    float pillW = Math.Min(_orbDiameter - 12f, bounds.Width + 28f);
                    float pillH = 30f;
                    float pillX = Math.Clamp(centerX - pillW / 2f, 4f, Math.Max(4f, _virtW - pillW - 4f));

                    byte bgAlpha = (byte)(232 * _opacity);
                    byte fgAlpha = (byte)(255 * _opacity);
                    uint bgColor = ((uint)bgAlpha << 24) | 0x0012141E;
                    uint fgColor = ((uint)fgAlpha << 24) | 0x00FFFFFF;

                    FillRoundedRect(graphics, pillX, pillY, pillW, pillH, 15f, bgColor);

                    if (SneakNativeMethods.GdipCreateSolidFill(fgColor, out nint textBrush) == 0)
                    {
                        var textRect = new SneakNativeMethods.RectF
                        {
                            X = pillX + 14f,
                            Y = pillY + 4f,
                            Width = pillW - 22f,
                            Height = pillH - 4f
                        };
                        SneakNativeMethods.GdipDrawString(graphics, text, text.Length, font, ref textRect, 0, textBrush);
                        SneakNativeMethods.GdipDeleteBrush(textBrush);
                    }
                }
                finally
                {
                    SneakNativeMethods.GdipDeleteFont(font);
                }
            }
            finally
            {
                SneakNativeMethods.GdipDeleteFontFamily(family);
            }
        }
        finally
        {
            SneakNativeMethods.GdipDeleteGraphics(graphics);
        }
    }

    private static void FillRoundedRect(nint graphics, float x, float y, float w, float h, float r, uint argb)
    {
        if (SneakNativeMethods.GdipCreatePath(0, out nint path) != 0)
        {
            return;
        }

        try
        {
            float d = r * 2f;
            SneakNativeMethods.GdipAddPathArc(path, x, y, d, d, 180, 90);
            SneakNativeMethods.GdipAddPathArc(path, x + w - d, y, d, d, 270, 90);
            SneakNativeMethods.GdipAddPathArc(path, x + w - d, y + h - d, d, d, 0, 90);
            SneakNativeMethods.GdipAddPathArc(path, x, y + h - d, d, d, 90, 90);
            SneakNativeMethods.GdipClosePathFigure(path);

            if (SneakNativeMethods.GdipCreateSolidFill(argb, out nint brush) == 0)
            {
                SneakNativeMethods.GdipFillPath(graphics, brush, path);
                SneakNativeMethods.GdipDeleteBrush(brush);
            }
        }
        finally
        {
            SneakNativeMethods.GdipDeletePath(path);
        }
    }

    private void PresentOrbDirty(SneakNativeMethods.RECT dirtyRect)
    {
        if (dirtyRect.Width <= 0 || dirtyRect.Height <= 0)
        {
            return;
        }

        var dst = new SneakNativeMethods.POINT { X = _virtX, Y = _virtY };
        var size = new SneakNativeMethods.SIZE { cx = _virtW, cy = _virtH };
        var src = new SneakNativeMethods.POINT { X = 0, Y = 0 };
        var blend = new SneakNativeMethods.BLENDFUNCTION
        {
            BlendOp = SneakNativeMethods.AC_SRC_OVER,
            BlendFlags = 0,
            SourceConstantAlpha = 255,
            AlphaFormat = SneakNativeMethods.AC_SRC_ALPHA
        };

        var info = new SneakNativeMethods.UPDATELAYEREDWINDOWINFO
        {
            cbSize = (uint)Marshal.SizeOf<SneakNativeMethods.UPDATELAYEREDWINDOWINFO>(),
            hdcDst = _screenDc,
            pptDst = (nint)(&dst),
            psize = (nint)(&size),
            hdcSrc = _orbDc,
            pptSrc = (nint)(&src),
            crKey = 0,
            pblend = (nint)(&blend),
            dwFlags = SneakNativeMethods.ULW_ALPHA,
            prcDirty = (nint)(&dirtyRect)
        };

        SneakNativeMethods.UpdateLayeredWindowIndirect(_hwnd, ref info);
        if (!_windowShown)
        {
            _windowShown = true;
            SneakNativeMethods.ShowWindow(_hwnd, SneakNativeMethods.SW_SHOWNOACTIVATE);
        }
    }

    private bool CaptureTargetWindow(nint targetHwnd, int width, int height)
    {
        if (width <= 0 || height <= 0 || _capScreenDc == 0)
        {
            return false;
        }

        if (_capDc == 0 || width != _capW || height != _capH)
        {
            ReleaseCaptureBuffer();

            _capDc = SneakNativeMethods.CreateCompatibleDC(_capScreenDc);
            if (_capDc == 0)
            {
                return false;
            }

            var bmi = new SneakNativeMethods.BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<SneakNativeMethods.BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height, // Top-down DIB
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0
            };

            _capBmp = SneakNativeMethods.CreateDIBSection(_capDc, ref bmi, 0, out nint bits, 0, 0);
            if (_capBmp == 0 || bits == 0)
            {
                ReleaseCaptureBuffer();
                return false;
            }

            _capOldBmp = SneakNativeMethods.SelectObject(_capDc, _capBmp);
            _capPixels = (uint*)bits;
            _capW = width;
            _capH = height;
        }

        return SneakNativeMethods.PrintWindow(targetHwnd, _capDc, SneakNativeMethods.PW_RENDERFULLCONTENT);
    }

    private void InitVirtualOrbBuffer()
    {
        ReleaseOrbBuffer();

        _orbDc = SneakNativeMethods.CreateCompatibleDC(_screenDc);
        var bmi = new SneakNativeMethods.BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<SneakNativeMethods.BITMAPINFOHEADER>(),
            biWidth = _virtW,
            biHeight = -_virtH, // Top-down DIB
            biPlanes = 1,
            biBitCount = 32,
            biCompression = 0
        };

        _orbBmp = SneakNativeMethods.CreateDIBSection(_orbDc, ref bmi, 0, out nint bits, 0, 0);
        if (_orbBmp != 0 && bits != 0)
        {
            _orbOldBmp = SneakNativeMethods.SelectObject(_orbDc, _orbBmp);
            _orbPixels = (uint*)bits;
        }
    }

    private void EnsureAlphaLut(int diameter)
    {
        if (_alphaLutDiameter == diameter && _alphaLut.Length == diameter * diameter)
        {
            return;
        }

        var lut = new byte[diameter * diameter];
        float radius = diameter / 2f;
        float fadeStart = radius * 0.82f;
        float fadeSpan = Math.Max(1f, radius - fadeStart);

        for (int y = 0; y < diameter; y++)
        {
            float dy = y - radius;
            int row = y * diameter;
            for (int x = 0; x < diameter; x++)
            {
                float dx = x - radius;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                if (dist >= radius)
                {
                    continue;
                }

                float a = 1f;
                if (dist > fadeStart)
                {
                    float t = Math.Clamp((dist - fadeStart) / fadeSpan, 0f, 1f);
                    a = 1f - (t * t * (3f - 2f * t));
                }
                lut[row + x] = (byte)Math.Clamp((int)MathF.Round(255f * a), 0, 255);
            }
        }

        _alphaLut = lut;
        _alphaLutDiameter = diameter;
    }

    private void ReleaseOrbBuffer()
    {
        if (_orbDc != 0)
        {
            if (_orbOldBmp != 0) SneakNativeMethods.SelectObject(_orbDc, _orbOldBmp);
            if (_orbBmp != 0) SneakNativeMethods.DeleteObject(_orbBmp);
            SneakNativeMethods.DeleteDC(_orbDc);
            _orbDc = 0;
            _orbBmp = 0;
            _orbOldBmp = 0;
            _orbPixels = null;
        }
    }

    private void ReleaseCaptureBuffer()
    {
        if (_capDc != 0)
        {
            if (_capOldBmp != 0) SneakNativeMethods.SelectObject(_capDc, _capOldBmp);
            if (_capBmp != 0) SneakNativeMethods.DeleteObject(_capBmp);
            SneakNativeMethods.DeleteDC(_capDc);
            _capDc = 0;
            _capBmp = 0;
            _capOldBmp = 0;
            _capPixels = null;
            _capW = 0;
            _capH = 0;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _isActive = false;
        _captureWake.Set();

        if (_threadId != 0)
        {
            SneakNativeMethods.PostThreadMessage(_threadId, SneakNativeMethods.WM_QUIT, 0, 0);
        }
    }
}
