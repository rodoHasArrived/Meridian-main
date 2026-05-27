# MSIX Packaging & Signing (Windows Desktop)

This guide covers producing MSIX packages for the Windows Desktop app and signing them
for development or release distribution.

## CI Release Packaging

Tag pushes matching `v*` trigger `.github/workflows/desktop-installer-packaging.yml`, which:

- builds `win-x64` and `win-arm64` MSIX installers,
- uploads installer artifacts to the workflow run, and
- attaches installer packages to the corresponding GitHub Release.

For trusted release signing in CI, configure repository secrets:

- `MDC_SIGNING_CERT_PFX_BASE64` (base64-encoded PFX contents)
- `MDC_SIGNING_CERT_PASSWORD` (PFX password)

If those secrets are not configured, CI falls back to a temporary development certificate.
Installers signed with that fallback certificate are not trusted by default on end-user machines.
For production release tags, verify the workflow log shows `Release signing certificate configured.`
before distributing the package.

## Quick Start

The easiest way to build and install the Desktop App:

```powershell
# Build, trust certificate, and install in one command
.\build\scripts\install\install.ps1 -Mode Desktop

# Build for ARM64 devices (Surface Pro X, etc.)
.\build\scripts\install\install.ps1 -Mode Desktop -Architecture ARM64

# Auto-install .NET SDK 10.0 and other prerequisites
.\build\scripts\install\install.ps1 -Mode Desktop -AutoInstallPrereqs

# Build only (no installation)
.\build\scripts\install\install.ps1 -Mode Desktop -SkipInstall

# Lower-disk local publish (disables ReadyToRun)
.\build\scripts\install\install.ps1 -Mode Desktop -DisableReadyToRun

# Release-style local publish (enables ReadyToRun)
.\build\scripts\install\install.ps1 -Mode Desktop -EnableReadyToRun
```

## Build MSIX Packages

**PowerShell install script:**

```powershell
.\build\scripts\install\install.ps1 -Mode Desktop

# Lower-disk local publish
.\build\scripts\install\install.ps1 -Mode Desktop -DisableReadyToRun

# Release-style local publish
.\build\scripts\install\install.ps1 -Mode Desktop -EnableReadyToRun
```

The script outputs MSIX packages under:

```
dist\win-x64\msix\    (install script, x64)
dist\win-arm64\msix\  (install script, ARM64)
```

The WPF package uses `src/Meridian.Wpf/Package.appxmanifest` as its explicit package manifest.
That manifest declares Meridian as a full-trust desktop app and includes a `desktop7:Shortcut`
contract that creates `Meridian.lnk` on the current user's Desktop when the MSIX is installed.
The shortcut extension requires Windows 10 build 19645 or newer, so the package manifest sets the
desktop device-family minimum to `10.0.19645.0`.

After a successful install, the installer summary should point operators to the Start Menu entry,
the Desktop shortcut, and this guide at `docs/operations/msix-packaging.md`.

## Installation Options

The install script supports several options for Desktop mode:

| Option | Description |
|--------|-------------|
| `-Architecture x64` | Build for x64 (default) |
| `-Architecture ARM64` | Build for ARM64 (Surface Pro X, etc.) |
| `-AutoInstallPrereqs` | Auto-install missing prerequisites via winget |
| `-SkipInstall` | Build only, do not install the MSIX |
| `-DisableReadyToRun` | Reduce disk usage during local publish by disabling ReadyToRun |
| `-EnableReadyToRun` | Enable ReadyToRun for release-style local packaging |
| `-NoTrustCert` | Skip the certificate trust prompt |
| `-DetailedOutput` | Show detailed build output |

## Uninstalling

To uninstall the Desktop App:

```powershell
.\build\scripts\install\install.ps1 -Mode UninstallDesktop
```

Or use the interactive menu:

```powershell
.\build\scripts\install\install.ps1
# Select option 7) Uninstall Desktop App
```

## Optional AppInstaller File

To generate an AppInstaller alongside the MSIX package, provide the AppInstaller URI:

```powershell
$env:MDC_APPINSTALLER_URI = "https://example.com/meridian/Meridian.appinstaller"
```

The current repository does not define a `make desktop-publish` target; use the PowerShell install
script for desktop package builds unless that target is restored.

## Signing for Development (Self-Signed)

MSIX packages must be signed. For local development you can use a self-signed certificate.
Create one and export a PFX:

```powershell
$cert = New-SelfSignedCertificate `
  -Type Custom `
  -Subject "CN=Meridian" `
  -KeyUsage DigitalSignature `
  -FriendlyName "Meridian Dev Certificate" `
  -CertStoreLocation "Cert:\CurrentUser\My"

$password = ConvertTo-SecureString -String "dev-password" -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath "$env:USERPROFILE\Documents\Meridian.Dev.pfx" -Password $password
```

Trust the certificate for local installs:

```powershell
Import-Certificate -FilePath "$env:USERPROFILE\Documents\Meridian.Dev.pfx" -CertStoreLocation "Cert:\CurrentUser\TrustedPeople"
```

Then pass the certificate to the build:

```powershell
$env:MDC_SIGNING_CERT_PFX = "$env:USERPROFILE\Documents\Meridian.Dev.pfx"
$env:MDC_SIGNING_CERT_PASSWORD = "dev-password"
```

## Signing for Release (Code-Signing Certificate)

For production distribution, use a trusted code-signing certificate from a CA:

1. Purchase a code-signing certificate (standard or EV).
2. Ensure the **Publisher** in `Package.appxmanifest` and
   `Meridian.Wpf.csproj` matches the certificate subject exactly.
3. Provide the PFX path and password via environment variables:

```powershell
$env:MDC_SIGNING_CERT_PFX = "C:\secure\Meridian.Release.pfx"
$env:MDC_SIGNING_CERT_PASSWORD = "<secure-password>"
```

The install script reads the same signing environment variables when packaging.

## Notes

- Keep the package identity values in the project file and manifest in sync.
- Keep the Desktop shortcut target, icon path, and display name in `Package.appxmanifest`; validate
  changes with `python -m unittest tests/scripts/test_wpf_msix_manifest.py`.
- AppInstaller generation is optional; omit the URI to skip it.
- Local desktop packaging defaults to `PublishReadyToRun=false` to reduce disk usage.
- CI and release packaging should pass `PublishReadyToRun=true` explicitly.
