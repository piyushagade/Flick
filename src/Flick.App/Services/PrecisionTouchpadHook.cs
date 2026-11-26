using System.Runtime.InteropServices;
using Flick.Core.Models;
using Flick.Core.Touchpad;

namespace Flick.App.Services;

/// <summary>
/// Listens for Windows Precision Touchpad HID reports via Raw Input (WM_INPUT)
/// and feeds multi-finger contact frames into <see cref="TouchpadGestureTracker"/>.
/// </summary>
public sealed class PrecisionTouchpadHook : IDisposable
{
    private sealed class DeviceLayout : IDisposable
    {
        public nint PreparsedData;
        public ushort[] ContactLinkCollections = [];
        public int MaxX = 1;
        public int MaxY = 1;

        public void Dispose()
        {
            if (PreparsedData != 0)
            {
                Marshal.FreeHGlobal(PreparsedData);
                PreparsedData = 0;
            }
        }
    }

    private readonly Dictionary<nint, DeviceLayout> _deviceCache = [];
    private readonly TouchpadGestureTracker _tracker = new();
    private Thread? _thread;
    private uint _threadId;
    private nint _hwnd;
    private NativeMethods.WndProcDelegate? _wndProc;
    private bool _disposed;

    public bool IsEnabled { get; set; } = true;
    public bool IsTouchpadDetected { get; private set; }
    public Func<bool>? ShouldIgnoreCurrentWindow { get; set; }

    public double MinNormalizedSwipeDistance
    {
        get => _tracker.MinNormalizedSwipeDistance;
        set => _tracker.MinNormalizedSwipeDistance = value;
    }

    public event Action<IReadOnlyList<IReadOnlyList<GesturePoint>>, IReadOnlyList<GesturePoint>, int>? TouchpadStrokeUpdated;
    public event Action<TouchpadGestureResult>? TouchpadGestureCompleted;

    public PrecisionTouchpadHook()
    {
        _tracker.StrokeUpdated += (fingerPaths, centroid, fingers) =>
            TouchpadStrokeUpdated?.Invoke(fingerPaths, centroid, fingers);
    }

    public void Start()
    {
        if (_thread is not null)
        {
            return;
        }

        _thread = new Thread(TouchpadLoop)
        {
            IsBackground = true,
            Name = "Flick.PrecisionTouchpad"
        };
        _thread.Start();
    }

    private void TouchpadLoop()
    {
        _threadId = NativeMethods.GetCurrentThreadId();
        _wndProc = WndProc;

        string className = "FlickTouchpadSinkClass";
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
            0, className, "FlickTouchpadSink", 0,
            0, 0, 0, 0,
            0, 0, hInstance, 0);

        var rid = new[]
        {
            new NativeTouchpadMethods.RAWINPUTDEVICE
            {
                usUsagePage = NativeTouchpadMethods.HidUsagePageDigitizer,
                usUsage = NativeTouchpadMethods.HidUsageTouchPad,
                dwFlags = NativeTouchpadMethods.RIDEV_INPUTSINK,
                hwndTarget = _hwnd
            }
        };

        IsTouchpadDetected = NativeTouchpadMethods.RegisterRawInputDevices(
            rid, 1, (uint)Marshal.SizeOf<NativeTouchpadMethods.RAWINPUTDEVICE>());

        while (NativeMethods.GetMessage(out var msg, 0, 0, 0) > 0)
        {
            NativeMethods.TranslateMessage(ref msg);
            NativeMethods.DispatchMessage(ref msg);
        }

        foreach (var layout in _deviceCache.Values)
        {
            layout.Dispose();
        }
        _deviceCache.Clear();

        if (_hwnd != 0)
        {
            NativeMethods.DestroyWindow(_hwnd);
            _hwnd = 0;
        }
    }

    private nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == NativeTouchpadMethods.WM_INPUT && IsEnabled)
        {
            ProcessRawInput(lParam);
            return 0;
        }

        return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void ProcessRawInput(nint hRawInput)
    {
        if (ShouldIgnoreCurrentWindow?.Invoke() == true)
        {
            _tracker.Reset();
            return;
        }

        uint size = 0;
        uint headerSize = (uint)Marshal.SizeOf<NativeTouchpadMethods.RAWINPUTHEADER>();
        _ = NativeTouchpadMethods.GetRawInputData(hRawInput, NativeTouchpadMethods.RID_INPUT, 0, ref size, headerSize);
        if (size <= headerSize)
        {
            return;
        }

        nint buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (NativeTouchpadMethods.GetRawInputData(hRawInput, NativeTouchpadMethods.RID_INPUT, buffer, ref size, headerSize) != size)
            {
                return;
            }

            var header = Marshal.PtrToStructure<NativeTouchpadMethods.RAWINPUTHEADER>(buffer);
            if (header.dwType != NativeTouchpadMethods.RIM_TYPEHID)
            {
                return;
            }

            var layout = GetOrLoadDeviceLayout(header.hDevice);
            if (layout is null || layout.ContactLinkCollections.Length == 0)
            {
                return;
            }

            var rawHid = Marshal.PtrToStructure<NativeTouchpadMethods.RAWHID>(buffer + (int)headerSize);
            int hidDataOffset = (int)headerSize + Marshal.SizeOf<NativeTouchpadMethods.RAWHID>();
            if (rawHid.dwSizeHid == 0 || hidDataOffset + rawHid.dwSizeHid > size)
            {
                return;
            }

            byte[] report = new byte[rawHid.dwSizeHid];
            Marshal.Copy(buffer + hidDataOffset, report, 0, (int)rawHid.dwSizeHid);

            var activeContacts = new List<TouchContact>(layout.ContactLinkCollections.Length);

            foreach (ushort link in layout.ContactLinkCollections)
            {
                ushort[] usages = new ushort[8];
                uint usageLen = (uint)usages.Length;
                int status = NativeTouchpadMethods.HidP_GetUsages(
                    NativeTouchpadMethods.HidP_Input,
                    NativeTouchpadMethods.HidUsagePageDigitizer,
                    link,
                    usages,
                    ref usageLen,
                    layout.PreparsedData,
                    report,
                    rawHid.dwSizeHid);

                bool tipDown = false;
                if (status == NativeTouchpadMethods.HIDP_STATUS_SUCCESS)
                {
                    for (int i = 0; i < usageLen; i++)
                    {
                        if (usages[i] == NativeTouchpadMethods.HidUsageTipSwitch)
                        {
                            tipDown = true;
                            break;
                        }
                    }
                }

                if (!tipDown)
                {
                    continue;
                }

                _ = NativeTouchpadMethods.HidP_GetUsageValue(
                    NativeTouchpadMethods.HidP_Input,
                    NativeTouchpadMethods.HidUsagePageDigitizer,
                    link,
                    NativeTouchpadMethods.HidUsageContactId,
                    out uint contactId,
                    layout.PreparsedData,
                    report,
                    rawHid.dwSizeHid);

                if (NativeTouchpadMethods.HidP_GetUsageValue(
                        NativeTouchpadMethods.HidP_Input,
                        NativeTouchpadMethods.HidUsagePageGenericDesktop,
                        link,
                        NativeTouchpadMethods.HidUsageX,
                        out uint rawX,
                        layout.PreparsedData,
                        report,
                        rawHid.dwSizeHid) != NativeTouchpadMethods.HIDP_STATUS_SUCCESS)
                {
                    continue;
                }

                if (NativeTouchpadMethods.HidP_GetUsageValue(
                        NativeTouchpadMethods.HidP_Input,
                        NativeTouchpadMethods.HidUsagePageGenericDesktop,
                        link,
                        NativeTouchpadMethods.HidUsageY,
                        out uint rawY,
                        layout.PreparsedData,
                        report,
                        rawHid.dwSizeHid) != NativeTouchpadMethods.HIDP_STATUS_SUCCESS)
                {
                    continue;
                }

                double nx = Math.Clamp((double)rawX / layout.MaxX, 0.0, 1.0);
                double ny = Math.Clamp((double)rawY / layout.MaxY, 0.0, 1.0);
                activeContacts.Add(new TouchContact((int)contactId, nx, ny));
            }

            var completed = _tracker.ProcessFrame(activeContacts);
            if (completed is not null)
            {
                TouchpadGestureCompleted?.Invoke(completed.Value);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private DeviceLayout? GetOrLoadDeviceLayout(nint hDevice)
    {
        if (_deviceCache.TryGetValue(hDevice, out var cached))
        {
            return cached;
        }

        uint preparsedSize = 0;
        _ = NativeTouchpadMethods.GetRawInputDeviceInfo(hDevice, NativeTouchpadMethods.RIDI_PREPARSEDDATA, 0, ref preparsedSize);
        if (preparsedSize == 0)
        {
            return null;
        }

        nint preparsed = Marshal.AllocHGlobal((int)preparsedSize);
        if (NativeTouchpadMethods.GetRawInputDeviceInfo(hDevice, NativeTouchpadMethods.RIDI_PREPARSEDDATA, preparsed, ref preparsedSize) == uint.MaxValue)
        {
            Marshal.FreeHGlobal(preparsed);
            return null;
        }

        if (NativeTouchpadMethods.HidP_GetCaps(preparsed, out var caps) != NativeTouchpadMethods.HIDP_STATUS_SUCCESS ||
            caps.NumberInputValueCaps == 0)
        {
            Marshal.FreeHGlobal(preparsed);
            return null;
        }

        var valueCaps = new NativeTouchpadMethods.HIDP_VALUE_CAPS[caps.NumberInputValueCaps];
        ushort len = caps.NumberInputValueCaps;
        if (NativeTouchpadMethods.HidP_GetValueCaps(NativeTouchpadMethods.HidP_Input, valueCaps, ref len, preparsed) != NativeTouchpadMethods.HIDP_STATUS_SUCCESS)
        {
            Marshal.FreeHGlobal(preparsed);
            return null;
        }

        var links = new SortedSet<ushort>();
        int maxX = 1;
        int maxY = 1;

        for (int i = 0; i < len; i++)
        {
            var vc = valueCaps[i];
            if (vc.LinkCollection == 0)
            {
                continue;
            }

            if (vc.UsagePage == NativeTouchpadMethods.HidUsagePageGenericDesktop && vc.Usage == NativeTouchpadMethods.HidUsageX)
            {
                links.Add(vc.LinkCollection);
                if (vc.LogicalMax > maxX) maxX = vc.LogicalMax;
            }
            else if (vc.UsagePage == NativeTouchpadMethods.HidUsagePageGenericDesktop && vc.Usage == NativeTouchpadMethods.HidUsageY)
            {
                links.Add(vc.LinkCollection);
                if (vc.LogicalMax > maxY) maxY = vc.LogicalMax;
            }
        }

        var layout = new DeviceLayout
        {
            PreparsedData = preparsed,
            ContactLinkCollections = [.. links],
            MaxX = maxX,
            MaxY = maxY
        };

        _deviceCache[hDevice] = layout;
        IsTouchpadDetected = true;
        return layout;
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
