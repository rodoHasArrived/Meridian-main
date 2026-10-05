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

For end users, production releases use a signed `Meridian-Setup.exe`. Check the selected
[release's label, architecture, and attached evidence](https://github.com/rodoHasArrived/Meridian-main/releases)
before installing: the evaluation prerelease (`eval-v*`) is a different channel, whose
`Desktop Evaluation Prerelease` workflow attaches an **unsigned** x64
`Meridian-Setup.exe` with a `Meridian-Setup.exe.sha256` checksum alongside the
self-signed MSIX packages. The consumer setup supports x64; the separate desktop MSIX also
supports ARM64. The consumer setup installs the self-contained local host,
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

For an installed Windows workstation:

1. Compare the downloaded file's SHA-256 with the checksum attached to that same release and
   verify its stated signing posture. An unsigned evaluation artifact is not production certification.
2. Run the matching setup artifact as the intended local operator and complete browser-first
   account setup. Keep the supervisor-generated setup token out of screenshots and support packets.
3. Confirm the workstation opens at the generated loopback URL and sign in. Run
   [authenticated preflight](preflight-checklist.md#authenticated-evidence-collection) against that URL.
4. Retain the selected release/commit, checksum, architecture, and startup outcome receipt.

## Build context for release maintainers

Developer and operator scripts under `build/scripts/install/` are release-pipeline
machinery, not end-user instructions. After resolving the declared payload, release packaging uses:

```powershell
pwsh ./build/scripts/install/resolve-postgresql-payload.ps1
pwsh ./build/scripts/install/build-consumer-setup.ps1 `
  -PostgreSqlPayloadRoot artifacts/postgresql-payload `
  -Runtimes win-x64
```

Run these release-maintainer commands in PowerShell 7 from the repository root on the declared
Windows runner, with the pinned .NET SDK, Node/npm, and release Windows SDK tooling available.
The builder recreates `artifacts/consumer-setup`; retain earlier release evidence outside that
output before rebuilding.

### PostgreSQL payload declaration

[`build/config/postgresql-payload.json`](../../build/config/postgresql-payload.json) declares
PostgreSQL **17.11** for **win-x64**, with source kind `github-hosted-runner` and the explicit
source path `C:\Program Files\PostgreSQL\17` on Windows 2025. Its source reference is the
[immutable Windows 2025 runner manifest](https://github.com/actions/runner-images/blob/413dcb5e5b4c397e81a2520584e621beed02e963/images/windows/Windows2025-Readme.md#postgresql).
Human governance approval of the pull request accepts this declaration; the declaration does
not assert that an earlier payload approval or release certification exists.

Installer packaging, installed-startup smoke, and evaluation packaging use the shared
[`resolve-postgresql-payload.ps1`](../../build/scripts/install/resolve-postgresql-payload.ps1)
resolver. It validates the declared version and source, stages `bin`, `lib`, `share`, and the
declared `server_license.txt` / `commandlinetools_3rd_party_licenses.txt` distribution notices under `artifacts/postgresql-payload/win-x64`, and writes
`artifacts/postgresql-payload/win-x64-payload.json`. This receipt records the resolved version,
source, runner identity, per-file SHA-256 hashes, and a canonical payload-tree SHA-256 hash.
Release evidence embeds it in `postgresqlPayloads`.

The consumer builder validates the staged payload and receipt before running npm or dotnet.
An unavailable source, a mismatched version or payload, or an unsupported architecture stops
packaging. Resolution never falls back to another installed version or architecture. The
`MDC_POSTGRES_PAYLOAD_ROOT` alternative selects a staged payload root; it does not waive receipt
validation.

When changing the payload, update the declaration and reviewed source reference in a governance
pull request. Rerun the unavailable/mismatched-payload tests, installed-startup smoke, and the
existing clean-install and N-1 upgrade/rollback certification. Retain the new payload receipt
and release evidence from the reviewed commit; record first-release exceptions only through the
existing predecessor lookup rules.

Tag builds sign and publish `Meridian-Setup.exe` through the protected Desktop Installer
Release workflow. Workflow changes require explicit human governance review.

## Canonical Validation

- Start command and launch posture remain defined in this section's parent operator documentation.
- Validate consumer x64 clean install, repair, uninstall, first-account creation, offline sample
  mode, and the existing upgrade/rollback cases in clean Windows virtual machines before
  publishing a production tag. Validate the separate desktop MSIX on x64 and ARM64.
- Validate supervisor `preflight`, `status`, `restart`, and `stop`, plus a generated session receipt
  and a clean dedicated-database exit.
- For support artifacts, include API readiness and operator inbox verification before handoff:
  - `GET /api/workstation/operator/inbox`
  - `GET /api/workstation/trading/readiness`

## Expected result and recovery

The supervisor reaches `Ready` and accepting work before it opens the workstation; startup
receipts identify the outcome and log locations. If setup is blocked, retain its receipt and use
[Verified Outcome Recovery](verified-outcome-recovery.md). If the package is missing the required
architecture/database payload, select the correct artifact or repair the packaging input.
If repair/uninstall cannot stop the owned processes, resolve the supervisor's reported failure
before rerunning setup. Preserve application data and credentials during repair.

## Legacy Migration

- Source content: [archive/docs/operations/web-workstation-installer.md](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/operations/web-workstation-installer.md)
- Archive copy: [archive/docs/operations/web-workstation-installer.md](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/operations/web-workstation-installer.md)

## Related operator pages

- [Operator Preflight Checklist](./preflight-checklist.md)
- [Workstation launch and commands](./README.md)
- [Lifecycle Control Plane Reference](../reference/lifecycle-control-plane.md)
