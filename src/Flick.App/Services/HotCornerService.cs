using Flick.Core.Models;

namespace Flick.App.Services;

/// <summary>
/// Monitors primary-screen corners and triggers configured actions when the cursor enters a corner.
/// </summary>
public sealed class HotCornerService : IDisposable
{
    private readonly Action<string, string, GesturePoint> _onCornerTriggered;
    private readonly Func<bool> _canTrigger;
    private Timer? _timer;
    private string? _lastCorner;
    private DateTimeOffset _lastTriggerTime = DateTimeOffset.MinValue;
    private Dictionary<string, string> _bindings = [];

    public bool IsEnabled { get; set; }

    public HotCornerService(Func<bool> canTrigger, Action<string, string, GesturePoint> onCornerTriggered)
    {
        _canTrigger = canTrigger;
        _onCornerTriggered = onCornerTriggered;
    }

    public void Configure(bool enabled, Dictionary<string, string> bindings)
    {
        IsEnabled = enabled;
        _bindings = new Dictionary<string, string>(bindings, StringComparer.OrdinalIgnoreCase);

        if (enabled && _timer is null)
        {
            _timer = new Timer(PollCursor, null, 200, 120);
        }
        else if (!enabled && _timer is not null)
        {
            _timer.Dispose();
            _timer = null;
        }
    }

    private void PollCursor(object? _)
    {
        if (!IsEnabled || !_canTrigger())
        {
            return;
        }

        if (!NativeMethods.GetCursorPos(out var pt))
        {
            return;
        }

        int w = NativeMethods.GetSystemMetrics(0);
        int h = NativeMethods.GetSystemMetrics(1);
        const int edge = 6;

        string? corner = null;
        if (pt.X <= edge && pt.Y <= edge) corner = "TopLeft";
        else if (pt.X >= w - edge && pt.Y <= edge) corner = "TopRight";
        else if (pt.X <= edge && pt.Y >= h - edge) corner = "BottomLeft";
        else if (pt.X >= w - edge && pt.Y >= h - edge) corner = "BottomRight";

        if (corner is null)
        {
            _lastCorner = null;
            return;
        }

        if (corner == _lastCorner || (DateTimeOffset.UtcNow - _lastTriggerTime).TotalMilliseconds < 900)
        {
            return;
        }

        _lastCorner = corner;
        if (_bindings.TryGetValue(corner, out string? action) && !string.IsNullOrWhiteSpace(action))
        {
            _lastTriggerTime = DateTimeOffset.UtcNow;
            _onCornerTriggered(corner, action, new GesturePoint(pt.X, pt.Y));
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }
}
