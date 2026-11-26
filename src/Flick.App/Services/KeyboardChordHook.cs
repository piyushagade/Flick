using System.Runtime.InteropServices;
using Flick.Core.Keyboard;
using Flick.Core.Models;
using Flick.Core.TextExpansion;

namespace Flick.App.Services;

/// <summary>
/// Global low-level keyboard hook (WH_KEYBOARD_LL) running on a dedicated message-loop thread.
/// Buffers simultaneous chord keys within a short window, suppresses matched chord keystrokes,
/// and feeds keystrokes to the parallel text expansion matcher.
/// </summary>
public sealed class KeyboardChordHook : IDisposable
{
    public const nuint FlickInjectedTag = 0x464C4B43; // "FLKC"

    private readonly KeyboardChordEngine _engine = new();
    private readonly TextExpansionMatcher _expansionMatcher = new();
    private readonly ForegroundWindowService _foreground = new();
    private readonly object _gate = new();
    private readonly Timer _flushTimer;
    private readonly Timer _seqCommitTimer;
    private ForegroundAppInfo _pendingSeqForeground;
    private nint _lastForegroundHwnd;

    private Thread? _hookThread;
    private uint _hookThreadId;
    private nint _hookHandle;
    private NativeMethods.LowLevelKeyboardProc? _proc;
    private bool _disposed;

    public bool IsEnabled { get; set; } = true;
    public bool IsTextExpansionEnabled { get; set; } = true;
    public int SimultaneousWindowMs { get; set; } = 50;
    public int SequenceTimeoutMs { get; set; } = 1000;
    public Func<IReadOnlyList<GestureRule>>? GetRules { get; set; }
    public Func<IReadOnlyList<TextExpansionRule>>? GetTextExpansions { get; set; }
    public Func<bool>? ShouldIgnoreCurrentWindow { get; set; }

    /// <summary>
    /// Optional recording callback: (vkCode, isKeyDown, activeModifiers) => true to swallow key during recording.
    /// </summary>
    public Func<int, bool, ChordModifiers, bool>? RecordKeyInterceptor { get; set; }

    public event Action<GestureRule, ForegroundAppInfo>? ChordMatched;
    public event Action<string>? SequencePending;
    public event Action<TextExpansionRule, TextExpansionMatchResult, ForegroundAppInfo>? TextExpansionMatched;

    public KeyboardChordHook()
    {
        _flushTimer = new Timer(OnFlushTimer, null, Timeout.Infinite, Timeout.Infinite);
        _seqCommitTimer = new Timer(OnSeqCommitTimer, null, Timeout.Infinite, Timeout.Infinite);
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
            _expansionMatcher.Reset();
            _flushTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _seqCommitTimer.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    public void ResetTextExpansionBuffer()
    {
        lock (_gate)
        {
            _expansionMatcher.Reset();
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

            if ((IsEnabled || IsTextExpansionEnabled) && ShouldIgnoreCurrentWindow?.Invoke() != true)
            {
                var fg = _foreground.GetForegroundApp();
                long nowMs = Environment.TickCount64;

                bool suppressForChord = false;
                if (IsEnabled)
                {
                    var rules = GetRules?.Invoke() ?? [];
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

                        if (_engine.HasPendingSequenceCommit)
                        {
                            _pendingSeqForeground = fg;
                            _seqCommitTimer.Change(_engine.GetRemainingSequenceCommitMs(nowMs) + 8, Timeout.Infinite);
                        }
                        else
                        {
                            _seqCommitTimer.Change(Timeout.Infinite, Timeout.Infinite);
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

                    suppressForChord = result.SuppressCurrentEvent;
                }

                if (!suppressForChord && IsTextExpansionEnabled && isKeyDown)
                {
                    if (ProcessTextExpansionKeyDown(vk, kbd.scanCode, mods, fg))
                    {
                        return 1;
                    }
                }

                if (suppressForChord)
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

    private void OnSeqCommitTimer(object? state)
    {
        GestureRule? matchedRule;
        ForegroundAppInfo fg;
        lock (_gate)
        {
            matchedRule = _engine.FlushExpiredSequenceCommit(Environment.TickCount64);
            fg = _pendingSeqForeground;
        }

        if (matchedRule is not null)
        {
            _ = Task.Run(() => ChordMatched?.Invoke(matchedRule, fg));
        }
    }

    private bool ProcessTextExpansionKeyDown(int vk, uint scanCode, ChordModifiers mods, ForegroundAppInfo fg)
    {
        var rules = GetTextExpansions?.Invoke();
        if (rules is null || rules.Count == 0)
        {
            return false;
        }

        if (fg.WindowHandle != _lastForegroundHwnd)
        {
            _lastForegroundHwnd = fg.WindowHandle;
            lock (_gate)
            {
                _expansionMatcher.Reset();
            }
        }

        // Reset buffer on Ctrl/Win shortcuts (or Alt without AltGr).
        bool altGr = (NativeMethods.GetAsyncKeyState(0xA5) & 0x8000) != 0; // VK_RMENU
        if (mods.HasFlag(ChordModifiers.Ctrl) ||
            mods.HasFlag(ChordModifiers.Win) ||
            (mods.HasFlag(ChordModifiers.Alt) && !altGr))
        {
            lock (_gate)
            {
                _expansionMatcher.Reset();
            }
            return false;
        }

        // Backspace updates buffer; navigation/escape/delete keys reset it.
        if (vk == 0x08) // VK_BACK
        {
            lock (_gate)
            {
                _expansionMatcher.ProcessBackspace();
            }
            return false;
        }

        if (vk is 0x1B or (>= 0x21 and <= 0x28) or 0x2E)
        {
            lock (_gate)
            {
                _expansionMatcher.Reset();
            }
            return false;
        }

        if (!TryTranslateKeyToChar(vk, scanCode, fg.WindowHandle, out char ch))
        {
            return false;
        }

        TextExpansionMatchResult match;
        lock (_gate)
        {
            match = _expansionMatcher.ProcessCharacter(ch, rules, fg.ProcessName, fg.WindowTitle);
        }

        if (match.MatchedRule is not null)
        {
            var matched = match.MatchedRule;
            _ = Task.Run(() => TextExpansionMatched?.Invoke(matched, match, fg));
            return match.SuppressCurrentEvent;
        }

        return false;
    }

    private static bool TryTranslateKeyToChar(int vk, uint scanCode, nint fgHwnd, out char ch)
    {
        if (vk == 0x0D) { ch = '\n'; return true; }
        if (vk == 0x09) { ch = '\t'; return true; }
        if (vk == 0x20) { ch = ' '; return true; }

        // Ignore pure modifier keys.
        if (vk is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5 or 0x14)
        {
            ch = default;
            return false;
        }

        var state = new byte[256];
        if ((NativeMethods.GetAsyncKeyState(0x10) & 0x8000) != 0) state[0x10] = 0x80; // Shift
        if ((NativeMethods.GetKeyState(0x14) & 0x0001) != 0) state[0x14] = 0x01; // CapsLock
        if ((NativeMethods.GetAsyncKeyState(0xA5) & 0x8000) != 0)
        {
            state[0x11] = 0x80;
            state[0x12] = 0x80;
        }

        uint tid = fgHwnd != 0 ? NativeMethods.GetWindowThreadProcessId(fgHwnd, out _) : 0;
        nint hkl = NativeMethods.GetKeyboardLayout(tid);

        var buf = new char[4];
        // wFlags = 4 avoids mutating the active dead-key state on Windows 10 1607+.
        int count = NativeMethods.ToUnicodeEx((uint)vk, scanCode, state, buf, buf.Length, 4, hkl);
        if (count == 1 && !char.IsControl(buf[0]))
        {
            ch = buf[0];
            return true;
        }

        ch = default;
        return false;
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
        _seqCommitTimer.Dispose();
        if (_hookThreadId != 0)
        {
            NativeMethods.PostThreadMessage(_hookThreadId, NativeMethods.WM_QUIT, 0, 0);
        }
    }
}
