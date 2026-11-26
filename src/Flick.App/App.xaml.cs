using Flick.App.Services;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace Flick.App;

public partial class App : Application
{
    private MainWindow? _window;
    private bool _isExiting;

    public static GestureEngineService Engine { get; } = new();
    public static TrayIconService Tray { get; } = new();
    public static nint MainWindowHandle { get; private set; }

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();

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

        Tray.IsEngineEnabled = () => Engine.Profile.Settings.IsEnabled;
        Tray.OpenRequested += () =>
        {
            _window.DispatcherQueue.TryEnqueue(() =>
            {
                NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
                NativeMethods.SetForegroundWindow(hwnd);
                _window.Activate();
            });
        };
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
                Tray.Dispose();
                Engine.Dispose();
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
