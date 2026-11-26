using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Flick.App.Services.Sneak;

internal sealed record SneakTargetWindow(
    nint Handle,
    SneakNativeMethods.RECT WindowRect,
    SneakNativeMethods.RECT ExtendedBounds,
    string Title,
    string ProcessName);

/// <summary>
/// Walks the Win32 Z-order below the active foreground window to find
/// visible, non-cloaked application windows.
/// </summary>
internal static class SneakWindowFinder
{
    private static readonly ConcurrentDictionary<uint, string> ProcessNameCache = new();

    private static readonly HashSet<string> IgnoredClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "Windows.UI.Core.CoreWindow"
    };

    public static IReadOnlyList<SneakTargetWindow> FindAllWindowsUnderneath(
        nint foregroundHwnd,
        nint ignoreOverlayHwnd = 0)
    {
        if (foregroundHwnd == 0)
        {
            foregroundHwnd = SneakNativeMethods.GetForegroundWindow();
        }

        if (foregroundHwnd == 0)
        {
            return [];
        }

        var results = new List<SneakTargetWindow>();
        char[] classBuf = new char[128];
        char[] titleBuf = new char[256];

        // Walk Z-order starting immediately after the active foreground window.
        nint current = SneakNativeMethods.GetWindow(foregroundHwnd, SneakNativeMethods.GW_HWNDNEXT);
        int guard = 0;
        while (current != 0 && guard++ < 512)
        {
            if (current != ignoreOverlayHwnd &&
                SneakNativeMethods.IsWindowVisible(current) &&
                !SneakNativeMethods.IsIconic(current))
            {
                nint exStyle = SneakNativeMethods.GetWindowLongPtr(current, SneakNativeMethods.GWL_EXSTYLE);
                if ((exStyle & (nint)SneakNativeMethods.WS_EX_TOOLWINDOW) == 0 && !IsCloaked(current))
                {
                    int classLen = SneakNativeMethods.GetClassName(current, classBuf, classBuf.Length);
                    string className = classLen > 0 ? new string(classBuf, 0, classLen) : string.Empty;

                    if (!IgnoredClasses.Contains(className))
                    {
                        var extBounds = GetExtendedBounds(current);
                        if (extBounds.Width > 0 && extBounds.Height > 0 &&
                            SneakNativeMethods.GetWindowRect(current, out var winRect) &&
                            winRect.Width > 0 && winRect.Height > 0)
                        {
                            int titleLen = SneakNativeMethods.GetWindowText(current, titleBuf, titleBuf.Length);
                            string title = titleLen > 0 ? new string(titleBuf, 0, titleLen) : string.Empty;
                            string procName = GetProcessName(current);
                            results.Add(new SneakTargetWindow(current, winRect, extBounds, title, procName));
                        }
                    }
                }
            }

            current = SneakNativeMethods.GetWindow(current, SneakNativeMethods.GW_HWNDNEXT);
        }

        return results;
    }

    private static bool IsCloaked(nint hwnd)
    {
        int hr = SneakNativeMethods.DwmGetWindowAttribute(
            hwnd,
            SneakNativeMethods.DWMWA_CLOAKED,
            out int cloaked,
            sizeof(int));

        return hr == 0 && cloaked != 0;
    }

    private static SneakNativeMethods.RECT GetExtendedBounds(nint hwnd)
    {
        int hr = SneakNativeMethods.DwmGetWindowAttribute(
            hwnd,
            SneakNativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
            out SneakNativeMethods.RECT rect,
            Marshal.SizeOf<SneakNativeMethods.RECT>());

        if (hr == 0)
        {
            return rect;
        }

        return SneakNativeMethods.GetWindowRect(hwnd, out var fallback) ? fallback : default;
    }

    private static string GetProcessName(nint hwnd)
    {
        _ = SneakNativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0)
        {
            return string.Empty;
        }

        if (ProcessNameCache.TryGetValue(pid, out string? cached))
        {
            return cached;
        }

        try
        {
            using var proc = Process.GetProcessById((int)pid);
            string name = proc.ProcessName;
            if (ProcessNameCache.Count > 256)
            {
                ProcessNameCache.Clear();
            }
            ProcessNameCache[pid] = name;
            return name;
        }
        catch
        {
            return string.Empty;
        }
    }
}
