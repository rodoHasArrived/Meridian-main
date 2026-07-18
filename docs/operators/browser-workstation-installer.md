# Browser Workstation Installer (Canonical)

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-07-13

This is the canonical operator entry for browser workstation installation and validation.

## Scope

- Local installer/deploy path for browser workstation surfaces.
- Validation posture for browser-run support workflows.
- Route validation and fallback to support evidence packets.

## Deployment Sequence

For end users, the supported release artifact is the production-signed
`Meridian-Setup.exe`. It detects x64 or ARM64, installs the self-contained local host,
browser assets, desktop workstation, and launcher, creates one Start Menu entry, and
launches browser-first setup. It requires no PowerShell, SDK, Node, Git, certificate
installation, or user-selected port.

The launcher creates a random loopback port and a one-use account-bootstrap token. The
token remains in the URL fragment until the setup page posts it to the loopback-only
bootstrap endpoint and is invalidated when the first local administrator is created.
Application data and credentials remain outside the installation directory.

Developer and operator scripts under `build/scripts/install/` are release-pipeline
machinery, not end-user instructions. Release packaging uses:

```powershell
pwsh ./build/scripts/install/build-consumer-setup.ps1
```

Tag builds sign and publish `Meridian-Setup.exe` through the protected Desktop Installer
Release workflow. Workflow changes require explicit human governance review.

## Canonical Validation

- Start command and launch posture remain defined in this section's parent operator documentation.
- Validate clean install, repair, uninstall, first-account creation, offline sample mode,
  x64, and ARM64 in clean Windows virtual machines before publishing a production tag.
- For support artifacts, include API readiness and operator inbox verification before handoff:
  - `GET /api/workstation/operator/inbox`
  - `GET /api/workstation/trading/readiness`

## Legacy Migration

- Source content: [archive/docs/operations/web-workstation-installer.md](../../archive/docs/operations/web-workstation-installer.md)
- Archive copy: [archive/docs/operations/web-workstation-installer.md](../../archive/docs/operations/web-workstation-installer.md)

## Related operator pages

- [Operator Preflight Checklist](./preflight-checklist.md)
- [Workstation launch and commands](./README.md)
