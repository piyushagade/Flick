using System.Runtime.InteropServices;
using Flick.Core.Knock;
using Flick.Core.Models;

namespace Flick.App.Services;

/// <summary>
/// Captures default microphone audio via WinMM waveIn (48 kHz mono 16-bit, 20 ms buffers),
/// runs <see cref="KnockOnsetDetector"/>, and matches knock sequences via <see cref="KnockPatternMatcher"/>.
/// </summary>
public sealed class KnockAudioService : IDisposable
{
    private const int SampleRate = 48000;
    private const int BufferSamples = 960; // 20 ms @ 48 kHz
    private const int BufferBytes = BufferSamples * 2;
    private const int BufferCount = 4;

    private const uint WAVE_MAPPER = 0xFFFFFFFF;
    private const uint CALLBACK_FUNCTION = 0x00030000;
    private const uint WIM_DATA = 0x3C0;
    private const ushort WAVE_FORMAT_PCM = 1;

    private delegate void WaveInProc(nint hwi, uint uMsg, nint dwInstance, nint dwParam1, nint dwParam2);

    [StructLayout(LayoutKind.Sequential)]
    private struct WAVEFORMATEX
    {
        public ushort wFormatTag;
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WAVEHDR
    {
        public nint lpData;
        public uint dwBufferLength;
        public uint dwBytesRecorded;
        public nint dwUser;
        public uint dwFlags;
        public uint dwLoops;
        public nint lpNext;
        public nint reserved;
    }

    [DllImport("winmm.dll")]
    private static extern int waveInOpen(out nint phwi, uint uDeviceID, ref WAVEFORMATEX pwfx, WaveInProc dwCallback, nint dwInstance, uint fdwOpen);

    [DllImport("winmm.dll")]
    private static extern int waveInClose(nint hwi);

    [DllImport("winmm.dll")]
    private static extern int waveInPrepareHeader(nint hwi, nint pwh, uint cbwh);

    [DllImport("winmm.dll")]
    private static extern int waveInUnprepareHeader(nint hwi, nint pwh, uint cbwh);

    [DllImport("winmm.dll")]
    private static extern int waveInAddBuffer(nint hwi, nint pwh, uint cbwh);

    [DllImport("winmm.dll")]
    private static extern int waveInStart(nint hwi);

    [DllImport("winmm.dll")]
    private static extern int waveInStop(nint hwi);

    [DllImport("winmm.dll")]
    private static extern int waveInReset(nint hwi);

    private readonly ForegroundWindowService _foreground = new();
    private readonly KnockPatternMatcher _matcher = new();
    private readonly object _gate = new();
    private readonly Timer _finalizeTimer;

    private nint _hWaveIn;
    private WaveInProc? _callback;
    private readonly nint[] _headers = new nint[BufferCount];
    private readonly nint[] _dataBuffers = new nint[BufferCount];
    private KnockOnsetDetector? _detector;
    private volatile bool _isRunning;
    private bool _disposed;
    private long _lastTouchpadContactMs;

    public bool IsEnabled { get; private set; }
    public double Sensitivity { get; set; } = 0.65;
    public int PatternTimeoutMs { get; set; } = 480;
    public string? LastError { get; private set; }

    public Func<IReadOnlyList<GestureRule>>? GetRules { get; set; }
    public Func<IReadOnlyList<KnockSurfaceProfile>>? GetSurfaces { get; set; }
    public Func<bool>? ShouldIgnoreCurrentWindow { get; set; }

    /// <summary>
    /// Optional recording callback invoked on every detected knock onset during calibration or rule recording.
    /// </summary>
    public Action<KnockOnsetEvent>? RecordKnockCallback { get; set; }

    public event Action<KnockSequenceMatch, ForegroundAppInfo>? KnockPatternMatched;

    public KnockAudioService()
    {
        _finalizeTimer = new Timer(OnFinalizeTimer, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void NotifyTouchpadActivity()
    {
        Interlocked.Exchange(ref _lastTouchpadContactMs, Environment.TickCount64);
    }

    public void Configure(bool enabled, double sensitivity, int patternTimeoutMs)
    {
        IsEnabled = enabled;
        Sensitivity = Math.Clamp(sensitivity, 0.1, 1.0);
        PatternTimeoutMs = Math.Clamp(patternTimeoutMs, 200, 1500);

        lock (_gate)
        {
            if (_detector is not null)
            {
                _detector.Sensitivity = Sensitivity;
            }
        }

        if (enabled && !_isRunning)
        {
            StartCapture();
        }
        else if (!enabled && _isRunning)
        {
            StopCapture();
        }
    }

    private void StartCapture()
    {
        if (_disposed || _isRunning) return;

        try
        {
            var fmt = new WAVEFORMATEX
            {
                wFormatTag = WAVE_FORMAT_PCM,
                nChannels = 1,
                nSamplesPerSec = SampleRate,
                wBitsPerSample = 16,
                nBlockAlign = 2,
                nAvgBytesPerSec = SampleRate * 2,
                cbSize = 0
            };

            _callback = OnWaveInCallback;
            int res = waveInOpen(out _hWaveIn, WAVE_MAPPER, ref fmt, _callback, 0, CALLBACK_FUNCTION);
            if (res != 0 || _hWaveIn == 0)
            {
                LastError = $"waveInOpen failed (code {res})";
                _hWaveIn = 0;
                return;
            }

            lock (_gate)
            {
                _detector = new KnockOnsetDetector(SampleRate)
                {
                    Sensitivity = Sensitivity
                };
            }

            int hdrSize = Marshal.SizeOf<WAVEHDR>();
            for (int i = 0; i < BufferCount; i++)
            {
                _dataBuffers[i] = Marshal.AllocHGlobal(BufferBytes);
                _headers[i] = Marshal.AllocHGlobal(hdrSize);
                var hdr = new WAVEHDR
                {
                    lpData = _dataBuffers[i],
                    dwBufferLength = BufferBytes
                };
                Marshal.StructureToPtr(hdr, _headers[i], false);
                waveInPrepareHeader(_hWaveIn, _headers[i], (uint)hdrSize);
                waveInAddBuffer(_hWaveIn, _headers[i], (uint)hdrSize);
            }

            _isRunning = true;
            waveInStart(_hWaveIn);
            LastError = null;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            StopCapture();
        }
    }

    private void StopCapture()
    {
        _isRunning = false;
        _finalizeTimer.Change(Timeout.Infinite, Timeout.Infinite);

        nint h = _hWaveIn;
        _hWaveIn = 0;

        if (h != 0)
        {
            try
            {
                waveInStop(h);
                waveInReset(h);
                int hdrSize = Marshal.SizeOf<WAVEHDR>();
                for (int i = 0; i < BufferCount; i++)
                {
                    if (_headers[i] != 0)
                    {
                        waveInUnprepareHeader(h, _headers[i], (uint)hdrSize);
                        Marshal.FreeHGlobal(_headers[i]);
                        _headers[i] = 0;
                    }
                    if (_dataBuffers[i] != 0)
                    {
                        Marshal.FreeHGlobal(_dataBuffers[i]);
                        _dataBuffers[i] = 0;
                    }
                }
                waveInClose(h);
            }
            catch
            {
                // Best-effort cleanup
            }
        }

        lock (_gate)
        {
            _detector = null;
            _matcher.Reset();
        }
    }

    private void OnWaveInCallback(nint hwi, uint uMsg, nint dwInstance, nint dwParam1, nint dwParam2)
    {
        if (uMsg != WIM_DATA || !_isRunning || dwParam1 == 0) return;

        var hdr = Marshal.PtrToStructure<WAVEHDR>(dwParam1);
        int bytes = (int)hdr.dwBytesRecorded;
        if (bytes >= 2 && hdr.lpData != 0)
        {
            int samples = bytes / 2;
            var mono = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                short s = Marshal.ReadInt16(hdr.lpData, i * 2);
                mono[i] = s / 32768f;
            }
            ProcessAudioFrame(mono);
        }

        if (_isRunning && _hWaveIn != 0)
        {
            waveInAddBuffer(_hWaveIn, dwParam1, (uint)Marshal.SizeOf<WAVEHDR>());
        }
    }

    private void ProcessAudioFrame(float[] mono)
    {
        long nowMs = Environment.TickCount64;
        List<KnockOnsetEvent> events;
        lock (_gate)
        {
            if (_detector is null) return;
            events = _detector.ProcessSamples(mono, nowMs);
        }

        if (events.Count == 0) return;

        var recorder = RecordKnockCallback;
        if (recorder is not null)
        {
            foreach (var ev in events)
            {
                recorder(ev);
            }
            return;
        }

        if (!IsEnabled || ShouldIgnoreCurrentWindow?.Invoke() == true)
        {
            return;
        }

        lock (_gate)
        {
            foreach (var ev in events)
            {
                _matcher.AddKnock(ev);
            }
            _finalizeTimer.Change(PatternTimeoutMs + 15, Timeout.Infinite);
        }
    }

    private void OnFinalizeTimer(object? state)
    {
        long nowMs = Environment.TickCount64;
        var rules = GetRules?.Invoke() ?? [];
        var surfaces = GetSurfaces?.Invoke() ?? [];
        var fg = _foreground.GetForegroundApp();
        bool touchpadCorroborated = (nowMs - Interlocked.Read(ref _lastTouchpadContactMs)) <= 500;

        KnockSequenceMatch? match;
        lock (_gate)
        {
            match = _matcher.TryFinalizeIfExpired(
                nowMs,
                PatternTimeoutMs,
                rules,
                surfaces,
                fg.ProcessName,
                fg.WindowTitle,
                touchpadCorroborated);
        }

        if (match is not null)
        {
            KnockPatternMatched?.Invoke(match, fg);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _finalizeTimer.Dispose();
        StopCapture();
    }
}
