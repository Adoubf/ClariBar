# Third-party notices

ClariBar's original source code and brand artwork are copyright (c) 2026 Haoyue and licensed under the [MIT License](LICENSE). Third-party components retain their own licenses.

- **LibreHardwareMonitorLib 0.9.6** — [Source](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor), Mozilla Public License 2.0. Used without source modifications in the independent SensorBroker process. The original [license](licenses/LibreHardwareMonitor-LICENSE.txt) and [third-party notices](licenses/LibreHardwareMonitor-THIRD-PARTY.txt) are included in this repository and the installer.
- **Windows App SDK / WinUI** — [Windows App SDK](https://github.com/microsoft/WindowsAppSDK), [WinUI](https://github.com/microsoft/microsoft-ui-xaml). Package license terms and associated third-party notices apply.
- **.NET 10** — [Source](https://github.com/dotnet/runtime), MIT and third-party licenses. Self-contained distributions include the runtime license and notices.
- **PawnIO** — [Source and license](https://github.com/namazso/PawnIO). The installer bundles the unmodified, digitally signed [PawnIO Setup 2.2.0](https://github.com/namazso/PawnIO.Setup/releases/tag/2.2.0) as an optional prerequisite. Driver, user-library, and signed-module license terms apply.
- **Inno Setup 7.1.0** — [Source and license](https://github.com/jrsoftware/issrc). Its license is included in the installer distribution.

The native host and Explorer adapter were written for ClariBar. They use Windows SDK and C++/WinRT headers under Microsoft's license terms. The Windows.UI.Xaml runtime is supplied by Windows.

API references:

- [InitializeXamlDiagnosticsEx](https://learn.microsoft.com/en-us/windows/win32/api/xamlom/nf-xamlom-initializexamldiagnosticsex)
- [IXamlDiagnostics](https://learn.microsoft.com/en-us/windows/win32/api/xamlom/nn-xamlom-ixamldiagnostics)
- [IVisualTreeService3](https://learn.microsoft.com/en-us/windows/win32/api/xamlom/nn-xamlom-ivisualtreeservice3)
