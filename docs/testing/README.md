# Testing Documentation

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-10-07

Use [Engineering](../engineering/README.md#buildtestrun) for current build and test commands.
This folder owns scenario-specific acceptance procedures and retained evidence.

| Task | Document | Scope |
| --- | --- | --- |
| Run the paper-cockpit regression slice | [Wave 2 acceptance tests](WAVE2_ACCEPTANCE_TESTS.md) | Automated checks and failure diagnosis. |
| Capture paper-session continuity evidence | [Wave 2 reliability runbook](wave2-cockpit-reliability-evidence-runbook.md) | Automated and manual evidence sequence. |
| Understand the original Wave 2 gates | [Wave 2 gate checklist](WAVE2_ACCEPTANCE_GATE_CHECKLIST.md) | Historical requirements and implementation snapshot. |
| Validate Alpaca account synchronization and recovery | [Alpaca paper portfolio sandbox](alpaca-paper-portfolio-sandbox.md) | Opt-in broker evidence, deterministic fixtures, and validation limits. |
| Verify accounting trust corrections | [Accounting trust acceptance](accounting-trust-corrections.md) | Scenario checklist; operator decisions remain explicit. |
| Verify posted amount provenance | [W10 amount provenance](w10-amount-provenance.md) | Scoped evidence selection, review regressions, and validation limits. |
| Evaluate close readiness and mark freshness | [W10 operator acceptance](w10-mark-seam-operator-acceptance.md) | Candidate, population, criterion decisions, and validation limits. |
| Inspect retained W10 evidence | [Candidate evidence packet](evidence/w10-615abde9/README.md) | Results bound to its recorded commit and environment. |
| Validate endpoint isolation and concurrency | [Endpoint fixture isolation](endpoint-fixture-isolation.md) | Reproducible benchmark and recorded isolation evidence. |

A procedure describes what to verify; a retained packet records what was actually observed. Keep
packet dates, commits, and unresolved findings intact. Readiness and release acceptance remain in
the [readiness tracker](../product/implementation-todo-list.md) and [roadmap registry](../roadmap/README.md).

## Bank statement reconciliation on PostgreSQL

`StatementLedgerReconciliationPostgresTests` in
[`tests/Meridian.Tests/Integration/`](../../tests/Meridian.Tests/Integration/StatementLedgerReconciliationPostgresTests.cs)
exercises the camt.053 and BAI2 golden statements through import, governed PostgreSQL journal
postings, the live ledger population provider, deterministic split matching, and operator casework.
Fresh service instances and duplicate imports must preserve the retained match groups, case ids,
operations timeline, and immutable PostgreSQL reporting evidence even after another journal arrives.
The journal, fund-account, operations, and reporting stores use PostgreSQL; statement matching and
casework use the production durable file stores.

Run the suite with Docker available, or set `MERIDIAN_REPORTING_CONNECTION_STRING` to a disposable
PostgreSQL database. Each scenario creates and drops its own schemas. These tests use
`ReportingDatabaseFact`, so searching only for `LedgerDatabaseFact` misses this coverage.

```sh
python build/python/cli/buildctl.py test --project tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~StatementLedgerReconciliationPostgresTests" --queue
```

The `Category=Integration` suite includes these scenarios in the service-backed integration gate
and Production Certification. Passing automated evidence does not record operator acceptance.
