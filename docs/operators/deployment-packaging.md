# Deployment And Packaging Operations (Canonical)

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-11-14

This is the canonical operator entry for packaging and distribution posture for Meridian desktop and workstation artifacts.

## Scope

- Package generation and signing posture for desktop operator artifacts.
- Installer/surface validation before promotion or support handoff.
- Artifact evidence requirements for release and support evidence packets.

## Canonical Flow

This is release-maintainer guidance for Windows artifacts. End-user installation starts at
[Browser Workstation Installer](browser-workstation-installer.md). Run build scripts in PowerShell 7
from the repository root with the pinned .NET SDK, Node/npm, Windows SDK packaging/signing tools,
and the PostgreSQL payload for every requested runtime. Use the selected workflow's protected
signing environment; credentials are not command examples or repository inputs.

1. Verify host/build command set used in the environment is current.
2. Build and validate package contents and manifest metadata against release profile.
3. Apply signing in the supported deployment path.
4. Perform install/run smoke checks on a clean validation host.
5. Capture artifact evidence and link to the support packet.

Packaging and certification are separate gates. `-SkipInstall` is valid only for producing a package;
it is never clean-machine certification. Release tags must also complete the installed lifecycle jobs
on native runners for every supported architecture. [ADR-019](../adr/019-production-support-matrix-and-deployment-posture.md#first-release-architecture-scope-2026-11-14)
bounds v0.1.0 and its release candidates to Windows 11 x64; later versions retain native x64 and
ARM64 requirements. The version policy determines this exception, not a manual skip input.

## One-Commit Candidate Train

`Desktop Installer Release` is the release coordinator. It runs the reusable `Production
Certification` workflow (PostgreSQL integration, zero-skip TRX, dependency scans, encrypted recovery
drill and documentation evidence), `Publish Smoke` for `web-workstation`/`win-x64`, and signed
installer lifecycle certification against the event's immutable source commit. The recovery and
documentation jobs already belong to `Production Certification`; their receipts are part of the
same candidate packet.

The coordinator checks the reports and upstream artifacts against `PRD-000` and `PRD-013` through
`PRD-017` before emitting `release-candidate-evidence.json` and the completed evidence bundle.
Missing or failed evidence stops certification; a failed upstream job or checker produces no
certified candidate JSON or completed bundle upload. The original upstream receipts and readiness
summary remain the failure evidence. A green archive round trip cannot supply recovery acceptance,
and a green workflow cannot supply administrator confirmation of required-check activation.
All P0 rows in the [readiness tracker](../product/implementation-todo-list.md), including credential
ownership `PRD-002`, remain requirements for a supported release.

1. Merge the reviewed workflow changes through the protected-branch process and identify the
   exact `main` SHA with green required checks, including documentation. Do not move `rc.1`.
2. The signing owner provisions `MDC_SIGNING_CERT_PFX_BASE64` and `MDC_SIGNING_CERT_PASSWORD`
   in the protected `desktop-release-signing` environment. Use the publisher's valid code-signing
   certificate with a private key and matching password. Keep both values out of the repository,
   logs, workflow inputs and release packet. A generated test certificate is rehearsal evidence.
3. Run `Release Readiness` to verify the certificate without retaining its private key, then a
   signed manual `Desktop Installer Release` rehearsal at that SHA with
   `rehearsal_version=0.1.0-rc.2`. A manual run does not publish. Resolve each failed gate and
   retain the completed bundle once the checker passes, including the actual
   [recovery acceptance procedure](failover-and-recovery.md#complete-and-validate-the-recovery-evidence).
   For the waiting recovery-acceptance job, set the protected environment's non-secret
   `MDC_RECOVERY_COMPLETION_JSON` variable to the completed JSON for this run's exact drill,
   then approve the job. Approval alone is not acceptance. The independent validator must
   produce `objectiveStatus: proven` within the actual RPO/RTO budgets; a stale completion
   document or late acceptance fails and requires a fresh drill.
4. Record the administrator's required-check activation and operations-owner review. Recheck
   that the chosen SHA remains on `main` with green required checks. If source changes, repeat
   the train on the new SHA; do not combine reports from different commits.
5. Create `v0.1.0-rc.2` at that exact green SHA. The tag-push run must pass its own signed
   installed lifecycle and evidence gates before publication. Preserve its run URL, bundle,
   artifact digests and tag identity. A failed tag run is not a certified installation.

For the bounded first envelope, `0.1.0-rc.N` maps to MSIX identity `0.1.0.N` for
`N=1..65534`, and stable `0.1.0` maps to `0.1.0.65535`. Thus `rc.2` can upgrade to stable
without a package-version collision. Other 0.1.0 suffixes retain revision zero; versions outside
0.1.0 retain the existing version policy. Every publish still checks actual published package
identities for monotonic increase.

This procedure records the release path. It does not assert that signing secrets were provisioned,
that the candidate is green, or that operator acceptance has occurred.

## Required Release Evidence

The `Desktop Installer Release` workflow produces and retains:

- publisher-signed MSIX and consumer setup artifacts;
- `SHA256SUMS`, SPDX SBOMs, and GitHub/Sigstore artifact attestations;
- current NuGet/npm scan evidence from `Production Certification`;
- receipts for every supported architecture proving N-1 install and launch, update to current, repair and relaunch,
  rollback to N-1 and relaunch, and final uninstall; and
- the release evidence manifest tied to the workflow run and source commit.

For releases that support ARM64, its job targets native Windows ARM64 capacity (the hosted
`windows-11-arm` label). Emulation or an x64 `-SkipInstall` build cannot satisfy that gate. A tag
is not releasable while any supported architecture's installed lifecycle job is queued, failed,
or missing. The first-release N-1 exception records update and rollback as explicitly
`not-applicable` only after a successful repository lookup establishes that no published
predecessor exists; lookup failures do not establish this exception.

These are the production workflow's required evidence outputs, not an assertion that a particular
tag passed. Inspect the selected release commit and workflow results. Evaluation `eval-v*`
artifacts have a separate signing/support posture and do not satisfy these production gates.

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

## Expected result and failure handling

- A candidate has the expected architecture payloads, signature, checksum/SBOM/provenance, and
  installed lifecycle receipts for the same source commit. Archive those before rebuilding because
  packaging scripts recreate their output directories.
- On a missing payload, build/signing error, queued native runner, failed smoke, or missing receipt,
  keep the candidate unpromoted and correct the named input or failed workflow stage. Packaging
  success alone is insufficient when installed lifecycle evidence is absent.
- When an installed candidate fails, collect [startup/lifecycle diagnostics](operator-runbook.md#diagnostics-bundle)
  and follow the release's tested repair/rollback procedure. Preserve the database/data-root recovery
  unit described in [Failover and Recovery](failover-and-recovery.md).

## Deployment Entry Points

- Browser deployment posture: [Browser Workstation Installer](./browser-workstation-installer.md)
- WPF and desktop packaging: [README operator launch and procedures](./README.md)
- Legacy deployment guide archived at: [archive/docs/operations/msix-packaging.md](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/operations/msix-packaging.md)

## Legacy Migration

- Source content: [archive/docs/operations/msix-packaging.md](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/operations/msix-packaging.md)
- Archive copy: [archive/docs/operations/msix-packaging.md](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/operations/msix-packaging.md)
