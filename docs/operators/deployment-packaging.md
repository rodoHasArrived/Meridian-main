# Deployment And Packaging Operations (Canonical)

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-07-19

This is the canonical operator entry for packaging and distribution posture for Meridian desktop and workstation artifacts.

## Scope

- Package generation and signing posture for desktop operator artifacts.
- Installer/surface validation before promotion or support handoff.
- Artifact evidence requirements for release and support evidence packets.

## Canonical Flow

1. Verify host/build command set used in the environment is current.
2. Build and validate package contents and manifest metadata against release profile.
3. Apply signing in the supported deployment path.
4. Perform install/run smoke checks on a clean validation host.
5. Capture artifact evidence and link to the support packet.

Packaging and certification are separate gates. `-SkipInstall` is valid only for producing a package;
it is never clean-machine certification. Release tags must also complete the installed lifecycle jobs
on native x64 and ARM64 runners.

## Required Release Evidence

The `Desktop Installer Release` workflow produces and retains:

- publisher-signed MSIX and consumer setup artifacts;
- `SHA256SUMS`, SPDX SBOMs, and GitHub/Sigstore artifact attestations;
- current NuGet/npm scan evidence from `Production Certification`;
- x64 and ARM64 receipts proving N-1 install and launch, update to current, repair and relaunch,
  rollback to N-1 and relaunch, and final uninstall; and
- the release evidence manifest tied to the workflow run and source commit.

The ARM64 job intentionally targets a native self-hosted `Windows`/`ARM64` runner. Emulation or an
x64 `-SkipInstall` build cannot satisfy this gate. A tag is not releasable while either architecture's
installed lifecycle job is queued, failed, or missing.

The `Publish Smoke` workflow's `web-workstation`/`win-x64` lane copies the just-published host into an
isolated installer root, starts that exact artifact through the lifecycle supervisor with required
authentication and a dedicated PostgreSQL payload, then fetches `/startupz`, `/healthz`,
`/workstation/`, and the first referenced JS/CSS asset.

## Current Evidence Surfaces

- `docs/reference/provider-integration-status.md` for provider-linked deployment dependencies.
- `docs/reference/provider-validation-evidence-schema.md` for packet evidence fields.
- `docs/reference/provider-validation-matrix.md` for readiness gates.
- `.github/workflows/production-certification.yml` for integration, coverage, dependency, recovery,
  and same-commit documentation evidence.
- `.github/workflows/desktop-installer-packaging.yml` for signed artifacts, SBOM/checksum/provenance,
  and clean-machine install lifecycle evidence.
- `.github/workflows/publish-smoke.yml` for actual published-host startup and browser asset proof.

## Deployment Entry Points

- Browser deployment posture: [Browser Workstation Installer](./browser-workstation-installer.md)
- WPF and desktop packaging: [README operator launch and procedures](./README.md)
- Legacy deployment guide archived at: [archive/docs/operations/msix-packaging.md](../../archive/docs/operations/msix-packaging.md)

## Legacy Migration

- Source content: [archive/docs/operations/msix-packaging.md](../../archive/docs/operations/msix-packaging.md)
- Archive copy: [archive/docs/operations/msix-packaging.md](../../archive/docs/operations/msix-packaging.md)
