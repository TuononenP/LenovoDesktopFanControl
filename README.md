# Lenovo Desktop Fan Control

A modern WPF application for monitoring and controlling fan speeds, custom fan curves, and tower lighting on supported Lenovo desktop PCs. It uses Lenovo WMI interfaces and the Windows LampArray API, with a visual test service for UI development without compatible hardware.

![Lenovo Desktop Fan Control dashboard](docs/images/dashboard.png)

## Key Features

- Quiet, Balanced, Performance, and Custom SmartFan modes
- Live fan RPM and temperature monitoring
- Per-fan target speeds and interactive custom fan curves
- Multi-channel fan-zone telemetry
- Tower-lighting power, brightness, global color, and per-zone color controls
- Persistent fan, lighting-preference, language, startup, and tray settings
- English and Finnish localization
- Start with Windows and minimize-to-tray support
- Visual test mode for development on unsupported hardware

## Requirements

- Windows 10 or Windows 11
- .NET 10 SDK or runtime
- A supported Lenovo desktop for hardware control
- Administrator privileges for Lenovo WMI access, requested through `app.manifest`

The project targets `net10.0-windows10.0.26100.0`.

## Building and Running

```powershell
dotnet build
dotnet run
dotnet test
```

Run the application from an elevated terminal when accessing real hardware.

## Codebase Structure

The solution uses MVVM and is split into the WPF application and its xUnit test project.

```text
LenovoDesktopFanControl/
|-- LenovoDesktopFanControl.sln
|-- LenovoDesktopFanControl/                 WPF application
|   |-- App.xaml(.cs)                        Application startup and shared resources
|   |-- MainWindow.xaml(.cs)                 Main dashboard, tray, and window lifecycle
|   |-- app.manifest                         Administrator elevation configuration
|   |-- Assets/                              Application icon and asset generation
|   |-- Models/
|   |   |-- FanInfo.cs                       Fan and telemetry-channel data
|   |   |-- FanSettings.cs                   Persisted application settings
|   |   |-- FanTable.cs                      Ten-point firmware fan curves
|   |   |-- LightingDeviceInfo.cs            Lighting devices, zones, and colors
|   |   `-- SmartFanMode.cs                  Firmware operating modes
|   |-- Services/
|   |   |-- WmiFanControlService.cs          Lenovo fan discovery and control
|   |   |-- WmiLightingService.cs            Lenovo WMI lighting control
|   |   |-- LampArrayLightingService.cs      Windows Dynamic Lighting integration
|   |   |-- SettingsService.cs               JSON settings persistence
|   |   |-- LocalizationService.cs           Runtime language selection
|   |   |-- AutoStartService.cs              Windows startup registration
|   |   |-- FanFirmwareCompatibility.cs      Model and firmware compatibility checks
|   |   |-- NativeWindowTheme.cs             Native title-bar appearance
|   |   `-- VisualTestFanControlService.cs   Hardware-free UI development service
|   |-- ViewModels/
|   |   |-- MainViewModel.cs                 App state, polling, settings, and commands
|   |   |-- FanViewModel.cs                  Fan-zone control and summarized telemetry
|   |   |-- FanChannelViewModel.cs           Individual telemetry channels
|   |   |-- LightingViewModel.cs             Lighting discovery and control state
|   |   `-- RelayCommand.cs                  MVVM command implementation
|   |-- Views/
|   |   |-- Controls/                        Fan cards, icons, and curve editor
|   |   |-- Converters/                      WPF binding converters
|   |   |-- Markup/                          Localization markup extension
|   |   `-- TrayMenuRenderer.cs              System-tray menu presentation
|   |-- Themes/                              Colors, controls, and typography resources
|   `-- Resources/                           English and Finnish resource files
|-- LenovoDesktopFanControl.Tests/           xUnit unit and view-model tests
|   |-- MainViewModelTests.cs
|   |-- FanViewModelTests.cs
|   |-- LightingViewModelTests.cs
|   |-- SettingsServiceTests.cs
|   |-- WmiFanControlServiceTests.cs
|   `-- TestDoubles.cs
`-- docs/images/                             README images
```

### Main Runtime Flow

`MainWindow` creates `MainViewModel`, which loads persisted settings and coordinates the fan and lighting services. `WmiFanControlService` handles Lenovo fan firmware operations. Lighting is exposed through `ILightingControlService` and can be backed by Lenovo WMI or Windows LampArray. View models expose this state to the XAML controls and save user selections through `SettingsService`.

LampArray lighting is controlled while the application is running. The selected lighting preferences are restored when the application starts, but the hardware may return to its firmware-defined profile after the application exits.

### Settings and Logs

Settings are stored as JSON under:

```text
%LOCALAPPDATA%\LenovoDesktopFanControl\settings.json
```

Application diagnostics are written by `Services/Log.cs` under the same application data area.

## Visual Test Mode

For UI development without supported Lenovo hardware, construct the main view model with `VisualTestFanControlService`. It provides simulated fans and telemetry while keeping the normal view and view-model layers intact.

## License

Licensed under the [MIT License](LICENSE).
