using System.Windows;

namespace LenovoDesktopFanControl.Services;

internal static class MotionPreferences
{
    public static bool AnimationsEnabled =>
        SystemParameters.ClientAreaAnimation && !VisualScaleVerifier.IsRequested;
}
