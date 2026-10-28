# Third-party dependencies

ClariBar's original source code and brand artwork are copyright (c) 2026 Haoyue and licensed under the MIT License in LICENSE. This does not change the licenses of third-party packages, bundled runtimes, or reference material described below.

ClariBar uses the following independently maintained components. Their binaries and original package metadata/licenses remain in the development cache. This file does not replace their licenses.

- LibreHardwareMonitorLib 0.9.6: https://github.com/LibreHardwareMonitor/LibreHardwareMonitor — Mozilla Public License 2.0; includes dependencies with separate notices. Used without source modifications in the independent SensorBroker process. Source and third-party notices: https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/v0.9.6/THIRD-PARTY-NOTICES.txt .
- Microsoft Windows App SDK / WinUI: https://github.com/microsoft/WindowsAppSDK and https://github.com/microsoft/microsoft-ui-xaml — package license terms and associated third-party notices apply.
- .NET: https://github.com/dotnet/runtime — MIT and third-party licenses; the installer bundles the .NET 10 x64 runtime with both the UI and independent broker, including its runtime notices.
- PawnIO: https://github.com/namazso/PawnIO — driver, user library, and signed modules have their own license terms. The installer includes the unmodified, digitally signed PawnIO Setup 2.2.0 from https://github.com/namazso/PawnIO.Setup/releases/tag/2.2.0 as an optional, separate prerequisite. It is skipped when PawnIO is already installed; ClariBar does not replace or uninstall an existing shared driver. Driver source/license: https://github.com/namazso/PawnIO .

The C++ native host and Explorer XAML diagnostics adapter were written for this project. The adapter uses the Windows SDK and C++/WinRT projection headers under their Microsoft license terms. The system Windows.UI.Xaml runtime is supplied by Windows, not bundled. No CTM binaries/assets, TranslucentTB source, or Windhawk source are included. The Windows Settings reference screenshot is a user-supplied design reference in docs, not shipped UI artwork.

Implementation references (API and compatibility research; no source incorporated):

- Microsoft InitializeXamlDiagnosticsEx: https://learn.microsoft.com/en-us/windows/win32/api/xamlom/nf-xamlom-initializexamldiagnosticsex
- Microsoft IXamlDiagnostics: https://learn.microsoft.com/en-us/windows/win32/api/xamlom/nn-xamlom-ixamldiagnostics
- Microsoft IVisualTreeService3: https://learn.microsoft.com/en-us/windows/win32/api/xamlom/nn-xamlom-ivisualtreeservice3


The installer is compiled with Inno Setup 7.1.0 (https://github.com/jrsoftware/issrc). Its original license terms apply. The small C++ setup bootstrap and the ClariBar brand artwork were authored for this project.
