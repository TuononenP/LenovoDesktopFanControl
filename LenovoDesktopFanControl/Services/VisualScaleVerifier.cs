using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LenovoDesktopFanControl.Services;

internal static class VisualScaleVerifier
{
    internal const string OutputEnvironmentVariable = "LENOVO_FAN_CONTROL_VISUAL_TEST_OUTPUT";
    internal const string MinimumSizeEnvironmentVariable = "LENOVO_FAN_CONTROL_VISUAL_TEST_MINIMUM";

    public static bool IsRequested =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(OutputEnvironmentVariable));

    public static void Render(Window window)
    {
        var outputDirectory = Environment.GetEnvironmentVariable(OutputEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(outputDirectory))
            return;

        Directory.CreateDirectory(outputDirectory);
        if (string.Equals(
                Environment.GetEnvironmentVariable(MinimumSizeEnvironmentVariable),
                "1",
                StringComparison.Ordinal))
        {
            window.Width = window.MinWidth;
            window.Height = window.MinHeight;
        }

        window.UpdateLayout();
        var width = Math.Max(1, window.ActualWidth);
        var height = Math.Max(1, window.ActualHeight);
        var scaleFactors = new[] { 1.0, 1.25, 1.5, 2.0 };
        var results = new List<string>();

        foreach (var scale in scaleFactors)
        {
            var dpi = 96 * scale;
            var pixelWidth = (int)Math.Ceiling(width * scale);
            var pixelHeight = (int)Math.Ceiling(height * scale);
            var bitmap = new RenderTargetBitmap(
                pixelWidth,
                pixelHeight,
                dpi,
                dpi,
                PixelFormats.Pbgra32);
            bitmap.Render(window);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            var scaleLabel = $"{scale * 100:0}";
            var filePath = Path.Combine(outputDirectory, $"main-{scaleLabel}.png");
            using (var stream = File.Create(filePath))
                encoder.Save(stream);

            results.Add($"{scaleLabel}%: {pixelWidth}x{pixelHeight} @ {dpi:0} DPI");
        }

        File.WriteAllLines(Path.Combine(outputDirectory, "verification.txt"), results);
    }
}
