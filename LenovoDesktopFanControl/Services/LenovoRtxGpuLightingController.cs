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
    private const uint NvApiI2cWriteExId = 0x283AC65A;
    private const uint NvApiI2cReadExId = 0x4D7B0709;
    private const uint I2cInfoVersion3 = 0x00030040;
    private const uint DisplayMask = 0x100;
    private const byte I2cDeviceAddress = 0xB6; // Lenovo uses the 8-bit form of 7-bit address 0x5B.
    private const uint I2cSpeedDeprecated = 0xFFFF;
    private const uint I2cSpeed100Khz = 4;
    private const byte PortId = 1;
    private const int MaxPhysicalGpus = 64;

    private readonly object _sync = new();
    private nint _library;
    private nint _gpuHandle;
    private NvApiUnload? _unload;
    private NvApiI2cWrite? _i2cWrite;
    private NvApiI2cRead? _i2cRead;
    private bool _nvApiInitialized;
    private bool _readbackLogged;
    private bool _disposed;

    public bool IsAvailable => _gpuHandle != 0 && _i2cWrite != null && !_disposed;

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
                _i2cWrite = GetDelegate<NvApiI2cWrite>(query, NvApiI2cWriteExId);
                _i2cRead = GetDelegate<NvApiI2cRead>(query, NvApiI2cReadExId);

                if (initialize == null || enumerate == null || getPciIdentifiers == null || _i2cWrite == null)
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

                    _gpuHandle = handles[index];
                    Log.Info(
                        "Lenovo RTX GPU lighting detected: " +
                        $"device=0x{deviceId:X8}, subsystem=0x{subsystemId:X8}, " +
                        $"I2C=0x{I2cDeviceAddress >> 1:X2}, port={PortId}");
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

            var level = MapBrightness(brightness);
            try
            {
                foreach (var command in BuildStaticCommands(red, green, blue, level, enabled))
                {
                    var status = WriteRegister(command.Register, command.Value);
                    if (status != 0)
                    {
                        Log.Warn(
                            $"Lenovo RTX lighting write failed at register 0x{command.Register:X2}: " +
                            $"NVAPI status {status}");
                        return false;
                    }

                    if (command.DelayAfterMilliseconds > 0)
                        Thread.Sleep(command.DelayAfterMilliseconds);
                }

                Log.Info(
                    $"Lenovo RTX lighting applied: enabled={enabled}, brightness={level}/7, " +
                    $"r={red}, g={green}, b={blue}");
                if (!_readbackLogged)
                {
                    LogReadback(red, green, blue, level, enabled);
                    _readbackLogged = true;
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn($"Lenovo RTX lighting apply failed: {ex.Message}");
                return false;
            }
        }
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
            new(0x18, blue),
            new(0x19, green),
            new(0x1A, red),
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

        if (enabled)
        {
            commands.Add(new(0x30, 0xB0));
            commands.Add(new(0x40, 0x01));
            commands.Add(new(0x14, 0x01)); // Static mode.
            commands.Add(new(0x31, 0xB1));
        }

        // Lenovo repeats the logo/output latch sequence four times.
        for (var pass = 0; pass < 4; pass++)
        {
            commands.Add(new(0x30, 0xB0));
            commands.Add(new(0x50, enabled ? (byte)1 : (byte)0));
            commands.Add(new(0x31, 0xB1));
            commands.Add(new(0x32, 0xB2, 1));
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
                dataHandle.AddrOfPinnedObject());
            uint transactionId = 0;
            return _i2cWrite!(_gpuHandle, ref info, ref transactionId);
        }
        finally
        {
            dataHandle.Free();
            registerHandle.Free();
        }
    }

    private void LogReadback(byte red, byte green, byte blue, byte brightness, bool enabled)
    {
        if (_i2cRead == null)
            return;

        var expected = new List<(byte Register, byte Value)>
        {
            (0x17, brightness),
            (0x18, blue),
            (0x19, green),
            (0x1A, red),
            (0x50, enabled ? (byte)1 : (byte)0)
        };
        if (enabled)
            expected.Insert(0, (0x14, 1));

        var results = new List<string>(expected.Count);
        foreach (var item in expected)
        {
            var status = ReadRegister(item.Register, out var actual);
            results.Add(status == 0
                ? $"0x{item.Register:X2}=0x{actual:X2} (expected 0x{item.Value:X2})"
                : $"0x{item.Register:X2}=NVAPI {status}");
        }

        Log.Info($"Lenovo RTX lighting readback: {string.Join(", ", results)}");
    }

    private int ReadRegister(byte register, out byte value)
    {
        var registerBytes = new[] { register };
        var dataBytes = new byte[1];
        var registerHandle = GCHandle.Alloc(registerBytes, GCHandleType.Pinned);
        var dataHandle = GCHandle.Alloc(dataBytes, GCHandleType.Pinned);
        try
        {
            var info = CreateI2cInfo(
                registerHandle.AddrOfPinnedObject(),
                dataHandle.AddrOfPinnedObject());
            uint transactionId = 0;
            var status = _i2cRead!(_gpuHandle, ref info, ref transactionId);
            value = dataBytes[0];
            return status;
        }
        finally
        {
            dataHandle.Free();
            registerHandle.Free();
        }
    }

    internal static NvI2cInfoV3 CreateI2cInfo(nint registerAddress, nint data) => new()
    {
        Version = I2cInfoVersion3,
        DisplayMask = DisplayMask,
        // RGB controllers use NVIDIA's internal I2C port rather than a
        // monitor-facing DDC port. NVAPI may return success for the wrong
        // route even though the target controller never sees the request.
        IsDdcPort = 0,
        I2cDeviceAddress = I2cDeviceAddress,
        I2cRegisterAddress = registerAddress,
        RegisterAddressSize = 1,
        Data = data,
        Size = 1,
        I2cSpeed = I2cSpeedDeprecated,
        I2cSpeedKhz = I2cSpeed100Khz,
        PortId = PortId,
        IsPortIdSet = 1
    };

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
        _i2cWrite = null;
        _i2cRead = null;
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
    private delegate int NvApiI2cWrite(
        nint gpuHandle,
        ref NvI2cInfoV3 info,
        ref uint transactionId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvApiI2cRead(
        nint gpuHandle,
        ref NvI2cInfoV3 info,
        ref uint transactionId);
}
