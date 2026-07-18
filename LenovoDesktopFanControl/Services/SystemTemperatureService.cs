using System.Diagnostics;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.PawnIo;
using LenovoDesktopFanControl.Models;

namespace LenovoDesktopFanControl.Services;

public interface ISystemTemperatureService : IDisposable
{
    Task<IReadOnlyList<SystemTemperatureReading>> ReadAsync();
}

public sealed class SystemTemperatureService : ISystemTemperatureService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(5);
    private static readonly string[] CpuSensorNames = ["CPU Package", "Core Max", "Core Average"];
    private static readonly string[] MotherboardSensorNames = ["Motherboard", "System"];
    private readonly Computer _computer = new()
    {
        IsCpuEnabled = true,
        IsMotherboardEnabled = true,
        IsStorageEnabled = true
    };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTime _lastReadUtc = DateTime.MinValue;
    private IReadOnlyList<SystemTemperatureReading> _cached = [];
    private bool _opened;
    private bool _sensorInventoryLogged;

    public async Task<IReadOnlyList<SystemTemperatureReading>> ReadAsync()
    {
        if (DateTime.UtcNow - _lastReadUtc < CacheDuration)
            return _cached;

        await _gate.WaitAsync();
        try
        {
            if (DateTime.UtcNow - _lastReadUtc < CacheDuration)
                return _cached;

            if (!_opened)
            {
                _computer.Open();
                _opened = true;
            }

            return _cached = await Task.Run(ReadSensors);
        }
        catch (Exception ex)
        {
            Log.Warn($"Hardware sensor discovery failed: {ex.Message}");
            return _cached = UnavailableReadings();
        }
        finally
        {
            _lastReadUtc = DateTime.UtcNow;
            _gate.Release();
        }
    }

    private IReadOnlyList<SystemTemperatureReading> ReadSensors()
    {
        _computer.Accept(new UpdateVisitor());
        var hardware = _computer.Hardware.SelectMany(Flatten).ToArray();
        LogSensorInventory(hardware);

        var cpuSensor = FindCpuTemperature(
            hardware.Where(item => item.HardwareType == HardwareType.Cpu));
        var motherboardSensor = FindNamedTemperature(
            hardware.Where(item => item.HardwareType == HardwareType.SuperIO),
            MotherboardSensorNames,
            allowNamePrefix: true);

        return
        [
            ReadNvidiaGpuTemperature(),
            CreateReading("CPU", cpuSensor, RequiresPawnIo("CPU")),
            CreateReading(
                "SSD",
                FindNamedTemperature(
                    hardware.Where(item => item.HardwareType == HardwareType.Storage),
                    ["Temperature", "Composite Temperature"],
                    allowNamePrefix: true),
                LocalizationService.Get("DetailDriveSmartTemperatureUnavailable")),
            CreateReading("Motherboard", motherboardSensor, RequiresPawnIo("motherboard"))
        ];
    }

    private static SystemTemperatureReading ReadNvidiaGpuTemperature()
    {
        try
        {
            var start = new ProcessStartInfo(
                "nvidia-smi.exe",
                "--query-gpu=temperature.gpu --format=csv,noheader,nounits")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var process = Process.Start(start);
            if (process == null || !process.WaitForExit(2_000) || process.ExitCode != 0)
                return new SystemTemperatureReading("GPU", null, LocalizationService.Get("DetailNvidiaTelemetryUnavailable"));

            var output = process.StandardOutput.ReadToEnd().Trim();
            return int.TryParse(output.Split('\n')[0].Trim(), out var temperature)
                ? new SystemTemperatureReading("GPU", temperature, LocalizationService.Get("DetailNvidiaGpuTemperature"))
                : new SystemTemperatureReading("GPU", null, LocalizationService.Get("DetailNvidiaTelemetryUnavailable"));
        }
        catch
        {
            return new SystemTemperatureReading("GPU", null, LocalizationService.Get("DetailNvidiaTelemetryUnavailable"));
        }
    }

    private static IEnumerable<IHardware> Flatten(IHardware hardware)
    {
        yield return hardware;
        foreach (var child in hardware.SubHardware)
            foreach (var nested in Flatten(child))
                yield return nested;
    }

    private static ISensor? FindCpuTemperature(IEnumerable<IHardware> hardware)
    {
        var sensors = GetValidTemperatureSensors(hardware).ToArray();
        var preferred = FindNamedTemperature(sensors, CpuSensorNames, allowNamePrefix: false);
        if (preferred != null)
            return preferred;

        return sensors
            .Where(sensor => !sensor.Name.Contains("Distance to TjMax", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(sensor => sensor.Value)
            .FirstOrDefault();
    }

    private static ISensor? FindNamedTemperature(
        IEnumerable<IHardware> hardware,
        IReadOnlyList<string> preferredNames,
        bool allowNamePrefix) =>
        FindNamedTemperature(
            GetValidTemperatureSensors(hardware),
            preferredNames,
            allowNamePrefix);

    private static ISensor? FindNamedTemperature(
        IEnumerable<ISensor> sensors,
        IReadOnlyList<string> preferredNames,
        bool allowNamePrefix)
    {
        var available = sensors.ToArray();
        foreach (var preferredName in preferredNames)
        {
            var exact = available.FirstOrDefault(sensor =>
                sensor.Name.Equals(preferredName, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
                return exact;

            if (!allowNamePrefix)
                continue;

            var prefixed = available.FirstOrDefault(sensor =>
                sensor.Name.StartsWith(preferredName, StringComparison.OrdinalIgnoreCase));
            if (prefixed != null)
                return prefixed;
        }

        return null;
    }

    private static IEnumerable<ISensor> GetValidTemperatureSensors(IEnumerable<IHardware> hardware) =>
        hardware
            .SelectMany(item => item.Sensors)
            .Where(sensor =>
                sensor.SensorType == SensorType.Temperature &&
                sensor.Value is > 0 and <= 125 &&
                float.IsFinite(sensor.Value.Value));

    private static SystemTemperatureReading CreateReading(
        string name,
        ISensor? sensor,
        string unavailableDetail) =>
        sensor?.Value is float value
            ? new SystemTemperatureReading(
                name,
                (int)Math.Round(value),
                $"{sensor.Hardware.Name} / {sensor.Name}")
            : new SystemTemperatureReading(name, null, unavailableDetail);

    private static string RequiresPawnIo(string sensorName) =>
        PawnIo.IsInstalled
            ? LocalizationService.Get("DetailNoSensorExposed", sensorName)
            : LocalizationService.Get("DetailInstallPawnIo", sensorName);

    private void LogSensorInventory(IReadOnlyList<IHardware> hardware)
    {
        if (_sensorInventoryLogged)
            return;

        _sensorInventoryLogged = true;
        Log.Info($"LibreHardwareMonitor PawnIO installed: {PawnIo.IsInstalled}");

        foreach (var item in hardware.Where(item =>
                     item.HardwareType is HardwareType.Cpu or HardwareType.Motherboard or HardwareType.SuperIO))
        {
            var temperatures = item.Sensors
                .Where(sensor => sensor.SensorType == SensorType.Temperature)
                .Select(sensor => $"{sensor.Name}={sensor.Value?.ToString("F1") ?? "null"} °C")
                .ToArray();
            Log.Info(
                $"LibreHardwareMonitor {item.HardwareType} '{item.Name}': " +
                (temperatures.Length == 0 ? "no temperature sensors" : string.Join(", ", temperatures)));
        }
    }

    private static IReadOnlyList<SystemTemperatureReading> UnavailableReadings() =>
    [
        new("GPU", null, LocalizationService.Get("DetailNvidiaTelemetryUnavailable")),
        new("CPU", null, RequiresPawnIo("CPU")),
        new("SSD", null, LocalizationService.Get("DetailDriveSmartTemperatureUnavailable")),
        new("Motherboard", null, RequiresPawnIo("motherboard"))
    ];

    public void Dispose()
    {
        if (_opened)
            _computer.Close();
        _gate.Dispose();
    }

    private sealed class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) => computer.Traverse(this);
        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (var child in hardware.SubHardware)
                child.Accept(this);
        }
        public void VisitSensor(ISensor sensor) { }
        public void VisitParameter(IParameter parameter) { }
    }
}
