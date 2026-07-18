using System.Diagnostics;
using System.Management;
using LenovoDesktopFanControl.Models;

namespace LenovoDesktopFanControl.Services;

public interface ISystemTemperatureService
{
    Task<IReadOnlyList<SystemTemperatureReading>> ReadAsync();
}

public sealed class SystemTemperatureService : ISystemTemperatureService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(5);
    private DateTime _lastReadUtc = DateTime.MinValue;
    private IReadOnlyList<SystemTemperatureReading> _cached = [];

    public async Task<IReadOnlyList<SystemTemperatureReading>> ReadAsync()
    {
        if (DateTime.UtcNow - _lastReadUtc < CacheDuration)
            return _cached;

        var readings = new List<SystemTemperatureReading>
        {
            new("CPU", null, "Not exposed by the current firmware"),
            new("Motherboard", null, "Not exposed by the current firmware")
        };
        readings.Insert(0, await ReadGpuAsync());
        readings.AddRange(await Task.Run(ReadSsd));
        _cached = readings;
        _lastReadUtc = DateTime.UtcNow;
        return _cached;
    }

    private static async Task<SystemTemperatureReading> ReadGpuAsync()
    {
        try
        {
            var start = new ProcessStartInfo("nvidia-smi.exe", "--query-gpu=temperature.gpu --format=csv,noheader,nounits")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var process = Process.Start(start);
            if (process == null)
                return new SystemTemperatureReading("GPU", null, "NVIDIA telemetry unavailable");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(true); } catch { }
                return new SystemTemperatureReading("GPU", null, "NVIDIA telemetry timed out");
            }
            if (process.ExitCode != 0)
                return new SystemTemperatureReading("GPU", null, "NVIDIA telemetry unavailable");
            var output = (await process.StandardOutput.ReadToEndAsync()).Trim();
            return int.TryParse(output.Split('\n')[0].Trim(), out var temperature)
                ? new SystemTemperatureReading("GPU", temperature, "NVIDIA GPU temperature")
                : new SystemTemperatureReading("GPU", null, "NVIDIA telemetry unavailable");
        }
        catch
        {
            return new SystemTemperatureReading("GPU", null, "NVIDIA telemetry unavailable");
        }
    }

    private static IReadOnlyList<SystemTemperatureReading> ReadSsd()
    {
        var readings = new List<SystemTemperatureReading>();
        try
        {
            var scope = new ManagementScope("\\\\.\\root\\Microsoft\\Windows\\Storage");
            using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM MSFT_PhysicalDisk"));
            foreach (ManagementObject disk in searcher.Get())
            {
                using var counter = disk.InvokeMethod("GetReliabilityCounter", null, null) as ManagementBaseObject;
                var value = counter?["Temperature"];
                if (value is byte temperature && temperature > 0)
                    readings.Add(new SystemTemperatureReading("SSD", temperature, disk["FriendlyName"]?.ToString() ?? "Drive temperature"));
            }
        }
        catch (Exception ex)
        {
            Log.Info($"SSD temperature telemetry unavailable: {ex.Message}");
        }
        if (readings.Count == 0)
            readings.Add(new SystemTemperatureReading("SSD", null, "Drive telemetry unavailable"));
        return readings;
    }
}
