namespace LenovoDesktopFanControl.Models;

public sealed class TemperatureHistory
{
    public const int DetailedHistoryHours = 1;
    public const int MaximumHistoryHours = 12;
    private static readonly TimeSpan DetailedHistory = TimeSpan.FromHours(DetailedHistoryHours);
    private static readonly TimeSpan MaximumHistory = TimeSpan.FromHours(MaximumHistoryHours);
    private static readonly TimeSpan CompactBucket = TimeSpan.FromMinutes(5);

    public List<TemperatureHistorySample> Samples { get; set; } = [];

    public void Add(DateTime timestampUtc, int celsius)
    {
        timestampUtc = timestampUtc.ToUniversalTime();
        Samples.Add(new TemperatureHistorySample(timestampUtc, celsius));
        Compact(timestampUtc);
    }

    public void Compact(DateTime nowUtc)
    {
        nowUtc = nowUtc.ToUniversalTime();
        var oldest = nowUtc - MaximumHistory;
        var detailCutoff = nowUtc - DetailedHistory;
        var retained = Samples.Where(sample => sample.TimestampUtc >= oldest).ToArray();
        var compacted = retained
            .Where(sample => sample.TimestampUtc < detailCutoff)
            .GroupBy(sample => new DateTime(
                sample.TimestampUtc.Ticks / CompactBucket.Ticks * CompactBucket.Ticks,
                DateTimeKind.Utc))
            .Select(group => new TemperatureHistorySample(
                group.Key + TimeSpan.FromTicks(CompactBucket.Ticks / 2),
                (int)Math.Round(group.Average(sample => sample.Celsius), MidpointRounding.AwayFromZero)))
            .Concat(retained.Where(sample => sample.TimestampUtc >= detailCutoff))
            .OrderBy(sample => sample.TimestampUtc)
            .ToList();
        Samples = compacted;
    }
}
