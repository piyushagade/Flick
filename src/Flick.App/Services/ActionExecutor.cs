using System.Diagnostics;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using Flick.Core.Actions;
using Flick.Core.Models;

namespace Flick.App.Services;

/// <summary>
/// Executes the configured action for a matched <see cref="GestureRule"/>.
/// </summary>
public sealed class ActionExecutor
{
    private static readonly HttpClient SharedHttp = new()
    {
        Timeout = TimeSpan.FromSeconds(8)
    };

    [DllImport("user32.dll")]
    private static extern bool LockWorkStation();

    [DllImport("powrprof.dll")]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint SHEmptyRecycleBin(nint hwnd, string? pszRootPath, uint dwFlags);

    [DllImport("sas.dll")]
    private static extern void SendSAS(bool asUser);
    private static readonly Dictionary<string, ushort> KeyMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CTRL"] = 0x11,
        ["CONTROL"] = 0x11,
        ["ALT"] = 0x12,
        ["SHIFT"] = 0x10,
        ["WIN"] = 0x5B,
        ["LWIN"] = 0x5B,
        ["LEFT"] = 0x25,
        ["UP"] = 0x26,
        ["RIGHT"] = 0x27,
        ["DOWN"] = 0x28,
        ["ENTER"] = 0x0D,
        ["RETURN"] = 0x0D,
        ["ESC"] = 0x1B,
        ["ESCAPE"] = 0x1B,
        ["TAB"] = 0x09,
        ["SPACE"] = 0x20,
        ["BACK"] = 0x08,
        ["BACKSPACE"] = 0x08,
        ["DELETE"] = 0x2E,
        ["DEL"] = 0x2E,
        ["HOME"] = 0x24,
        ["END"] = 0x23,
        ["PGUP"] = 0x21,
        ["PAGEUP"] = 0x21,
        ["PGDN"] = 0x22,
        ["PAGEDOWN"] = 0x22,
        ["PRINTSCREEN"] = 0x2C,
        ["VOLUME_MUTE"] = 0xAD,
        ["VOLUME_DOWN"] = 0xAE,
        ["VOLUME_UP"] = 0xAF,
        ["MEDIA_NEXT"] = 0xB0,
        ["MEDIA_PREV"] = 0xB1,
        ["MEDIA_PLAY_PAUSE"] = 0xB3,
        ["F1"] = 0x70, ["F2"] = 0x71, ["F3"] = 0x72, ["F4"] = 0x73,
        ["F5"] = 0x74, ["F6"] = 0x75, ["F7"] = 0x76, ["F8"] = 0x77,
        ["F9"] = 0x78, ["F10"] = 0x79, ["F11"] = 0x7A, ["F12"] = 0x7B
    };

    public Action<bool>? ToggleSneakHandler { get; set; }
    private nint _lastMaxToggleHwnd;
    private long _lastMaxToggleTick;

    public void Execute(GestureRule rule, nint targetWindow)
    {
        if (rule.Steps is { Count: > 0 } steps)
        {
            var stepsCopy = steps.ToArray();
            _ = Task.Run(async () =>
            {
                foreach (var step in stepsCopy)
                {
                    if (step.DelayBeforeMs > 0)
                    {
                        await Task.Delay(step.DelayBeforeMs).ConfigureAwait(false);
                    }
                    ExecuteSingle(step.ActionType, step.ActionPayload, targetWindow);
                }
            });
            return;
        }

        ExecuteSingle(rule.ActionType, rule.ActionPayload, targetWindow);
    }

    public void ExecuteSingle(ActionType actionType, string payload, nint targetWindow)
    {
        switch (actionType)
        {
            case ActionType.SendHotkey:
                SendHotkey(payload);
                break;

            case ActionType.MouseAction:
                ExecuteMouseAction(payload);
                break;

            case ActionType.WindowManagement:
                ExecuteWindowAction(payload, targetWindow);
                break;

            case ActionType.SystemAction:
                ExecuteSystemAction(payload);
                break;

            case ActionType.MediaControl:
                ExecuteMediaAction(payload);
                break;

            case ActionType.OpenUrl:
                OpenUrl(payload);
                break;

            case ActionType.WindowsIntent:
                LaunchIntent(payload);
                break;

            case ActionType.HttpRequest:
                _ = ExecuteHttpAsync(payload);
                break;

            case ActionType.IftttWebhook:
                _ = ExecuteIftttAsync(payload);
                break;

            case ActionType.MqttPublish:
                _ = ExecuteMqttAsync(payload);
                break;

            case ActionType.RunProgram:
                RunProgram(payload);
                break;

            case ActionType.RunPowerShell:
                RunPowerShell(payload);
                break;

            case ActionType.TextSnippet:
                SendUnicodeText(payload);
                break;
        }
    }

    public static void SendHotkey(string hotkey)
    {
        if (string.IsNullOrWhiteSpace(hotkey))
        {
            return;
        }

        var parts = hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var keys = new List<ushort>();

        foreach (string part in parts)
        {
            if (KeyMap.TryGetValue(part, out ushort vk))
            {
                keys.Add(vk);
            }
            else if (part.Length == 1)
            {
                char ch = char.ToUpperInvariant(part[0]);
                if (ch is >= 'A' and <= 'Z' or >= '0' and <= '9')
                {
                    keys.Add(ch);
                }
            }
        }

        if (keys.Count == 0)
        {
            return;
        }

        var inputs = new NativeMethods.INPUT[keys.Count * 2];
        for (int i = 0; i < keys.Count; i++)
        {
            inputs[i] = CreateKeyInput(keys[i], keyUp: false);
        }

        for (int i = 0; i < keys.Count; i++)
        {
            inputs[keys.Count + i] = CreateKeyInput(keys[keys.Count - 1 - i], keyUp: true);
        }

        _ = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private void ExecuteWindowAction(string command, nint targetWindow)
    {
        string trimmed = command.Trim();
        if (string.Equals(trimmed, "sneak", StringComparison.OrdinalIgnoreCase))
        {
            ToggleSneakHandler?.Invoke(false);
            return;
        }
        if (string.Equals(trimmed, "sneakall", StringComparison.OrdinalIgnoreCase))
        {
            ToggleSneakHandler?.Invoke(true);
            return;
        }

        if (targetWindow == 0)
        {
            targetWindow = NativeMethods.GetForegroundWindow();
        }
        if (targetWindow != 0)
        {
            nint root = NativeMethods.GetAncestor(targetWindow, NativeMethods.GA_ROOT);
            if (root != 0) targetWindow = root;
        }

        switch (command.Trim().ToLowerInvariant())
        {
            case "minimize":
                if (targetWindow != 0) NativeMethods.ShowWindow(targetWindow, NativeMethods.SW_MINIMIZE);
                break;
            case "maximize":
                if (targetWindow != 0)
                {
                    long now = Environment.TickCount64;
                    if (targetWindow == _lastMaxToggleHwnd && now - _lastMaxToggleTick < 400)
                    {
                        break;
                    }
                    _lastMaxToggleHwnd = targetWindow;
                    _lastMaxToggleTick = now;

                    nint hwnd = targetWindow;
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(60).ConfigureAwait(false);
                        nint sc = NativeMethods.IsZoomed(hwnd)
                            ? NativeMethods.SC_RESTORE
                            : NativeMethods.SC_MAXIMIZE;
                        NativeMethods.PostMessage(hwnd, NativeMethods.WM_SYSCOMMAND, sc, 0);
                    });
                }
                break;
            case "restore":
                if (targetWindow != 0) NativeMethods.PostMessage(targetWindow, NativeMethods.WM_SYSCOMMAND, NativeMethods.SC_RESTORE, 0);
                break;
            case "close":
                if (targetWindow != 0) NativeMethods.PostMessage(targetWindow, NativeMethods.WM_CLOSE, 0, 0);
                break;
            case "snapleft":
                SendHotkey("Win+Left");
                break;
            case "snapright":
                SendHotkey("Win+Right");
                break;
            case "showdesktop":
                SendHotkey("Win+D");
                break;
            case "taskview":
                SendHotkey("Win+Tab");
                break;
            case "desktopleft":
                SendHotkey("Ctrl+Win+Left");
                break;
            case "desktopright":
                SendHotkey("Ctrl+Win+Right");
                break;
        }
    }

    private static void ExecuteMouseAction(string command)
    {
        switch (command.Trim().ToLowerInvariant())
        {
            case "leftclick":
                SendMouseClick(NativeMethods.MOUSEEVENTF_LEFTDOWN, NativeMethods.MOUSEEVENTF_LEFTUP);
                break;
            case "doubleclick":
                SendMouseClick(NativeMethods.MOUSEEVENTF_LEFTDOWN, NativeMethods.MOUSEEVENTF_LEFTUP);
                SendMouseClick(NativeMethods.MOUSEEVENTF_LEFTDOWN, NativeMethods.MOUSEEVENTF_LEFTUP);
                break;
            case "middleclick":
                SendMouseClick(NativeMethods.MOUSEEVENTF_MIDDLEDOWN, NativeMethods.MOUSEEVENTF_MIDDLEUP);
                break;
            case "rightclick":
                SendMouseClick(NativeMethods.MOUSEEVENTF_RIGHTDOWN, NativeMethods.MOUSEEVENTF_RIGHTUP);
                break;
            case "backclick":
                SendMouseClick(NativeMethods.MOUSEEVENTF_XDOWN, NativeMethods.MOUSEEVENTF_XUP, mouseData: 1);
                break;
            case "forwardclick":
                SendMouseClick(NativeMethods.MOUSEEVENTF_XDOWN, NativeMethods.MOUSEEVENTF_XUP, mouseData: 2);
                break;
            case "scrollup":
                SendMouseWheel(NativeMethods.MOUSEEVENTF_WHEEL, 360);
                break;
            case "scrolldown":
                SendMouseWheel(NativeMethods.MOUSEEVENTF_WHEEL, -360);
                break;
            case "scrollleft":
                SendMouseWheel(NativeMethods.MOUSEEVENTF_HWHEEL, -360);
                break;
            case "scrollright":
                SendMouseWheel(NativeMethods.MOUSEEVENTF_HWHEEL, 360);
                break;
        }
    }

    private static void SendMouseClick(uint downFlag, uint upFlag, uint mouseData = 0)
    {
        var inputs = new NativeMethods.INPUT[2];
        inputs[0] = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_MOUSE,
            u = new NativeMethods.INPUTUNION { mi = new NativeMethods.MOUSEINPUT { dwFlags = downFlag, mouseData = mouseData } }
        };
        inputs[1] = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_MOUSE,
            u = new NativeMethods.INPUTUNION { mi = new NativeMethods.MOUSEINPUT { dwFlags = upFlag, mouseData = mouseData } }
        };
        _ = NativeMethods.SendInput(2, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static void SendMouseWheel(uint wheelFlag, int delta)
    {
        var inputs = new[]
        {
            new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_MOUSE,
                u = new NativeMethods.INPUTUNION
                {
                    mi = new NativeMethods.MOUSEINPUT { dwFlags = wheelFlag, mouseData = unchecked((uint)delta) }
                }
            }
        };
        _ = NativeMethods.SendInput(1, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static void ExecuteSystemAction(string command)
    {
        switch (command.Trim().ToLowerInvariant())
        {
            case "lockscreen":
                _ = LockWorkStation();
                break;
            case "sleep":
                _ = SetSuspendState(hibernate: false, forceCritical: false, disableWakeEvent: false);
                break;
            case "shutdown":
                Process.Start(new ProcessStartInfo("shutdown.exe", "/s /t 0") { CreateNoWindow = true, UseShellExecute = false });
                break;
            case "restart":
                Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 0") { CreateNoWindow = true, UseShellExecute = false });
                break;
            case "snippingtool":
                LaunchIntent("ms-screenclip:");
                break;
            case "clipboardhistory":
                SendHotkey("Win+V");
                break;
            case "emojipicker":
                SendHotkey("Win+.");
                break;
            case "taskmanager":
                SendHotkey("Ctrl+Shift+Esc");
                break;
            case "startmenu":
                SendHotkey("Win");
                break;
            case "rundialog":
                SendHotkey("Win+R");
                break;
            case "ctrlaltdel":
                try { SendSAS(asUser: true); } catch { }
                break;
            case "quicksettings":
                SendHotkey("Win+A");
                break;
            case "emptyrecyclebin":
                _ = SHEmptyRecycleBin(0, null, 0x00000007); // NOCONFIRMATION | NOPROGRESSUI | NOSOUND
                break;
            case "togglemicrophone":
                _ = DevicePrivacyService.ToggleMicrophoneMute();
                break;
            case "togglecamera":
                _ = DevicePrivacyService.ToggleCameraAccess();
                break;
        }
    }

    private static void LaunchIntent(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            return;
        }

        Process.Start(new ProcessStartInfo(uri.Trim()) { UseShellExecute = true });
    }

    private static async Task ExecuteHttpAsync(string raw)
    {
        try
        {
            var spec = ActionPayloadParser.ParseHttp(raw);
            if (spec is null) return;

            using var req = new HttpRequestMessage(new HttpMethod(spec.Method), spec.Url);
            if (!string.IsNullOrEmpty(spec.JsonBody))
            {
                req.Content = new StringContent(spec.JsonBody, Encoding.UTF8, "application/json");
            }
            using var _ = await SharedHttp.SendAsync(req).ConfigureAwait(false);
        }
        catch
        {
            // Background fire-and-forget; network errors shouldn't crash the hook loop.
        }
    }

    private static async Task ExecuteIftttAsync(string raw)
    {
        try
        {
            string? url = ActionPayloadParser.BuildIftttUrl(raw);
            if (url is null) return;

            using var content = new StringContent("{}", Encoding.UTF8, "application/json");
            using var _ = await SharedHttp.PostAsync(url, content).ConfigureAwait(false);
        }
        catch
        {
            // Ignore transient network errors.
        }
    }

    private static async Task ExecuteMqttAsync(string raw)
    {
        try
        {
            var spec = ActionPayloadParser.ParseMqtt(raw);
            if (spec is null) return;

            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await client.ConnectAsync(spec.Host, spec.Port, cts.Token).ConfigureAwait(false);
            using var stream = client.GetStream();

            byte[] connectPkt = ActionPayloadParser.BuildMqttConnectPacket(
                $"flick-{Environment.ProcessId}", spec.Username, spec.Password);
            await stream.WriteAsync(connectPkt, cts.Token).ConfigureAwait(false);

            // Brief wait for CONNACK before PUBLISH
            byte[] connack = new byte[4];
            _ = await stream.ReadAsync(connack, cts.Token).ConfigureAwait(false);

            byte[] publishPkt = ActionPayloadParser.BuildMqttPublishPacket(spec.Topic, spec.Payload);
            await stream.WriteAsync(publishPkt, cts.Token).ConfigureAwait(false);

            // DISCONNECT (0xE0, 0x00)
            await stream.WriteAsync(new byte[] { 0xE0, 0x00 }, cts.Token).ConfigureAwait(false);
        }
        catch
        {
            // Ignore transient broker/network errors.
        }
    }

    private static void ExecuteMediaAction(string command)
    {
        ushort vk = command.Trim().ToLowerInvariant() switch
        {
            "playpause" => 0xB3,
            "nexttrack" => 0xB0,
            "previoustrack" => 0xB1,
            "volumeup" => 0xAF,
            "volumedown" => 0xAE,
            "mute" => 0xAD,
            _ => 0
        };

        if (vk == 0)
        {
            return;
        }

        var inputs = new[]
        {
            CreateKeyInput(vk, keyUp: false),
            CreateKeyInput(vk, keyUp: true)
        };
        _ = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        string target = url.Trim();
        if (!target.Contains("://", StringComparison.Ordinal))
        {
            target = "https://" + target;
        }

        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    }

    private static void RunProgram(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        Process.Start(new ProcessStartInfo(command.Trim()) { UseShellExecute = true });
    }

    private static void RunPowerShell(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -WindowStyle Hidden -Command \"{script.Replace("\"", "\\\"")}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        });
    }

    private static void SendUnicodeText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var inputs = new NativeMethods.INPUT[text.Length * 2];
        for (int i = 0; i < text.Length; i++)
        {
            inputs[i * 2] = new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_KEYBOARD,
                u = new NativeMethods.INPUTUNION
                {
                    ki = new NativeMethods.KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = text[i],
                        dwFlags = NativeMethods.KEYEVENTF_UNICODE
                    }
                }
            };
            inputs[i * 2 + 1] = new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_KEYBOARD,
                u = new NativeMethods.INPUTUNION
                {
                    ki = new NativeMethods.KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = text[i],
                        dwFlags = NativeMethods.KEYEVENTF_UNICODE | NativeMethods.KEYEVENTF_KEYUP
                    }
                }
            };
        }

        _ = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static NativeMethods.INPUT CreateKeyInput(ushort vk, bool keyUp)
    {
        uint flags = keyUp ? NativeMethods.KEYEVENTF_KEYUP : 0;
        if (vk is >= 0x21 and <= 0x2E or 0x5B or >= 0xAD and <= 0xB3)
        {
            flags |= NativeMethods.KEYEVENTF_EXTENDEDKEY;
        }

        return new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            u = new NativeMethods.INPUTUNION
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = vk,
                    dwFlags = flags
                }
            }
        };
    }
}
