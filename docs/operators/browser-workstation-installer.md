# Browser Workstation Installer (Canonical)

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-07-17

This is the canonical operator entry for browser workstation installation and validation.

## Scope

- Local installer/deploy path for browser workstation surfaces.
- Validation posture for browser-run support workflows.
- Route validation and fallback to support evidence packets.

## Deployment Sequence

For end users, the supported release artifact is the production-signed
`Meridian-Setup.exe`. No production (`v*`) release exists yet; until one does, the
download channel is the evaluation prerelease (`eval-v*`), whose
`Desktop Evaluation Prerelease` workflow attaches an **unsigned** x64
`Meridian-Setup.exe` with a `Meridian-Setup.exe.sha256` checksum alongside the
self-signed MSIX packages. It detects x64 or ARM64, installs the self-contained local host,
browser assets, desktop workstation, lifecycle supervisor, dedicated PostgreSQL runtime, and thin
launcher, creates one Start Menu entry, and launches browser-first setup. It requires no
PowerShell, SDK, Node, Git, certificate installation, database installation, or user-selected port.

The persistent per-user supervisor owns the dedicated database and host process identities,
creates a random loopback port, and creates a one-use account-bootstrap token. The
token remains in the URL fragment until the setup page posts it to the loopback-only
bootstrap endpoint and is invalidated when the first local administrator is created.
Application data and credentials remain outside the installation directory.

Closing the browser or WPF client does not stop the service. Repair and uninstall first request a
supervisor-managed cooperative shutdown and refuse to replace files when that bounded shutdown
fails. Uninstall removes application binaries but preserves data unless a separately governed data
removal flow is used.

Developer and operator scripts under `build/scripts/install/` are release-pipeline
machinery, not end-user instructions. Release packaging uses:

```powershell
pwsh ./build/scripts/install/build-consumer-setup.ps1 `
  -PostgreSqlPayloadRoot D:\release-inputs\postgresql
```

The payload root must contain runtime-specific `win-x64` and `win-arm64` folders, each with
`bin\postgres.exe`, `bin\pg_ctl.exe`, and `bin\initdb.exe`. The release pipeline may supply the same
path through `MDC_POSTGRES_PAYLOAD_ROOT`.

Tag builds sign and publish `Meridian-Setup.exe` through the protected Desktop Installer
Release workflow. Workflow changes require explicit human governance review.

## Canonical Validation

- Start command and launch posture remain defined in this section's parent operator documentation.
- Validate clean install, repair, uninstall, first-account creation, offline sample mode,
  x64, and ARM64 in clean Windows virtual machines before publishing a production tag.
- Validate supervisor `preflight`, `status`, `restart`, and `stop`, plus a generated session receipt
  and a clean dedicated-database exit.
- For support artifacts, include API readiness and operator inbox verification before handoff:
  - `GET /api/workstation/operator/inbox`
  - `GET /api/workstation/trading/readiness`

## Legacy Migration

- Source content: [archive/docs/operations/web-workstation-installer.md](../../archive/docs/operations/web-workstation-installer.md)
- Archive copy: [archive/docs/operations/web-workstation-installer.md](../../archive/docs/operations/web-workstation-installer.md)

## Related operator pages

- [Operator Preflight Checklist](./preflight-checklist.md)
- [Workstation launch and commands](./README.md)
- [Lifecycle Control Plane Reference](../reference/lifecycle-control-plane.md)
