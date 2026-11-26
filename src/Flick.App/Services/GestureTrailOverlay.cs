using System.Runtime.InteropServices;
using Flick.Core.Models;
using Flick.Core.Recognition;

namespace Flick.App.Services;

/// <summary>
/// Renders a hardware-friendly transparent, click-through, topmost GDI+ gesture trail
/// across the virtual desktop without stealing window focus.
/// </summary>
public sealed class GestureTrailOverlay : IDisposable
{
    private const uint WmUpdateTrail = NativeMethods.WM_USER + 101;
    private const uint WmClearTrail = NativeMethods.WM_USER + 102;
    private const uint WmRenderRipple = NativeMethods.WM_USER + 103;

    private Thread? _thread;
    private nint _hwnd;
    private uint _threadId;
    private nuint _gdipToken;
    private NativeMethods.WndProcDelegate? _wndProc;

    private readonly object _gate = new();
    private GesturePoint[][] _pendingPaths = [];
    private string? _pendingHudText;
    private GesturePoint _pendingHudPos;
    private GesturePoint _rippleOrigin;
    private float _rippleProgress;
    private bool _rippleReverse;
    private int _rippleGeneration;
    private int _activePatchSize = 260;
    private uint[]? _capturedPatch;
    private uint _trailColorArgb = 0xFF0078D4;
    private float _rippleRadius = 105f;
    private float _rippleThickness = 28f;
    private float _rippleSpeed = 1.0f;

    private static readonly uint[] MultiFingerPalette =
    [
        0xFF0078D4, // Finger 1 (overridden by user's configured trail color)
        0xFF10B981, // Finger 2 (Emerald)
        0xFF8B5CF6, // Finger 3 (Violet)
        0xFFF59E0B, // Finger 4 (Amber)
        0xFFF43F5E  // Finger 5 (Coral)
    ];
    private float _trailThickness = 4.5f;
    private int _virtX;
    private int _virtY;
    private int _virtW;
    private int _virtH;
    private bool _disposed;

    public void Start()
    {
        if (_thread is not null)
        {
            return;
        }

        _thread = new Thread(OverlayLoop)
        {
            IsBackground = true,
            Name = "Flick.TrailOverlay"
        };
        _thread.Start();
    }

    public void Configure(
        string hexColor,
        double thickness,
        double rippleRadius = 105.0,
        double rippleThickness = 28.0,
        double rippleSpeed = 1.0)
    {
        _trailColorArgb = ParseHexColor(hexColor);
        _trailThickness = (float)Math.Clamp(thickness, 1.5, 16.0);
        _rippleRadius = (float)Math.Clamp(rippleRadius, 40.0, 220.0);
        _rippleThickness = (float)Math.Clamp(rippleThickness, 10.0, 60.0);
        _rippleSpeed = (float)Math.Clamp(rippleSpeed, 0.3, 3.0);
    }

    public void UpdateTrail(IReadOnlyList<GesturePoint> screenPoints, string? hudText = null)
    {
        if (_hwnd == 0 || screenPoints.Count < 2)
        {
            return;
        }

        lock (_gate)
        {
            _rippleGeneration++;
            _pendingPaths = [[.. screenPoints]];
            _pendingHudText = hudText;
            _pendingHudPos = screenPoints[^1];
        }

        NativeMethods.PostMessage(_hwnd, WmUpdateTrail, 0, 0);
    }

    public void UpdateMultiTrail(IReadOnlyList<IReadOnlyList<GesturePoint>> screenPaths, string? hudText = null, GesturePoint? hudPos = null)
    {
        if (_hwnd == 0 || screenPaths.Count == 0)
        {
            return;
        }

        var copy = new GesturePoint[screenPaths.Count][];
        bool hasAnyLine = false;
        for (int i = 0; i < screenPaths.Count; i++)
        {
            copy[i] = [.. screenPaths[i]];
            if (copy[i].Length >= 2) hasAnyLine = true;
        }

        if (!hasAnyLine)
        {
            return;
        }

        lock (_gate)
        {
            _rippleGeneration++;
            _pendingPaths = copy;
            _pendingHudText = hudText;
            _pendingHudPos = hudPos ?? copy[0][^1];
        }

        NativeMethods.PostMessage(_hwnd, WmUpdateTrail, 0, 0);
    }

    public void TriggerRipple(GesturePoint screenOrigin, bool reverse = false)
    {
        if (_hwnd == 0)
        {
            return;
        }

        int patchSize = Math.Clamp((int)Math.Ceiling((_rippleRadius + _rippleThickness + 8f) * 2f), 160, 560);
        int halfPatch = patchSize / 2;
        int gen;
        lock (_gate)
        {
            _pendingPaths = [];
            _rippleOrigin = screenOrigin;
            _rippleProgress = 0f;
            _rippleReverse = reverse;
            _activePatchSize = patchSize;
            _capturedPatch = null;
            gen = ++_rippleGeneration;
        }

        // Hide trail first, then capture the real desktop pixels beneath the cursor and animate refraction.
        NativeMethods.PostMessage(_hwnd, WmClearTrail, 0, 0);

        _ = Task.Run(async () =>
        {
            await Task.Delay(16).ConfigureAwait(false);
            if (_disposed) return;

            var patch = CaptureScreenPatch((int)screenOrigin.X - halfPatch, (int)screenOrigin.Y - halfPatch, patchSize);
            lock (_gate)
            {
                if (_rippleGeneration != gen) return;
                _capturedPatch = patch;
            }

            float totalMs = 310f / _rippleSpeed;
            int totalFrames = Math.Clamp((int)Math.Round(totalMs / 14f), 8, 50);
            int delayMs = Math.Max(10, (int)Math.Round(totalMs / totalFrames));

            for (int frame = 0; frame <= totalFrames; frame++)
            {
                if (_disposed) return;

                lock (_gate)
                {
                    if (_rippleGeneration != gen) return;
                    _rippleProgress = (float)frame / totalFrames;
                }

                NativeMethods.PostMessage(_hwnd, WmRenderRipple, 0, 0);
                await Task.Delay(delayMs).ConfigureAwait(false);
            }

            lock (_gate)
            {
                if (_rippleGeneration != gen) return;
            }
            NativeMethods.PostMessage(_hwnd, WmClearTrail, 0, 0);
        });
    }

    public void Clear()
    {
        if (_hwnd == 0)
        {
            return;
        }

        lock (_gate)
        {
            _rippleGeneration++;
            _pendingPaths = [];
        }

        NativeMethods.PostMessage(_hwnd, WmClearTrail, 0, 0);
    }

    private void OverlayLoop()
    {
        _threadId = NativeMethods.GetCurrentThreadId();

        var gdipInput = new NativeMethods.GdiplusStartupInput { GdiplusVersion = 1 };
        _ = NativeMethods.GdiplusStartup(out _gdipToken, ref gdipInput, 0);

        _virtX = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        _virtY = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        _virtW = Math.Max(1, NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN));
        _virtH = Math.Max(1, NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN));

        _wndProc = WndProc;
        string className = "FlickTrailOverlayClass";
        nint hInstance = NativeMethods.GetModuleHandle(null);

        var wc = new NativeMethods.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = hInstance,
            lpszClassName = className
        };

        NativeMethods.RegisterClassEx(ref wc);

        uint exStyle = NativeMethods.WS_EX_LAYERED |
                       NativeMethods.WS_EX_TRANSPARENT |
                       NativeMethods.WS_EX_TOPMOST |
                       NativeMethods.WS_EX_TOOLWINDOW |
                       NativeMethods.WS_EX_NOACTIVATE;

        _hwnd = NativeMethods.CreateWindowEx(
            exStyle,
            className,
            "FlickTrailOverlay",
            NativeMethods.WS_POPUP,
            _virtX, _virtY, _virtW, _virtH,
            0, 0, hInstance, 0);

        while (NativeMethods.GetMessage(out var msg, 0, 0, 0) > 0)
        {
            NativeMethods.TranslateMessage(ref msg);
            NativeMethods.DispatchMessage(ref msg);
        }

        if (_hwnd != 0)
        {
            NativeMethods.DestroyWindow(_hwnd);
            _hwnd = 0;
        }

        if (_gdipToken != 0)
        {
            NativeMethods.GdiplusShutdown(_gdipToken);
            _gdipToken = 0;
        }
    }

    private nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == WmUpdateTrail)
        {
            GesturePoint[][] paths;
            string? hudText;
            GesturePoint hudPos;
            lock (_gate)
            {
                paths = _pendingPaths;
                hudText = _pendingHudText;
                hudPos = _pendingHudPos;
            }

            if (paths.Length > 0)
            {
                NativeMethods.ShowWindow(hWnd, NativeMethods.SW_SHOWNOACTIVATE);
                RenderTrails(hWnd, paths, hudText, hudPos);
            }
            return 0;
        }

        if (msg == WmRenderRipple)
        {
            GesturePoint origin;
            float t;
            lock (_gate)
            {
                origin = _rippleOrigin;
                t = _rippleProgress;
            }

            NativeMethods.ShowWindow(hWnd, NativeMethods.SW_SHOWNOACTIVATE);
            RenderRipple(hWnd, origin, t);
            return 0;
        }

        if (msg == WmClearTrail)
        {
            RenderEmpty(hWnd);
            NativeMethods.ShowWindow(hWnd, NativeMethods.SW_HIDE);
            return 0;
        }

        return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void RenderTrails(nint hWnd, GesturePoint[][] screenPaths, string? hudText, GesturePoint hudPos)
    {
        RenderToLayeredWindow(hWnd, graphics =>
        {
            for (int f = 0; f < screenPaths.Length; f++)
            {
                var raw = screenPaths[f];
                if (raw.Length < 2) continue;

                var pts = StrokeSmoother.Smooth(raw, denoisePasses: 3, chaikinIterations: 2);

                var gdiPoints = new NativeMethods.PointF[pts.Length];
                for (int i = 0; i < pts.Length; i++)
                {
                    gdiPoints[i] = new NativeMethods.PointF
                    {
                        X = (float)(pts[i].X - _virtX),
                        Y = (float)(pts[i].Y - _virtY)
                    };
                }

                uint color = f == 0
                    ? _trailColorArgb
                    : MultiFingerPalette[f % MultiFingerPalette.Length];
                uint glowColor = (color & 0x00FFFFFF) | 0x48000000;

                if (NativeMethods.GdipCreatePen1(glowColor, _trailThickness * 2.6f, 2, out nint glowPen) == 0)
                {
                    NativeMethods.GdipSetPenStartCap(glowPen, 2);
                    NativeMethods.GdipSetPenEndCap(glowPen, 2);
                    NativeMethods.GdipSetPenLineJoin(glowPen, 2);
                    NativeMethods.GdipDrawCurve2(graphics, glowPen, gdiPoints, gdiPoints.Length, 0.4f);
                    NativeMethods.GdipDeletePen(glowPen);
                }

                if (NativeMethods.GdipCreatePen1(color, _trailThickness, 2, out nint corePen) == 0)
                {
                    NativeMethods.GdipSetPenStartCap(corePen, 2);
                    NativeMethods.GdipSetPenEndCap(corePen, 2);
                    NativeMethods.GdipSetPenLineJoin(corePen, 2);
                    NativeMethods.GdipDrawCurve2(graphics, corePen, gdiPoints, gdiPoints.Length, 0.4f);
                    NativeMethods.GdipDeletePen(corePen);
                }

                if (NativeMethods.GdipCreateSolidFill(0xFFFFFFFF, out nint brush) == 0)
                {
                    float r = _trailThickness * 1.0f;
                    NativeMethods.GdipFillEllipse(graphics, brush, gdiPoints[0].X - r, gdiPoints[0].Y - r, r * 2, r * 2);
                    NativeMethods.GdipDeleteBrush(brush);
                }
            }

            if (!string.IsNullOrWhiteSpace(hudText))
            {
                DrawHudPill(graphics, hudText, (float)(hudPos.X - _virtX), (float)(hudPos.Y - _virtY));
            }
        });
    }

    private void DrawHudPill(nint graphics, string text, float cursorX, float cursorY)
    {
        if (NativeMethods.GdipCreateFontFamilyFromName("Segoe UI", 0, out nint family) != 0)
        {
            return;
        }

        if (NativeMethods.GdipCreateFont(family, 13f, 1, 3, out nint font) != 0) // Bold, Pixel
        {
            NativeMethods.GdipDeleteFontFamily(family);
            return;
        }

        var measureRect = new NativeMethods.RectF { X = 0, Y = 0, Width = 420, Height = 60 };
        NativeMethods.GdipMeasureString(graphics, text, text.Length, font, ref measureRect, 0, out var box, out _, out _);

        float padX = 14f;
        float padY = 8f;
        float pillW = box.Width + padX * 2f;
        float pillH = box.Height + padY * 2f;
        float pillX = Math.Clamp(cursorX + 18f, 8f, Math.Max(8f, _virtW - pillW - 8f));
        float pillY = Math.Clamp(cursorY - pillH - 14f, 8f, Math.Max(8f, _virtH - pillH - 8f));
        float radius = pillH * 0.5f;

        if (NativeMethods.GdipCreatePath(0, out nint path) == 0)
        {
            float d = radius * 2f;
            NativeMethods.GdipAddPathArc(path, pillX, pillY, d, d, 180, 90);
            NativeMethods.GdipAddPathArc(path, pillX + pillW - d, pillY, d, d, 270, 90);
            NativeMethods.GdipAddPathArc(path, pillX + pillW - d, pillY + pillH - d, d, d, 0, 90);
            NativeMethods.GdipAddPathArc(path, pillX, pillY + pillH - d, d, d, 90, 90);
            NativeMethods.GdipClosePathFigure(path);

            if (NativeMethods.GdipCreateSolidFill(0xDC1C1C1E, out nint bgBrush) == 0)
            {
                NativeMethods.GdipFillPath(graphics, bgBrush, path);
                NativeMethods.GdipDeleteBrush(bgBrush);
            }

            uint borderColor = (_trailColorArgb & 0x00FFFFFF) | 0xB0000000;
            if (NativeMethods.GdipCreatePen1(borderColor, 1.4f, 2, out nint borderPen) == 0)
            {
                NativeMethods.GdipDrawPath(graphics, borderPen, path);
                NativeMethods.GdipDeletePen(borderPen);
            }

            NativeMethods.GdipDeletePath(path);
        }

        if (NativeMethods.GdipCreateSolidFill(0xFFF5F5F7, out nint textBrush) == 0)
        {
            var textRect = new NativeMethods.RectF
            {
                X = pillX + padX,
                Y = pillY + padY,
                Width = box.Width + 6f,
                Height = box.Height + 4f
            };
            NativeMethods.GdipDrawString(graphics, text, text.Length, font, ref textRect, 0, textBrush);
            NativeMethods.GdipDeleteBrush(textBrush);
        }

        NativeMethods.GdipDeleteFont(font);
        NativeMethods.GdipDeleteFontFamily(family);
    }

    private unsafe void RenderRipple(nint hWnd, GesturePoint origin, float t)
    {
        uint[]? srcPatch;
        int patchSize;
        bool reverse;
        lock (_gate)
        {
            srcPatch = _capturedPatch;
            patchSize = _activePatchSize;
            reverse = _rippleReverse;
        }

        if (srcPatch is null || srcPatch.Length != patchSize * patchSize)
        {
            return;
        }

        int halfPatch = patchSize / 2;

        // Smooth macOS-style cubic/quartic ease-out (or inward collapse when reverse = true)
        float clampedT = Math.Clamp(t, 0f, 1f);
        float maxSpan = Math.Max(20f, _rippleRadius - 10f);
        float waveR;
        float fade;
        if (reverse)
        {
            float collapse = MathF.Pow(clampedT, 2.2f);
            waveR = _rippleRadius - collapse * maxSpan;
            fade = MathF.Sin(MathF.PI * Math.Clamp(clampedT * 0.9f + 0.05f, 0f, 1f));
        }
        else
        {
            float eased = 1f - MathF.Pow(1f - clampedT, 3.2f);
            waveR = 10f + eased * maxSpan;
            fade = MathF.Pow(1f - clampedT, 1.3f);
        }

        float band = _rippleThickness;
        float strength = 8.5f * fade; // Subtle, refined optical bend

        int dstX = (int)origin.X - halfPatch;
        int dstY = (int)origin.Y - halfPatch;

        nint screenDc = NativeMethods.GetDC(0);
        nint memDc = NativeMethods.CreateCompatibleDC(screenDc);

        var bmi = new NativeMethods.BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
            biWidth = patchSize,
            biHeight = -patchSize,
            biPlanes = 1,
            biBitCount = 32,
            biCompression = 0
        };

        nint hBitmap = NativeMethods.CreateDIBSection(memDc, ref bmi, 0, out nint pvBits, 0, 0);
        nint oldBitmap = NativeMethods.SelectObject(memDc, hBitmap);

        if (pvBits != 0)
        {
            uint* dst = (uint*)pvBits;
            fixed (uint* src = srcPatch)
            {
                for (int py = 0; py < patchSize; py++)
                {
                    float dy = py - halfPatch;
                    int rowOffset = py * patchSize;

                    for (int px = 0; px < patchSize; px++)
                    {
                        float dx = px - halfPatch;
                        float dist = MathF.Sqrt(dx * dx + dy * dy);
                        float delta = MathF.Abs(dist - waveR);

                        if (delta >= band || dist < 1.5f)
                        {
                            dst[rowOffset + px] = 0;
                            continue;
                        }

                        // Smooth cosine-squared wave envelope
                        float u = delta / band;
                        float bell = MathF.Cos(u * (MathF.PI * 0.5f));
                        bell *= bell;

                        float invDist = 1f / dist;
                        float nx = dx * invDist;
                        float ny = dy * invDist;

                        // Subtle chromatic dispersion (R, G, B bend at slightly different indices)
                        float dispG = bell * strength;
                        float dispR = dispG * 1.18f;
                        float dispB = dispG * 0.82f;

                        byte r = SampleBilinearChannel(src, patchSize, px - nx * dispR, py - ny * dispR, 16);
                        byte g = SampleBilinearChannel(src, patchSize, px - nx * dispG, py - ny * dispG, 8);
                        byte b = SampleBilinearChannel(src, patchSize, px - nx * dispB, py - ny * dispB, 0);

                        // Whisper-soft specular glass sheen along the wave crest
                        float sheen = MathF.Pow(bell, 2.2f) * 20f * fade;
                        r = (byte)Math.Min(255, r + (int)sheen);
                        g = (byte)Math.Min(255, g + (int)sheen);
                        b = (byte)Math.Min(255, b + (int)(sheen * 1.1f));

                        // Ultra-smooth feathered alpha so there is no hard ring boundary
                        byte a = (byte)Math.Clamp((int)(255f * MathF.Min(1f, bell * 1.35f) * fade), 0, 255);

                        uint pb = (uint)(b * a / 255);
                        uint pg = (uint)(g * a / 255);
                        uint pr = (uint)(r * a / 255);
                        dst[rowOffset + px] = ((uint)a << 24) | (pr << 16) | (pg << 8) | pb;
                    }
                }
            }
        }

        var ptDst = new NativeMethods.POINT { X = dstX, Y = dstY };
        var size = new NativeMethods.SIZE { cx = patchSize, cy = patchSize };
        var ptSrc = new NativeMethods.POINT { X = 0, Y = 0 };
        var blend = new NativeMethods.BLENDFUNCTION
        {
            BlendOp = NativeMethods.AC_SRC_OVER,
            BlendFlags = 0,
            SourceConstantAlpha = 255,
            AlphaFormat = NativeMethods.AC_SRC_ALPHA
        };

        NativeMethods.UpdateLayeredWindow(hWnd, screenDc, ref ptDst, ref size, memDc, ref ptSrc, 0, ref blend, NativeMethods.ULW_ALPHA);

        NativeMethods.SelectObject(memDc, oldBitmap);
        NativeMethods.DeleteObject(hBitmap);
        NativeMethods.DeleteDC(memDc);
        NativeMethods.ReleaseDC(0, screenDc);
    }

    private static unsafe byte SampleBilinearChannel(uint* src, int patchSize, float fx, float fy, int shift)
    {
        float cx = Math.Clamp(fx, 0f, patchSize - 1.001f);
        float cy = Math.Clamp(fy, 0f, patchSize - 1.001f);

        int x0 = (int)cx;
        int y0 = (int)cy;
        int x1 = x0 + 1;
        int y1 = y0 + 1;

        float tx = cx - x0;
        float ty = cy - y0;

        float c00 = (src[y0 * patchSize + x0] >> shift) & 0xFF;
        float c10 = (src[y0 * patchSize + x1] >> shift) & 0xFF;
        float c01 = (src[y1 * patchSize + x0] >> shift) & 0xFF;
        float c11 = (src[y1 * patchSize + x1] >> shift) & 0xFF;

        float top = c00 + (c10 - c00) * tx;
        float bot = c01 + (c11 - c01) * tx;
        return (byte)Math.Clamp((int)(top + (bot - top) * ty), 0, 255);
    }

    private static unsafe uint[] CaptureScreenPatch(int srcX, int srcY, int patchSize)
    {
        var pixels = new uint[patchSize * patchSize];
        nint screenDc = NativeMethods.GetDC(0);
        nint memDc = NativeMethods.CreateCompatibleDC(screenDc);

        var bmi = new NativeMethods.BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
            biWidth = patchSize,
            biHeight = -patchSize,
            biPlanes = 1,
            biBitCount = 32,
            biCompression = 0
        };

        nint hBitmap = NativeMethods.CreateDIBSection(memDc, ref bmi, 0, out nint pvBits, 0, 0);
        nint oldBitmap = NativeMethods.SelectObject(memDc, hBitmap);

        NativeMethods.BitBlt(memDc, 0, 0, patchSize, patchSize, screenDc, srcX, srcY, NativeMethods.SRCCOPY);

        if (pvBits != 0)
        {
            Marshal.Copy(pvBits, (int[])(object)pixels, 0, pixels.Length);
        }

        NativeMethods.SelectObject(memDc, oldBitmap);
        NativeMethods.DeleteObject(hBitmap);
        NativeMethods.DeleteDC(memDc);
        NativeMethods.ReleaseDC(0, screenDc);
        return pixels;
    }

    private void RenderEmpty(nint hWnd) => RenderToLayeredWindow(hWnd, _ => { });

    private void RenderToLayeredWindow(nint hWnd, Action<nint> drawAction)
    {
        nint screenDc = NativeMethods.GetDC(0);
        nint memDc = NativeMethods.CreateCompatibleDC(screenDc);

        var bmi = new NativeMethods.BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
            biWidth = _virtW,
            biHeight = -_virtH, // Top-down DIB
            biPlanes = 1,
            biBitCount = 32,
            biCompression = 0 // BI_RGB
        };

        nint hBitmap = NativeMethods.CreateDIBSection(memDc, ref bmi, 0, out _, 0, 0);
        nint oldBitmap = NativeMethods.SelectObject(memDc, hBitmap);

        if (NativeMethods.GdipCreateFromHDC(memDc, out nint graphics) == 0)
        {
            NativeMethods.GdipSetSmoothingMode(graphics, 4); // AntiAlias
            drawAction(graphics);
            NativeMethods.GdipDeleteGraphics(graphics);
        }

        var ptDst = new NativeMethods.POINT { X = _virtX, Y = _virtY };
        var size = new NativeMethods.SIZE { cx = _virtW, cy = _virtH };
        var ptSrc = new NativeMethods.POINT { X = 0, Y = 0 };
        var blend = new NativeMethods.BLENDFUNCTION
        {
            BlendOp = NativeMethods.AC_SRC_OVER,
            BlendFlags = 0,
            SourceConstantAlpha = 255,
            AlphaFormat = NativeMethods.AC_SRC_ALPHA
        };

        NativeMethods.UpdateLayeredWindow(hWnd, screenDc, ref ptDst, ref size, memDc, ref ptSrc, 0, ref blend, NativeMethods.ULW_ALPHA);

        NativeMethods.SelectObject(memDc, oldBitmap);
        NativeMethods.DeleteObject(hBitmap);
        NativeMethods.DeleteDC(memDc);
        NativeMethods.ReleaseDC(0, screenDc);
    }

    private static uint ParseHexColor(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return 0xFF0078D4;
        }

        string clean = hex.Trim().TrimStart('#');
        if (clean.Length == 6 && uint.TryParse(clean, System.Globalization.NumberStyles.HexNumber, null, out uint rgb))
        {
            return 0xFF000000 | rgb;
        }
        if (clean.Length == 8 && uint.TryParse(clean, System.Globalization.NumberStyles.HexNumber, null, out uint argb))
        {
            return argb;
        }
        return 0xFF0078D4;
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
