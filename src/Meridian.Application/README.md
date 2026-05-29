---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-APP
path: src/Meridian.Application
status: active
owner_lane: Runtime Host
last_reviewed: 2026-05-29
---

# src/Meridian.Application

## Purpose

Meridian application layer contains use cases, orchestration services, commands, and workflow
coordination.

## Layer responsibility

This module owns application workflows that coordinate providers, storage, execution, ledger,
reporting, and UI-facing services through contracts. Keep transport, persistence implementation,
and UI presentation concerns in their owning layers.

## Key folders and files

- `Commands/` - CLI command handlers and operator workflows.
- `DirectLending/` - loan command/query orchestration, direct-lending ledger projection, and the
  daily accrual worker. Recurring accrual posting now checks ledger accounting-period state before
  calling the direct-lending command service; period-blocked originating accruals are routed to the
  Accounting operator inbox with `FundReconciliation` navigation instead of becoming log-only
  failures. Ledger-impacting commands project balanced `LedgerJournalEntryWrite` records before
  persistence and pass them to the direct-lending state store with the same generated loan event id
  as ledger source lineage.
- `OperationsContinuity/` - account-period continuity aggregate, command transitions, audit
  timeline, and server-derived gate status for broker, Security Master, ledger, reconciliation,
  and approval close lanes. Approval and close commands enforce shared close-checklist control
  approvals before the workflow can become ready for close or close against a report pack. Close
  commands also publish governed close-package metadata on the workflow, including signer,
  sign-off rationale, retained manifest id/route, evidence hash, report pack id, evidence links,
  and checklist approvals. Close readiness is scored server-side across Security Master, provider-data freshness, position, cash,
  ledger, pricing, reconciliation, report, and approval components. Readiness blockers use the
  shared Operations Continuity blocker-code matrix so browser and WPF routes do not need
  client-local close-readiness codes. The provider freshness component uses the same broker-sync
  stale posture signal that blocks the broker ingest gate, so controller close calendars do not
  treat stale provider data as merely a UI warning. Gate posture
  also accepts required and degraded provider capability gaps from the provider routing matrix:
  required balance, position, reconciliation, or account-scoped gaps block broker ingest, while
  quote-history, corporate-action, factor-schedule, or asset-class degradation moves broker ingest
  to review-required and reduces close readiness until an operator resolves or accepts the gap. The approval
  policy matrix service projects the
  same server-owned reviewer, permission, report-pack, checklist, and audit-event rules for
  configuration surfaces and accepts governed rule upserts with rationale, actor, correlation, and
  storage-root persistence under `governance/operations-approval-policy-rules.json`. The close
  calendar service projects each workflow's next due close task, owner, readiness score, component
  breakdown, blocker codes, next actions, and approval counts from the workflow service instead of
  client-local scheduling rules; governed owner/due-date overrides persist under
  `governance/operations-close-calendar-items.json` with actor, rationale, and correlation
  evidence. Ledger posting commands also enforce line-level Security Master symbol, identity,
  explicit approval reference, provenance, and ledger-mapping evidence for every instrument-bearing
  journal line before the durable journal can be appended, including securities-style account lines
  that omit symbol metadata. Candidate and line-level provenance must reference the resolved
  Security Master id carried by the journal metadata or instrument line, line status must still be
  active, and instrument line symbols must match the journal-level Security Master symbol before
  posting. Ledger-mapping references must also identify the same resolved symbol or Security Master
  id instead of using a generic account mapping token.
- `Reconciliation/` - statement reconciliation orchestration and broker/custodian intake that
  validates canonical external statement files, creates durable reconciliation cases for unresolved
  cash/activity rows, requires row currency equality before broker/custodian auto-match, appends
  reconciliation decision journals through crash-safe copy-on-write JSONL writes, and attaches
  break explanations plus retained statement-row evidence.
- `ProviderRouting/` - relationship-aware provider capability routing. Provider-ledger accounting
  workflows use these capability gates to block missing balance/position/reconciliation feeds and
  degrade corporate-action or factor-schedule support when the account's provider route cannot
  supply the required feed.
- `SecurityMaster/` - Security Master orchestration, aggregate rebuild helpers, instrument
  passport composition, and the ledger bridge that posts dividends, splits, distributions, and
  factor/principal paydowns into the Security Master ledger view for downstream reconciliation and
  valuation evidence. The same folder owns the starter custom asset profile catalog and profile-backed
  validation rules for approved profile-version pinning, typed no-code field values, profile approval
  metadata, and identifier coverage. Security Master create/amend orchestration preserves pinned
  profile-backed `CustomAsset` and `OtherSecurity` payloads in projection and event evidence while
  reusing the existing generic-security domain backing model. The query service keeps ordinary text
  search delegated to the storage index and uses the projected Security Master universe only when
  custom profile id, version, field-key, or field-value filters are supplied. Profile definitions
  are governed by `SecurityAssetProfileGovernanceService`, which merges seeded starter definitions
  with storage-root persisted drafts, approvals, rollback-created versions, and audit lineage.
- `FundStructure/` - organization, fund, portfolio, account, ledger-group, cash-flow, and ledger
  mapping workbench orchestration. Ownership links have first-class lifecycle commands for update,
  expiration, replacement, and graph validation, and ledger mapping resolution stays server-side and
  reuses fund-structure assignments before falling back to account ledger references.
- `FundAccounts/` - internal account balance snapshots, statement intake, account readiness, and
  provider-link history. Balance snapshots preserve optional realized and unrealized P&L values so
  shared provider-ledger reconciliation can compare broker marks with retained internal book
  measures without UI-local calculations.
- `Services/` - application use cases and orchestration services.
- `Composition/` - application feature registration and service wiring.

## Important workflows

Use this module when changing command behavior, workflow orchestration, feature registration, or
application service contracts consumed by host and UI surfaces.

## API contract notes

- Options-chain provider IDs are normalized with trim plus invariant lowercase before deduplication, health lookup, fallback detection, logging, and metrics.
- `ExecutionSimulationOrchestrator` backs the `--simulate-execution` CLI path and now emits
  inferred queue diagnostics, confidence grade, warnings, fill-rate, average-slippage placeholder,
  and `isInferred` labels in simulation artifacts. This is a baseline L3-style inference path, not
  exchange-grade per-order L3 replay.

## Diagrams

See `DIA-ASSURANCE-LOOP` in `docs/source/data/diagram-index.yml`.

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-APP -->
| Roadmap item | Title |
| --- | --- |
| `W2-TRD-001` | Paper trading cockpit reliability |
| `W2-PROMO-001` | Paper promotion evidence and operator acceptance |
| `W3-CONT-001` | Research to paper continuity |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-APP -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```bash
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "Category!=Integration" --logger "console;verbosity=normal"
```

## Change rules

Keep orchestration here. Do not leak transport/UI concerns into this layer or add direct
infrastructure details when an abstraction already exists.

## Related docs

- `docs/architecture/module-map.md`
- `docs/developer/build-test-run.md`
- `docs/source/generated/source-module-index.md`
