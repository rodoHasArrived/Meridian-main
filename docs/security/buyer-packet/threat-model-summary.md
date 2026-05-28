# Meridian Buyer Security Packet — Threat Model Summary

- **Document Owner:** Application Security
- **Version:** 2026.05.27.1
- **Last Reviewed:** 2026-05-27
- **Next Review Due:** 2026-08-31
- **Classification:** Buyer Diligence / Controlled Distribution

## Scope and Method
This summary captures Meridian's current practical threat posture across data ingestion, operator workflows, and evidence/reporting surfaces. It reflects the actively maintained security references under `docs/security/` and current workflow architecture.

## Current Threat Posture (Condensed)

### 1) Upstream Data/Provider Threats
- **Threats:** Malformed payloads, stale feeds, provider outages, degraded trust in market/reference inputs.
- **Impact:** Incorrect readiness posture, polluted downstream analysis, false confidence in execution pathways.
- **Current Mitigations:** Provider validation runs, degradation calibration workflows, route-scoped readiness checks, and operator-visible posture signals.

### 2) Unauthorized or Unsafe Operator Actions
- **Threats:** Excessive permissions, accidental misuse of high-impact commands, weak separation of duties.
- **Impact:** Data loss, incorrect account actions, untracked operational changes.
- **Current Mitigations:** Command discoverability with explicit flags, surface-specific workflows (Trading/Portfolio/Accounting/Reporting/Strategy/Data/Settings), and evidence-producing scripted runbooks.

### 3) Data Integrity and Replay-Evidence Drift
- **Threats:** Corrupt write-ahead logs, stale replay verification, package/statement ingest mismatch.
- **Impact:** Inability to reconstruct events, broken readiness assertions, weakened audit trail.
- **Current Mitigations:** WAL repair/validation workflows, replay endpoints and parity packet generation paths, and explicit statement/package validation commands.

### 4) Build/Release Supply-Chain Risk
- **Threats:** Dependency vulnerabilities, insecure build automation, insufficient pre-release checks.
- **Impact:** Introduction of exploitable code paths or unstable deployments.
- **Current Mitigations:** Standardized build/test command lanes, pre-PR profiles, targeted test slices for changed routes/workspaces, and retained automation artifacts.

### 5) Incident Detection and Recovery Gaps
- **Threats:** Slow triage, incomplete evidence, inconsistent incident handling.
- **Impact:** Prolonged exposure and delayed containment/recovery.
- **Current Mitigations:** Diagnostics command surface, operator-inbox/readiness workflows, health endpoints, and documented operational workflows for repeatable recovery.

## Residual Risk Themes
- Dependence on external providers remains a structural risk; Meridian mitigates but cannot eliminate upstream compromise/outage risk.
- Human-in-the-loop operator workflows reduce blind automation risk but require ongoing runbook quality and training discipline.
- Evidence freshness is central: stale validation artifacts materially reduce assurance confidence.

## Planned Maturity Focus
- Tighten formalized threat-library mapping to every high-impact route/workflow.
- Expand continuous evidence freshness checks so stale packets are auto-flagged.
- Continue hardening provider calibration governance gates and incident response drill cadence.

## Freshness and Quarterly Refresh Checklist
- [ ] Reconcile this summary with latest `docs/security/threat-model-current-state.md` updates.
- [ ] Confirm all listed mitigations still map to active commands/scripts/tests.
- [ ] Add/remove residual risk themes based on current incidents and retrospectives.
- [ ] Update owner/version/last-reviewed metadata.
- [ ] Update revision row in `document-index.md`.
