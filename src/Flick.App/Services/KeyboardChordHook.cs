using System.Runtime.InteropServices;
using Flick.Core.Keyboard;
using Flick.Core.Models;

namespace Flick.App.Services;

/// <summary>
/// Global low-level keyboard hook (WH_KEYBOARD_LL) running on a dedicated message-loop thread.
/// Buffers simultaneous chord keys within a short window and suppresses matched chord keystrokes.
/// </summary>
public sealed class KeyboardChordHook : IDisposable
{
    private const nuint FlickInjectedTag = 0x464C4B43; // "FLKC"

    private readonly KeyboardChordEngine _engine = new();
    private readonly ForegroundWindowService _foreground = new();
    private readonly object _gate = new();
    private readonly Timer _flushTimer;

    private Thread? _hookThread;
    private uint _hookThreadId;
    private nint _hookHandle;
    private NativeMethods.LowLevelKeyboardProc? _proc;
    private bool _disposed;

    public bool IsEnabled { get; set; } = true;
    public int SimultaneousWindowMs { get; set; } = 50;
    public int SequenceTimeoutMs { get; set; } = 1000;
    public Func<IReadOnlyList<GestureRule>>? GetRules { get; set; }
    public Func<bool>? ShouldIgnoreCurrentWindow { get; set; }

    /// <summary>
    /// Optional recording callback: (vkCode, isKeyDown, activeModifiers) => true to swallow key during recording.
    /// </summary>
    public Func<int, bool, ChordModifiers, bool>? RecordKeyInterceptor { get; set; }

    public event Action<GestureRule, ForegroundAppInfo>? ChordMatched;
    public event Action<string>? SequencePending;

    public KeyboardChordHook()
    {
        _flushTimer = new Timer(OnFlushTimer, null, Timeout.Infinite, Timeout.Infinite);
    }

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
            Name = "Flick.KeyboardHook"
        };
        _hookThread.Start();
    }

    public void ResetState()
    {
        lock (_gate)
        {
            _engine.Reset();
            _flushTimer.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    private void HookThreadLoop()
    {
        _hookThreadId = NativeMethods.GetCurrentThreadId();
        nint moduleHandle = NativeMethods.GetModuleHandle(null);
        _hookHandle = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _proc!, moduleHandle, 0);

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
        if (nCode >= 0)
        {
            var kbd = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);

            // Never intercept our own re-injected keys or synthetic ActionExecutor hotkeys.
            if ((kbd.flags & NativeMethods.LLKHF_INJECTED) != 0 || kbd.dwExtraInfo == FlickInjectedTag)
            {
                return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
            }

            int msg = (int)wParam;
            bool isKeyDown = msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN;
            bool isKeyUp = msg == NativeMethods.WM_KEYUP || msg == NativeMethods.WM_SYSKEYUP;

            if (!isKeyDown && !isKeyUp)
            {
                return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
            }

            int vk = (int)kbd.vkCode;
            var mods = GetCurrentModifiers();

            var recorder = RecordKeyInterceptor;
            if (recorder is not null)
            {
                if (recorder(vk, isKeyDown, mods))
                {
                    return 1;
                }
                return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
            }

            if (IsEnabled && ShouldIgnoreCurrentWindow?.Invoke() != true)
            {
                var rules = GetRules?.Invoke() ?? [];
                var fg = _foreground.GetForegroundApp();
                long nowMs = Environment.TickCount64;

                ChordProcessResult result;
                lock (_gate)
                {
                    result = _engine.ProcessKeyEvent(
                        vk,
                        isKeyDown,
                        mods,
                        nowMs,
                        rules,
                        fg.ProcessName,
                        fg.WindowTitle,
                        SimultaneousWindowMs,
                        SequenceTimeoutMs);

                    if (_engine.HasPendingSimultaneous)
                    {
                        _flushTimer.Change(SimultaneousWindowMs + 8, Timeout.Infinite);
                    }
                    else
                    {
                        _flushTimer.Change(Timeout.Infinite, Timeout.Infinite);
                    }
                }

                if (result.KeysToReplayDown.Count > 0)
                {
                    ReplayKeysDown(result.KeysToReplayDown);
                }

                if (result.MatchedRule is not null)
                {
                    var matchedRule = result.MatchedRule;
                    _ = Task.Run(() => ChordMatched?.Invoke(matchedRule, fg));
                }
                else if (result.PendingSequenceLabel is not null)
                {
                    string label = result.PendingSequenceLabel;
                    _ = Task.Run(() => SequencePending?.Invoke(label));
                }

                if (result.SuppressCurrentEvent)
                {
                    return 1;
                }
            }
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private void OnFlushTimer(object? state)
    {
        IReadOnlyList<int> toReplay;
        lock (_gate)
        {
            toReplay = _engine.FlushExpiredSimultaneous(Environment.TickCount64);
        }

        if (toReplay.Count > 0)
        {
            ReplayKeysDown(toReplay);
        }
    }

    private static ChordModifiers GetCurrentModifiers()
    {
        ChordModifiers mods = ChordModifiers.None;
        if ((NativeMethods.GetAsyncKeyState(ChordKeyHelper.VkControl) & 0x8000) != 0) mods |= ChordModifiers.Ctrl;
        if ((NativeMethods.GetAsyncKeyState(ChordKeyHelper.VkMenu) & 0x8000) != 0) mods |= ChordModifiers.Alt;
        if ((NativeMethods.GetAsyncKeyState(ChordKeyHelper.VkShift) & 0x8000) != 0) mods |= ChordModifiers.Shift;
        if ((NativeMethods.GetAsyncKeyState(ChordKeyHelper.VkLWin) & 0x8000) != 0 ||
            (NativeMethods.GetAsyncKeyState(ChordKeyHelper.VkRWin) & 0x8000) != 0)
        {
            mods |= ChordModifiers.Win;
        }
        return mods;
    }

    private static void ReplayKeysDown(IReadOnlyList<int> vkCodes)
    {
        var inputs = new NativeMethods.INPUT[vkCodes.Count];
        for (int i = 0; i < vkCodes.Count; i++)
        {
            inputs[i] = new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_KEYBOARD,
                u = new NativeMethods.INPUTUNION
                {
                    ki = new NativeMethods.KEYBDINPUT
                    {
                        wVk = (ushort)vkCodes[i],
                        dwFlags = 0,
                        dwExtraInfo = FlickInjectedTag
                    }
                }
            };
        }

        NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _flushTimer.Dispose();
        if (_hookThreadId != 0)
        {
            NativeMethods.PostThreadMessage(_hookThreadId, NativeMethods.WM_QUIT, 0, 0);
        }
    }
}
