using System.Diagnostics;

namespace LenovoDesktopFanControl.Services;

/// <summary>
/// Retains Dynamic Lighting ownership for the entire user session. The UI
/// communicates with this host instead of opening LampArray itself.
/// </summary>
internal sealed class LightingBackgroundHost : IDisposable
{
    internal const string BackgroundArgument = "--lighting-background";
    private const string LightingOwnershipMutexName =
        @"Local\LenovoDesktopFanControl.LightingOwnership";
#if DEBUG
    private const string HostMutexName =
        @"Local\LenovoDesktopFanControl.LightingBackground.Debug";
    private const string StopEventName =
        @"Local\LenovoDesktopFanControl.StopLightingBackground.Debug";
    private const string ReleaseHostMutexName =
        @"Local\LenovoDesktopFanControl.LightingBackground";
#else
    private const string HostMutexName =
        @"Local\LenovoDesktopFanControl.LightingBackground";
    private const string StopEventName =
        @"Local\LenovoDesktopFanControl.StopLightingBackground";
#endif
    private static readonly TimeSpan HostStopTimeout = TimeSpan.FromSeconds(8);

    private readonly EventWaitHandle _stopEvent = new(false, EventResetMode.AutoReset, StopEventName);
    private Mutex? _hostMutex;
    private bool _ownsHostMutex;
    private Mutex? _lightingOwnershipMutex;
    private bool _ownsLightingOwnershipMutex;

    private LightingBackgroundHost()
    {
    }

    internal static bool TryCreate(IReadOnlyList<string> arguments, out LightingBackgroundHost? host)
    {
        host = null;
        if (arguments.Count == 1 &&
            string.Equals(arguments[0], BackgroundArgument, StringComparison.OrdinalIgnoreCase))
        {
            host = new LightingBackgroundHost();
            return true;
        }

        return false;
    }

    internal static bool StopExisting()
    {
        try
        {
            using var stopEvent = new EventWaitHandle(false, EventResetMode.AutoReset, StopEventName);
            stopEvent.Set();

            using var hostMutex = new Mutex(false, HostMutexName);
            if (!hostMutex.WaitOne(HostStopTimeout))
            {
                Log.Warn("Timed out waiting for the lighting background host to stop");
                return false;
            }

            hostMutex.ReleaseMutex();
            return true;
        }
        catch (AbandonedMutexException)
        {
            // The previous host was terminated. Windows has already released
            // its lighting ownership, so the interactive app can continue.
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"Unable to stop the lighting background host: {ex.Message}");
            return false;
        }
    }

    internal static bool EnsureRunning()
    {
        try
        {
            var startInfo = ApplicationLaunch.CreateProcessStartInfo(BackgroundArgument);

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                Log.Warn("Unable to start the lighting background host");
                return false;
            }

            Log.Info($"Started lighting background host with PID {process.Id}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"Unable to start the lighting background host: {ex.Message}");
            return false;
        }
    }

    internal async Task RunAsync()
    {
#if DEBUG
        if (Mutex.TryOpenExisting(ReleaseHostMutexName, out var releaseHostMutex))
        {
            releaseHostMutex.Dispose();
            Log.Warn("The installed lighting background host is already running; stop it before using the Debug host");
            return;
        }
#endif

        _lightingOwnershipMutex = new Mutex(false, LightingOwnershipMutexName);
        try
        {
            _ownsLightingOwnershipMutex = _lightingOwnershipMutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            _ownsLightingOwnershipMutex = true;
        }
        if (!_ownsLightingOwnershipMutex)
        {
            Log.Warn("Another build owns the Lenovo lighting controller");
            return;
        }

        _hostMutex = new Mutex(initiallyOwned: true, HostMutexName, out var createdNew);
        if (!createdNew)
        {
            Log.Info("A lighting background host is already running");
            return;
        }

        _ownsHostMutex = true;

        try
        {
            RefreshAutoStartTask();
            Log.Info("Lighting background host started");
            using var controller = new LightingHostController(new LampArrayLightingService());
            using var cancellation = new CancellationTokenSource();
            var server = new LightingHostServer(controller);
            var serverTask = server.RunAsync(cancellation.Token);

            await controller.InitializeAsync(cancellation.Token);
            await Task.Run(() => _stopEvent.WaitOne());
            cancellation.Cancel();
            await serverTask;
            Log.Info("Lighting background host stopped at the interactive app's request");
        }
        catch (Exception ex)
        {
            Log.Error("Lighting background host failed", ex);
        }
    }

    private static void RefreshAutoStartTask()
    {
        try
        {
            var settings = new SettingsService().Load();
            if (!settings.StartWithWindows)
                return;

            var executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                Log.Warn("Unable to refresh lighting startup: executable path is unavailable");
                return;
            }

            new AutoStartService().Enable(executablePath);
            Log.Info("Refreshed the lighting background startup task");
        }
        catch (Exception ex)
        {
            Log.Warn($"Unable to refresh lighting background startup: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _stopEvent.Dispose();
        if (_hostMutex != null)
        {
            if (_ownsHostMutex)
                _hostMutex.ReleaseMutex();
            _hostMutex.Dispose();
            _hostMutex = null;
            _ownsHostMutex = false;
        }
        if (_lightingOwnershipMutex != null)
        {
            if (_ownsLightingOwnershipMutex)
                _lightingOwnershipMutex.ReleaseMutex();
            _lightingOwnershipMutex.Dispose();
            _lightingOwnershipMutex = null;
            _ownsLightingOwnershipMutex = false;
        }
    }
}
