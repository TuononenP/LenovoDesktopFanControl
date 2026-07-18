using LenovoDesktopFanControl.Models;

namespace LenovoDesktopFanControl.Tests;

public class SmartFanModeTests
{
    [Fact]
    public void Values_MatchLenovoGameZoneFirmwareProtocol()
    {
        Assert.Equal(1, (int)SmartFanMode.Quiet);
        Assert.Equal(2, (int)SmartFanMode.Balanced);
        Assert.Equal(3, (int)SmartFanMode.Performance);
        Assert.Equal(255, (int)SmartFanMode.Custom);
    }
}

public partial class FanTableTests
{
    [Fact]
    public void Presets_ReturnExpectedIndependentCurves()
    {
        var firstDefault = FanTable.Default();
        var secondDefault = FanTable.Default();

        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, firstDefault.GetBytes());
        Assert.Equal(new byte[] { 1, 1, 1, 1, 1, 1, 1, 1, 3, 5 }, FanTable.Minimum().Speeds);
        Assert.NotSame(firstDefault.Speeds, secondDefault.Speeds);
    }

    [Fact]
    public void IsValid_AcceptsSafeTenPointCurves()
    {
        Assert.True(FanTable.Minimum().IsValid());
        Assert.True(FanTable.Default().IsValid());
    }

    [Fact]
    public void IsValid_RejectsInvalidLengthsAndDescendingCurves()
    {
        Assert.False(new FanTable { Speeds = [1, 2, 3, 4, 5, 6, 7] }.IsValid());
        Assert.False(new FanTable { Speeds = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 10] }.IsValid());
        Assert.False(new FanTable { Speeds = [1, 2, 3, 4, 3, 6, 7, 8, 9, 10] }.IsValid());
        Assert.False(new FanTable { Speeds = [0, 1, 1, 1, 1, 1, 1, 1, 3, 5] }.IsValid());
        Assert.False(new FanTable { Speeds = [1, 1, 1, 1, 1, 1, 1, 1, 3, 11] }.IsValid());
    }

    [Theory]
    [InlineData(-100, 1, 5)]
    [InlineData(0, 1, 5)]
    [InlineData(50, 1, 5)]
    [InlineData(100, 3, 10)]
    [InlineData(200, 3, 10)]
    public void FromPercentage_ClampsAndBuildsMonotonicTenPointCurve(
        int percentage,
        byte expectedFirst,
        byte expectedLast)
    {
        var table = FanTable.FromPercentage(percentage);

        Assert.Equal(10, table.Speeds.Length);
        Assert.Equal(expectedFirst, table.Speeds[0]);
        Assert.Equal(expectedLast, table.Speeds[^1]);
        Assert.True(table.IsValid());
    }

    [Theory]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(30)]
    [InlineData(70)]
    [InlineData(80)]
    [InlineData(90)]
    public void FromPercentage_AlwaysProducesValidCurve(int percentage)
    {
        var table = FanTable.FromPercentage(percentage);

        Assert.Equal(10, table.Speeds.Length);
        Assert.True(table.IsValid());
    }

    [Fact]
    public void FromPercentage_AtZeroYieldsMinimumSafeCurve()
    {
        var table = FanTable.FromPercentage(0);

        Assert.Equal(FanTable.MinimumSpeeds, table.Speeds);
    }

    [Fact]
    public void FromPercentage_AtHundredReachesMaximumAtEnd()
    {
        var table = FanTable.FromPercentage(100);

        Assert.Equal((byte)10, table.Speeds[^1]);
        Assert.True(table.IsValid());
    }

    [Fact]
    public void FromPercentage_ProducesMonotonicallyNonDecreasingCurve()
    {
        var table = FanTable.FromPercentage(45);

        for (var i = 1; i < table.Speeds.Length; i++)
            Assert.True(table.Speeds[i] >= table.Speeds[i - 1]);
    }

    [Fact]
    public void Minimum_ReturnsDefensiveCopyOfStaticSpeeds()
    {
        var first = FanTable.Minimum();
        var second = FanTable.Minimum();

        Assert.Equal(FanTable.MinimumSpeeds, first.Speeds);
        Assert.NotSame(FanTable.MinimumSpeeds, first.Speeds);
        Assert.NotSame(first.Speeds, second.Speeds);
    }

    [Fact]
    public void GetBytes_ReturnsSameReferenceAsSpeeds()
    {
        var table = FanTable.Default();

        Assert.Same(table.Speeds, table.GetBytes());
    }

    [Fact]
    public void PointCount_IsTen()
    {
        Assert.Equal(10, FanTable.PointCount);
    }

    [Fact]
    public void IsValid_RejectsCurveWithDuplicatePlateauBelowMinimum()
    {
        Assert.False(new FanTable
        {
            Speeds = [1, 1, 1, 1, 1, 1, 1, 2, 2, 2]
        }.IsValid());
    }

    [Fact]
    public void IsValid_AcceptsFlatCurveAtTen()
    {
        Assert.True(new FanTable
        {
            Speeds = [10, 10, 10, 10, 10, 10, 10, 10, 10, 10]
        }.IsValid());
    }
}

public class FanSettingsTests
{
    [Fact]
    public void GetOrDefaultCurve_ReturnsStoredTenPointCurve()
    {
        byte[] stored = [1, 1, 2, 2, 3, 3, 4, 4, 5, 5];
        var settings = new FanSettings { FanCurves = { [7] = stored } };

        Assert.Same(stored, settings.GetOrDefaultCurve(7));
    }

    [Fact]
    public void SetGlobalCurve_ReplacesLegacyPerFanCurvesAndReturnsDefensiveCopy()
    {
        byte[] curve = [1, 1, 2, 2, 3, 3, 4, 5, 6, 7];
        var settings = new FanSettings { FanCurves = { [7] = FanTable.Default().Speeds } };

        settings.SetGlobalCurve(curve);
        curve[0] = 9;

        Assert.Empty(settings.FanCurves);
        Assert.Equal([1, 1, 2, 2, 3, 3, 4, 5, 6, 7], settings.GetOrDefaultCurve(999));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public void GetOrDefaultCurve_ReturnsDefaultForMissingOrInvalidCurve(int length)
    {
        var settings = new FanSettings();
        if (length > 0)
            settings.FanCurves[7] = new byte[length];

        Assert.Equal(FanTable.Default().Speeds, settings.GetOrDefaultCurve(7));
    }

    [Fact]
    public void GetOrDefaultCurve_RejectsUnsafeLegacyCurve()
    {
        var settings = new FanSettings
        {
            FanCurves = { [7] = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0] }
        };

        Assert.Equal(FanTable.Default().Speeds, settings.GetOrDefaultCurve(7));
    }

    [Fact]
    public void SetGlobalCurve_ThrowsForInvalidCurve()
    {
        var settings = new FanSettings();

        Assert.Throws<ArgumentException>(() =>
            settings.SetGlobalCurve([1, 2, 3, 4, 5, 6, 7, 8, 9, 11]));
        Assert.Null(settings.GlobalFanCurve);
        Assert.Empty(settings.FanCurves);
    }

    [Fact]
    public void Defaults_AreConsistentForNewSettings()
    {
        var settings = new FanSettings();

        Assert.Equal(SmartFanMode.Balanced, settings.Mode);
        Assert.Null(settings.GlobalFanCurve);
        Assert.Empty(settings.FanCurves);
        Assert.Equal(2000, settings.PollingIntervalMs);
        Assert.False(settings.StartWithWindows);
        Assert.True(settings.MinimizeToTray);
        Assert.Equal("en", settings.Language);
    }

    [Fact]
    public void GetOrDefaultCurve_PrefersPerFanCurveOverLegacyGlobalCurve()
    {
        byte[] globalCurve = [1, 1, 2, 2, 3, 3, 4, 5, 6, 7];
        byte[] perFanCurve = [2, 2, 3, 3, 4, 4, 5, 6, 7, 8];
        var settings = new FanSettings
        {
            GlobalFanCurve = globalCurve,
            FanCurves = { [7] = perFanCurve }
        };

        Assert.Same(perFanCurve, settings.GetOrDefaultCurve(7));
    }

    [Fact]
    public void GetOrDefaultCurve_PrefersValidGlobalCurveEvenWhenLegacyCurveExists()
    {
        byte[] globalCurve = [1, 1, 2, 2, 3, 3, 4, 5, 6, 7];
        var settings = new FanSettings
        {
            GlobalFanCurve = globalCurve,
            FanCurves = { [7] = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0] }
        };

        Assert.Same(globalCurve, settings.GetOrDefaultCurve(7));
    }

    [Fact]
    public void SetCurve_StoresDefensiveCopyWithoutChangingOtherFanCurves()
    {
        byte[] curve = [1, 1, 2, 2, 3, 3, 4, 5, 6, 7];
        var settings = new FanSettings
        {
            FanCurves = { [2] = FanTable.Default().Speeds }
        };

        settings.SetCurve(7, curve);
        curve[0] = 9;

        Assert.Equal([1, 1, 2, 2, 3, 3, 4, 5, 6, 7], settings.FanCurves[7]);
        Assert.True(settings.FanCurves.ContainsKey(2));
    }

    [Fact]
    public void SetCurve_RejectsInvalidCurveWithoutPersistingIt()
    {
        var settings = new FanSettings();

        Assert.Throws<ArgumentException>(() => settings.SetCurve(7, [1, 2, 3]));

        Assert.Empty(settings.FanCurves);
    }
}
