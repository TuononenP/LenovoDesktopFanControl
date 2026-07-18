using System.IO.Pipes;
using System.IO;
using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LenovoDesktopFanControl.Models;

namespace LenovoDesktopFanControl.Services;

internal static class LightingHostProtocol
{
    // Named pipes are machine-wide. Include both the user SID and session ID so
    // per-user installs cannot reach another user's lighting host.
    internal static string PipeName { get; } = CreatePipeName();

    internal const string Discover = "discover";
    internal const string SetEnabled = "set-enabled";
    internal const string SetBrightness = "set-brightness";
    internal const string SetColor = "set-color";
    internal const string SetZoneColor = "set-zone-color";
    internal const string SetZoneBrightness = "set-zone-brightness";
    internal const string SetZoneEnabled = "set-zone-enabled";
    internal const string ReapplyGpuState = "reapply-gpu-state";

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static string CreatePipeName()
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value ?? "UnknownUser";
        var sessionId = Process.GetCurrentProcess().SessionId;
        return $"LenovoDesktopFanControl.LightingHost{BuildChannelSuffix}.{sid}.{sessionId}";
    }

#if DEBUG
    private const string BuildChannelSuffix = ".Debug";
#else
    private const string BuildChannelSuffix = "";
#endif
}

internal sealed record LightingHostRequest(
    string Operation,
    int ZoneIndex = 0,
    bool Enabled = false,
    double Brightness = 0,
    byte Red = 0,
    byte Green = 0,
    byte Blue = 0);

internal sealed record LightingHostResponse(
    bool Succeeded,
    string? Error = null,
    LightingDeviceInfo? Device = null);

internal sealed class LightingHostServer
{
    private const int MaximumRequestCharacters = 16 * 1024;
    private readonly LightingHostController _controller;
    private readonly string _pipeName;
    private readonly TimeSpan _requestTimeout;

    internal LightingHostServer(
        LightingHostController controller,
        string? pipeName = null,
        TimeSpan? requestTimeout = null)
    {
        _controller = controller;
        _pipeName = pipeName ?? LightingHostProtocol.PipeName;
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(5);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                await pipe.WaitForConnectionAsync(cancellationToken);
                await HandleConnectionAsync(pipe, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Log.Warn($"Lighting host pipe failed: {ex.Message}");
            }
        }
    }

    private async Task HandleConnectionAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, leaveOpen: true);
        using var writer = new StreamWriter(stream, leaveOpen: true) { AutoFlush = true };
        using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestCancellation.CancelAfter(_requestTimeout);

        LightingHostResponse response;
        try
        {
            var line = await ReadRequestLineAsync(reader, requestCancellation.Token);
            if (string.IsNullOrWhiteSpace(line))
                return;

            var request = JsonSerializer.Deserialize<LightingHostRequest>(line, LightingHostProtocol.JsonOptions)
                ?? throw new InvalidOperationException("The lighting-host request was empty");
            response = await _controller.HandleAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (
            requestCancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            Log.Warn($"Lighting host request did not finish within {_requestTimeout.TotalSeconds:N0} seconds");
            return;
        }
        catch (Exception ex)
        {
            Log.Warn($"Lighting host request failed: {ex.Message}");
            response = new LightingHostResponse(false, ex.Message);
        }

        await writer.WriteLineAsync(JsonSerializer.Serialize(response, LightingHostProtocol.JsonOptions));
    }

    private static async Task<string?> ReadRequestLineAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var request = new StringBuilder();
        var buffer = new char[1];
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0)
                return request.Length == 0 ? null : request.ToString();

            var character = buffer[0];
            if (character == '\n')
                return request.ToString();
            if (character == '\r')
                continue;
            if (request.Length == MaximumRequestCharacters)
                throw new InvalidOperationException("The lighting-host request exceeds the maximum supported size");

            request.Append(character);
        }
    }
}

internal sealed class LightingHostController : IDisposable
{
    private readonly ILightingControlService _lightingService;
    private readonly ISettingsService _settingsService;
    private readonly TimeSpan _savedStateSettleDelay;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private LightingDeviceInfo? _device;

    internal LightingHostController(
        ILightingControlService lightingService,
        ISettingsService? settingsService = null,
        TimeSpan? savedStateSettleDelay = null)
    {
        _lightingService = lightingService;
        _settingsService = settingsService ?? new SettingsService();
        _savedStateSettleDelay = savedStateSettleDelay ?? TimeSpan.FromMilliseconds(900);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            _device = await _lightingService.DiscoverAsync();
            if (_device == null)
            {
                Log.Warn("Lighting background host could not find a Lenovo lighting controller");
                return;
            }

            await ApplySavedLightingAsync(_settingsService.Load());
            Log.Info("Lighting background host applied the saved per-zone lighting state");
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<LightingHostResponse> HandleAsync(
        LightingHostRequest request,
        CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            switch (request.Operation)
            {
                case LightingHostProtocol.Discover:
                    if (_device == null)
                    {
                        _device = await _lightingService.DiscoverAsync();
                        if (_device != null)
                        {
                            await ApplySavedLightingAsync(_settingsService.Load());
                            Log.Info("Lighting background host applied saved lighting after delayed controller discovery");
                        }
                    }
                    return new LightingHostResponse(true, Device: _device);

                case LightingHostProtocol.SetEnabled:
                    await _lightingService.SetEnabledAsync(request.Enabled);
                    break;
                case LightingHostProtocol.SetBrightness:
                    await _lightingService.SetBrightnessAsync(request.Brightness);
                    break;
                case LightingHostProtocol.SetColor:
                    await _lightingService.SetColorAsync(request.Red, request.Green, request.Blue);
                    break;
                case LightingHostProtocol.SetZoneColor:
                    await _lightingService.SetZoneColorAsync(
                        request.ZoneIndex, request.Red, request.Green, request.Blue);
                    break;
                case LightingHostProtocol.SetZoneBrightness:
                    await _lightingService.SetZoneBrightnessAsync(request.ZoneIndex, request.Brightness);
                    break;
                case LightingHostProtocol.SetZoneEnabled:
                    await _lightingService.SetZoneEnabledAsync(request.ZoneIndex, request.Enabled);
                    break;
                case LightingHostProtocol.ReapplyGpuState:
                    await _lightingService.ReapplyGpuStateAsync();
                    break;
                default:
                    return new LightingHostResponse(false, $"Unknown lighting-host operation '{request.Operation}'");
            }

            return new LightingHostResponse(true);
        }
        catch (Exception ex)
        {
            Log.Warn($"Lighting-host operation '{request.Operation}' failed: {ex.Message}");
            return new LightingHostResponse(false, ex.Message);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task ApplySavedLightingAsync(FanSettings settings)
    {
        if (_device == null)
            return;

        await _lightingService.SetBrightnessAsync(Math.Clamp(settings.LightingBrightness, 0, 100) / 100d);
        foreach (var zone in _device.Zones)
        {
            var enabled = !settings.LightingZoneEnabled.TryGetValue(zone.Index, out var savedEnabled) || savedEnabled;
            var brightness = settings.LightingZoneBrightness.TryGetValue(zone.Index, out var savedBrightness)
                ? Math.Clamp(savedBrightness, 0, 100) / 100d
                : 1d;
            var color = settings.LightingZoneColors.TryGetValue(zone.Index, out var savedColor)
                ? savedColor
                : new LightingZoneColor(zone.Index, 91, 157, 255);

            await _lightingService.SetZoneEnabledAsync(zone.Index, enabled);
            await _lightingService.SetZoneBrightnessAsync(zone.Index, brightness);
            await _lightingService.SetZoneColorAsync(zone.Index, color.Red, color.Green, color.Blue);
        }

        await _lightingService.SetEnabledAsync(settings.LightingEnabled);
        await Task.Delay(_savedStateSettleDelay);
        await _lightingService.ReapplyGpuStateAsync();
    }

    public void Dispose()
    {
        _operationGate.Dispose();
        _lightingService.Dispose();
    }
}
