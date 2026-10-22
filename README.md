# TempGuard

TempGuard is a lightweight Windows system-tray utility that shows the hottest current CPU or GPU temperature. The tray icon changes from green to orange to red so an overheating system is visible at a glance.

## Download and run

TempGuard currently supports 64-bit Windows 10 and Windows 11 with .NET Framework 4.8.

1. Download `TempGuard-1.0.0-win-x64.zip` from the project release.
2. Verify its SHA-256 value against the accompanying `.sha256` file.
3. Extract the whole ZIP to a folder you control; do not run the EXE from inside the ZIP.
4. Open `TempGuard.exe`.

Closing the window keeps monitoring in the tray. Right-click the tray icon and choose **Exit** to stop it.

The application is not yet code-signed, so Windows SmartScreen may show an unknown-publisher warning. Only download a release from this project's official repository and verify its published SHA-256 value.

## If no temperature appears

Connecting a charger only changes the power status; it does not make Windows expose a temperature reading.

- **Sensor driver required:** choose **Install sensor support**. After confirmation, TempGuard downloads the official PawnIO 2.1 installer from its GitHub release, verifies the pinned SHA-256 hash, and asks for administrator approval before running it.
- **Sensor unavailable:** choose **Restart as administrator**. Some laptop firmware may still not expose a supported sensor.

Driver installation is optional. TempGuard never downloads or installs it without explicit confirmation.

## Color guide

- **Green (below 75°C by default):** temperature is stable.
- **Orange (75–87°C):** reduce heavy CPU/GPU work and improve airflow.
- **Red (88°C or higher):** stop heavy work; if charging, unplug it when safe and let the laptop cool.
- **Gray:** no trustworthy sensor reading. Gray never means the laptop is safe.

The orange and red thresholds are configurable. The hottest valid CPU/GPU sensor controls the color so an average cannot hide a hotspot.

## Features

- Live temperature and thermal color in the Windows tray
- Detail window listing every detected CPU/GPU temperature sensor
- Actionable notifications when sensor availability or thermal state changes
- Two-degree threshold hysteresis to prevent notification spam near a boundary
- Charging-state guidance
- First-run prompt offering automatic start at Windows sign-in
- Per-user auto-start without an elevated scheduled task or UAC bypass
- No telemetry and no background network requests

## Important safety note

TempGuard is an advisory monitor, not a hardware safety controller. It cannot universally stop charging, change fan curves, or guarantee shutdown prevention. Keep vents clear, use the laptop on a hard surface, and have persistent overheating inspected.

## Build from source

Requirements: 64-bit Windows 10/11, Windows PowerShell 5.1, and .NET Framework 4.8.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1
```

The script restores the pinned LibreHardwareMonitorLib 0.9.6 package and dependencies from NuGet. It creates:

- `build\TempGuard-win-x64\` — unpacked application
- `artifacts\TempGuard-1.0.0-win-x64.zip` — distributable package
- `artifacts\TempGuard-1.0.0-win-x64.zip.sha256` — package checksum

Generated dependencies, binaries, and release archives are ignored by Git. Releases should be produced by the included GitHub Actions workflow so the source and build log are public.

## Privacy and security

Temperature data stays on the computer. The only network request made by the running app occurs after the user explicitly chooses to install sensor support. See [SECURITY.md](SECURITY.md) for the trust model and reporting instructions.

## License

TempGuard source code is available under the MIT License. Third-party components keep their own licenses; see [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).
