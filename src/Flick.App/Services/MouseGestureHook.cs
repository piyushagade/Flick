using System.Runtime.InteropServices;
using Flick.Core.Models;
using Flick.Core.Recognition;

namespace Flick.App.Services;

/// <summary>
/// Global low-level mouse hook running on a dedicated message-loop thread.
/// Buffers the trigger button down until drag exceeds MinimumStrokeLength;
/// if released early, replays a normal click so context menus work undisturbed.
/// </summary>
public sealed class MouseGestureHook : IDisposable
{
    private readonly List<GesturePoint> _points = [];
    private readonly object _gate = new();
    private Thread? _hookThread;
    private uint _hookThreadId;
    private nint _hookHandle;
    private NativeMethods.LowLevelMouseProc? _proc;

    private bool _isButtonDown;
    private bool _isGestureActive;
    private GesturePoint _downPoint;
    private uint _downMouseData;
    private bool _disposed;

    public bool IsEnabled { get; set; } = true;
    public TriggerButton TriggerButton { get; set; } = TriggerButton.Right;
    public double MinimumStrokeLength { get; set; } = 28.0;
    public Func<bool>? ShouldIgnoreCurrentWindow { get; set; }

    public event Action<GesturePoint>? StrokeStarted;
    public event Action<IReadOnlyList<GesturePoint>>? StrokeUpdated;
    public event Action<IReadOnlyList<GesturePoint>>? StrokeCompleted;
    public event Action? StrokeCancelled;

    public void Start()
    {
        if (_hookThread is not null)
        {
            return;
        }

        _proc = HookCallback;
        _hookThread = new Thread(HookThreadLoop)
        {
            IsBackground = true,
            Name = "Flick.MouseHook"
        };
        _hookThread.Start();
    }

    private void HookThreadLoop()
    {
        _hookThreadId = NativeMethods.GetCurrentThreadId();
        nint moduleHandle = NativeMethods.GetModuleHandle(null);
        _hookHandle = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _proc!, moduleHandle, 0);

        while (NativeMethods.GetMessage(out var msg, 0, 0, 0) > 0)
        {
            NativeMethods.TranslateMessage(ref msg);
            NativeMethods.DispatchMessage(ref msg);
        }

        if (_hookHandle != 0)
        {
            NativeMethods.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = 0;
        }
    }

    private nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0 && IsEnabled)
        {
            var hookStruct = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);

            // Never intercept our own replayed clicks.
            if ((hookStruct.flags & NativeMethods.LLMHF_INJECTED) == 0)
            {
                int msg = (int)wParam;

                if (IsTriggerDown(msg, hookStruct.mouseData))
                {
                    if (ShouldIgnoreCurrentWindow?.Invoke() == true)
                    {
                        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
                    }

                    lock (_gate)
                    {
                        _isButtonDown = true;
                        _isGestureActive = false;
                        _downPoint = new GesturePoint(hookStruct.pt.X, hookStruct.pt.Y);
                        _downMouseData = hookStruct.mouseData;
                        _points.Clear();
                        _points.Add(_downPoint);
                    }

                    // Swallow initial button-down; we replay if user releases without dragging.
                    return 1;
                }

                if (msg == NativeMethods.WM_MOUSEMOVE && _isButtonDown)
                {
                    var pt = new GesturePoint(hookStruct.pt.X, hookStruct.pt.Y);
                    bool becameActive = false;
                    GesturePoint[] snapshot;

                    lock (_gate)
                    {
                        if (_points.Count == 0 ||
                            Math.Abs(_points[^1].X - pt.X) >= 2 ||
                            Math.Abs(_points[^1].Y - pt.Y) >= 2)
                        {
                            _points.Add(pt);
                        }

                        if (!_isGestureActive && GestureRecognizer.ComputePathLength(_points) >= MinimumStrokeLength)
                        {
                            _isGestureActive = true;
                            becameActive = true;
                        }

                        snapshot = [.. _points];
                    }

                    if (becameActive)
                    {
                        StrokeStarted?.Invoke(_downPoint);
                    }

                    if (_isGestureActive)
                    {
                        StrokeUpdated?.Invoke(snapshot);
                    }
                }

                if (IsTriggerUp(msg, hookStruct.mouseData) && _isButtonDown)
                {
                    bool wasGesture;
                    GesturePoint[] snapshot;

                    lock (_gate)
                    {
                        wasGesture = _isGestureActive;
                        _isButtonDown = false;
                        _isGestureActive = false;
                        snapshot = [.. _points];
                        _points.Clear();
                    }

                    if (wasGesture)
                    {
                        StrokeCompleted?.Invoke(snapshot);
                    }
                    else
                    {
                        StrokeCancelled?.Invoke();
                        ReplayClick(TriggerButton, _downMouseData);
                    }

                    return 1;
                }
            }
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private bool IsTriggerDown(int msg, uint mouseData) => TriggerButton switch
    {
        TriggerButton.Right => msg == NativeMethods.WM_RBUTTONDOWN,
        TriggerButton.Middle => msg == NativeMethods.WM_MBUTTONDOWN,
        TriggerButton.XButton1 => msg == NativeMethods.WM_XBUTTONDOWN && ((mouseData >> 16) == 1),
        TriggerButton.XButton2 => msg == NativeMethods.WM_XBUTTONDOWN && ((mouseData >> 16) == 2),
        _ => false
    };

    private bool IsTriggerUp(int msg, uint mouseData) => TriggerButton switch
    {
        TriggerButton.Right => msg == NativeMethods.WM_RBUTTONUP,
        TriggerButton.Middle => msg == NativeMethods.WM_MBUTTONUP,
        TriggerButton.XButton1 => msg == NativeMethods.WM_XBUTTONUP && ((mouseData >> 16) == 1),
        TriggerButton.XButton2 => msg == NativeMethods.WM_XBUTTONUP && ((mouseData >> 16) == 2),
        _ => false
    };

    private static void ReplayClick(TriggerButton button, uint mouseData)
    {
        (uint downFlag, uint upFlag, uint data) = button switch
        {
            TriggerButton.Right => (NativeMethods.MOUSEEVENTF_RIGHTDOWN, NativeMethods.MOUSEEVENTF_RIGHTUP, 0u),
            TriggerButton.Middle => (NativeMethods.MOUSEEVENTF_MIDDLEDOWN, NativeMethods.MOUSEEVENTF_MIDDLEUP, 0u),
            TriggerButton.XButton1 or TriggerButton.XButton2 =>
                (NativeMethods.MOUSEEVENTF_XDOWN, NativeMethods.MOUSEEVENTF_XUP, mouseData >> 16),
            _ => (0u, 0u, 0u)
        };

        if (downFlag == 0)
        {
            return;
        }

        var inputs = new NativeMethods.INPUT[2];
        inputs[0] = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_MOUSE,
            u = new NativeMethods.INPUTUNION
            {
                mi = new NativeMethods.MOUSEINPUT { dwFlags = downFlag, mouseData = data }
            }
        };
        inputs[1] = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_MOUSE,
            u = new NativeMethods.INPUTUNION
            {
                mi = new NativeMethods.MOUSEINPUT { dwFlags = upFlag, mouseData = data }
            }
        };

        _ = NativeMethods.SendInput(2, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        if (_hookThreadId != 0)
        {
            NativeMethods.PostThreadMessage(_hookThreadId, NativeMethods.WM_QUIT, 0, 0);
        }
    }
}
