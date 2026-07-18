using System.Windows;

namespace LenovoDesktopFanControl;

using SystemColors = System.Windows.SystemColors;

public partial class App : System.Windows.Application
{
    internal static bool StartMinimized { get; private set; }
#if DEBUG
    private const string ApplicationMutexName = @"Local\LenovoDesktopFanControl.Debug";
    private const string StopApplicationEventName =
        @"Local\LenovoDesktopFanControl.StopApplication.Debug";
#else
    private const string ApplicationMutexName = @"Local\LenovoDesktopFanControl";
    private const string StopApplicationEventName =
        @"Local\LenovoDesktopFanControl.StopApplication";
#endif

    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _stopApplicationEvent;
    private readonly Dictionary<string, System.Windows.Media.Color> _standardPalette = [];

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Length == 1 &&
            string.Equals(e.Args[0], "--uninstall-lighting-host", StringComparison.OrdinalIgnoreCase))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            base.OnStartup(e);
            var hostStopped = Services.LightingBackgroundHost.StopExisting();
            new Services.AutoStartService().Disable();
            if (!hostStopped)
                Environment.ExitCode = 1;
            Shutdown();
            return;
        }

        if (Services.LightingBackgroundHost.TryCreate(e.Args, out var lightingBackgroundHost))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            base.OnStartup(e);
            _ = RunLightingBackgroundHostAsync(lightingBackgroundHost!);
            return;
        }

        if (Services.LenovoTowerLightingPersistence.TryParseDeferredPersistenceRequest(
                e.Args,
                out var persistenceRequest))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            base.OnStartup(e);
            _ = RunDeferredLightingPersistenceAsync(persistenceRequest);
            return;
        }

        // The host retains LampArray ownership for its entire lifetime. The
        // interactive window communicates with it over a local named pipe.
        Services.LightingBackgroundHost.EnsureRunning();
        EnsureLightingBackgroundStartsWithWindows();

        StartMinimized = e.Args.Any(
            argument => string.Equals(argument, "--minimized", StringComparison.OrdinalIgnoreCase));

        _singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            name: ApplicationMutexName,
            createdNew: out var createdNew);
        if (!createdNew)
        {
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            System.Windows.MessageBox.Show(
                Services.LocalizationService.Get("MsgApplicationAlreadyRunning"),
                Services.LocalizationService.Get("AppTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }
        _stopApplicationEvent = new EventWaitHandle(
            false,
            EventResetMode.AutoReset,
            StopApplicationEventName);

        base.OnStartup(e);

        foreach (var key in PaletteKeys)
            _standardPalette[key] = (System.Windows.Media.Color)Resources[key];

        ApplySystemContrastPalette();
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
        _ = WaitForApplicationStopRequestAsync(mainWindow);
    }

    private async Task WaitForApplicationStopRequestAsync(MainWindow mainWindow)
    {
        try
        {
            var stopEvent = _stopApplicationEvent;
            if (stopEvent == null)
                return;

            await Task.Run(() => stopEvent.WaitOne());
            await Dispatcher.InvokeAsync(mainWindow.RequestExitForUninstall);
        }
        catch (ObjectDisposedException)
        {
            // Normal application shutdown disposed the wait handle.
        }
    }

    private async Task RunLightingBackgroundHostAsync(
        Services.LightingBackgroundHost lightingBackgroundHost)
    {
        using (lightingBackgroundHost)
        {
            await lightingBackgroundHost.RunAsync();
        }

        Shutdown();
    }

    private static void EnsureLightingBackgroundStartsWithWindows()
    {
        try
        {
            var settings = new Services.SettingsService().Load();
            if (!settings.StartWithWindows)
                return;

            var executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                Services.Log.Warn("Unable to enable lighting background startup: executable path is unavailable");
                return;
            }

            new Services.AutoStartService().Enable(executablePath);
            Services.Log.Info("Configured the lighting background host to start at Windows sign-in");
        }
        catch (Exception ex)
        {
            Services.Log.Warn($"Unable to configure lighting background startup: {ex.Message}");
        }
    }

    private async Task RunDeferredLightingPersistenceAsync(
        Services.LenovoTowerLightingPersistence.DeferredLightingPersistenceRequest request)
    {
        try
        {
            try
            {
                using var parent = System.Diagnostics.Process.GetProcessById(request.ParentProcessId);
                if (!parent.HasExited)
                    await parent.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            }
            catch (ArgumentException)
            {
                // The main process exited before the helper opened its process handle.
            }

            // Allow Windows to finish releasing LampArray ownership before making
            // the firmware profile the final lighting write.
            await Task.Delay(500);
            await Task.Run(() =>
            {
                var persistence = new Services.LenovoTowerLightingPersistence();
                if (!persistence.TrySaveStaticColor(
                        request.Red,
                        request.Green,
                        request.Blue,
                        request.Brightness,
                        request.Enabled))
                {
                    Services.Log.Warn("Post-exit tower lighting persistence failed");
                }
            });
        }
        catch (TimeoutException)
        {
            Services.Log.Warn("Post-exit tower lighting persistence timed out waiting for the main process");
        }
        catch (Exception ex)
        {
            Services.Log.Error("Post-exit tower lighting persistence failed", ex);
        }
        finally
        {
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        _singleInstanceMutex = null;
        _stopApplicationEvent?.Dispose();
        _stopApplicationEvent = null;
        base.OnExit(e);
    }

    private void OnSystemParametersChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SystemParameters.HighContrast))
            return;

        if (Dispatcher.CheckAccess())
            ApplySystemContrastPalette();
        else
            _ = Dispatcher.BeginInvoke(ApplySystemContrastPalette);
    }

    private void ApplySystemContrastPalette()
    {
        if (!SystemParameters.HighContrast)
        {
            foreach (var entry in _standardPalette)
                Resources[entry.Key] = entry.Value;
            return;
        }

        SetPaletteColor("WindowColor", SystemColors.WindowColor);
        SetPaletteColor("SurfaceColor", SystemColors.WindowColor);
        SetPaletteColor("SurfaceElevatedColor", SystemColors.ControlColor);
        SetPaletteColor("SurfaceHoverColor", SystemColors.ControlColor);
        SetPaletteColor("BorderColor", SystemColors.WindowTextColor);
        SetPaletteColor("AccentColor", SystemColors.HighlightColor);
        SetPaletteColor("AccentHoverColor", SystemColors.HotTrackColor);
        SetPaletteColor("TextPrimaryColor", SystemColors.WindowTextColor);
        SetPaletteColor("TextOnAccentColor", SystemColors.HighlightTextColor);
        SetPaletteColor("TextSecondaryColor", SystemColors.WindowTextColor);
        SetPaletteColor("TextMutedColor", SystemColors.WindowTextColor);
        SetPaletteColor("SuccessColor", SystemColors.WindowTextColor);
        SetPaletteColor("WarningColor", SystemColors.WindowTextColor);
        SetPaletteColor("DangerColor", SystemColors.WindowTextColor);
        SetPaletteColor("UnsupportedColor", SystemColors.WindowTextColor);
        SetPaletteColor("AccentSoftColor", SystemColors.ControlColor);
        SetPaletteColor("WarningSoftColor", SystemColors.ControlColor);
        SetPaletteColor("DangerSoftColor", SystemColors.ControlColor);
    }

    private void SetPaletteColor(string key, System.Windows.Media.Color color) => Resources[key] = color;

    private static readonly string[] PaletteKeys =
    [
        "WindowColor", "SurfaceColor", "SurfaceElevatedColor", "SurfaceHoverColor",
        "BorderColor", "AccentColor", "AccentHoverColor", "TextPrimaryColor",
        "TextOnAccentColor", "TextSecondaryColor", "TextMutedColor", "SuccessColor", "WarningColor",
        "DangerColor", "UnsupportedColor", "AccentSoftColor", "WarningSoftColor", "DangerSoftColor"
    ];
}
