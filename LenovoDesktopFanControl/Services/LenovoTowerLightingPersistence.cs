using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace LenovoDesktopFanControl.Services;

internal interface ILenovoTowerLightingPersistence
{
    bool TrySaveStaticColor(byte red, byte green, byte blue, double brightness, bool enabled);
    bool TryScheduleStaticColorAfterProcessExit(
        byte red,
        byte green,
        byte blue,
        double brightness,
        bool enabled);
}

internal sealed class LenovoTowerLightingPersistence : ILenovoTowerLightingPersistence
{
    private const string DeferredPersistenceArgument = "--persist-lighting-after-exit";
    private const int DeferredPersistenceArgumentCount = 7;
    internal const ushort LenovoVendorId = 0x17EF;
    internal const ushort LenovoLightingProductId = 0xC955;
    internal const ushort LightingUsagePage = 0xFF89;
    internal const ushort LightingUsage = 0x00CC;
    internal const ushort FeatureReportLength = 64;

    private const byte FeatureReportId = 0xCC;
    private const byte StaticMode = 0x01;
    private const byte DefaultSpeed = 0x01;
    private const byte ZoneOne = 0x12;
    private const byte ZoneTwo = 0x11;
    private const uint DigcfPresent = 0x00000002;
    private const uint DigcfDeviceInterface = 0x00000010;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const int ErrorNoMoreItems = 259;
    private const int HidpStatusSuccess = 0x00110000;
    private static readonly Guid HidDeviceInterfaceGuid =
        new("4D1E55B2-F16F-11CF-88CB-001111000030");

    public bool TryScheduleStaticColorAfterProcessExit(
        byte red,
        byte green,
        byte blue,
        double brightness,
        bool enabled)
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            Log.Warn("Unable to schedule post-exit lighting persistence: executable path is unavailable");
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo(executablePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            foreach (var argument in BuildDeferredPersistenceArguments(
                         Environment.ProcessId,
                         red,
                         green,
                         blue,
                         brightness,
                         enabled))
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var helper = Process.Start(startInfo);
            if (helper == null)
            {
                Log.Warn("Unable to schedule post-exit lighting persistence: helper did not start");
                return false;
            }

            Log.Info(
                $"Scheduled post-exit tower lighting persistence with helper PID {helper.Id}: " +
                $"enabled={enabled}, brightness={brightness:F2}, r={red}, g={green}, b={blue}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"Unable to schedule post-exit lighting persistence: {ex.Message}");
            return false;
        }
    }

    internal static IReadOnlyList<string> BuildDeferredPersistenceArguments(
        int parentProcessId,
        byte red,
        byte green,
        byte blue,
        double brightness,
        bool enabled) =>
    [
        DeferredPersistenceArgument,
        parentProcessId.ToString(CultureInfo.InvariantCulture),
        red.ToString(CultureInfo.InvariantCulture),
        green.ToString(CultureInfo.InvariantCulture),
        blue.ToString(CultureInfo.InvariantCulture),
        brightness.ToString("R", CultureInfo.InvariantCulture),
        enabled ? "1" : "0"
    ];

    internal static bool TryParseDeferredPersistenceRequest(
        IReadOnlyList<string> arguments,
        out DeferredLightingPersistenceRequest request)
    {
        request = default;
        if (arguments.Count != DeferredPersistenceArgumentCount ||
            !string.Equals(arguments[0], DeferredPersistenceArgument, StringComparison.Ordinal) ||
            !int.TryParse(arguments[1], NumberStyles.None, CultureInfo.InvariantCulture, out var parentProcessId) ||
            parentProcessId <= 0 ||
            !byte.TryParse(arguments[2], NumberStyles.None, CultureInfo.InvariantCulture, out var red) ||
            !byte.TryParse(arguments[3], NumberStyles.None, CultureInfo.InvariantCulture, out var green) ||
            !byte.TryParse(arguments[4], NumberStyles.None, CultureInfo.InvariantCulture, out var blue) ||
            !double.TryParse(arguments[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var brightness) ||
            !int.TryParse(arguments[6], NumberStyles.None, CultureInfo.InvariantCulture, out var enabledValue) ||
            enabledValue is not (0 or 1))
        {
            return false;
        }

        request = new DeferredLightingPersistenceRequest(
            parentProcessId,
            red,
            green,
            blue,
            Math.Clamp(brightness, 0, 1),
            enabledValue == 1);
        return true;
    }

    public bool TrySaveStaticColor(
        byte red,
        byte green,
        byte blue,
        double brightness,
        bool enabled)
    {
        var hidDeviceInterfaceGuid = HidDeviceInterfaceGuid;
        var deviceInfoSet = SetupDiGetClassDevs(
            ref hidDeviceInterfaceGuid,
            0,
            0,
            DigcfPresent | DigcfDeviceInterface);
        if (deviceInfoSet == new nint(-1))
        {
            Log.Warn($"Unable to enumerate Lenovo lighting HID interfaces: Win32 {Marshal.GetLastWin32Error()}");
            return false;
        }

        try
        {
            for (uint index = 0; ; index++)
            {
                var interfaceData = new SpDeviceInterfaceData
                {
                    Size = (uint)Marshal.SizeOf<SpDeviceInterfaceData>()
                };

                if (!SetupDiEnumDeviceInterfaces(
                        deviceInfoSet,
                        0,
                        ref hidDeviceInterfaceGuid,
                        index,
                        ref interfaceData))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error != ErrorNoMoreItems)
                        Log.Warn($"Lenovo lighting HID enumeration failed: Win32 {error}");
                    break;
                }

                using var handle = OpenInterface(deviceInfoSet, ref interfaceData);
                if (handle == null || handle.IsInvalid || !IsSupportedInterface(handle))
                    continue;

                var firmwareBrightness = enabled ? MapBrightness(brightness) : (byte)1;
                var outputRed = enabled ? red : (byte)0;
                var outputGreen = enabled ? green : (byte)0;
                var outputBlue = enabled ? blue : (byte)0;

                foreach (var zone in new[] { ZoneOne, ZoneTwo })
                {
                    foreach (var report in BuildFeatureReports(
                                 zone,
                                 outputRed,
                                 outputGreen,
                                 outputBlue,
                                 firmwareBrightness))
                    {
                        if (HidD_SetFeature(handle, report, report.Length))
                            continue;

                        Log.Warn(
                            $"Saving Lenovo tower lighting failed for zone 0x{zone:X2}: " +
                            $"Win32 {Marshal.GetLastWin32Error()}");
                        return false;
                    }
                }

                Log.Info(
                    $"Saved Lenovo tower static lighting to firmware: enabled={enabled}, " +
                    $"brightness={firmwareBrightness}/4, r={outputRed}, g={outputGreen}, b={outputBlue}");
                return true;
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }

        Log.Warn("The persistent Lenovo tower lighting HID interface was not available");
        return false;
    }

    internal static bool IsSupportedHidInterface(
        ushort vendorId,
        ushort productId,
        ushort usagePage,
        ushort usage,
        ushort featureReportLength) =>
        vendorId == LenovoVendorId &&
        productId == LenovoLightingProductId &&
        usagePage == LightingUsagePage &&
        usage == LightingUsage &&
        featureReportLength == FeatureReportLength;

    internal static byte MapBrightness(double brightness) =>
        (byte)Math.Clamp(
            (int)Math.Round(1 + Math.Clamp(brightness, 0, 1) * 3, MidpointRounding.AwayFromZero),
            1,
            4);

    internal static IReadOnlyList<byte[]> BuildFeatureReports(
        byte zone,
        byte red,
        byte green,
        byte blue,
        byte brightness)
    {
        brightness = (byte)Math.Clamp(brightness, (byte)1, (byte)4);

        var applyReport = new byte[FeatureReportLength];
        applyReport[0] = FeatureReportId;
        applyReport[1] = zone;
        applyReport[2] = StaticMode;
        applyReport[3] = DefaultSpeed;
        applyReport[4] = brightness;
        applyReport[5] = red;
        applyReport[6] = green;
        applyReport[7] = blue;

        var saveReport = new byte[FeatureReportLength];
        saveReport[0] = FeatureReportId;
        saveReport[1] = 0x28;
        saveReport[2] = 0x06;
        saveReport[33] = zone;
        saveReport[34] = StaticMode;
        saveReport[35] = DefaultSpeed;
        saveReport[36] = brightness;
        saveReport[37] = red;
        saveReport[38] = green;
        saveReport[39] = blue;

        return [applyReport, saveReport];
    }

    private static SafeFileHandle? OpenInterface(
        nint deviceInfoSet,
        ref SpDeviceInterfaceData interfaceData)
    {
        SetupDiGetDeviceInterfaceDetail(
            deviceInfoSet,
            ref interfaceData,
            0,
            0,
            out var requiredSize,
            0);
        if (requiredSize == 0)
            return null;

        var detailData = Marshal.AllocHGlobal((int)requiredSize);
        try
        {
            Marshal.WriteInt32(detailData, nint.Size == 8 ? 8 : 6);
            if (!SetupDiGetDeviceInterfaceDetail(
                    deviceInfoSet,
                    ref interfaceData,
                    detailData,
                    requiredSize,
                    out _,
                    0))
            {
                return null;
            }

            // SP_DEVICE_INTERFACE_DETAIL_DATA_W stores DevicePath at byte offset 4
            // on both architectures, while cbSize is 8 on x64 due to alignment.
            var path = Marshal.PtrToStringUni(nint.Add(detailData, 4));
            if (string.IsNullOrWhiteSpace(path))
                return null;

            return CreateFile(
                path,
                GenericRead | GenericWrite,
                FileShareRead | FileShareWrite,
                0,
                OpenExisting,
                0,
                0);
        }
        finally
        {
            Marshal.FreeHGlobal(detailData);
        }
    }

    private static bool IsSupportedInterface(SafeFileHandle handle)
    {
        var attributes = new HiddAttributes
        {
            Size = Marshal.SizeOf<HiddAttributes>()
        };
        if (!HidD_GetAttributes(handle, ref attributes))
            return false;

        if (!HidD_GetPreparsedData(handle, out var preparsedData))
            return false;

        try
        {
            if (HidP_GetCaps(preparsedData, out var capabilities) != HidpStatusSuccess)
                return false;

            return IsSupportedHidInterface(
                attributes.VendorId,
                attributes.ProductId,
                capabilities.UsagePage,
                capabilities.Usage,
                capabilities.FeatureReportByteLength);
        }
        finally
        {
            HidD_FreePreparsedData(preparsedData);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData
    {
        public uint Size;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public nint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HiddAttributes
    {
        public int Size;
        public ushort VendorId;
        public ushort ProductId;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HidpCapabilities
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        public ushort[] Reserved;

        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern nint SetupDiGetClassDevs(
        ref Guid classGuid,
        nint enumerator,
        nint parentWindow,
        uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInterfaces(
        nint deviceInfoSet,
        nint deviceInfoData,
        ref Guid interfaceClassGuid,
        uint memberIndex,
        ref SpDeviceInterfaceData deviceInterfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(
        nint deviceInfoSet,
        ref SpDeviceInterfaceData deviceInterfaceData,
        nint deviceInterfaceDetailData,
        uint deviceInterfaceDetailDataSize,
        out uint requiredSize,
        nint deviceInfoData);

    [DllImport("setupapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(nint deviceInfoSet);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        nint securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        nint templateFile);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_GetAttributes(
        SafeFileHandle hidDeviceObject,
        ref HiddAttributes attributes);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_GetPreparsedData(
        SafeFileHandle hidDeviceObject,
        out nint preparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_FreePreparsedData(nint preparsedData);

    [DllImport("hid.dll")]
    private static extern int HidP_GetCaps(
        nint preparsedData,
        out HidpCapabilities capabilities);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_SetFeature(
        SafeFileHandle hidDeviceObject,
        byte[] reportBuffer,
        int reportBufferLength);

    internal readonly record struct DeferredLightingPersistenceRequest(
        int ParentProcessId,
        byte Red,
        byte Green,
        byte Blue,
        double Brightness,
        bool Enabled);
}
