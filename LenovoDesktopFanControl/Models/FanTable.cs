namespace LenovoDesktopFanControl.Models;

public class FanTable
{
    public const int PointCount = 10;
    public static readonly byte[] MinimumSpeeds = [1, 1, 1, 1, 1, 1, 1, 1, 3, 5];

    public byte[] Speeds { get; set; } = new byte[10];

    public byte[] GetBytes() => Speeds;

    public static FanTable Default() => new() { Speeds = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10] };
    public static FanTable Minimum() => new() { Speeds = [.. MinimumSpeeds] };

    public bool IsValid()
    {
        if (Speeds.Length != PointCount)
            return false;
        for (var i = 0; i < Speeds.Length; i++)
        {
            if (Speeds[i] < MinimumSpeeds[i] || Speeds[i] > 10)
                return false;
            if (i > 0 && Speeds[i] < Speeds[i - 1])
                return false;
        }
        return true;
    }

    public static FanTable FromPercentage(int percentage)
    {
        var target = Math.Clamp((int)Math.Round(percentage / 10.0), 0, 10);
        var speeds = new byte[PointCount];
        var min = target == 0 ? 0 : Math.Max(1, target / 3);
        for (var i = 0; i < PointCount; i++)
        {
            var t = i / (PointCount - 1d);
            var v = min + (int)Math.Round((target - min) * t);
            speeds[i] = (byte)Math.Clamp(Math.Max(v, MinimumSpeeds[i]), 0, 10);
        }
        return new FanTable { Speeds = speeds };
    }
}
