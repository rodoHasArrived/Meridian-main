---
title: Operator Documentation
status: active
owner: core-team
reviewed: 2026-07-17
audience: operators
---

# Operator Documentation

This is the canonical operator procedure lane for Meridian setup, provider workflows, launch/deployment, runbooks, troubleshooting, and support evidence.

## What Is Canonical Here

- **Canonical**: this page + active operator procedure files below.
- **Source-Material**: legacy `docs/operations/*`, archived `docs/providers/*`, and one-off operator snapshots until they are canonicalized or archived.
- **Generated**: route to `docs/generated/` and generated registry outputs.
- **Archive**: superseded high-traffic paths moved with replacement links under `archive/docs/`.

Use this page when the question is: *how do I run, operate, repair, deploy, or support Meridian?*

Lookup tables and contract shape belong in [Reference](../reference/README.md). Implementation behavior belongs in [Engineering](../engineering/README.md).

## Canonical Operator Scope

- local launch and workstation operation
- provider credential and incident handling
- deployment and packaging
- reconciliation, failover, and recovery
- preflight and support evidence readiness
- provider operations and capability evidence routing

## Operator Start Paths

| Need | Start here | Notes |
| --- | --- | --- |
| First local setup | [Start](../start/README.md) | fastest contributor/operator orientation |
| Product operating scope | [Meridian Design Document](../product/meridian-design-document.md) | stakeholder design context for operator posture |
| Daily operator controls | [Operators](./README.md) | this page |
| Startup, restart, and shutdown control | [Lifecycle Control Plane](../reference/lifecycle-control-plane.md) | states, supervisor commands, database ownership, and receipts |
| Governed reporting, schedules, and delivery | [Governed Reporting Operations](./governed-reporting-operations.md) | reporting preflight, hard-close evidence, recovery, and secure relay operation |
| Deployment and packaging | [Deployment and Packaging](./deployment-packaging.md) | canonical packaging/checksum/sign-off posture |
| Troubleshooting and support evidence | [Operator Preflight Checklist](./preflight-checklist.md) | readiness gate and rollback posture |

## Active Operator Procedure Files

- [Browser Workstation Installer](./browser-workstation-installer.md)
- [Lifecycle Control Plane Reference](../reference/lifecycle-control-plane.md)
- [Deployment and Packaging](./deployment-packaging.md)
- [Failover and Recovery](./failover-and-recovery.md)
- [Fund Operations Persistence Cutover](./fund-ops-persistence-cutover.md)
- [Governed Reporting Operations](./governed-reporting-operations.md)
- [Operator Preflight Checklist](./preflight-checklist.md)
- [Provider Backfill Operations](./provider-backfill-operations.md)
- [Provider Credentials and Access](./provider-credentials.md)
- [Plaid Provider Operations](./plaid-provider-operations.md)
- [Provider Onboarding: Alpaca](./provider-onboarding-alpaca.md)
- [Provider Onboarding: Interactive Brokers](./provider-onboarding-interactive-brokers.md)
- [Reconciliation Operations](./reconciliation-operations.md)

## High-Traffic Legacy Path Migration Status

The table below tracks active legacy/high-traffic routes and their replacements in this canonical lane.

| Legacy path | Canonical replacement | Status | Notes |
| --- | --- | --- | --- |
| `docs/operations/README.md` | [README.md](./README.md) | Canonical | legacy routing hub points to operator canonical entry |
| `docs/operations/broker-order-routing-phased-runbook.md` | [README.md](./README.md) | Canonical | migration retained as historical archive + routing index |
| `docs/operations/canonical-buyer-workflow.md` | [README.md](./README.md) | Canonical | migration retained as historical archive + routing index |
| `docs/operations/operator-runbook.md` | [docs/operators/README.md](./README.md) | Canonical | route consolidated |
| `docs/operations/provider-credential-management.md` | [provider-credentials.md](./provider-credentials.md) | Canonical | archived copy maintained |
| `docs/operations/preflight-checklist.md` | [preflight-checklist.md](./preflight-checklist.md) | Canonical | legacy as source-material during migration |
| `docs/operations/reconciliation-operations.md` | [reconciliation-operations.md](./reconciliation-operations.md) | Canonical | archived source retained |
| `docs/operations/reconciliation-runbook.md` | [reconciliation-operations.md](./reconciliation-operations.md) | Canonical | legacy reconciliation runbook merged into canonical lane |
| `docs/operations/reconciliation-policy-operations.md` | [reconciliation-operations.md](./reconciliation-operations.md) | Canonical | legacy policy content merged |
| `docs/operations/reconciliation-resilience-runbook.md` | [reconciliation-operations.md](./reconciliation-operations.md) | Canonical | resilience posture indexed from canonical lane |
| `docs/operations/msix-packaging.md` | [deployment-packaging.md](./deployment-packaging.md) | Canonical | migration mapping retained |
| `docs/operations/deployment.md` | [deployment-packaging.md](./deployment-packaging.md) | Canonical | maintenance/deployment legacy migration |
| `docs/operations/environment-and-deployment-standard.md` | [deployment-packaging.md](./deployment-packaging.md) | Canonical | deployment standards moved to canonical lane |
| `docs/operations/web-workstation-installer.md` | [browser-workstation-installer.md](./browser-workstation-installer.md) | Canonical | migration mapping retained |
| `docs/operations/failover-and-recovery-runbook.md` | [failover-and-recovery.md](./failover-and-recovery.md) | Canonical | active recovery policy |
| `docs/operations/high-availability.md` | [failover-and-recovery.md](./failover-and-recovery.md) | Canonical | high-availability posture remains in failover lane |
| `docs/operations/service-level-objectives.md` | [failover-and-recovery.md](./failover-and-recovery.md) | Canonical | SLO posture indexed from failover lane |
| `docs/operations/slo-review-template.md` | [README.md](./README.md) | Canonical | template retained as source-material pattern |
| `docs/operations/cleanup-and-maintenance.md` | [README.md](./README.md) | Canonical | legacy cleanup posture migration |
| `docs/operations/disk-space-hygiene.md` | [README.md](./README.md) | Canonical | cleanup/ops hygiene migration |
| `docs/operations/error-budget-policy-runbook.md` | [README.md](./README.md) | Canonical | ops reliability posture migration |
| `docs/operations/ibkr-promotion-checklist.md` | [provider-onboarding-interactive-brokers.md](./provider-onboarding-interactive-brokers.md) | Canonical | provider promotion migration |
| `docs/operations/provider-degradation-calibration.md` | [provider-onboarding-interactive-brokers.md](./provider-onboarding-interactive-brokers.md) | Canonical | provider reliability calibration indexed to onboarding policy |
| `docs/operations/provider-degradation-policy.md` | [provider-onboarding-interactive-brokers.md](./provider-onboarding-interactive-brokers.md) | Canonical | provider reliability policy indexed to onboarding posture |
| `docs/operations/fund-ops-persistence-cutover-runbook.md` | [fund-ops-persistence-cutover.md](./fund-ops-persistence-cutover.md) | Canonical | high-risk persistence control migration completed |
| `docs/operations/live-execution-controls.md` | [../reference/provider-capability-matrix.md](../reference/provider-capability-matrix.md) | Canonical | live execution controls stay in capability/operations policy surface |
| `docs/operations/governance-operator-workflow.md` | [README.md](./README.md) | Canonical | governance/operator workflow mapped to canonical index |
| `docs/operations/portable-data-packager.md` | [README.md](./README.md) | Canonical | portability and packaging evidence remains source-only |
| `docs/operations/performance-tuning.md` | [README.md](./README.md) | Canonical | operations performance tuning preserved as historical context |
| `docs/operations/tradier-provider-endpoint-catalog.md` | [provider-onboarding-interactive-brokers.md](./provider-onboarding-interactive-brokers.md) | Canonical | catalog content mapped as source for provider onboarding references |
| `docs/operations/workstation-governance-approval-runbook.md` | [fund-ops-persistence-cutover.md](./fund-ops-persistence-cutover.md) | Canonical | accounting-control approval gates mapped to operator cutover lane |
| `docs/operations/orphaned-doc-triage-index.md` | [README.md](./README.md) | Canonical | migration inventory remains here |
| `archive/docs/providers/alpaca-setup.md` | [provider-onboarding-alpaca.md](./provider-onboarding-alpaca.md) | Canonical | provider onboarding canonicalized |
| `archive/docs/providers/interactive-brokers-setup.md` | [provider-onboarding-interactive-brokers.md](./provider-onboarding-interactive-brokers.md) | Canonical | provider onboarding canonicalized |
| `archive/docs/providers/backfill-guide.md` | [provider-backfill-operations.md](./provider-backfill-operations.md) | Canonical | backfill operations canonicalized |
| `archive/docs/providers/README.md` | [provider-credentials.md](./provider-credentials.md) | Canonical | provider onboarding/program overview canonicalized |
| `archive/docs/providers/provider-comparison.md` | [provider-capability-matrix.md](../reference/provider-capability-matrix.md) | Canonical | provider comparison merged into capability matrix |
| `archive/docs/providers/provider-confidence-baseline.md` | [provider-validation-matrix.md](../reference/provider-validation-matrix.md) | Canonical | provider confidence thresholds moved to validation matrix |
| `archive/docs/providers/security-master-guide.md` | [provider-capability-matrix.md](../reference/provider-capability-matrix.md) | Canonical | security/provider controls routing moved to capability matrix |
| `archive/docs/providers/stocksharp-connectors.md` | [provider-integration-status.md](../reference/provider-integration-status.md) | Canonical | connector inventory moved to provider integration status |
| `archive/docs/providers/data-sources.md` | [provider-capability-matrix.md](../reference/provider-capability-matrix.md) | Canonical | data source mapping merged into capability matrix |
| `archive/docs/providers/tradestation-endpoint-inventory.md` | [provider-capability-matrix.md](../reference/provider-capability-matrix.md) | Canonical | tradestation endpoint data migrated to capability matrix |
| `archive/docs/providers/broker-adapter-template-guide.md` | [provider-integration-status.md](../reference/provider-integration-status.md) | Canonical | broker adapter template guidance moved to provider integration status |
| `archive/docs/providers/interactive-brokers-free-equity-reference.md` | [provider-onboarding-interactive-brokers.md](./provider-onboarding-interactive-brokers.md) | Canonical | IBKR free-equity notes merged into IBKR onboarding page |

For quick operator evidence lookups, map claims to:

- [provider-integration-status.md](../reference/provider-integration-status.md) (operational posture and phase)
- [provider-validation-matrix.md](../reference/provider-validation-matrix.md) (gates and evidence criteria)
- [provider-validation-evidence-schema.md](../reference/provider-validation-evidence-schema.md) (artifact shape)

## Operator Claim Status Model

Use this simple status model for operator-facing claims:

- **Complete**: proof artifact linked + route validated + runbook path exists.
- **In Progress**: proof path exists, evidence capture underway.
- **Blocked**: policy or operational dependency is unmet and linked to mitigation path.

## Canonical Operator Procedures

### Workstation launch and packaging

- [Browser Workstation Installer](./browser-workstation-installer.md)
- [Deployment and Packaging](./deployment-packaging.md)

### Provider operations

- [Provider Credential Operations](./provider-credentials.md)
- [Plaid Provider Operations](./plaid-provider-operations.md)
- [Provider Onboarding: Alpaca](./provider-onboarding-alpaca.md)
- [Provider Onboarding: Interactive Brokers](./provider-onboarding-interactive-brokers.md)
- [Provider Backfill Operations](./provider-backfill-operations.md)

### Reconciliation and reliability

- [Reconciliation Operations](./reconciliation-operations.md)
- [Governed Reporting Operations](./governed-reporting-operations.md)
- [Operator Preflight Checklist](./preflight-checklist.md)
- [Failover and Recovery](./failover-and-recovery.md)
- [Fund Operations Persistence Cutover](./fund-ops-persistence-cutover.md)

### Command entry points

- `dotnet run --project src/Meridian/Meridian.csproj -- --mode desktop --http-port 8080`
- `dotnet run --project src/Meridian/Meridian.csproj -- --mode workstation --http-port 8080`
- `pwsh ./scripts/dev/run-desktop.ps1 -Fixture`
- `npm --prefix src/Meridian.Ui/dashboard run dev`

## Operator Rules

- Use provider lookup/capability claims only via [Reference](../reference/README.md#canonical-lookup-areas).
- Keep setup/procedure docs in this folder and `docs/start/`; archive superseded one-off paths after replacement links exist.
- Do not claim operational readiness until evidence artifacts from support packets, status proofs, and runbook-linked checks are attached.
- Preserve evidence-first rollback and handoff language for all incident and deployment updates.
- Do not hand-edit generated operator-facing docs. If generation is required, update source inputs and regenerate outputs.

## Evidence Attachments and Proof Surfaces

- [provider-integration-status.md](../reference/provider-integration-status.md)
- [provider-validation-evidence-schema.md](../reference/provider-validation-evidence-schema.md)
- [provider-validation-matrix.md](../reference/provider-validation-matrix.md)
- [ROADMAP data and generated views](../roadmap/README.md)
- [Generated documentation policy](../generated/README.md)
- [Documentation ownership contract](../documentation-ownership.md)

## Ownership Alignment

- Operator behavior and runbook claims stay here.
- Provider lookup tables and validation contracts stay in `docs/reference/`.
- Source-of-truth for implementation remains in the owning `src/**` module READMEs and `docs/source/data/source-modules.yml`.
- Runbook posture that changes delivery state must align to roadmap rows in `docs/roadmap/data/*.yml` and generated roadmap outputs.

## Legacy-to-Archive Rules

- After a legacy path is replaced and linked here, keep full historical content in `archive/docs/operations/` with explicit replacement reason and date.
- Keep legacy stubs only where a high-traffic inbound path would otherwise break; remove stubs once canonical replacement has external links.
