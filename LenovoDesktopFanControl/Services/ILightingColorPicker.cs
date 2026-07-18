using LenovoDesktopFanControl.Models;
using LenovoDesktopFanControl.Views.Controls;

namespace LenovoDesktopFanControl.Services;

public interface ILightingColorPicker
{
    LightingColorOption? Pick(
        LightingColorOption initialColor,
        Action<LightingColorOption>? previewColor = null);
}

public sealed class CustomLightingColorPicker : ILightingColorPicker
{
    public LightingColorOption? Pick(
        LightingColorOption initialColor,
        Action<LightingColorOption>? previewColor = null)
    {
        var dialog = new ColorPickerWindow(initialColor, previewColor)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        return dialog.ShowDialog() == true ? dialog.SelectedColor : null;
    }
}
