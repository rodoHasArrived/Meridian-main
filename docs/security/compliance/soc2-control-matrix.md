# SOC 2 Control Matrix (Meridian)

**Last Updated:** 2026-05-27  
**Control Framework:** AICPA Trust Services Criteria (TSC)

This matrix maps relevant SOC 2 criteria families to implemented Meridian controls and evidence sources.

| TSC Family | Meridian control statement | Control owner | Evidence source(s) |
|---|---|---|---|
| CC1 (Control Environment) | Security, operations, and development responsibilities are documented and routed through runbooks, architecture docs, and ownership workflows. | Engineering Manager | `docs/architecture/module-map.md`; `docs/operators/README.md`; `docs/product/meridian-design-document.md` |
| CC2 (Communication & Information) | Security and operational changes are communicated through maintained docs, status dashboards, and evidence packets. | Program Manager | `docs/status/doc-health-dashboard.md`; `docs/status/evidence/wave2-cockpit-evidence-packet.md`; `docs/security/README.md` |
| CC3 (Risk Assessment) | Threat and abuse scenarios are documented and severity-calibrated with explicit assumptions and residual risk notes. | Security Lead | `docs/security/threat-model-current-state.md`; `docs/security/known-vulnerabilities.md` |
| CC4 (Monitoring Activities) | Operational health, provider posture, and validation gates are periodically reviewed with documented outputs. | Operations Lead | `docs/status/kernel-readiness-dashboard.md`; `docs/reference/provider-validation-matrix.md`; `docs/operators/failover-and-recovery.md` |
| CC5 (Control Activities) | Change workflows require build/test checks and focused validation for impacted areas before release/promotion. | Release Manager | `docs/engineering/README.md`; `docs/operators/README.md`; `docs/start/README.md` |
| CC6 (Logical & Physical Access) | Authentication and permission controls gate sensitive endpoints and credential workflows; role-based access is enforced on key mutation routes. | Security Lead | `docs/security/threat-model-current-state.md`; `docs/operators/provider-credentials.md`; `docs/operators/deployment-packaging.md` |
| CC7 (System Operations) | Platform operations follow preflight, deployment, reconciliation, and incident-oriented runbooks with clear operator steps. | Operations Lead | `docs/operators/preflight-checklist.md`; `docs/operators/deployment-packaging.md`; `docs/operators/reconciliation-operations.md` |
| CC8 (Change Management) | CI/CD and release automation include repeatable build/test workflows, diagnostics, and artifact traceability. | DevEx Lead | `docs/engineering/README.md`; `docs/operators/deployment-packaging.md`; `.github/workflows/security.yml` |
| CC9 (Risk Mitigation) | Provider degradation policy, calibration, and promotion evidence mitigate external data/integration risk before trust promotion. | Data Platform Lead | `docs/reference/provider-capability-matrix.md`; `docs/reference/provider-validation-evidence-schema.md`; `docs/status/evidence/dk1-pilot-parity-runbook.md` |
| A1 (Availability) | High-availability, SLO, and recovery operational guidance define uptime objectives and recovery expectations. | Operations Lead | `docs/operators/failover-and-recovery.md`; `docs/operators/preflight-checklist.md`; `docs/operators/README.md` |
| C1 (Confidentiality) | Credential handling and sensitive configuration practices define restricted handling expectations for secrets and integration data. | Security Lead | `docs/operators/provider-credentials.md`; `docs/reference/environment-variables.md`; `docs/security/threat-model-current-state.md` |

## Notes for audit preparation

- For each control row, attach execution-period artifacts (logs, screenshots, test reports, approval records) in the SOC evidence repository when available.
- Keep control owners aligned to named roles, then map to specific individuals in the audit engagement roster.
