using System.Management;

namespace LenovoDesktopFanControl.Services;

internal enum DynamicLightingFirmwareRecoveryResult
{
    Unavailable,
    Unsupported,
    AlreadyEnabled,
    Enabled,
    Failed
}

internal interface IDynamicLightingFirmwareService
{
    Task<DynamicLightingFirmwareRecoveryResult> EnsureEnabledAsync();
}

internal sealed class DynamicLightingFirmwareRecovery
{
    internal const ushort LenovoVendorId = 0x17EF;
    internal const ushort LenovoLightingProductId = 0xC955;

    private readonly IDynamicLightingFirmwareService _firmwareService;

    public DynamicLightingFirmwareRecovery(IDynamicLightingFirmwareService firmwareService)
    {
        _firmwareService = firmwareService;
    }

    public async Task<bool> TryEnableAsync(ushort vendorId, ushort productId, int lampCount)
    {
        if (!ShouldAttempt(vendorId, productId, lampCount))
            return false;

        var result = await _firmwareService.EnsureEnabledAsync().ConfigureAwait(false);
        Log.Info($"Dynamic Lighting firmware recovery result: {result}");
        return result == DynamicLightingFirmwareRecoveryResult.Enabled;
    }

    internal static bool ShouldAttempt(ushort vendorId, ushort productId, int lampCount) =>
        vendorId == LenovoVendorId &&
        productId == LenovoLightingProductId &&
        lampCount == 0;
}

internal sealed class WmiDynamicLightingFirmwareService : IDynamicLightingFirmwareService
{
    private const string Scope = "root\\WMI";
    private const string GameZoneQuery = "SELECT * FROM LENOVO_GAMEZONE_DATA";

    public async Task<DynamicLightingFirmwareRecoveryResult> EnsureEnabledAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(Scope, GameZoneQuery);
                foreach (ManagementObject gameZone in searcher.Get())
                {
                    using (gameZone)
                    {
                        var supported = InvokeUInt32(gameZone, "IsSupportDynamicLighting");
                        Log.Info($"IsSupportDynamicLighting = {supported}");
                        if (supported == 0)
                            return DynamicLightingFirmwareRecoveryResult.Unsupported;

                        var state = InvokeUInt32(gameZone, "GetDynamicLighting");
                        Log.Info($"GetDynamicLighting = {state}");
                        if (state != 0)
                            return DynamicLightingFirmwareRecoveryResult.AlreadyEnabled;

                        using var inParams = gameZone.GetMethodParameters("SetDynamicLighting");
                        inParams["Data"] = 1u;
                        gameZone.InvokeMethod("SetDynamicLighting", inParams, null);
                        Log.Info("SetDynamicLighting(1) succeeded");
                        return DynamicLightingFirmwareRecoveryResult.Enabled;
                    }
                }

                Log.Warn("Dynamic Lighting firmware recovery found no LENOVO_GAMEZONE_DATA objects");
                return DynamicLightingFirmwareRecoveryResult.Unavailable;
            }
            catch (Exception ex)
            {
                Log.Warn($"Dynamic Lighting firmware recovery failed: {ex.Message}");
                return DynamicLightingFirmwareRecoveryResult.Failed;
            }
        }).ConfigureAwait(false);
    }

    private static uint InvokeUInt32(ManagementObject target, string methodName)
    {
        using var inParams = target.GetMethodParameters(methodName);
        using var outParams = target.InvokeMethod(methodName, inParams, null);
        if (outParams?["Data"] == null)
            throw new InvalidOperationException($"{methodName} returned no Data value");

        return Convert.ToUInt32(outParams["Data"]);
    }
}
