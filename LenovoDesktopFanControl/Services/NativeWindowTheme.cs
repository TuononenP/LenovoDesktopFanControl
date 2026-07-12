using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LenovoDesktopFanControl.Services;

internal static class NativeWindowTheme
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmUseImmersiveDarkModeLegacy = 19;
    private const int DwmWindowCornerPreference = 33;
    private const int DwmBorderColor = 34;
    private const int DwmCaptionColor = 35;
    private const int DwmTextColor = 36;
    private const int DwmCornerPreferenceRound = 2;
    private const int CaptionColor = 0x0017100B;
    private const int CaptionTextColor = 0x00FBF7F4;
    private const int WindowBorderColor = 0x00473628;

    public static void Apply(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
                return;

            var enabled = 1;
            if (SetDwmAttribute(handle, DwmUseImmersiveDarkMode, enabled) != 0)
                SetDwmAttribute(handle, DwmUseImmersiveDarkModeLegacy, enabled);

            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
                return;

            SetDwmAttribute(handle, DwmWindowCornerPreference, DwmCornerPreferenceRound);
            SetDwmAttribute(handle, DwmCaptionColor, CaptionColor);
            SetDwmAttribute(handle, DwmTextColor, CaptionTextColor);
            SetDwmAttribute(handle, DwmBorderColor, WindowBorderColor);
        }
        catch (Exception ex)
        {
            Log.Warn($"Failed to apply native window theme: {ex.Message}");
        }
    }

    private static int SetDwmAttribute(IntPtr windowHandle, int attribute, int value)
    {
        return DwmSetWindowAttribute(windowHandle, attribute, ref value, Marshal.SizeOf<int>());
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr windowHandle,
        int attribute,
        ref int attributeValue,
        int attributeSize);
}
