# Third-party notices

ClariBar's original source code and brand artwork are copyright (c) 2026 Haoyue and licensed under the [MIT License](LICENSE). Third-party components retain their own licenses.

- **LibreHardwareMonitorLib 0.9.6** — [Source](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor), Mozilla Public License 2.0. Used without source modifications in the independent SensorBroker process. The original [license](licenses/LibreHardwareMonitor-LICENSE.txt) and [third-party notices](licenses/LibreHardwareMonitor-THIRD-PARTY.txt) are included in this repository and the installer.
- **Windows App SDK / WinUI** — [Windows App SDK](https://github.com/microsoft/WindowsAppSDK), [WinUI](https://github.com/microsoft/microsoft-ui-xaml). Package license terms and associated third-party notices apply.
- **SkiaSharp 3.119.2 / Skia** — [Source](https://github.com/mono/SkiaSharp). Used for image decoding, alpha composition, antialiased annotations and image effects. The [MIT license](licenses/SkiaSharp-LICENSE.txt) and [native Skia third-party notices](licenses/Skia-THIRD-PARTY-NOTICES.txt) are included.
- **WinUIEx** — [Source](https://github.com/dotMorten/WinUIEx). The native transparent-window background follows its DWM configuration pattern. The [MIT license](licenses/WinUIEx-LICENSE.txt) is included; the WinUIEx package is not required at runtime.
- **.NET 10** — [Source](https://github.com/dotnet/runtime), MIT and third-party licenses. Self-contained distributions include the runtime license and notices.
- **PawnIO** — [Source and license](https://github.com/namazso/PawnIO). The installer bundles the unmodified, digitally signed [PawnIO Setup 2.2.0](https://github.com/namazso/PawnIO.Setup/releases/tag/2.2.0) as an optional prerequisite. Driver, user-library, and signed-module license terms apply.
- **Inno Setup 7.1.0** — [Source and license](https://github.com/jrsoftware/issrc). Its license is included in the installer distribution.

The native host and Explorer adapter were written for ClariBar. They use Windows SDK and C++/WinRT headers under Microsoft's license terms. The Windows.UI.Xaml runtime is supplied by Windows.

API references:

- [InitializeXamlDiagnosticsEx](https://learn.microsoft.com/en-us/windows/win32/api/xamlom/nf-xamlom-initializexamldiagnosticsex)
- [IXamlDiagnostics](https://learn.microsoft.com/en-us/windows/win32/api/xamlom/nn-xamlom-ixamldiagnostics)
- [IVisualTreeService3](https://learn.microsoft.com/en-us/windows/win32/api/xamlom/nn-xamlom-ivisualtreeservice3)

Screenshot translation:

- **MyMemory** — the online translation service uses its public [GET API](https://mymemory.translated.net/doc/spec.php) under the provider's [terms](https://mymemory.translated.net/doc/tos.php) and [usage limits](https://mymemory.translated.net/doc/usagelimits.php). Only recognized text is queried; ClariBar does not submit translation-memory contributions.
- **Optional offline runtime** — downloaded only after the user enables offline translation. [CPython 3.12.10](https://www.python.org/downloads/release/python-31210/) (PSF license), [CTranslate2 4.6.0](https://github.com/OpenNMT/CTranslate2) (MIT), [SentencePiece 0.2.1](https://github.com/google/sentencepiece) (Apache 2.0), NumPy 2.2.6 (BSD and bundled notices), and PyYAML 6.0.2 (MIT). Their archives retain their license files. These components and model weights are not bundled in the default installer.
- **Optional Chinese/English models** — unmodified Argos Translate 1.9 model packages from the [official package index](https://github.com/argosopentech/argospm-index). Package metadata, notices and license files are retained on installation. Pinned SHA-256 values and source URLs are in `src/ClariBar.App/Translation/offline-manifest.json`.
