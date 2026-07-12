# Build Commands

## Build
```sh
dotnet build
```

## Run (requires admin elevation via app.manifest)
```sh
dotnet run
```

## Project Structure
- `LenovoDesktopFanControl/` — WPF .NET 10 project
  - `Models/` — Data models (FanInfo, FanTable, FanSettings, SmartFanMode)
  - `Services/` — WMI fan control, settings, autostart, logging
  - `ViewModels/` — MVVM view models (MainViewModel, FanViewModel, RelayCommand)
  - `Views/Controls/` — FanCard, FanCurveEditor
  - `Views/Converters/` — WPF value converters