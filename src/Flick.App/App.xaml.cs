using Flick.App.Services;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace Flick.App;

public partial class App : Application
{
    private MainWindow? _window;
    private bool _isExiting;
    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _activateEvent;

    public static GestureEngineService Engine { get; } = new();
    public static TrayIconService Tray { get; } = new();
    public static nint MainWindowHandle { get; private set; }
    public static MainWindow? MainWindowInstance { get; private set; }

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _singleInstanceMutex = new Mutex(true, @"Local\Flick.SingleInstance.Mutex", out bool createdNew);
        if (!createdNew)
        {
            try
            {
                using var existingEv = EventWaitHandle.OpenExisting(@"Local\Flick.SingleInstance.Activate");
                existingEv.Set();
            }
            catch { }

            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Exit();
            return;
        }

        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Flick.SingleInstance.Activate");

        _window = new MainWindow();
        MainWindowInstance = _window;

        nint hwnd = WindowNative.GetWindowHandle(_window);
        MainWindowHandle = hwnd;
        _window.AppWindow.Closing += (_, e) =>
        {
            if (!_isExiting && Engine.Profile.Settings.MinimizeToTray)
            {
                e.Cancel = true;
                NativeMethods.ShowWindow(hwnd, NativeMethods.SW_HIDE);
            }
        };

        ApplyTheme(Engine.Profile.Settings.ThemePreference);
        Engine.Start(_window.DispatcherQueue);

        void ShowMainWindow()
        {
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
            NativeMethods.SetForegroundWindow(hwnd);
            _window.Activate();
        }

        _ = Task.Run(() =>
        {
            while (!_isExiting && _activateEvent is { } ev)
            {
                try
                {
                    if (ev.WaitOne() && !_isExiting)
                    {
                        _window.DispatcherQueue.TryEnqueue(ShowMainWindow);
                    }
                }
                catch
                {
                    break;
                }
            }
        });

        Tray.IsEngineEnabled = () => Engine.Profile.Settings.IsEnabled;
        Tray.OpenRequested += () => _window.DispatcherQueue.TryEnqueue(ShowMainWindow);
        Tray.ToggleEnabledRequested += () =>
        {
            _window.DispatcherQueue.TryEnqueue(() =>
            {
                Engine.Profile.Settings.IsEnabled = !Engine.Profile.Settings.IsEnabled;
                Engine.SaveProfile();
            });
        };
        Tray.ExitRequested += () =>
        {
            _window.DispatcherQueue.TryEnqueue(() =>
            {
                _isExiting = true;
                try { _activateEvent?.Set(); } catch { }
                _activateEvent?.Dispose();
                _activateEvent = null;
                Tray.Dispose();
                Engine.Dispose();
                try { _singleInstanceMutex?.ReleaseMutex(); } catch { }
                _singleInstanceMutex?.Dispose();
                _singleInstanceMutex = null;
                _window.Close();
                Exit();
            });
        };
        Tray.Start();

        bool startMinimized = Environment.GetCommandLineArgs()
            .Any(a => string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase));

        if (!startMinimized)
        {
            _window.Activate();
        }
    }

    public void ApplyTheme(string themePreference)
    {
        if (_window?.Content is FrameworkElement root)
        {
            root.RequestedTheme = themePreference switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                _ => ElementTheme.Default
            };
        }
    }
}
