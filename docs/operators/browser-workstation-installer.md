# Browser Workstation Installer (Canonical)

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-05-31

This is the canonical operator entry for browser workstation installation and validation.

## Scope

- Local installer/deploy path for browser workstation surfaces.
- Validation posture for browser-run support workflows.
- Route validation and fallback to support evidence packets.

## Deployment Sequence

1. Build workstation artifacts from the supported command surface.
2. Install or refresh local workstation package path.
3. Validate launch path and API endpoint connectivity.
4. Confirm asset hash/manifest sanity for the installed package.
5. Capture evidence snapshot for operator handoff.

## Canonical Validation

- Start command and launch posture remain defined in this section's parent operator documentation.
- For support artifacts, include API readiness and operator inbox verification before handoff:
  - `GET /api/workstation/operator/inbox`
  - `GET /api/workstation/trading/readiness`

## Legacy Migration

- Source content: [archive/docs/operations/web-workstation-installer.md](../../archive/docs/operations/web-workstation-installer.md)
- Archive copy: [archive/docs/operations/web-workstation-installer.md](../../archive/docs/operations/web-workstation-installer.md)

## Related operator pages

- [Operator Preflight Checklist](./preflight-checklist.md)
- [Workstation launch and commands](./README.md)
