<div align="center">
  <img src="assets/branding/claribar-128.png" width="96" height="96" alt="ClariBar logo">
  <h1>ClariBar</h1>
  <p>A clearer taskbar. Your system at a glance.</p>
  <p><a href="https://github.com/Adoubf/ClariBar/releases">Download</a> · <a href="https://github.com/Adoubf/ClariBar/issues">Report an issue</a> · <a href="LICENSE">MIT License</a></p>
</div>

**English** | [简体中文](README.zh-CN.md)

ClariBar is a Windows 11 taskbar customization and live resource monitoring tool. It combines a native settings-style interface with taskbar transparency, embedded monitoring, and configurable text and system icon colors.

## Screenshots

Actual application pages. Click an image to view it at full size.

| Overview · Light | Overview · Dark |
| --- | --- |
| ![Light overview](assets/screenshots/en-US/overview-light.png) | ![Dark overview](assets/screenshots/en-US/overview-dark.png) |

| Resource monitor | Text & icons |
| --- | --- |
| ![Resource monitor](assets/screenshots/en-US/sensors.png) | ![Text and icon settings](assets/screenshots/en-US/colors.png) |

## Features

- **Taskbar appearance:** follow Windows, use a transparent background, or apply a tint across multiple taskbars.
- **Live monitoring:** CPU, GPU, memory, disk and network usage, plus temperatures, clocks and fan speeds exposed by your hardware.
- **Embedded display:** place up to 8 metrics in the taskbar using a single line, two columns or two rows, with automatic width and click-through enabled by default.
- **Physical network uplink:** select the default physical route and exclude virtual, tunnel and loopback adapters.
- **Fonts and colors:** per-metric colors, font selection, size, bold text, random colors and four color presets.
- **System icon colors:** customize time, date, tray glyphs, network, volume, input method and battery indicators.
- **Live preview:** preview the taskbar from settings, with collapsible navigation, responsive layouts and transitions.
- **Languages:** follow the Windows display language by default, or choose English or Simplified Chinese in General settings.

## Download and install

1. Open [Releases](https://github.com/Adoubf/ClariBar/releases) and download `ClariBar-Setup-*-x64.exe`.
2. Run the installer and choose where to store the application and runtime data.
3. Start ClariBar and configure Taskbar and Resource monitor.

Requires **Windows 11 x64**. The installer includes the .NET and Windows App SDK runtimes; development tools are not needed to run the app.

Some CPU and motherboard temperatures and fan readings require hardware authorization in the app. The installer can optionally install the official PawnIO component. Upgrades preserve existing settings, and uninstalling preserves personal runtime data.

Current builds are unsigned installation previews. Each Release includes a SHA-256 checksum file; use PowerShell `Get-FileHash` to verify a download.

## Quick start

1. Choose a background style in **Taskbar** and enable embedded monitoring.
2. Select and pin up to 8 metrics in **Resource monitor**.
3. Adjust layout, colors, fonts and text size in **Text & icons**, using the live preview.
4. Select a language in **General**, or leave it set to **Follow system**.
5. Closing settings keeps the app in the system tray by default. Use **Restore all** or quit from the tray menu to restore the taskbar.

Shortcuts: `F1` for help, `Ctrl+B` to collapse navigation, and `Ctrl+F` to find sensors.

## Build from source

### Requirements

- Windows 11 x64
- Visual Studio or Build Tools with Desktop development with C++ and Windows SDK 10.0.26100 or later
- .NET SDK 10.0.403 or a newer SDK in the same major version
- CMake 3.25 or later, Ninja, and Git
- For installer packaging: Inno Setup 7.1 and the official [PawnIO installer](https://github.com/namazso/PawnIO.Setup/releases)

### Build and run

```powershell
git clone https://github.com/Adoubf/ClariBar.git
cd ClariBar
.\scripts\build.ps1 -Check
.\scripts\run.ps1 -Development
```

You can also open `ClariBar.slnx` in Visual Studio. By default, build outputs, caches and artifacts use the repository’s `.build/` directory. Set `CLARIBAR_ROOT` to use another development directory. Scripts support both portable tools inside that directory and a system development environment.

### Create an installer

```powershell
.\scripts\package.ps1 -PawnIOInstaller ".\PawnIO_setup.exe"
```

The script builds the application, publishes self-contained runtimes and creates the installer. It locates Inno Setup in the development directory or a standard installation; use `-InnoCompiler` to provide a different `ISCC.exe` path. Output appears under `artifacts/ClariBar/releases/` in the development directory.

## Project structure

| Directory | Contents |
| --- | --- |
| `src/ClariBar.App` | C# / WinUI 3 settings interface |
| `src/ClariBar.Core` | Settings, models, metric selection and language resources |
| `src/ClariBar.SensorBroker` | Independent hardware sensor process |
| `src/ClariBar.Native` | Native C++ windows, system metrics and taskbar bridge |
| `src/ClariBar.ExplorerTap` | C++ / WinRT taskbar integration |
| `assets` | Branding and application screenshots |
| `installer` | Setup wizard and bootstrap |
| `scripts` | Build, packaging and verification scripts |
| `tests` | Core and taskbar integration checks |
| `licenses` | Third-party licenses |

## Compatibility

- Taskbar integration depends on Windows 11 internal controls. Windows updates and other taskbar customization tools can affect compatibility.
- Sensor availability depends on hardware, drivers and access permissions. Missing readings are never substituted with data from another sensor.
- System colors apply to supported text, glyphs and vector icons. Third-party color tray bitmaps are not recolored.
- Hardware monitoring is read-only; ClariBar does not control fan speeds, voltages or power.

## Feedback and contributions

Use the [bug report form](https://github.com/Adoubf/ClariBar/issues/new?template=01-bug-report.yml) or [feature request form](https://github.com/Adoubf/ClariBar/issues/new?template=03-feature-request.yml), or contribute a pull request. English and Chinese Issue Forms are available.

Bug reports require the ClariBar version, Windows version and build, affected feature, reproduction steps, expected behavior and actual behavior. Hardware details, display settings, screenshots and logs are optional. Remove personal paths, account information and tokens before sharing attachments.

## Author and support

**Author:** [Haoyue](https://github.com/Adoubf)

**Email:** [mosaryalex@gmail.com](mailto:mosaryalex@gmail.com)

Thank you to everyone who uses, reports issues or contributes. Contact the author by email about sponsorship. Include a publicly displayable name if you would like an acknowledgement on the app’s About page.

## License

Original ClariBar code and artwork use the [MIT License](LICENSE). Third-party components retain their respective licenses; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
