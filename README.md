# Lenovo Desktop Fan Control

A modern, high-performance Windows desktop application built with WPF and .NET 10 to monitor and control fan speeds, custom curves, and lighting profiles on Lenovo desktop PCs using WMI interfaces and Windows LampArray APIs.

---

## 🚀 Key Features

- **Dynamic Fan Speed Control**: Fine-tune fan behavior or switch between system presets (Quiet, Balanced, Performance).
- **Interactive Curve Editor**: Beautiful UI control for plotting custom temperature-to-speed curves.
- **RGB Lighting Integration**: Manage system lighting via the Windows LampArray API (`LampArrayLightingService`).
- **System Theme Harmony**: Integrates natively with Windows Light and Dark themes, adjusting title bar styling and colors dynamically.
- **Multilingual Support**: Fully localized with resource dictionaries for English (`en`) and Finnish (`fi-FI`).
- **Auto-Start Integration**: Seamless options to launch the controller minimized or directly at system boot.
- **Visual Mock / Simulation Mode**: Built-in `VisualTestFanControlService` to run, preview, and test UI components safely on any Windows machine without requiring direct Lenovo hardware.

---

## 🛠️ System Requirements & Architecture

- **Operating System**: Windows 10/11 (Target SDK: `windows10.0.26100.0`)
- **Runtime**: .NET 10.0
- **Privileges**: **Administrator rights** are required to interface with Lenovo WMI hardware control classes. This is enforced via `app.manifest`.

---

## 📂 Codebase Structure

The project follows clean MVVM (Model-View-ViewModel) design principles and is divided into two primary projects:

```
LenovoDesktopFanControl/
├── LenovoDesktopFanControl/                     # Main WPF Application
│   ├── Assets/                                  # App icons and media
│   ├── Models/                                  # Domain entities (FanInfo, FanTable, SmartFanMode)
│   ├── Services/                                # Hardware access, settings, localization, and theme services
│   │   ├── AutoStartService.cs                  # Configures startup behavior in Windows Registry
│   │   ├── FanFirmwareCompatibility.cs         # Checks system model/firmware matching
│   │   ├── LampArrayLightingService.cs          # Direct lighting control implementation
│   │   ├── NativeWindowTheme.cs                 # Native Windows system theme sync
│   │   ├── VisualTestFanControlService.cs       # Mock service for non-Lenovo hardware development
│   │   └── WmiFanControlService.cs              # WMI communication with Lenovo fan controller
│   ├── ViewModels/                              # Presenters (MainViewModel, FanViewModel, RelayCommand)
│   └── Views/                                   # XAML Pages, custom UserControls, Styles, and Value Converters
│
└── LenovoDesktopFanControl.Tests/               # Unit testing Suite
    ├── MainViewModelTests.cs                    # UI logic and command tests
    ├── ModelTests.cs                            # Configuration and parser tests
    ├── LocalizationServiceTests.cs              # Multi-language verification tests
    └── FanFirmwareCompatibilityTests.cs          # Hardware identification and compatibility logic tests
```

---

## 💻 Building and Running

### Prerequisites
1. Ensure the latest .NET 10 SDK is installed.
2. Launch your command prompt or IDE (such as Rider or Visual Studio) as **Administrator**.

### Commands

**Build the Solution:**
```sh
dotnet build
```

**Run the Application:**
```sh
dotnet run
```

**Run Unit Tests:**
```sh
dotnet test
```

---

## 🧪 Visual Test Mode & Non-Lenovo Devices

For development and UI design modifications, you can configure the app to run with `VisualTestFanControlService` instead of `WmiFanControlService`. This allows developers on non-Lenovo PCs (or laptops) to fully run the application, interact with the fan curve UI, and verify theme adjustments without running into WMI communication errors.

---

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](file:///c:/Users/petri/RiderProjects/LenovoDesktopFanControl/LICENSE) file for details.
