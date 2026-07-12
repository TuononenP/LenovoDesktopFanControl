# Plan: Lenovo Desktop Fan Control WPF Application

## Target Hardware
- **Lenovo Legion Tower 7i Gen 10 (Intel)** desktop PC
- Lenovo desktop PCs expose fan control through WMI (Windows Management Instrumentation) in the `root\WMI` namespace, using the same `LENOVO_FAN_METHOD`, `LENOVO_FAN_TABLE_DATA`, and `LENOVO_GAMEZONE_DATA` WMI classes used by Lenovo Legion laptops. The Lenovo Vantage Gaming Feature Driver (or Lenovo Energy Management) must be installed for these WMI classes to be available.

## Project Context
- **Solution**: `LenovoDesktopFanControl.sln`
- **Project**: `LenovoDesktopFanControl` (WPF, .NET 10, `net10.0-windows`)
- **Existing files**: `App.xaml`/`App.xaml.cs`, `MainWindow.xaml`/`MainWindow.xaml.cs`, `AssemblyInfo.cs`
- The project is a blank WPF template with `UseWPF=true`, `Nullable=enable`, `ImplicitUsings=enable`.

---

## 1. Architecture Overview

The app follows an MVVM (Model-View-ViewModel) pattern with three layers:

```
┌──────────────────────────────────────────────┐
│                   WPF UI                     │
│  (MainWindow.xaml, FanControl.xaml,          │
│   FanSpeedSlider.xaml, Converters)            │
├──────────────────────────────────────────────┤
│                ViewModels                     │
│  (MainViewModel, FanViewModel)                │
│   INotifyPropertyChanged, DispatcherTimer    │
├──────────────────────────────────────────────┤
│              Services / Models                │
│  WmiFanControlService                         │
│  FanInfo, FanTable, SmartFanMode enums        │
│  FanSettings (JSON persistence)               │
├──────────────────────────────────────────────┤
│              WMI (root\WMI)                   │
│  LENOVO_FAN_METHOD                            │
│  LENOVO_FAN_TABLE_DATA                        │
│  LENOVO_GAMEZONE_DATA                         │
└──────────────────────────────────────────────┘
```

### Key Design Decisions
- **Admin privileges required**: The app must run as Administrator to access WMI fan control methods. The app manifest (`app.manifest`) will request `requireAdministrator`.
- **Polling for fan speed**: A `DispatcherTimer` polls current fan RPM every 1–2 seconds via `Fan_GetCurrentFanSpeed`.
- **Fan table for speed control**: Fan speed is set via the `Fan_Set_Table` WMI method using a 10-element byte array (values 0–10 representing 10%–100% speed steps at 10 temperature thresholds).
- **SmartFanMode**: To use custom fan curves, the SmartFanMode must be set to Custom (value 255). Setting back to Quiet (0), Balanced (1), or Performance (2) returns control to the BIOS.
- **Settings persistence**: Fan curves and preferences saved to a JSON file in `%LOCALAPPDATA%\LenovoDesktopFanControl\settings.json`.

---

## 2. WMI Fan Control API (Root\WMI Namespace)

Based on analysis of the LenovoLegionToolkit source code and the pjt222/fancontrol project, the following WMI classes and methods are used:

### 2.1 LENOVO_GAMEZONE_DATA

Used to check fan control support and switch SmartFanMode.

| Method | Parameters | Return | Purpose |
|--------|-----------|--------|---------|
| `IsSupportSmartFan` | none | `Data` (int) | Returns SmartFan version (0 = not supported, 4+ = supported) |
| `GetSmartFanMode` | none | `Data` (int) | Returns current mode: 0=Quiet, 1=Balanced, 2=Performance, 255=Custom |
| `SetSmartFanMode` | `Data` (int) | none | Sets the SmartFan mode |

**WMI Query**: `SELECT * FROM LENOVO_GAMEZONE_DATA`

### 2.2 LENOVO_FAN_METHOD

Used to read fan speeds, temperatures, and set fan tables.

| Method | Parameters | Return | Purpose |
|--------|-----------|--------|---------|
| `Fan_GetCurrentFanSpeed` | `FanID` (int) | `CurrentFanSpeed` (int) | Returns current RPM for the given fan |
| `Fan_GetCurrentSensorTemperature` | `SensorID` (int) | `CurrentSensorTemperature` (int) | Returns temperature in tenths of °C for the given sensor |
| `Fan_Set_Table` | `FanTable` (byte[10]) | none | Sets the 10-step fan speed curve |
| `Fan_Get_FullSpeed` | none | `Status` (bool) | Returns whether full-speed mode is active |
| `Fan_Set_FullSpeed` | `Status` (int) | none | Enable (1) or disable (0) full-speed mode |

**WMI Query**: `SELECT * FROM LENOVO_FAN_METHOD`

### 2.3 LENOVO_FAN_TABLE_DATA

Read-only class that provides the current fan table configuration.

**WMI Query**: `SELECT * FROM LENOVO_FAN_TABLE_DATA`

**Properties**:
- `Mode` (int): Power mode index
- `Fan_Id` (byte): Fan identifier
- `Sensor_ID` (byte): Sensor identifier
- `FanTable_Data` (ushort[10]): Fan speed values at each temperature step
- `SensorTable_Data` (ushort[10]): Temperature thresholds for each step

### 2.4 Fan/Sensor ID Mapping (Legion Tower Desktop)

For Legion desktops (Gen 10 Intel), the fan/sensor IDs typically are:

| Fan | FanID | SensorID | Type |
|-----|-------|----------|------|
| CPU Fan | 0 | 0 | CPU |
| GPU Fan | 1 | 4 | GPU |
| Front/Chassis Fan | 2 | 5 | Chassis (if available) |

> **Note**: The exact fan/sensor IDs may vary by BIOS. The app should discover available fans by iterating FanID 0–7 and checking which return valid RPMs. The `LENOVO_FAN_TABLE_DATA` query also reveals available fan/sensor combinations.

### 2.5 FanTable Format

The `Fan_Set_Table` method accepts a `byte[10]` array where each value is 0–10, representing the fan speed at a corresponding temperature step. The temperature steps are defined by the `SensorTable_Data` from `LENOVO_FAN_TABLE_DATA`.

- Value 0 = 0% (fan off or minimum)
- Value 1 = 10%
- Value 10 = 100% (full speed)
- Values must be **non-decreasing** (monotonically increasing) for safety.

Default fan table: `[1, 2, 3, 4, 5, 6, 7, 8, 9, 10]`
Minimum safe table (V2): `[1, 1, 1, 1, 1, 1, 1, 1, 3, 5]`

---

## 3. Project File Structure

```
LenovoDesktopFanControl/
├── LenovoDesktopFanControl.csproj      (modify: add app.manifest)
├── app.manifest                        (NEW: request admin privileges)
├── App.xaml                            (modify: add styles/resources)
├── App.xaml.cs                         (no change needed)
├── AssemblyInfo.cs                     (no change)
├── Models/
│   ├── FanInfo.cs                      (NEW: fan data model)
│   ├── FanTable.cs                     (NEW: 10-step fan curve model)
│   ├── SmartFanMode.cs                 (NEW: enum for fan modes)
│   └── FanSettings.cs                  (NEW: persisted settings model)
├── Services/
│   ├── IWmiFanControlService.cs        (NEW: interface for fan control)
│   ├── WmiFanControlService.cs         (NEW: WMI implementation)
│   └── SettingsService.cs             (NEW: JSON settings load/save)
├── ViewModels/
│   ├── MainViewModel.cs                (NEW: main window VM)
│   ├── FanViewModel.cs                 (NEW: per-fan VM with RPM, slider)
│   └── RelayCommand.cs                 (NEW: ICommand implementation)
├── Views/
│   ├── MainWindow.xaml                 (modify: full layout)
│   ├── MainWindow.xaml.cs              (modify: DataContext binding)
│   ├── Controls/
│   │   ├── FanCard.xaml                (NEW: individual fan card)
│   │   ├── FanCard.xaml.cs             (NEW: code-behind)
│   │   ├── FanCurveEditor.xaml         (NEW: 10-point curve editor)
│   │   └── FanCurveEditor.xaml.cs      (NEW: code-behind)
│   └── Converters/
│       └── RpmToStringConverter.cs     (NEW: value converter)
└── Assets/
    └── (fan icons, app icon)
```

---

## 4. Implementation Plan

### Phase 1: WMI Fan Control Service

#### 4.1.1 `Models/SmartFanMode.cs`
```csharp
namespace LenovoDesktopFanControl.Models;

public enum SmartFanMode
{
    Quiet = 0,
    Balanced = 1,
    Performance = 2,
    Custom = 255
}
```

#### 4.1.2 `Models/FanInfo.cs`
```csharp
namespace LenovoDesktopFanControl.Models;

public class FanInfo
{
    public int FanId { get; init; }
    public string Name { get; init; } = "";
    public int CurrentRpm { get; set; }
    public int Temperature { get; set; }
    public int SensorId { get; init; }
    public bool IsAvailable { get; set; }
}
```

#### 4.1.3 `Models/FanTable.cs`
```csharp
namespace LenovoDesktopFanControl.Models;

public class FanTable
{
    public byte[] Speeds { get; set; } = new byte[10];

    public byte[] GetBytes() => Speeds;

    public static FanTable Default() => new() { Speeds = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10] };
    public static FanTable Minimum() => new() { Speeds = [1, 1, 1, 1, 1, 1, 1, 1, 3, 5] };
}
```

#### 4.1.4 `Models/FanSettings.cs`
```csharp
namespace LenovoDesktopFanControl.Models;

public class FanSettings
{
    public SmartFanMode Mode { get; set; } = SmartFanMode.Balanced;
    public Dictionary<int, byte[]> FanCurves { get; set; } = new();
    public int PollingIntervalMs { get; set; } = 2000;
    public bool StartWithWindows { get; set; } = false;
    public bool MinimizeToTray { get; set; } = false;
}
```

#### 4.1.5 `Services/IWmiFanControlService.cs`
```csharp
namespace LenovoDesktopFanControl.Services;

public interface IWmiFanControlService : IDisposable
{
    Task<bool> IsSupportedAsync();
    Task<SmartFanMode> GetSmartFanModeAsync();
    Task SetSmartFanModeAsync(SmartFanMode mode);
    Task<List<FanInfo>> DiscoverFansAsync();
    Task<int> GetFanSpeedAsync(int fanId);
    Task<int> GetSensorTemperatureAsync(int sensorId);
    Task SetFanTableAsync(byte[] fanTable);
    Task<bool> GetFullSpeedAsync();
    Task SetFullSpeedAsync(bool enabled);
    Task<FanTable?> GetFanTableDataAsync();
}
```

#### 4.1.6 `Services/WmiFanControlService.cs`

Core WMI implementation using `System.Management` namespace.

**Key methods**:
- `IsSupportedAsync()`: Queries `SELECT * FROM LENOVO_GAMEZONE_DATA` and calls `IsSupportSmartFan`. Returns true if result > 0.
- `DiscoverFansAsync()`: Iterates FanID 0–7, calls `Fan_GetCurrentFanSpeed`. Fans that return a valid RPM (≥ 0 and no exception) are added to the list.
- `GetFanSpeedAsync(fanId)`: Calls `Fan_GetCurrentFanSpeed` on `LENOVO_FAN_METHOD`.
- `GetSensorTemperatureAsync(sensorId)`: Calls `Fan_GetCurrentSensorTemperature` on `LENOVO_FAN_METHOD`. Returns temperature in tenths of °C (divide by 10 for °C).
- `SetFanTableAsync(byte[])`: Calls `Fan_Set_Table` on `LENOVO_FAN_METHOD` with the byte array.
- `GetSmartFanModeAsync()`: Calls `GetSmartFanMode` on `LENOVO_GAMEZONE_DATA`.
- `SetSmartFanModeAsync(mode)`: Calls `SetSmartFanMode` on `LENOVO_GAMEZONE_DATA`.
- `GetFullSpeedAsync()`: Calls `Fan_Get_FullSpeed` on `LENOVO_FAN_METHOD`.
- `SetFullSpeedAsync(bool)`: Calls `Fan_Set_FullSpeed` on `LENOVO_FAN_METHOD`.

WMI implementation pattern (from LenovoLegionToolkit `WMI.cs`):
```csharp
using System.Management;

private async Task CallWmiMethod(string scope, string query, 
    string method, Dictionary<string, object> parameters)
{
    var mos = new ManagementObjectSearcher(scope, query);
    foreach (ManagementObject mo in mos.Get())
    {
        var inParams = mo.GetMethodParameters(method);
        foreach (var p in parameters)
            inParams[p.Key] = p.Value;
        mo.InvokeMethod(method, inParams, null);
    }
}

private async Task<T> CallWmiMethod<T>(string scope, string query,
    string method, Dictionary<string, object> parameters,
    Func<PropertyDataCollection, T> converter)
{
    var mos = new ManagementObjectSearcher(scope, query);
    foreach (ManagementObject mo in mos.Get())
    {
        var inParams = mo.GetMethodParameters(method);
        foreach (var p in parameters)
            inParams[p.Key] = p.Value;
        var outParams = mo.InvokeMethod(method, inParams, null);
        return converter(outParams.Properties);
    }
    throw new InvalidOperationException("No WMI results");
}
```

**Csproj change**: Add `System.Management` package reference:
```xml
<PackageReference Include="System.Management" Version="9.0.0" />
```

#### 4.1.7 `Services/SettingsService.cs`
- Load/save `FanSettings` as JSON to `%LOCALAPPDATA%\LenovoDesktopFanControl\settings.json`.
- Uses `System.Text.Json` serializer.

### Phase 2: ViewModels

#### 4.2.1 `ViewModels/RelayCommand.cs`
Standard `ICommand` implementation with `Action` execute and `Func<bool>` canExecute.

#### 4.2.2 `ViewModels/FanViewModel.cs`
Per-fan view model for data binding:
- `FanName` (string): Display name ("CPU Fan", "GPU Fan", "Chassis Fan")
- `CurrentRpm` (int): Current RPM, updated by polling timer
- `MaxRpm` (int): Estimated max RPM for percentage calculation (e.g., 2000 RPM typical)
- `SpeedPercentage` (int): Calculated as `CurrentRpm / MaxRpm * 100`
- `Temperature` (int): Current temperature in °C
- `TargetSpeedPercentage` (int): Slider value (0–100) that the user wants
- `IsManualControlEnabled` (bool): Whether this fan is under manual control
- `FanCurve` (byte[10]): 10-step fan curve for this fan
- `ApplySpeedCommand`: Converts `TargetSpeedPercentage` to FanTable and calls `SetFanTableAsync`
- Implements `INotifyPropertyChanged`

#### 4.2.3 `ViewModels/MainViewModel.cs`
Main window view model:
- `Fans` (`ObservableCollection<FanViewModel>`): List of discovered fans
- `SelectedFanMode` (`SmartFanMode`): Current SmartFanMode (Quiet/Balanced/Performance/Custom)
- `IsFullSpeed` (bool): Full-speed toggle
- `IsSupported` (bool): Whether the device supports fan control
- `StatusMessage` (string): Status/error messages
- `RefreshCommand`: Polls all fan speeds and temperatures
- `ApplyModeCommand`: Sets SmartFanMode
- `ApplyFanCurveCommand`: Applies custom fan curves to all fans
- `FullSpeedCommand`: Toggles full-speed mode
- `ResetToDefaultCommand`: Resets to default fan table and Balanced mode
- `SaveSettingsCommand`: Saves settings to JSON
- `DispatcherTimer` (polling every 2s): Calls `RefreshCommand` to update RPM/temperature

### Phase 3: UI (WPF XAML)

#### 4.3.1 `app.manifest`
```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <assemblyIdentity version="1.0.0.0" name="LenovoDesktopFanControl" />
  <trustInfo xmlns="urn:schemas-microsoft-com:asm.v2">
    <security>
      <requestedPrivileges xmlns="urn:schemas-microsoft-com:asm.v3">
        <requestedExecutionLevel level="requireAdministrator" uiAccess="false" />
      </requestedPrivileges>
    </security>
  </trustInfo>
</assembly>
```

#### 4.3.2 `MainWindow.xaml` — Main Layout

```
┌─────────────────────────────────────────────────────────────┐
│  Lenovo Desktop Fan Control                    [_] [□] [X]   │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  ┌─────────────────────────────────────────────────────┐   │
│  │  SmartFan Mode:                                      │   │
│  │  [ Quiet ] [ Balanced ] [ Performance ] [ Custom ]   │   │
│  │                                                       │   │
│  │  [x] Full Speed Mode    [ Apply ] [ Reset to Default ]│   │
│  └─────────────────────────────────────────────────────┘   │
│                                                             │
│  ┌──────────────────┐  ┌──────────────────┐                │
│  │   CPU Fan        │  │   GPU Fan        │                │
│  │                  │  │                  │                │
│  │  RPM: 1200       │  │  RPM: 0          │                │
│  │  ████████░░ 60%  │  │  ░░░░░░░░░░ 0%   │                │
│  │  Temp: 45°C      │  │  Temp: 38°C      │                │
│  │                  │  │                  │                │
│  │  Speed: [━━●━━]  │  │  Speed: [●━━━]  │                │
│  │  0%    50%  100% │  │  0%    50%  100%│                │
│  │                  │  │                  │                │
│  │  [Apply] [Curve] │  │  [Apply] [Curve] │                │
│  └──────────────────┘  └──────────────────┘                │
│                                                             │
│  ┌──────────────────┐                                      │
│  │  Chassis Fan     │                                      │
│  │  RPM: 800        │                                      │
│  │  ██████░░░░ 40% │                                      │
│  │  Temp: 35°C      │                                      │
│  │  Speed: [━━━●━]  │                                      │
│  │  [Apply] [Curve] │                                      │
│  └──────────────────┘                                      │
│                                                             │
│  Status: Connected - 3 fans detected                        │
└─────────────────────────────────────────────────────────────┘
```

**Layout details**:
- Top panel: SmartFanMode radio buttons (Quiet/Balanced/Performance/Custom), full-speed toggle, apply/reset buttons
- Middle area: `ItemsControl` bound to `Fans` collection, using `FanCard.xaml` as `ItemTemplate`
- Each `FanCard` shows: fan name, current RPM (large text), speed bar (percentage), temperature, speed slider (0–100%), "Apply" button, "Edit Curve" button
- Bottom: Status bar with connection status and fan count
- Window: 800×600, resizable, dark theme

#### 4.3.3 `Views/Controls/FanCard.xaml`

Each fan card is a `Border` with:
- **Header**: Fan name (e.g., "CPU Fan") with an icon
- **RPM display**: Large `TextBlock` showing current RPM (e.g., "1200 RPM")
- **Speed bar**: `ProgressBar` bound to `SpeedPercentage` (0–100)
- **Temperature**: `TextBlock` showing temperature in °C
- **Speed slider**: `Slider` (0–100) bound to `TargetSpeedPercentage`
- **Apply button**: `Button` bound to `ApplySpeedCommand`
- **Curve editor button**: `Button` that opens `FanCurveEditor` window

#### 4.3.4 `Views/Controls/FanCurveEditor.xaml`

A dialog window for editing the 10-point fan curve:
- 10 vertical `Slider` controls side by side, each representing one temperature step
- X-axis labels: temperature thresholds (from `SensorTable_Data`)
- Y-axis labels: 0% to 100%
- A `Canvas` overlay draws the curve connecting the 10 points
- "Apply" and "Cancel" buttons
- Validates non-decreasing values before applying

```
┌──────────────────────────────────────────────┐
│  CPU Fan Curve Editor                        │
│                                              │
│ 100% ─│                                      │
│       │              ╱────                   │
│  80% ─│         ╱───╱                        │
│       │    ╱───╱                              │
│  60% ─│ ╱─╱                                   │
│       │╱                                     │
│  40% ─│                                      │
│       │                                      │
│  20% ─│                                      │
│       │                                      │
│   0% ─└───────────────────────────── °C       │
│       30  40  50  60  70  80  90 100          │
│                                              │
│  [━━] [━━] [━━] [━━] [━━] [━━] [━━] [━━] [━] │
│   1    2    3    4    5    6    7    8    9   │
│                                              │
│         [ Apply ]    [ Cancel ]               │
└──────────────────────────────────────────────┘
```

#### 4.3.5 `MainWindow.xaml.cs`
- Sets `DataContext` to `MainViewModel`
- On `Loaded`: calls `MainViewModel.InitializeAsync()` which checks compatibility, discovers fans, starts polling
- On `Closing`: saves settings

#### 4.3.6 `App.xaml`
- Add merged dictionaries for a dark theme (or use a library like `Wpf.Ui` for Fluent design)
- Suggested: use `Wpf.Ui` NuGet package for modern Windows 11 Fluent style controls

**Csproj additions**:
```xml
<PackageReference Include="System.Management" Version="9.0.0" />
<PackageReference Include="WPF-UI" Version="3.0.5" />
```

---

## 5. User Workflow

### 5.1 Startup
1. App launches (requests admin elevation via manifest)
2. `MainViewModel.InitializeAsync()` runs:
   - Checks `IsSupportedAsync()` — if not supported, shows error message
   - If supported, calls `DiscoverFansAsync()` to detect all available fans
   - Reads current `SmartFanMode` from WMI
   - Loads saved settings from JSON
   - Starts polling timer (2-second interval)

### 5.2 Viewing Fan Speeds
- Each fan card displays real-time RPM and temperature (updated every 2 seconds)
- Speed bar shows percentage of max RPM

### 5.3 Setting Fan Speed (Simple)
1. User selects "Custom" SmartFanMode
2. User adjusts the speed slider on a fan card (0–100%)
3. User clicks "Apply" → converts percentage to a FanTable (all 10 steps set to the same value, or a scaled curve) → calls `SetFanTableAsync`
4. Fan speed changes immediately

### 5.4 Setting Fan Curve (Advanced)
1. User clicks "Edit Curve" on a fan card
2. `FanCurveEditor` opens showing the current 10-point curve
3. User adjusts the 10 sliders (each 0–10 representing 0–100%)
4. Validation ensures values are non-decreasing
5. User clicks "Apply" → calls `SetFanTableAsync` with the new curve
6. App saves the curve to settings JSON

### 5.5 Switching Modes
- Click "Quiet", "Balanced", or "Performance" → calls `SetSmartFanModeAsync` → returns control to BIOS
- Click "Custom" → enables manual fan table control → applies last saved curve

### 5.6 Full Speed Mode
- Toggle "Full Speed" checkbox → calls `SetFullSpeedAsync(true/false)`
- When enabled, all fans run at maximum RPM regardless of curve

### 5.7 Reset to Default
- Click "Reset to Default" → sets SmartFanMode to Balanced → applies default FanTable `[1,2,3,4,5,6,7,8,9,10]`

---

## 6. Error Handling & Safety

### 6.1 Compatibility Check
- On startup, query `LENOVO_GAMEZONE_DATA` → `IsSupportSmartFan`
- If the WMI class doesn't exist or returns 0, show: "This device does not support fan control via WMI. Make sure Lenovo Vantage Gaming Feature Driver is installed."
- Check `Win32_ComputerSystem.Manufacturer` == "LENOVO"

### 6.2 Fan Table Validation
- All 10 values must be 0–10
- Values must be **non-decreasing** (safety: prevents fan from spinning slower at higher temperatures)
- Minimum safe table enforced: high-temperature steps must have minimum speed values

### 6.3 WMI Error Handling
- Wrap all WMI calls in try-catch
- `ManagementException` → show error message in status bar
- If WMI method call fails, log the error and show a user-friendly message
- Do not crash on WMI failures; show error and continue polling

### 6.4 Recovery on Exit
- On app close: if Custom mode was active, optionally restore to Balanced mode
- Save current settings to JSON
- Stop the polling timer

---

## 7. Settings Persistence

### 7.1 Settings File Location
`%LOCALAPPDATA%\LenovoDesktopFanControl\settings.json`

### 7.2 Settings JSON Structure
```json
{
  "mode": 1,
  "fanCurves": {
    "0": [1, 2, 3, 4, 5, 6, 7, 8, 9, 10],
    "1": [2, 3, 4, 5, 6, 7, 8, 9, 10, 10],
    "2": [1, 1, 2, 3, 4, 5, 6, 7, 8, 9]
  },
  "pollingIntervalMs": 2000,
  "startWithWindows": false,
  "minimizeToTray": false
}
```

### 7.3 Save/Load
- `SettingsService.Load()`: Deserialize JSON. If file doesn't exist, return default settings.
- `SettingsService.Save(FanSettings)`: Serialize to JSON with indentation.
- Save on: mode change, curve apply, app close.
- Load on: app startup (after fan discovery).

---

## 8. Implementation Steps (Ordered)

### Step 1: Project Setup
- [ ] Add NuGet packages to `.csproj`: `System.Management`, `WPF-UI` (or skip if not needed)
- [ ] Create `app.manifest` with `requireAdministrator`
- [ ] Configure `.csproj` to use the manifest
- [ ] Create folder structure: `Models/`, `Services/`, `ViewModels/`, `Views/Controls/`, `Views/Converters/`

### Step 2: Models
- [ ] Create `Models/SmartFanMode.cs`
- [ ] Create `Models/FanInfo.cs`
- [ ] Create `Models/FanTable.cs`
- [ ] Create `Models/FanSettings.cs`

### Step 3: WMI Fan Control Service
- [ ] Create `Services/IWmiFanControlService.cs`
- [ ] Create `Services/WmiFanControlService.cs` with all WMI methods
- [ ] Create `Services/SettingsService.cs`
- [ ] Test: verify `IsSupportedAsync()` and `DiscoverFansAsync()` work on the target machine

### Step 4: ViewModels
- [ ] Create `ViewModels/RelayCommand.cs`
- [ ] Create `ViewModels/FanViewModel.cs`
- [ ] Create `ViewModels/MainViewModel.cs`
- [ ] Wire up polling timer and commands

### Step 5: Main Window UI
- [ ] Redesign `MainWindow.xaml` with the full layout
- [ ] Update `MainWindow.xaml.cs` with DataContext and initialization
- [ ] Add `Views/Converters/RpmToStringConverter.cs`
- [ ] Update `App.xaml` with theme resources

### Step 6: Fan Card Control
- [ ] Create `Views/Controls/FanCard.xaml` and `.xaml.cs`
- [ ] Bind to `FanViewModel` properties
- [ ] Implement speed slider and apply button

### Step 7: Fan Curve Editor
- [ ] Create `Views/Controls/FanCurveEditor.xaml` and `.xaml.cs`
- [ ] Implement 10-slider layout
- [ ] Add Canvas curve visualization
- [ ] Add validation and apply logic

### Step 8: Integration & Testing
- [ ] Connect all components
- [ ] Test fan discovery on the Legion Tower 7i Gen 10
- [ ] Test reading current fan speeds
- [ ] Test setting fan mode (Quiet/Balanced/Performance/Custom)
- [ ] Test setting fan speed via slider
- [ ] Test fan curve editor
- [ ] Test full-speed toggle
- [ ] Test settings persistence
- [ ] Test error handling (run without admin, without drivers)

### Step 9: Polish
- [ ] Add app icon
- [ ] Add fan status icons (spinning/static)
- [ ] Add tray icon support (optional)
- [ ] Add "Start with Windows" option (registry-based)
- [ ] Add logging to `%LOCALAPPDATA%\LenovoDesktopFanControl\log.txt`

---

## 9. Key References

- **LenovoLegionToolkit** (archived): https://github.com/BartoszCichecki/LenovoLegionToolkit
  - `WMI.LenovoFanMethod.cs` — fan speed read/write WMI methods
  - `WMI.LenovoFanTableData.cs` — fan table data WMI class
  - `WMI.LenovoGameZoneData.cs` — SmartFanMode WMI methods
  - `WMI.cs` — WMI helper utilities (CallAsync, ReadAsync patterns)
  - `GodModeControllerV1.cs` / `GodModeControllerV2.cs` — fan table apply logic
  - `Compatibility.cs` — device compatibility checking
  - `FanCurveControl.xaml` — UI pattern for curve editor

- **pjt222/fancontrol**: https://github.com/pjt222/fancontrol
  - Cross-platform Rust implementation; Lenovo WMI via PowerShell subprocess
  - Documents `Fan_Set_Table`, `Fan_GetCurrentFanSpeed`, SmartFanMode

- **Required drivers on target machine**:
  1. Lenovo Vantage Gaming Feature Driver
  2. Lenovo Energy Management (if available for desktop)

---

## 10. Known Limitations & Risks

1. **Desktop vs Laptop WMI**: The `LENOVO_FAN_METHOD` and `LENOVO_GAMEZONE_DATA` WMI classes are confirmed for Lenovo Legion laptops. The Legion Tower 7i Gen 10 desktop may expose the same classes if the Lenovo Vantage Gaming Feature Driver is installed, but the fan/sensor ID mapping may differ. The app must discover fans dynamically rather than hardcoding IDs.

2. **Fan Table Volatility**: Custom fan curves are volatile at the hardware level — they are lost on reboot, sleep/wake, or power mode change. The app should re-apply curves on startup and optionally after sleep/wake events.

3. **Admin Required**: WMI fan control methods require administrator privileges. The manifest enforces this, but users may need to approve UAC each time.

4. **Vantage/Legion Zone Conflict**: If Lenovo Vantage or Legion Zone is running, it may conflict with custom fan control. The app should detect and warn the user (or recommend disabling Vantage).

5. **Fan RPM Range**: The max RPM per fan varies. The app should estimate max RPM from the `LENOVO_FAN_TABLE_DATA` `CurrentFanMaxSpeed` property, or use a default of 2000 RPM.

6. **Temperature Units**: `Fan_GetCurrentSensorTemperature` returns temperature in tenths of °C (e.g., 450 = 45.0°C). The app must divide by 10 for display.