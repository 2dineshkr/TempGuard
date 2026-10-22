# Security

## Supported version

Only the latest published TempGuard release receives security fixes.

## Reporting a vulnerability

Please use the repository's private **Report a vulnerability** form under the GitHub Security tab. Do not include exploit details in a public issue.

## Trust model

- TempGuard runs as the signed-in user by default.
- Enabling **Start with Windows** writes a per-user `HKCU\...\Run` entry. It does not create an elevated scheduled task.
- Administrator access occurs only after the user explicitly chooses **Restart as administrator** or approves the optional sensor-driver installer.
- The CPU Load tab reads local process CPU and memory counters. It sends no process information anywhere.
- Ending a process is always user initiated and requires confirmation. TempGuard blocks its own process, other Windows sessions, and a set of Windows-critical processes; Windows access controls remain authoritative.
- PawnIO is not bundled. On explicit request, TempGuard downloads the exact official PawnIO Setup 2.1.0 binary over HTTPS and accepts it only if its SHA-256 is `A3A46226C5E2824F4CDD42BE0EECBABFC672C86F7889710F5AB1E6AD385B47A0`.
- TempGuard has no telemetry, update service, or background network activity.

Published TempGuard binaries are currently unsigned. Verify release ZIP checksums against the `.sha256` file produced by the public build workflow.
