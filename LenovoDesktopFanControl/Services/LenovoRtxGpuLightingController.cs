using System.Runtime.InteropServices;

namespace LenovoDesktopFanControl.Services;

internal interface ILenovoGpuLightingController : IDisposable
{
    bool IsAvailable { get; }
    bool TryDiscover();
    bool TryApplyStaticColor(byte red, byte green, byte blue, double brightness, bool enabled);
}

internal sealed class LenovoRtxGpuLightingController : ILenovoGpuLightingController
{
    internal const ushort NvidiaVendorId = 0x10DE;
    internal const ushort Rtx5080DeviceId = 0x2C02;
    internal const ushort LenovoSubsystemVendorId = 0x17AA;
    internal const ushort LenovoSubsystemDeviceId = 0xC770;

    private const uint NvApiInitializeId = 0x0150E828;
    private const uint NvApiUnloadId = 0xD22BDD7E;
    private const uint NvApiEnumPhysicalGpusId = 0xE5AC921F;
    private const uint NvApiGetPciIdentifiersId = 0x2DDFB66E;
    private const uint NvApiGpuGetAllDisplayIdsId = 0x785210A2;
    private const uint NvApiSysGetGpuAndOutputIdFromDisplayIdId = 0x112BA1A5;
    private const uint NvApiI2cWriteId = 0xE812EB07;
    private const uint I2cInfoVersion3 = 0x00030040;
    private const uint GpuDisplayIdsVersion3 = 0x00030010;
    private const uint HdmiConnectorType = 4;
    private const byte I2cDeviceAddress = 0x92; // 8-bit form of Lenovo's 7-bit controller address 0x49.
    private const uint I2cSpeedDeprecated = 0xFFFF;
    private const uint LenovoI2cSpeedKhz = 4;
    private const byte PortId = 1;
    private const int MaxPhysicalGpus = 64;
    private const int ApplyAttemptCount = 3;
    private const int ApplyRetryDelayMilliseconds = 20;

    private readonly object _sync = new();
    private nint _library;
    private nint _gpuHandle;
    private uint _displayMask;
    private NvApiUnload? _unload;
    private NvApiI2cWrite? _i2cWrite;
    private bool _nvApiInitialized;
    private bool _disposed;

    public bool IsAvailable =>
        _gpuHandle != 0 &&
        _displayMask != 0 &&
        _i2cWrite != null &&
        !_disposed;

    public bool TryDiscover()
    {
        lock (_sync)
        {
            if (_disposed)
                return false;
            if (IsAvailable)
                return true;

            try
            {
                if (!NativeLibrary.TryLoad("nvapi64.dll", out _library))
                {
                    Log.Info("Lenovo RTX lighting unavailable: nvapi64.dll was not found");
                    return false;
                }

                var query = Marshal.GetDelegateForFunctionPointer<NvApiQueryInterface>(
                    NativeLibrary.GetExport(_library, "nvapi_QueryInterface"));
                var initialize = GetDelegate<NvApiInitialize>(query, NvApiInitializeId);
                _unload = GetDelegate<NvApiUnload>(query, NvApiUnloadId);
                var enumerate = GetDelegate<NvApiEnumPhysicalGpus>(query, NvApiEnumPhysicalGpusId);
                var getPciIdentifiers = GetDelegate<NvApiGetPciIdentifiers>(query, NvApiGetPciIdentifiersId);
                var getAllDisplayIds =
                    GetDelegate<NvApiGpuGetAllDisplayIds>(query, NvApiGpuGetAllDisplayIdsId);
                var getGpuAndOutputId = GetDelegate<NvApiSysGetGpuAndOutputIdFromDisplayId>(
                    query,
                    NvApiSysGetGpuAndOutputIdFromDisplayIdId);
                _i2cWrite = GetDelegate<NvApiI2cWrite>(query, NvApiI2cWriteId);

                if (initialize == null ||
                    enumerate == null ||
                    getPciIdentifiers == null ||
                    getAllDisplayIds == null ||
                    getGpuAndOutputId == null ||
                    _i2cWrite == null)
                {
                    Log.Info("Lenovo RTX lighting unavailable: required NVAPI functions are missing");
                    ReleaseNativeLibrary();
                    return false;
                }

                var status = initialize();
                if (status != 0)
                {
                    Log.Warn($"Lenovo RTX lighting unavailable: NvAPI_Initialize returned {status}");
                    ReleaseNativeLibrary();
                    return false;
                }
                _nvApiInitialized = true;

                var handles = new nint[MaxPhysicalGpus];
                status = enumerate(handles, out var count);
                if (status != 0)
                {
                    Log.Warn($"Lenovo RTX lighting unavailable: NvAPI_EnumPhysicalGPUs returned {status}");
                    ReleaseNativeLibrary();
                    return false;
                }

                for (var index = 0; index < Math.Min(count, (uint)handles.Length); index++)
                {
                    status = getPciIdentifiers(
                        handles[index],
                        out var deviceId,
                        out var subsystemId,
                        out _,
                        out _);
                    if (status != 0)
                        continue;

                    if (!IsSupportedPciDevice(deviceId, subsystemId))
                        continue;

                    if (!TryGetHdmiOutputMask(
                            handles[index],
                            getAllDisplayIds,
                            getGpuAndOutputId,
                            out var displayMask))
                    {
                        Log.Warn(
                            "Lenovo RTX lighting unavailable: the GPU's internal HDMI lighting " +
                            "output was not found");
                        continue;
                    }

                    _gpuHandle = handles[index];
                    _displayMask = displayMask;
                    Log.Info(
                        "Lenovo RTX GPU lighting detected: " +
                        $"device=0x{deviceId:X8}, subsystem=0x{subsystemId:X8}, " +
                        $"displayMask=0x{_displayMask:X8}, I2C=0x{I2cDeviceAddress >> 1:X2}, port={PortId}");
                    return true;
                }

                Log.Info("Lenovo RTX GPU lighting was not detected");
                ReleaseNativeLibrary();
            }
            catch (Exception ex)
            {
                Log.Warn($"Lenovo RTX lighting discovery failed: {ex.Message}");
                ReleaseNativeLibrary();
            }

            return false;
        }
    }

    public bool TryApplyStaticColor(byte red, byte green, byte blue, double brightness, bool enabled)
    {
        lock (_sync)
        {
            if (!IsAvailable)
                return false;

            // Turning the master switch off must not rely only on the output
            // latch. The RTX card has more than one visible diffuser, and a
            // retained static frame can leave one illuminated if it misses an
            // output-latch transition. Clear the complete controller frame
            // first; the saved color remains in the service and is restored
            // on the next enabled write.
            var appliedRed = enabled ? red : (byte)0;
            var appliedGreen = enabled ? green : (byte)0;
            var appliedBlue = enabled ? blue : (byte)0;
            var level = enabled ? MapBrightness(brightness) : (byte)0;
            try
            {
                var commands = BuildStaticCommands(
                    appliedRed,
                    appliedGreen,
                    appliedBlue,
                    level,
                    enabled);
                for (var attempt = 1; attempt <= ApplyAttemptCount; attempt++)
                {
                    if (TryWriteCommands(commands, out var failedCommand, out var status))
                    {
                        Log.Info(
                            $"Lenovo RTX lighting writes accepted: enabled={enabled}, " +
                            $"brightness={level}/7, r={appliedRed}, g={appliedGreen}, b={appliedBlue}, " +
                            $"attempt={attempt}");
                        return true;
                    }

                    if (attempt == ApplyAttemptCount)
                    {
                        Log.Warn(
                            $"Lenovo RTX lighting write failed at register " +
                            $"0x{failedCommand.Register:X2}: NVAPI status {status} " +
                            $"after {attempt} attempts");
                        return false;
                    }

                    Log.Warn(
                        $"Lenovo RTX lighting transient write failure at register " +
                        $"0x{failedCommand.Register:X2}: NVAPI status {status}; " +
                        $"retrying full sequence ({attempt + 1}/{ApplyAttemptCount})");
                    Thread.Sleep(ApplyRetryDelayMilliseconds);
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"Lenovo RTX lighting apply failed: {ex.Message}");
            }

            return false;
        }
    }

    private bool TryWriteCommands(
        IReadOnlyList<GpuI2cCommand> commands,
        out GpuI2cCommand failedCommand,
        out int failureStatus)
    {
        foreach (var command in commands)
        {
            failureStatus = WriteRegister(command.Register, command.Value);
            if (failureStatus != 0)
            {
                failedCommand = command;
                return false;
            }

            if (command.DelayAfterMilliseconds > 0)
                Thread.Sleep(command.DelayAfterMilliseconds);
        }

        failedCommand = default;
        failureStatus = 0;
        return true;
    }

    internal static bool IsSupportedPciDevice(uint deviceId, uint subsystemId)
    {
        var vendor = (ushort)(deviceId & 0xFFFF);
        var device = (ushort)(deviceId >> 16);
        var subsystemVendor = (ushort)(subsystemId & 0xFFFF);
        var subsystemDevice = (ushort)(subsystemId >> 16);
        return vendor == NvidiaVendorId &&
               device == Rtx5080DeviceId &&
               subsystemVendor == LenovoSubsystemVendorId &&
               subsystemDevice == LenovoSubsystemDeviceId;
    }

    internal static byte MapBrightness(double brightness) =>
        (byte)Math.Clamp(
            (int)Math.Round(Math.Clamp(brightness, 0, 1) * 7, MidpointRounding.AwayFromZero),
            0,
            7);

    internal static IReadOnlyList<GpuI2cCommand> BuildStaticCommands(
        byte red,
        byte green,
        byte blue,
        byte brightness,
        bool enabled)
    {
        var commands = new List<GpuI2cCommand>
        {
            new(0x30, 0xB0),
            new(0x40, 0x01),
            new(0x15, 0x01),
            new(0x16, 0x00),
            new(0x17, (byte)Math.Min(brightness, (byte)7)),
            new(0x18, red),
            new(0x19, green),
            new(0x1A, blue),
            new(0x1B, 0x00),
            new(0x1C, 0xC8),
            new(0x1D, 0xFF),
            new(0x20, 0x00),
            new(0x21, 0x00),
            new(0x22, 0x00),
            new(0x23, 0x00),
            new(0x31, 0xB1),
            new(0x32, 0xB2)
        };

        // The output switch is independent from the effect mode. Lenovo still
        // programs static mode when the GPU light output itself is disabled.
        commands.Add(new(0x30, 0xB0));
        commands.Add(new(0x40, 0x01));
        commands.Add(new(0x14, 0x01)); // Static mode.
        commands.Add(new(0x31, 0xB1));

        // Lenovo repeats the logo/output latch sequence four times.
        for (var pass = 0; pass < 4; pass++)
        {
            commands.Add(new(0x30, 0xB0));
            commands.Add(new(0x50, enabled ? (byte)1 : (byte)0));
            commands.Add(new(0x31, 0xB1));
            // Lenovo's queue uses FF FF as a delay marker, not an I2C write.
            commands.Add(new(0x32, 0xB2, 5));
        }

        return commands;
    }

    private int WriteRegister(byte register, byte value)
    {
        var registerBytes = new[] { register };
        var dataBytes = new[] { value };
        var registerHandle = GCHandle.Alloc(registerBytes, GCHandleType.Pinned);
        var dataHandle = GCHandle.Alloc(dataBytes, GCHandleType.Pinned);
        try
        {
            var info = CreateI2cInfo(
                registerHandle.AddrOfPinnedObject(),
                dataHandle.AddrOfPinnedObject(),
                _displayMask);
            return _i2cWrite!(_gpuHandle, ref info);
        }
        finally
        {
            dataHandle.Free();
            registerHandle.Free();
        }
    }

    internal static NvI2cInfoV3 CreateI2cInfo(
        nint registerAddress,
        nint data,
        uint displayMask) => new()
    {
        Version = I2cInfoVersion3,
        DisplayMask = displayMask,
        // This looks counterintuitive for an onboard RGB controller, but it
        // is the route used by Lenovo's SEBasicLighting NVIDIA implementation.
        IsDdcPort = 1,
        I2cDeviceAddress = I2cDeviceAddress,
        I2cRegisterAddress = registerAddress,
        RegisterAddressSize = 1,
        Data = data,
        Size = 1,
        I2cSpeed = I2cSpeedDeprecated,
        I2cSpeedKhz = LenovoI2cSpeedKhz,
        PortId = PortId,
        IsPortIdSet = 1
    };

    private static bool TryGetHdmiOutputMask(
        nint gpuHandle,
        NvApiGpuGetAllDisplayIds getAllDisplayIds,
        NvApiSysGetGpuAndOutputIdFromDisplayId getGpuAndOutputId,
        out uint displayMask)
    {
        displayMask = 0;
        uint count = 0;
        var status = getAllDisplayIds(gpuHandle, 0, ref count);
        if (status != 0 || count == 0)
        {
            Log.Warn(
                "Lenovo RTX lighting display enumeration failed: " +
                $"NvAPI_GPU_GetAllDisplayIds returned {status}, count={count}");
            return false;
        }

        var displayIds = new NvGpuDisplayId[count];
        for (var index = 0; index < displayIds.Length; index++)
            displayIds[index].Version = GpuDisplayIdsVersion3;

        var displayIdsHandle = GCHandle.Alloc(displayIds, GCHandleType.Pinned);
        try
        {
            status = getAllDisplayIds(
                gpuHandle,
                displayIdsHandle.AddrOfPinnedObject(),
                ref count);
        }
        finally
        {
            displayIdsHandle.Free();
        }

        if (status != 0)
        {
            Log.Warn(
                "Lenovo RTX lighting display enumeration failed: " +
                $"NvAPI_GPU_GetAllDisplayIds returned {status}");
            return false;
        }

        foreach (var displayId in displayIds.Take((int)Math.Min(count, (uint)displayIds.Length)))
        {
            if (displayId.ConnectorType != HdmiConnectorType)
                continue;

            status = getGpuAndOutputId(
                displayId.DisplayId,
                out var mappedGpuHandle,
                out displayMask);
            if (status == 0 && mappedGpuHandle == gpuHandle && displayMask != 0)
                return true;
        }

        displayMask = 0;
        return false;
    }

    private static T? GetDelegate<T>(NvApiQueryInterface query, uint id) where T : Delegate
    {
        var pointer = query(id);
        return pointer == 0 ? null : Marshal.GetDelegateForFunctionPointer<T>(pointer);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            ReleaseNativeLibrary();
        }
    }

    private void ReleaseNativeLibrary()
    {
        _gpuHandle = 0;
        _displayMask = 0;
        _i2cWrite = null;
        if (_library == 0)
            return;

        try
        {
            if (_nvApiInitialized)
                _unload?.Invoke();
        }
        catch
        {
            // The process is releasing the native driver library anyway.
        }

        _nvApiInitialized = false;
        NativeLibrary.Free(_library);
        _library = 0;
        _unload = null;
    }

    internal readonly record struct GpuI2cCommand(
        byte Register,
        byte Value,
        int DelayAfterMilliseconds = 0);

    [StructLayout(LayoutKind.Sequential)]
    internal struct NvGpuDisplayId
    {
        public uint Version;
        public uint ConnectorType;
        public uint DisplayId;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NvI2cInfoV3
    {
        public uint Version;
        public uint DisplayMask;
        public byte IsDdcPort;
        public byte I2cDeviceAddress;
        public nint I2cRegisterAddress;
        public uint RegisterAddressSize;
        public nint Data;
        public uint Size;
        public uint I2cSpeed;
        public uint I2cSpeedKhz;
        public byte PortId;
        public uint IsPortIdSet;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint NvApiQueryInterface(uint id);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvApiInitialize();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvApiUnload();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvApiEnumPhysicalGpus(
        [Out] nint[] handles,
        out uint count);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvApiGetPciIdentifiers(
        nint gpuHandle,
        out uint deviceId,
        out uint subsystemId,
        out uint revisionId,
        out uint extDeviceId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvApiGpuGetAllDisplayIds(
        nint gpuHandle,
        nint displayIds,
        ref uint displayIdCount);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvApiSysGetGpuAndOutputIdFromDisplayId(
        uint displayId,
        out nint gpuHandle,
        out uint outputId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvApiI2cWrite(
        nint gpuHandle,
        ref NvI2cInfoV3 info);
}
