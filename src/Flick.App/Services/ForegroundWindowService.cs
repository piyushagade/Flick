using System.Diagnostics;
using System.Runtime.InteropServices;
using Flick.Core.Models;

namespace Flick.App.Services;

public sealed record ForegroundAppInfo(nint WindowHandle, string ProcessName, string WindowTitle);

/// <summary>
/// Inspects the active foreground window, fullscreen state, modifier keys, and running desktop processes.
/// </summary>
public sealed class ForegroundWindowService
{
    public static ModifierKeyFilter GetActiveModifiers()
    {
        var mods = ModifierKeyFilter.None;
        if ((NativeMethods.GetAsyncKeyState(0x11) & 0x8000) != 0) mods |= ModifierKeyFilter.Ctrl;
        if ((NativeMethods.GetAsyncKeyState(0x12) & 0x8000) != 0) mods |= ModifierKeyFilter.Alt;
        if ((NativeMethods.GetAsyncKeyState(0x10) & 0x8000) != 0) mods |= ModifierKeyFilter.Shift;
        return mods;
    }

    public bool IsForegroundFullscreen()
    {
        var app = GetForegroundApp();
        if (app.WindowHandle == 0 ||
            string.Equals(app.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!NativeMethods.GetWindowRect(app.WindowHandle, out var rect))
        {
            return false;
        }

        nint monitor = NativeMethods.MonitorFromWindow(app.WindowHandle, 2); // MONITOR_DEFAULTTONEAREST
        if (monitor == 0) return false;

        var mi = new NativeMethods.MONITORINFO { cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref mi))
        {
            return false;
        }

        return rect.Left <= mi.rcMonitor.Left &&
               rect.Top <= mi.rcMonitor.Top &&
               rect.Right >= mi.rcMonitor.Right &&
               rect.Bottom >= mi.rcMonitor.Bottom;
    }
    public ForegroundAppInfo GetForegroundApp()
    {
        nint hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == 0)
        {
            return new ForegroundAppInfo(0, string.Empty, string.Empty);
        }

        _ = NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        string processName = string.Empty;

        if (pid != 0)
        {
            try
            {
                using var proc = Process.GetProcessById((int)pid);
                processName = proc.ProcessName;
            }
            catch
            {
                // Process may have exited or require elevation.
            }
        }

        char[] buffer = new char[256];
        int len = NativeMethods.GetWindowText(hwnd, buffer, buffer.Length);
        string title = len > 0 ? new string(buffer, 0, len) : string.Empty;

        return new ForegroundAppInfo(hwnd, processName, title);
    }

    public IReadOnlyList<string> GetRunningWindowedProcesses()
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                if (proc.MainWindowHandle != 0 && !string.IsNullOrWhiteSpace(proc.ProcessName))
                {
                    names.Add(proc.ProcessName);
                }
            }
            catch
            {
                // Ignore inaccessible processes.
            }
            finally
            {
                proc.Dispose();
            }
        }

        return [.. names];
    }
}
