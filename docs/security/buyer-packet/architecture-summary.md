# Meridian Buyer Security Packet — Architecture Summary

- **Document Owner:** Security Architecture
- **Version:** 2026.05.27.1
- **Last Reviewed:** 2026-05-27
- **Next Review Due:** 2026-08-31
- **Classification:** Buyer Diligence / Controlled Distribution

## Purpose
This summary describes Meridian's high-level system boundaries, principal data flows, and trust boundaries so a buyer diligence team can evaluate architecture-driven security risk.

## System Boundaries

### In Scope Components
- **Core Host (`src/Meridian/`)**: Runtime orchestration, CLI workflows, diagnostics, and control-plane entry points.
- **Desktop Operator Shell (`src/Meridian.Wpf/`)**: WPF operator experience for trading, accounting, reporting, strategy, data, and settings workflows.
- **Browser Workstation (`src/Meridian.Ui/dashboard` + `src/Meridian.Ui/wwwroot/workstation/`)**: Operator-facing browser UI surface.
- **Shared API and Read Models (`src/Meridian.Ui.Services/`, `src/Meridian.Ui.Shared/`)**: Shared service and contract layer consumed by desktop and browser surfaces.
- **Provider and ingestion paths**: Market data, historical data, symbol search, and statement ingest interfaces.
- **Storage and artifacts**: Local data stores, package workflows, replay artifacts, and evidence outputs.

### Out of Scope Components
- Third-party provider infrastructure (broker/exchange APIs and upstream SaaS backends).
- End-user endpoint operating system controls outside Meridian process boundaries.
- Non-Meridian mobile clients (mobile lane intentionally excluded from product scope).

## High-Level Data Flows
1. **Ingress / acquisition**
   - Operator configures providers and symbol scope.
   - Meridian fetches stream/historical/reference payloads through provider adapters.
2. **Normalization / processing**
   - Inbound payloads are normalized into shared contracts/read models.
   - Validation and diagnostics annotate quality and readiness posture.
3. **Storage / replay / packaging**
   - Data is persisted to managed local stores/artifacts.
   - Replay and package commands produce recoverable evidence outputs.
4. **Presentation / operator decisioning**
   - Desktop and browser workstation surfaces query API/read-model layers.
   - Trading readiness, inbox/reconciliation, and reporting workflows expose state for operator action.
5. **Governance / evidence**
   - Validation scripts and status artifacts capture operator sign-off evidence, posture summaries, and route-scoped checks.

## Trust Boundaries

### Boundary A — External Provider Boundary
- **Crossing:** Internet-facing provider API traffic into Meridian provider clients.
- **Key Risks:** Untrusted payloads, spoofed responses, schema drift, degraded upstream trust.
- **Core Controls:** Adapter-level validation, explicit provider calibration workflows, command-scoped checks, and degradations surfaced to operator workflows.

### Boundary B — Operator Access Boundary
- **Crossing:** Human operators invoking desktop/browser workflows and administrative commands.
- **Key Risks:** Over-privileged use, unsafe operational actions, accidental destructive workflows.
- **Core Controls:** Role-oriented workstation segmentation, workflow-specific runbooks, and auditable evidence/output artifacts.

### Boundary C — Service/Storage Boundary
- **Crossing:** Application service layer writes to local persistence/artifact roots.
- **Key Risks:** Integrity loss, tampering, stale replay evidence, backup gaps.
- **Core Controls:** Validation/repair commands, deterministic package/statement workflows, and documented restore/testing playbooks.

### Boundary D — Change/Automation Boundary
- **Crossing:** CI/dev automation and scripted workflows introducing deployable artifacts.
- **Key Risks:** Unreviewed code, dependency drift, weak release hygiene.
- **Core Controls:** Pre-PR test profiles, focused validation slices, retention/cleanup automation, and evidence-capturing workflow scripts.

## Security Design Posture (Current)
- Security controls are concentrated in **workflow guardrails, provider validation, diagnostic observability, and evidence-producing operational scripts**.
- Meridian favors **auditable operator workflows** over opaque background automation for high-impact actions.
- Security readiness is reviewed as part of ongoing docs/status artifacts and gate-specific evidence packets.

## Freshness and Quarterly Refresh Checklist
- [ ] Reconfirm in-scope component list against current module map.
- [ ] Revalidate all trust boundaries and data-flow assumptions against current endpoints/workflows.
- [ ] Verify boundary controls map to current scripts/tests and status dashboards.
- [ ] Update version, review dates, and owner if responsibilities changed.
- [ ] Record changes in `document-index.md` revision notes.
