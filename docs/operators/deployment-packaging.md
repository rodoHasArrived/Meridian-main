# Deployment And Packaging Operations (Canonical)

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-05-31

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

## Current Evidence Surfaces

- `docs/reference/provider-integration-status.md` for provider-linked deployment dependencies.
- `docs/reference/provider-validation-evidence-schema.md` for packet evidence fields.
- `docs/reference/provider-validation-matrix.md` for readiness gates.

## Deployment Entry Points

- Browser deployment posture: [Browser Workstation Installer](./browser-workstation-installer.md)
- WPF and desktop packaging: [README operator launch and procedures](./README.md)
- Legacy deployment guide archived at: [archive/docs/operations/msix-packaging.md](../../archive/docs/operations/msix-packaging.md)

## Legacy Migration

- Source content: [archive/docs/operations/msix-packaging.md](../../archive/docs/operations/msix-packaging.md)
- Archive copy: [archive/docs/operations/msix-packaging.md](../../archive/docs/operations/msix-packaging.md)
