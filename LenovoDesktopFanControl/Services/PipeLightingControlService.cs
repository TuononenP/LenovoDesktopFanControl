using System.IO.Pipes;
using System.IO;
using System.Text.Json;
using LenovoDesktopFanControl.Models;

namespace LenovoDesktopFanControl.Services;

/// <summary>
/// UI-side proxy for the continuously running lighting host. The host owns
/// LampArray, so showing or closing the UI never releases the LEDs.
/// </summary>
public sealed class PipeLightingControlService : ILightingControlService
{
    private const int ConnectAttempts = 20;
    private static readonly TimeSpan ConnectRetryDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(5);
    private readonly Func<bool> _ensureHostRunning;
    private readonly string _pipeName;
    private readonly int _connectAttempts;
    private readonly TimeSpan _connectRetryDelay;
    private readonly TimeSpan _connectTimeout;
    private readonly TimeSpan _responseTimeout;
    private bool _isControlAvailable;

    public PipeLightingControlService()
        : this(
            LightingBackgroundHost.EnsureRunning,
            LightingHostProtocol.PipeName,
            ConnectAttempts,
            ConnectRetryDelay,
            TimeSpan.FromMilliseconds(500),
            ResponseTimeout)
    {
    }

    internal PipeLightingControlService(
        Func<bool> ensureHostRunning,
        string pipeName,
        int connectAttempts,
        TimeSpan connectRetryDelay,
        TimeSpan connectTimeout,
        TimeSpan responseTimeout)
    {
        ArgumentNullException.ThrowIfNull(ensureHostRunning);
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        ArgumentOutOfRangeException.ThrowIfLessThan(connectAttempts, 1);
        if (connectRetryDelay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(connectRetryDelay));
        if (connectTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(connectTimeout));
        if (responseTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(responseTimeout));

        _ensureHostRunning = ensureHostRunning;
        _pipeName = pipeName;
        _connectAttempts = connectAttempts;
        _connectRetryDelay = connectRetryDelay;
        _connectTimeout = connectTimeout;
        _responseTimeout = responseTimeout;
    }

    public bool IsControlAvailable => _isControlAvailable;
    public event EventHandler? AvailabilityChanged;

    public async Task<LightingDeviceInfo?> DiscoverAsync()
    {
        var response = await SendAsync(new LightingHostRequest(LightingHostProtocol.Discover));
        SetAvailability(response.Succeeded && response.Device != null);
        return response.Device;
    }

    public Task SetEnabledAsync(bool enabled) =>
        SendSucceededAsync(new LightingHostRequest(LightingHostProtocol.SetEnabled, Enabled: enabled));

    public Task SetBrightnessAsync(double brightness) =>
        SendSucceededAsync(new LightingHostRequest(LightingHostProtocol.SetBrightness, Brightness: brightness));

    public Task SetColorAsync(byte red, byte green, byte blue) =>
        SendSucceededAsync(new LightingHostRequest(
            LightingHostProtocol.SetColor, Red: red, Green: green, Blue: blue));

    public Task SetZoneColorAsync(int zoneIndex, byte red, byte green, byte blue) =>
        SendSucceededAsync(new LightingHostRequest(
            LightingHostProtocol.SetZoneColor, zoneIndex, Red: red, Green: green, Blue: blue));

    public Task SetZoneBrightnessAsync(int zoneIndex, double brightness) =>
        SendSucceededAsync(new LightingHostRequest(
            LightingHostProtocol.SetZoneBrightness, zoneIndex, Brightness: brightness));

    public Task SetZoneEnabledAsync(int zoneIndex, bool enabled) =>
        SendSucceededAsync(new LightingHostRequest(
            LightingHostProtocol.SetZoneEnabled, zoneIndex, Enabled: enabled));

    public Task ReapplyGpuStateAsync() =>
        SendSucceededAsync(new LightingHostRequest(LightingHostProtocol.ReapplyGpuState));

    public Task PersistStateAsync() => Task.CompletedTask;

    private async Task SendSucceededAsync(LightingHostRequest request)
    {
        var response = await SendAsync(request);
        if (!response.Succeeded)
            throw new InvalidOperationException(response.Error ?? "The lighting background host rejected the request");
    }

    private async Task<LightingHostResponse> SendAsync(LightingHostRequest request)
    {
        Exception? lastError = null;
        var recoveryAttempted = false;
        for (var attempt = 1; attempt <= _connectAttempts; attempt++)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(
                    ".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await pipe.ConnectAsync((int)_connectTimeout.TotalMilliseconds);
                using var reader = new StreamReader(pipe, leaveOpen: true);
                using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                await writer.WriteLineAsync(JsonSerializer.Serialize(request, LightingHostProtocol.JsonOptions));
                using var responseCancellation = new CancellationTokenSource(_responseTimeout);
                var line = await reader.ReadLineAsync(responseCancellation.Token);
                var response = string.IsNullOrWhiteSpace(line)
                    ? throw new InvalidOperationException("The lighting background host returned no response")
                    : JsonSerializer.Deserialize<LightingHostResponse>(line, LightingHostProtocol.JsonOptions)
                        ?? throw new InvalidOperationException("The lighting background host returned an invalid response");
                SetAvailability(response.Succeeded);
                return response;
            }
            catch (Exception ex) when (ex is TimeoutException or IOException or InvalidOperationException)
            {
                lastError = ex;
                if (!recoveryAttempted)
                {
                    recoveryAttempted = true;
                    TryRestartHost();
                }

                if (attempt < _connectAttempts)
                    await Task.Delay(_connectRetryDelay);
            }
            catch (OperationCanceledException)
            {
                SetAvailability(false);
                throw new InvalidOperationException(
                    $"The lighting background host did not respond within {_responseTimeout.TotalSeconds:N0} seconds");
            }
        }

        SetAvailability(false);
        throw new InvalidOperationException(
            "The lighting background host is not running or did not respond", lastError);
    }

    private void TryRestartHost()
    {
        try
        {
            if (_ensureHostRunning())
                Log.Info("Restarted the lighting background host after a pipe connection failure");
            else
                Log.Warn("Unable to restart the lighting background host after a pipe connection failure");
        }
        catch (Exception ex)
        {
            Log.Warn($"Unable to restart the lighting background host: {ex.Message}");
        }
    }

    private void SetAvailability(bool available)
    {
        if (_isControlAvailable == available)
            return;
        _isControlAvailable = available;
        AvailabilityChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
    }
}
