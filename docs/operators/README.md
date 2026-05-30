# Operator Documentation

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-05-30

This is the canonical operator procedure lane for Meridian setup, provider workflows, workstation usage, runbooks, deployment, troubleshooting, and support evidence.

Use this page first when the question is "how do I run, operate, repair, package, or support Meridian?" Lookup-only tables belong in [Reference](../reference/README.md); implementation guidance belongs in [Engineering](../engineering/README.md).

## Operator Start Paths

| Need | Start here | Notes |
| --- | --- | --- |
| First local setup | [Getting Started](../getting-started/README.md), [Pilot Operator Quickstart](../getting-started/pilot-operator-quickstart.md) | New setup content should migrate to `docs/start/` or this operator lane. |
| Local command help | [HELP](../HELP.md) | Operator-facing command discovery and FAQ. |
| Daily operations | [Operator Runbook](../operations/operator-runbook.md) | Day-to-day operational procedures. |
| Deployment | [Deployment](../operations/deployment.md), [Environment and Deployment Standard](../operations/environment-and-deployment-standard.md) | Deployment and environment rules. |
| Production posture | [Production Status](../status/production-status.md), [Service Level Objectives](../operations/service-level-objectives.md) | Status input plus SLO procedures. |

## Workstation Launch And Packaging

| Workflow | Source material | Notes |
| --- | --- | --- |
| Local API host | `dotnet run --project src/Meridian/Meridian.csproj -- --mode desktop --http-port 8080` | Launches the desktop-local API host on port 8080 by default. |
| Browser workstation | `npm --prefix src/Meridian.Ui/dashboard run dev` | Development workstation; production assets are served from `src/Meridian.Ui/wwwroot/workstation/`. |
| WPF desktop fixture | `pwsh ./scripts/dev/run-desktop.ps1 -Fixture` | Fixture-mode desktop launch for operator validation. |
| Web workstation installer | [Web Workstation Installer](../operations/web-workstation-installer.md) | Local browser-workstation app installation. |
| MSIX packaging | [MSIX Packaging](../operations/msix-packaging.md) | Desktop application packaging. |

## Provider And Credential Operations

| Workflow | Source material | Notes |
| --- | --- | --- |
| Provider setup overview | [Provider Documentation](../providers/README.md) | Legacy provider index retained as source material during migration. |
| Alpaca setup | [Alpaca Setup](../providers/alpaca-setup.md) | Credentialed provider setup. |
| Interactive Brokers setup | [Interactive Brokers Setup](../providers/interactive-brokers-setup.md), [IBKR Promotion Checklist](../operations/ibkr-promotion-checklist.md) | Broker-aligned setup and promotion checklist. |
| Credential storage and repair | [Provider Credential Management](../operations/provider-credential-management.md) | Encrypted local credential storage and repair routes. |
| Provider degradation | [Provider Degradation Calibration](../operations/provider-degradation-calibration.md), [Provider Degradation Policy](../operations/provider-degradation-policy.md) | Calibration, promotion, and governance gates. |
| Backfill operations | [Backfill Guide](../providers/backfill-guide.md) | Historical data backfill procedures. |
| Provider lookup | [Reference provider lookup](../reference/README.md#canonical-lookup-areas) | Capability and validation matrices are lookup material. |

## Governance, Reconciliation, And Reporting Operations

| Workflow | Source material | Notes |
| --- | --- | --- |
| Governance operator workflow | [Governance Operator Workflow](../operations/governance-operator-workflow.md) | Security Master, reconciliation queue, and governance export operations. |
| Governance approval | [Workstation Governance Approval Runbook](../operations/workstation-governance-approval-runbook.md) | Approval workflow procedures. |
| Fund-ops persistence cutover | [Fund Ops Persistence Cutover Runbook](../operations/fund-ops-persistence-cutover-runbook.md) | Persistence cutover procedures. |
| Reconciliation operations | [Reconciliation Operations](../operations/reconciliation-operations.md), [Reconciliation Runbook](../operations/reconciliation-runbook.md), [Reconciliation Resilience Runbook](../operations/reconciliation-resilience-runbook.md) | Break review, recovery, and resilience workflows. |
| Reconciliation policy | [Reconciliation Policy Operations](../operations/reconciliation-policy-operations.md) | Policy and controlled operations. |
| Portable data packages | [Portable Data Packager](../operations/portable-data-packager.md) | Creating and importing data packages. |

## Reliability, Recovery, And Release Controls

| Workflow | Source material | Notes |
| --- | --- | --- |
| Preflight checklist | [Preflight Checklist](../operations/preflight-checklist.md) | Pilot/release readiness checks. |
| Failover and recovery | [Failover and Recovery Runbook](../operations/failover-and-recovery-runbook.md) | Recovery procedures. |
| High availability | [High Availability](../operations/high-availability.md) | HA configuration. |
| Error budget policy | [Error Budget Policy Runbook](../operations/error-budget-policy-runbook.md) | Freeze and reliability-sprint triggers. |
| Live execution controls | [Live Execution Controls](../operations/live-execution-controls.md) | Broker/order safety controls. |
| Broker order routing | [Broker Order Routing Phased Runbook](../operations/broker-order-routing-phased-runbook.md) | Broker routing procedures. |
| Performance tuning | [Performance Tuning](../operations/performance-tuning.md) | Operational performance guidance. |

## Operational Readiness Rule

Operational-readiness claims require current SLOs, linked runbooks, and release evidence. High-severity alerts should name the symptom, likely cause, runbook section, immediate mitigation, and rollback criteria. Release gates should identify the exact tests, data-quality smoke checks, deployment verification, and rollback posture used for the change.

Historical readiness evaluations can explain why these controls exist, but they are not current proof of production readiness.

## Ingestion Operations Rule

Operator-facing ingestion controls should expose job state, checkpoint/resume posture, provider/fallback selection, backpressure or drop signals, and failure evidence. Backfill or realtime recovery procedures should name the current runbook, expected evidence artifact, and validation lane rather than relying on dated orchestration evaluations.

## Streaming And Storage Operations Rule

Operator runbooks for streaming or storage incidents should name the affected provider/source, pipeline or storage component, alert/runbook mapping, evidence artifact, recovery action, and verification command. Dated architecture evaluations are useful background only; current recovery claims need live runbook and validation evidence.

## Maintenance And Support Evidence

| Workflow | Source material | Notes |
| --- | --- | --- |
| Cleanup and maintenance | [Cleanup and Maintenance](../operations/cleanup-and-maintenance.md) | Safe generated-output cleanup, path hygiene, and repo maintenance rules. |
| Disk space hygiene | [Disk Space Hygiene](../operations/disk-space-hygiene.md) | Local disk-space triage and generated artifact retention. |
| Orphaned doc triage | [Orphaned Doc Triage Index](../operations/orphaned-doc-triage-index.md) | Link-recovery intake tied to docs health reports. |
| SLO review | [SLO Review Template](../operations/slo-review-template.md) | Monthly SLO compliance and action review. |
| Support evidence rule | [Generated Documentation](../generated/README.md), [Status Reporting](../status/README.md) | Operator-facing readiness claims must point to current artifacts, dashboards, or validation output. |

## Operator Guardrails

- Do not write provider secrets to user-level environment variables from new flows; use the shared credential store and documented config/data roots.
- Keep provider fallback order visible, explainable, and testable; desktop/provider settings must not fork provider logic away from shared backfill orchestration.
- Do not upgrade readiness language unless current evidence artifacts or validation output prove the claim.
- Keep setup/procedure docs in `docs/operators/`, `docs/start/`, or legacy source-material files linked from this page until migration is complete.
- Keep lookup tables in [Reference](../reference/README.md), not in procedural runbooks.
- Archive superseded runbooks and dated operational snapshots under `archive/docs/` after replacement links exist.
