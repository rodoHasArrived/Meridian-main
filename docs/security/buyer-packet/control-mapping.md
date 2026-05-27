# Meridian Buyer Security Packet — SOC 2 Control Mapping

- **Document Owner:** Security & Compliance
- **Version:** 2026.05.27.1
- **Last Reviewed:** 2026-05-27
- **Next Review Due:** 2026-08-31
- **Classification:** Buyer Diligence / Controlled Distribution

## Mapping Notes
- This is a buyer-facing summary map, not a formal attestation report.
- Criteria align to common SOC 2 Trust Services Criteria families (CC-series and related availability/confidentiality themes).
- Evidence artifacts reference Meridian docs, scripts, and commandable workflows suitable for diligence walkthroughs.

| SOC 2 Criterion (Summary) | Meridian Control Activity | Evidence Artifact Examples |
|---|---|---|
| CC1/CC2 Control environment & governance | Security ownership, documented operational guidance, route/workflow governance references | `docs/security/README.md`, `docs/plans/current-direction-and-status.md`, buyer packet `document-index.md` |
| CC3 Risk assessment | Threat modeling and current-state risk documentation | `docs/security/threat-model-current-state.md`, buyer packet `threat-model-summary.md` |
| CC4 Monitoring activities | Diagnostics commands, health checks, operator readiness/inbox visibility | CLI diagnostics help/usage docs, paper-trading readiness probes, status dashboards |
| CC5 Control activities (change management) | PR-gated changes with focused validation and pre-PR checks | Build/test command references, CI logs, targeted test artifacts |
| CC6 Logical access controls | Role-oriented workstation surfaces and controlled command execution | Access workflow documentation, operator runbooks, environment configs |
| CC7 System operations/security events | Incident triage/recovery scripts and evidence-generation workflows | Incident notes, provider validation outputs, replay verification artifacts |
| CC8 Change management | Versioned docs/scripts with review history and retained artifacts | Git history, release notes, quarterly packet refresh checklist completion |
| CC9 Risk mitigation (vendor/dependency) | Provider calibration, dependency scans, vulnerability triage/remediation | `docs/security/known-vulnerabilities.md`, remediation notes, provider calibration outputs |
| A1 Availability | Health endpoints, replay recoverability, backup/package validation workflows | `/healthz` checks, replay endpoint evidence, package validation outputs |
| C1 Confidentiality (as applicable) | Controlled distribution of sensitive ops/security docs and least-privilege operations | Buyer packet classification tags, access-control procedures, audit evidence |

## Evidence Packet Maintenance Expectations
- Every mapped row must have at least one currently discoverable artifact.
- Stale or deprecated artifact links must be replaced during quarterly refresh.
- Material control changes require map updates in the same change window.

## Freshness and Quarterly Refresh Checklist
- [ ] Validate each SOC2 row against at least one current evidence artifact.
- [ ] Replace stale evidence paths and mark deprecated references.
- [ ] Reconfirm criterion language and control descriptions with security owner.
- [ ] Update metadata and revision tracking in `document-index.md`.
