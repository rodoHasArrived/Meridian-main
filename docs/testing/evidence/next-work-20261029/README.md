# Next-work implementation and acceptance boundaries

This continuation begins at `1dbed7bc01fd5c314b078444ff7330b67670e68a`. Source delivery,
automated proof, installed operation and operator acceptance are separate requirements. This
record does not close a roadmap item or certify production. Current check results belong to the
candidate's local gate receipts and required hosted checks, rather than the historical baselines.

| Requested work | Delivered boundary | Evidence still required |
| --- | --- | --- |
| Credential ownership/runtime | Authenticated owned-connection creation, explicit host binding, scope fencing at vault operations and provider construction; retained ownership marker survives deletion. | Current full local/hosted gates, Windows protected-vault/runtime operation and PRD-002 release evidence. Existing instances need deliberate provider reconstruction/restart. |
| Migration/rollback | Generation-bound move of a retained provider-credential record, immutable retry receipt, both recovery generations, deferred-audit replay and unrotated rollback. | Installed Windows exercise. Separate legacy OAuth-token records deliberately block this bounded transfer and require provider-specific treatment; environment-only credentials are not silently imported. |
| LOT | Exact snapshot quantity/short economics; governed short opening/cover and matching Execution/Backtesting direction; opt-in v3 remaining-face amortization and automatic yield-to-worst. | Final automated proof, broader consumer cutover, unsupported exercise/valuation and short-tax cases, compatibility retirement, independent review and live shadow acceptance remain open. |
| MARK | Existing policy/preview/operator paths are retained; the Windows packet uses the maintained eight-position population specification. | Provisioned retained IDs, population-wide preview, affected-position drilldown, repair/override proof and attributed live verdicts. |
| SEAM | Existing shared scope/readiness contract is retained. | Same host/client candidate and complete declared scope, missing/stale/mismatched contributor refusal, repair recovery and live verdicts in both clients. |
| RECON | Existing durable lineage, run comparison and SLA-calendar paths are retained. | Current automated lineage proofs plus a retained run sequence demonstrating recurrence, valid clearing and business-calendar escalation; no operator verdict is supplied here. |

## Automated proof selection

The owner expanded LOT to include short opening/cover, remaining-face amortization and automatic
yield-to-worst. See [the accounting boundary and golden cases](accounting-expansion.md). The
implementation is subject to final candidate tests and hosted checks; this does not close LOT or
declare live acceptance. Earlier unsupported-branch statements describe the pre-expansion scope.

The focused credential packet includes `ProviderCredentialOwnershipMigrationTests`,
`ProviderRuntimeCredentialServiceTests`, `ProviderCredentialOwnershipEndpointsTests`,
`ProviderSetupOwnershipTests`, existing provider lifecycle/construction isolation tests and
`ScopedCredentialRecoveryTests`. The snapshot increment is covered by
`SecurityMasterSnapshotLotEconomicsTests` and the existing Security Master trust-snapshot endpoint
cases. Final validation must use the actual candidate build, not an earlier development assembly.

For financial continuation, select `CanonicalOpenLotConsumerTests`, `FactorPaydown`,
`ValuationFreshnessAcceptanceTests`, `FinancialOperationsCommandCenterReadService`,
`CloseReadinessSubjectSourceTests`, `FileReconciliationBreakQueueLineageTests`,
`StatementReconciliationLineageTests` and `ReconciliationSlaCalendarTests`. Distinguish ordinary
non-integration results from PostgreSQL results. Use only a disposable nonproduction database for
integration proof and retain its schema cleanup result. A Linux WPF stub is not runtime proof.

Run `bash scripts/ci.sh` for the complete local gate; required GitHub Actions checks remain the
integration authority. Keep logs, TRX results, skipped-case counts and candidate identity with the
gate receipt. Do not replace failures or missing evidence with a roadmap status change.

## Windows and release preparation

Use a dedicated Windows x64 test user on the development PC, or a disposable VM when it hosts
production. An isolated DataRoot alone does not isolate WPF's per-user state. Run
`scripts/dev/prepare-next-work-acceptance.ps1` into a new directory; it creates preparation only,
refuses overwrite and leaves financial provisioning, workstation versions, retained scope and
every live verdict pending. Follow the [operator acceptance specification](../../w10-mark-seam-operator-acceptance.md).
The implementation queue remains LOT → MARK → SEAM → RECON; the joint live session exercises
SEAM before MARK.

The [signing recheck](release-readiness.json) records Release Readiness run `40294900031`, attempt
4, with a fresh signing-job failure on 2026-10-29: `signing-secrets-unavailable`. Andrew Rowden is
the recorded signing owner. No release tag was created; the historical workflow SHA does not
certify this application candidate. Browser workbench PR #3326 merged separately into `main`
at `e6d6d75655`; this branch must integrate and validate against that delivered work.
