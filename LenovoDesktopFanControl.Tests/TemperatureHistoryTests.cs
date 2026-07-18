using LenovoDesktopFanControl.Models;

namespace LenovoDesktopFanControl.Tests;

public sealed class TemperatureHistoryTests
{
    [Fact]
    public void Compact_KeepsDetailedHour_CompactsOlderReadings_AndDropsExpiredData()
    {
        var now = new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc);
        var history = new TemperatureHistory
        {
            Samples =
            [
                new(now.AddHours(-13), 20),
                new(now.AddHours(-2).AddMinutes(1), 30),
                new(now.AddHours(-2).AddMinutes(3), 34),
                new(now.AddMinutes(-30), 40),
                new(now.AddMinutes(-29), 42)
            ]
        };

        history.Compact(now);

        Assert.Equal(3, history.Samples.Count);
        Assert.DoesNotContain(history.Samples, sample => sample.Celsius == 20);
        Assert.Contains(history.Samples, sample => sample.Celsius == 32);
        Assert.Contains(history.Samples, sample => sample.Celsius == 40);
        Assert.Contains(history.Samples, sample => sample.Celsius == 42);
    }

    [Fact]
    public void Add_NormalizesTimestampToUtc_AndKeepsSamplesChronological()
    {
        var history = new TemperatureHistory();
        var local = new DateTime(2026, 7, 18, 15, 0, 0, DateTimeKind.Local);

        history.Add(local, 47);

        var sample = Assert.Single(history.Samples);
        Assert.Equal(DateTimeKind.Utc, sample.TimestampUtc.Kind);
        Assert.Equal(local.ToUniversalTime(), sample.TimestampUtc);
        Assert.Equal(47, sample.Celsius);
    }

    [Fact]
    public void Compact_UsesBucketAverageWithAwayFromZeroRounding_AndMidpointTimestamp()
    {
        var now = new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc);
        var bucket = now.AddHours(-2).AddMinutes(1);
        var history = new TemperatureHistory
        {
            Samples =
            [
                new(bucket, 30),
                new(bucket.AddMinutes(2), 31),
                new(now.AddMinutes(-20), 44)
            ]
        };

        history.Compact(now);

        Assert.Collection(history.Samples,
            sample =>
            {
                Assert.Equal(bucket.Date.AddHours(10).AddMinutes(2).AddSeconds(30), sample.TimestampUtc);
                Assert.Equal(31, sample.Celsius);
            },
            sample => Assert.Equal(44, sample.Celsius));
    }

    [Fact]
    public void Compact_RetainsSamplesOnRetentionBoundaries()
    {
        var now = new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc);
        var oldest = now.AddHours(-TemperatureHistory.MaximumHistoryHours);
        var detailCutoff = now.AddHours(-TemperatureHistory.DetailedHistoryHours);
        var history = new TemperatureHistory
        {
            Samples =
            [
                new(oldest.AddTicks(-1), 10),
                new(oldest, 20),
                new(detailCutoff.AddTicks(-1), 30),
                new(detailCutoff, 40)
            ]
        };

        history.Compact(now);

        Assert.DoesNotContain(history.Samples, sample => sample.Celsius == 10);
        Assert.Contains(history.Samples, sample => sample.Celsius == 20);
        Assert.Contains(history.Samples, sample => sample.Celsius == 30);
        Assert.Contains(history.Samples, sample => sample.Celsius == 40 && sample.TimestampUtc == detailCutoff);
    }
}
