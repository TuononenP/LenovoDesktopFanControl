using System.Numerics;
using LenovoDesktopFanControl.Services;
using WinColor = Windows.UI.Color;

namespace LenovoDesktopFanControl.Tests;

public sealed class LampArrayLightingServiceTests
{
    [Fact]
    public void BuildContiguousSpatialGroups_SeparatesPhysicalLightingComponents()
    {
        Vector3[] positions =
        [
            new(0.40f, 0.30f, 0.10f),
            new(0.40f, 0.40f, 0.10f),
            new(0.40f, 0.30f, 0.00f),

            new(0.40f, 0.00f, 0.00f),
            new(0.40f, 0.10f, 0.00f),

            new(0.20f, 0.00f, 0.10f),
            new(0.30f, 0.00f, 0.10f),

            new(0.00f, 0.10f, 0.10f),
            new(0.00f, 0.20f, 0.10f),

            new(0.20f, 0.10f, 0.20f),
            new(0.20f, 0.20f, 0.20f)
        ];

        var groups = LampArrayLightingService.BuildContiguousSpatialGroups(positions);

        Assert.Equal(5, groups.Count);
        Assert.Equal([0, 1, 2], groups[0]);
        Assert.Equal([3, 4], groups[1]);
        Assert.Equal([5, 6], groups[2]);
        Assert.Equal([7, 8], groups[3]);
        Assert.Equal([9, 10], groups[4]);
        Assert.Equal(Enumerable.Range(0, positions.Length), groups.SelectMany(group => group));
    }

    [Fact]
    public void BuildContiguousSpatialGroups_HandlesEmptyAndSingleLampArrays()
    {
        Assert.Empty(LampArrayLightingService.BuildContiguousSpatialGroups([]));

        var group = Assert.Single(
            LampArrayLightingService.BuildContiguousSpatialGroups([Vector3.Zero]));
        Assert.Equal([0], group);
    }

    [Fact]
    public void GetDefaultTowerZoneName_UsesKnownHardwareNamesAndGenericFallback()
    {
        Assert.Equal("Case front fans", LampArrayLightingService.GetDefaultTowerZoneName(0));
        Assert.Equal("Case top fans", LampArrayLightingService.GetDefaultTowerZoneName(1));
        Assert.Equal("CPU watercooler", LampArrayLightingService.GetDefaultTowerZoneName(2));
        Assert.Equal("Legion logo", LampArrayLightingService.GetDefaultTowerZoneName(3));
        Assert.Equal("Back fan", LampArrayLightingService.GetDefaultTowerZoneName(4));
        Assert.Equal("Light Group 6", LampArrayLightingService.GetDefaultTowerZoneName(5));
    }

    [Theory]
    [InlineData(255, 128, 64, 0.5, 128, 64, 32)]
    [InlineData(255, 128, 64, 0, 0, 0, 0)]
    [InlineData(255, 128, 64, 1, 255, 128, 64)]
    public void ScaleColor_AppliesRelativeZoneBrightness(
        byte red,
        byte green,
        byte blue,
        double brightness,
        byte expectedRed,
        byte expectedGreen,
        byte expectedBlue)
    {
        var scaled = LampArrayLightingService.ScaleColor(
            WinColor.FromArgb(255, red, green, blue),
            brightness);

        Assert.Equal(expectedRed, scaled.R);
        Assert.Equal(expectedGreen, scaled.G);
        Assert.Equal(expectedBlue, scaled.B);
    }
}
