using System.Runtime.InteropServices;
using Flick.Core.Models;
using Microsoft.Win32;

namespace Flick.App.Services;

public sealed record TouchpadConflict(
    string ValueName,
    string FriendlyName,
    int? CurrentValue,
    string Reason)
{
    public string DisplayValue => CurrentValue.HasValue ? CurrentValue.Value.ToString() : "default";
}

/// <summary>
/// Detects Windows Precision Touchpad registry settings that can collide with active Flick touchpad rules
/// (2-finger pan/zoom/tap, 3-finger swipe/tap, 4-finger swipe/tap) and optionally disables them on approval.
/// </summary>
public static class TouchpadConflictService
{
    public const string PrecisionTouchpadKeyPath = @"Software\Microsoft\Windows\CurrentVersion\PrecisionTouchPad";

    public static IReadOnlyList<TouchpadConflict> DetectConflicts(FlickProfile profile)
    {
        if (!profile.Settings.IsEnabled || !profile.Settings.EnableTouchpadGestures)
        {
            return [];
        }

        using var key = Registry.CurrentUser.OpenSubKey(PrecisionTouchpadKeyPath, writable: false);
        if (key is null)
        {
            return [];
        }

        var activeTouchpadRules = profile.Rules
            .Where(r => r.IsEnabled &&
                        r.TriggerType == TriggerType.Gesture &&
                        (r.InputSource == GestureInputSource.Touchpad || r.InputSource == GestureInputSource.Any))
            .ToList();

        if (activeTouchpadRules.Count == 0)
        {
            return [];
        }

        static bool IsTap(GestureRule r) => string.Equals(r.GestureId, "builtin-tap", StringComparison.Ordinal);
        static bool IsPinch(GestureRule r) =>
            string.Equals(r.GestureId, "builtin-pinch-in", StringComparison.Ordinal) ||
            string.Equals(r.GestureId, "builtin-pinch-out", StringComparison.Ordinal);
        static bool IsSwipe(GestureRule r) => !IsTap(r) && !IsPinch(r);

        bool has2FSwipe = activeTouchpadRules.Any(r => (r.FingerCount == 2 || r.FingerCount == 0) && IsSwipe(r));
        bool has2FPinch = activeTouchpadRules.Any(r => (r.FingerCount == 2 || r.FingerCount == 0) && IsPinch(r));
        bool has2FTap = activeTouchpadRules.Any(r => (r.FingerCount == 2 || r.FingerCount == 0) && IsTap(r));

        bool has3FSwipe = activeTouchpadRules.Any(r => (r.FingerCount == 3 || r.FingerCount == 0) && IsSwipe(r));
        bool has3FTap = activeTouchpadRules.Any(r => (r.FingerCount == 3 || r.FingerCount == 0) && IsTap(r));

        bool has4FSwipe = activeTouchpadRules.Any(r => (r.FingerCount == 4 || r.FingerCount == 0) && IsSwipe(r));
        bool has4FTap = activeTouchpadRules.Any(r => (r.FingerCount == 4 || r.FingerCount == 0) && IsTap(r));

        var conflicts = new List<TouchpadConflict>();

        CheckValue(key, "PanEnabled", "Two-finger scroll/swipe", has2FSwipe, flagWhenMissing: false,
            "Conflicts with your 2-finger touchpad swipe rule(s).", conflicts);
        CheckValue(key, "ZoomEnabled", "Two-finger pinch-to-zoom", has2FPinch, flagWhenMissing: false,
            "Conflicts with your 2-finger pinch rule(s).", conflicts);
        CheckValue(key, "TwoFingerTapEnabled", "Two-finger tap (right-click)", has2FTap, flagWhenMissing: false,
            "Conflicts with your 2-finger tap rule(s).", conflicts);

        CheckValue(key, "ThreeFingerSlideEnabled", "Three-finger swipes", has3FSwipe, flagWhenMissing: true,
            "Conflicts with your 3-finger touchpad swipe rule(s).", conflicts);
        CheckValue(key, "ThreeFingerTapEnabled", "Three-finger tap", has3FTap, flagWhenMissing: true,
            "Conflicts with your 3-finger tap rule(s).", conflicts);

        CheckValue(key, "FourFingerSlideEnabled", "Four-finger swipes", has4FSwipe, flagWhenMissing: true,
            "Conflicts with your 4-finger touchpad swipe rule(s).", conflicts);
        CheckValue(key, "FourFingerTapEnabled", "Four-finger tap", has4FTap, flagWhenMissing: true,
            "Conflicts with your 4-finger tap rule(s).", conflicts);

        return conflicts;
    }

    private static void CheckValue(
        RegistryKey key,
        string valueName,
        string friendlyName,
        bool ruleExists,
        bool flagWhenMissing,
        string reason,
        List<TouchpadConflict> conflicts)
    {
        if (!ruleExists) return;
        object? raw = key.GetValue(valueName);
        if (raw is int val)
        {
            if (val != 0)
            {
                conflicts.Add(new TouchpadConflict(valueName, friendlyName, val, reason));
            }
        }
        else if (raw is null && flagWhenMissing)
        {
            conflicts.Add(new TouchpadConflict(valueName, friendlyName, null, reason));
        }
    }

    public static bool DisableConflicts(IEnumerable<TouchpadConflict> conflicts)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PrecisionTouchpadKeyPath, writable: true);
            if (key is null) return false;

            foreach (var c in conflicts)
            {
                key.SetValue(c.ValueName, 0, RegistryValueKind.DWord);
            }

            NotifyWindowsTouchpadChanged();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void NotifyWindowsTouchpadChanged()
    {
        try
        {
            const uint SpiGetTouchpadParameters = 0x00AE;
            const uint SpiSetTouchpadParameters = 0x00AF;
            const uint SpifSendChange = 0x0002;
            const uint WmSettingChange = 0x001A;
            nint hwndBroadcast = new(-1);

            byte[] buf = new byte[44];
            buf[0] = 1; // TOUCHPAD_PARAMETERS_VERSION_1
            if (SystemParametersInfoW(SpiGetTouchpadParameters, (uint)buf.Length, buf, 0))
            {
                _ = SystemParametersInfoW(SpiSetTouchpadParameters, (uint)buf.Length, buf, SpifSendChange);
            }

            _ = SendNotifyMessageW(hwndBroadcast, WmSettingChange, 0, PrecisionTouchpadKeyPath);
            _ = SendNotifyMessageW(hwndBroadcast, WmSettingChange, 0, "Windows.Input.PrecisionTouchpad");
        }
        catch
        {
            // Best-effort live notification; registry values are already updated.
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfoW(uint uiAction, uint uiParam, byte[] pvParam, uint fWinIni);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SendNotifyMessageW(nint hWnd, uint msg, nint wParam, string lParam);
}
