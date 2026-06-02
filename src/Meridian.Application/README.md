---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-APP
path: src/Meridian.Application
status: active
owner_lane: Runtime Host
last_reviewed: 2026-06-02
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
  as ledger source lineage. Loan terms that produce ledger postings must carry a
  `DirectLendingSecurityMasterReferenceDto`; the projector re-resolves that reference through the
  authoritative Security Master query service and then stamps server-derived Security Master id,
  symbol, approval, provenance, active status, and direct-lending ledger-mapping evidence on central
  ledger writes before the posting guard accepts direct-lending instrument lines.
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
  Security Master id carried by the journal metadata or instrument line, line status must be
  re-read from the server-side Security Master and still be active, and instrument line symbols
  must match the journal-level Security Master symbol before posting. Ledger-mapping references must also identify the same resolved symbol or Security Master
  id instead of using a generic account mapping token.
- Operations Continuity workflow DTO projection also derives the shared accounting-record summary
  from server-owned workflow state. The summary covers retained source records, normalized
  activity, reconciliation history, ledger evidence, approvals, and report-pack lineage so browser
  and WPF clients do not calculate accounting-record audit readiness locally. Report-pack lineage
  is complete only after close-package publication evidence exists, so a ready report-pack id alone
  does not imply retained export, document, manifest, or restatement provenance. The projection
  emits required-evidence labels for each category, including export manifests, document
  attachments, and restatement lineage for the report-pack row.
- `Reconciliation/` - statement reconciliation orchestration and broker/custodian intake that
  validates canonical external statement files, creates durable reconciliation cases for unresolved
  cash/activity rows, requires row currency equality before broker/custodian auto-match, appends
  reconciliation decision journals through crash-safe copy-on-write JSONL writes, attaches
  break explanations plus retained statement-row evidence, and owns the statement-run workflow that
  persists canonical imports, open breaks, and case materialization for shared UI consumers.
- `ProviderRouting/` - relationship-aware provider capability routing. Provider-ledger accounting
  workflows use these capability gates to block missing balance/position/reconciliation feeds and
  degrade corporate-action or factor-schedule support when the account's provider route cannot
  supply the required feed.
- `Config/Credentials/` - encrypted provider credential catalog and vault behavior. Plaid is a
  governed provider family with client id/secret fields, sandbox/development/production
  environment normalization, and browser Data provider setup support. Plaid setup is credential-only
  in this layer: it stores client credentials in the encrypted vault and does not seed a market-data
  `DataSourceConfig` or provider-routing binding.
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
  Security Master validation messages use operator-review wording for override audit remediation so
  application-layer guidance does not expose legacy Governance workspace language.
- `FundStructure/` - organization, fund, portfolio, account, ledger-group, cash-flow, and ledger
  mapping workbench orchestration. Ownership-link policy validation prevents invalid setup graphs
  by blocking self-parenting, active cycles, incompatible relationship types, overlapping primary
  links, invalid percentage ownership, sibling percentage over-allocation, and invalid effective
  windows before create, amend, expire, or replacement graph mutations are persisted. Ledger mapping resolution stays server-side and
  reuses fund-structure assignments before falling back to account ledger references.
- `Auth/` - scoped access assignment orchestration and authorization decisions that bind
  role/profile permissions to global or fund-structure scopes. The local JSON store persists under
  `governance/user-access-assignments.json` with atomic writes, while
  `MERIDIAN_SCOPED_ACCESS_CONNECTION_STRING` enables the Postgres-backed identity access store for
  shared multi-instance deployments. Governed mutations use versioned assignment records so
  concurrent Meridian instances fail closed instead of overwriting authority.
- `EnvironmentDesign/` - local-first organization environment drafts, validation, publishing,
  rollback, and runtime projection. Lane defaults normalize legacy `Research`, `Data Operations`,
  and `Governance` workspace/page tags into the canonical operator roots (`Strategy`, `Data`, and
  `Accounting`) while validation accepts the full design-document root set:
  `Trading`, `Portfolio`, `Accounting`, `Reporting`, `Strategy`, `Data`, and `Settings`.
- `FundAccounts/` - internal account balance snapshots, statement intake, account readiness, and
  provider-link history. Balance snapshots preserve optional realized and unrealized P&L values so
  shared provider-ledger reconciliation can compare broker marks with retained internal book
  measures without UI-local calculations.
- `Services/` - application use cases and orchestration services.
- `Composition/` - application feature registration and service wiring.

## Important workflows

Use this module when changing command behavior, workflow orchestration, feature registration, or
application service contracts consumed by host and UI surfaces.

The interactive configuration wizard presents historical analysis and backtesting as the canonical
`Strategy` use case while retaining the older `Research` enum member only as a compatibility alias.
Backtest Studio run orchestration records accepted and terminal runs through the shared
`StrategyRunEntry` lineage model: `StrategyId`, `StrategyName`, run id, engine, dataset/feed
references, parameter set, sweep id, and canonical sweep-definition hash stay with the run evidence.
Keep W6-BTSTUDIO-001 acceptance criteria in roadmap exit criteria and verify this lane with
`BacktestStudioRunOrchestratorTests` when changing backtesting evidence behavior.

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
| `W5-ACCT-001` | Accounting records and operational evidence |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-APP -->
| TODO | Title | Status | Priority |
| --- | --- | --- | --- |
| `TODO-SRC-APP-001` | Complete W6 backtesting evidence loop linkage to strategy lineage | done | medium |
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
