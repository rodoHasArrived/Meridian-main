---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-APP
path: src/Meridian.Application
status: active
owner_lane: Runtime Host
last_reviewed: 2026-06-06
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

- `Commands/` - CLI command handlers and operator workflow adapters. Runbook command flags adapt
  the Workflow-owned runbook store and executor instead of owning runbook state in Application;
  fund workflow command-state transitions also live in `Meridian.Workflow.Workflows`. The schema
  compatibility command adapts the Data Integration-owned `SchemaValidationService` instead of
  owning stored market-event schema checks in Application.
- Provider credential setup, testing, and token-refresh orchestration consumes
  `Meridian.DataIntegration.Credentials`; Application no longer owns generic provider credential
  store contracts. Provider plugin assembly loading and `DataSourceRegistry` discovery now live in
  ProviderSdk; Application and WPF consume the loader instead of keeping reflection-based provider
  discovery in Application services.
- ETL commands, composition, and orchestration services consume
  `Meridian.DataIntegration.Etl` contracts and normalization services. Application still owns
  current ETL job/orchestrator adapters while pipeline and ingestion job dependencies remain here.
  The ETL export service now lives in `Meridian.DataIntegration.Etl`. The ETL job-definition store
  and SFTP publisher port contracts live in `Meridian.Contracts.Etl`, and the local JSON-backed
  job-definition store implementation lives in `Meridian.Storage.Etl`.
- Canonicalization composition consumes `Meridian.DataIntegration.Canonicalization` contracts,
  provider condition-code mapping, venue normalization, parity metrics, and the default
  `EventCanonicalizer`. Application still owns the event-pipeline publisher decorator and
  pipeline-specific quarantine wiring until those pipeline dependencies can move or invert cleanly.
- Event pipeline queueing consumes `Meridian.Platform.Tracing.EventTraceContext` for trace
  propagation, platform-owned OpenTelemetry helpers for market-data activity/counter telemetry,
  the Platform `DefaultEventMetrics` implementation, and the Platform `TracedEventMetrics`
  decorator. The shared metrics contract, snapshot shape, monitoring webhook sink contract, and
  pipeline statistics DTO live in `Meridian.Contracts`; F# validation counters and market-data quality
  validators/analyzers, clock-skew estimation, spread monitoring, data-loss accounting, provider
  latency histograms, provider metrics snapshots, connection-health monitoring, and provider
  degradation scoring/config/calibration records live in
  `Meridian.DataIntegration.Monitoring`.
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
- Financial Operations integration - application composition registers the
  `Meridian.FinancialOperations.OperationsContinuity` services that now own account-period
  continuity workflows, including the aggregate, command workflow service, status derivation,
  repositories, audit hashing, and Postgres workflow store. The Financial Operations workflow
  enforces shared close-checklist control
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
  to review-required and reduces close readiness until an operator resolves or accepts the gap. The Financial Operations approval
  policy matrix service projects the
  same server-owned reviewer, permission, report-pack, checklist, and audit-event rules for
  configuration surfaces and accepts governed rule upserts with rationale, actor, correlation, and
  storage-root persistence under `governance/operations-approval-policy-rules.json`. The Financial
  Operations close calendar service consumes workflow reads through the module workflow service and
  projects each workflow's next due close task, owner, readiness score, component
  breakdown, blocker codes, next actions, and approval counts instead of
  client-local scheduling rules; governed owner/due-date overrides persist under
  `governance/operations-close-calendar-items.json` with actor, rationale, and correlation
  evidence. Payment approval and bank-side transaction records are also registered from
  `Meridian.FinancialOperations.Banking`; Application composition wires the module service but does
  not own the banking workflow state. Financial Operations ledger policy/projection services now
  own accounting-basis policy lookup and ledger write metadata stamping; application commands and
  composition consume those services. Ledger posting commands also enforce line-level Security Master symbol, identity,
  explicit approval reference, provenance, and ledger-mapping evidence for every instrument-bearing
  journal line before the durable journal can be appended, including securities-style account lines
  that omit symbol metadata. Candidate and line-level provenance must reference the resolved
  Security Master id carried by the journal metadata or instrument line, line status must be
  re-read from the server-side Security Master and still be active, and instrument line symbols
  must match the journal-level Security Master symbol before posting. Ledger-mapping references must also identify the same resolved symbol or Security Master
  id instead of using a generic account mapping token.
- Operations Continuity workflow DTO projection also derives the shared accounting-record summary
  from server-owned workflow state. The summary covers retained source records, normalized
  activity, reconciliation history, ledger evidence, approvals, report-pack lineage, export
  evidence, and restatement lineage so browser and WPF clients do not calculate accounting-record
  audit readiness locally. It also attaches the shared audit-pack readiness posture with measured
  timing, a 60-second target, missing evidence category keys, and warnings. Report-pack readiness is
  complete only after close-package publication evidence exists, so a ready report-pack id alone
  does not imply retained export, document, manifest, or restatement provenance.
- Financial Operations reconciliation integration - application command handlers and composition
  invoke `Meridian.FinancialOperations.Reconciliation` services for statement intake, validation,
  matching, decision journals, and statement-run persistence. Reconciliation workflow state, match
  rules, break classification, repository implementations, and durable case materialization are
  owned by the Financial Operations design module rather than the application layer. The
  Security Master-enriched portfolio-vs-ledger reconciliation engine also lives in Financial
  Operations and consumes the contracts-owned Security Master query interface.
- `Backfill/` - historical backfill request orchestration and execution coordination. Shared run
  results and per-symbol validation signals live in `Meridian.Contracts.Backfill`, while durable
  last-run status, checkpoints, and bar-count sidecars live in `Meridian.Storage.Backfill`.
- `ProviderRouting/` - relationship-aware provider capability routing. Provider-ledger accounting
  workflows use these capability gates to block missing balance/position/reconciliation feeds and
  degrade corporate-action or factor-schedule support when the account's provider route cannot
  supply the required feed.
- `Config/Credentials/` - application-owned credential testing, OAuth refresh, legacy resolver
  compatibility, CLI validation adapters, and composition support. Shared
  configuration JSON options, JSON Schema generation, FluentValidation rules, validation pipeline
  stages, credential placeholder detection, default config-path resolution, environment overrides,
  template generation, and config file hot-reload watching live in Core under
  `Meridian.Application.Config`; conversion from shared `StorageConfig` into durable
  `StorageOptions` lives in `Meridian.Storage`. Provider credential descriptors, encrypted vault storage,
  verification metadata, expiration/status records, OAuth token records, and provider-environment normalization now live in
  `Meridian.DataIntegration.Credentials`. Plaid setup remains credential-only from the application
  orchestration perspective: it stores client credentials through the Data Integration vault seam
  and does not seed a market-data `DataSourceConfig` or provider-routing binding. QuickBooks Online
  is cataloged by the Data Integration credential catalog as a credential-backed accounting-system
  provider; token exchange and GL evidence reads stay in the Data Integration provider seam and
  shared UI projection seam.
- `SecurityMaster/` - Security Master orchestration, aggregate rebuild helpers, instrument
  passport composition, and the ledger bridge that posts dividends, splits, distributions, and
  factor/principal paydowns into the Security Master ledger view for downstream reconciliation and
  valuation evidence. Asset-class mapping, instrument-kind mapping, the profile catalog contract,
  and seeded approved custom/private asset profile templates are owned by
  `Meridian.ReferenceData.SecurityMaster`; this folder consumes those reference-data contracts for
  validation, governance, readiness, projection rebuilds, and endpoint composition. Profile-backed
  validation rules still enforce approved profile-version pinning, typed no-code field values,
  profile approval metadata, and identifier coverage. Security Master create/amend orchestration preserves pinned
  profile-backed `CustomAsset` and `OtherSecurity` payloads in projection and event evidence while
  reusing the existing generic-security domain backing model. The query service keeps ordinary text
  search delegated to the storage index and uses the projected Security Master universe only when
  custom profile id, version, field-key, or field-value filters are supplied. Profile definitions
  are governed by `SecurityAssetProfileGovernanceService`, which merges seeded starter definitions
  with storage-root persisted drafts, approvals, rollback-created versions, and audit lineage.
  Security Master validation messages use operator-review wording for override audit remediation so
  application-layer guidance does not expose legacy Governance workspace language.
  `SecurityMasterOperationalReadinessService` layers operational readiness on top of the shared
  asset-class catalog, validator registry, and governed profile catalog for equities, options,
  futures, FX, fixed income, direct loans, structured/private `CustomAsset`, and `OtherSecurity`
  records. It declares required identifiers, economics, provider evidence, ledger classification,
  reconciliation signals, and close blockers while leaving missing live provider evidence as
  review-required/blocking evidence instead of fabricating completeness. Private-credit depth is
  represented on the canonical `DirectLoan` row with commitment, unfunded-commitment, paydown,
  covenant, and obligation evidence requirements. Structured and private assets stay on governed
  profile-backed `CustomAsset` rows, where servicer/trustee reports, warehouse tapes, NAV, capital
  calls, distributions, obligation schedules, and valuation approvals are treated as retained
  provider evidence before close readiness can become complete.
- Asset-specific instrument projection services for bonds, options, equity, futures, FX spot,
  crypto, deposits, certificates of deposit, commodities, swaps, money-market funds, and shared
  Asset Operations read/projection flows are owned by `Meridian.Instruments`. Money-market fund
  reference, liquidity, sweep-profile, family, and rebuild services are also owned there.
  Technical indicator calculation for live and historical market data also lives in
  `Meridian.Instruments.Indicators`, with Application no longer carrying the Skender indicator
  package dependency. Option-chain provider failover, discovery, quote/snapshot caching, and
  summary/status behavior live in `Meridian.Instruments.Options.OptionsChainService`.
  Application composition registers the Instruments services and storage-backed projection stores,
  but application orchestration no longer owns the instrument contract/reference lookup, asset
  operations projection, option-chain query behavior, or MMF reference/liquidity implementations.
- `FundStructure/` - organization, fund, portfolio, account, ledger-group, cash-flow, and ledger
  mapping workbench orchestration. The shared `IFundStructureService` contract lives in
  `Meridian.Contracts.Services`; the fund-account traversal query contract also lives there while
  Application keeps the current cached traversal implementation. The governance shared-data access
  contract also lives in `Meridian.Contracts.Services`; Application keeps the current
  Security Master, price, and backfill accessibility implementation, in-memory/PostgreSQL
  service implementations, and composition wiring. Local JSON and in-memory fund-structure state
  stores live in `Meridian.Storage.FundStructure` instead of Application. The PostgreSQL-backed service now supports the same shared
  governance cash-flow projection path as the local JSON/in-memory service, using stored
  structure rows plus fund-account snapshots, bank-statement rows, assignment metadata, and
  optional Security Master economic rules for realized/projected cash-flow evidence.
  Ownership-link policy validation is owned by `Meridian.Entities.FundStructure` and prevents
  invalid setup graphs by blocking self-parenting, active cycles, incompatible relationship types,
  overlapping primary links, invalid percentage ownership, sibling percentage over-allocation, and
  invalid effective windows before create, amend, expire, or replacement graph mutations are
  persisted. Ledger mapping orchestration stays server-side and consumes
  `Meridian.Entities.FundStructure.LedgerGroupingRules` for ledger-group assignment normalization
  and resolution before falling back to account ledger references.
- Identity integration - application composition wires the Identity-owned
  `FundStructureAccessScopeLineageProvider` against the shared fund-structure service contract.
  Scoped access assignments, auth role and permission contracts, user profiles, login sessions,
  auth-mode resolution, role-profile persistence, local JSON storage, and Postgres-backed scoped
  access persistence are owned by the Identity design module rather than the application layer.
- Environment Design integration - application composition registers the Workflow-owned
  `EnvironmentDesignerService` through `Meridian.Contracts.Services` interfaces. Lane defaults
  normalize legacy `Research`, `Data Operations`, and `Governance` workspace/page tags into the
  canonical operator roots (`Strategy`, `Data`, and `Accounting`) while validation accepts the full
  design-document root set: `Trading`, `Portfolio`, `Accounting`, `Reporting`, `Strategy`, `Data`,
  and `Settings`.
- `Scheduling/` - Application-owned backfill schedule managers and scheduled backfill execution
  coordination. The general operational scheduler implementation lives in
  `Meridian.Platform.Scheduling`, the US-market `TradingCalendar` implementation also lives in
  `Meridian.Platform.Scheduling`, and the scheduler/trading-calendar contracts live in
  `Meridian.Contracts.Services`.
- Portfolio Records integration - account query/management ports, internal account balance
  snapshots, statement intake, account readiness, provider-link history, reconciliation runs, and
  margin snapshots now live in `Meridian.PortfolioRecords`. Application composition registers those
  account-record services while orchestration consumers use the PortfolioRecords contracts.
- `Services/` - application use cases and orchestration services. Cross-domain trading-calendar
  status is provided by `Meridian.Platform.Scheduling.TradingCalendar` instead of an
  Application-owned service, and deployment/startup mode decisions use
  Platform-owned runtime helpers including `DeploymentContext` and
  `Meridian.Platform.Runtime.CliModeResolver` instead of Application-local runtime policy.
  Connectivity diagnostics and startup summaries use Platform runtime display helpers rather than
  Application-local display primitives. Runtime colocation profile activation is provided by
  `Meridian.Platform.Performance.CoLocationProfileActivator`. Hosted graceful-shutdown flush and
  shutdown sequence services live in Platform Runtime and consume the Core-owned
  `Meridian.Core.Services.IFlushable` and `IFlushableQueueDiagnostics` contracts. Application
  pipeline components expose queue diagnostics through that Core seam while consuming
  Platform-owned shutdown lifecycle diagnostics rather than defining shared lifecycle DTOs or
  diagnostic snapshots in Application. Diagnostic bundle generation lives in
  `Meridian.Platform.Diagnostics`; Application composition and endpoints consume it with Core-owned
  redaction/masking helpers, Platform-owned error tracking, and friendly error formatting rather
  than Application-local utility services. Sample market-event
  generation is registered from `Meridian.DataIntegration.Testing.SampleDataGenerator`. Canonical
  symbol resolution is registered from `Meridian.Storage.Services.CanonicalSymbolRegistry`.
  API documentation model generation is registered from
  `Meridian.Platform.ApiDocumentation.ApiDocumentationService`.
  Historical market-data JSONL query and bar aggregation is registered from
  `Meridian.DataIntegration.Historical.HistoricalDataQueryService`.
  Reconciliation governance exception classification now lives in
  `Meridian.Strategies.Services.GovernanceExceptionService`. Report-pack generation and NAV
  attribution live in `Meridian.Reporting` rather than Application-local services.
- `Monitoring/` - application monitoring adapters and status server support. The default
  static-backed implementation of the contracts-owned event metrics interface lives in
  `Meridian.Platform.Tracing`. Stored
  market-event schema compatibility checks, clock-skew estimation, spread monitoring, data-loss
  accounting, connection-health monitoring, connection-status notification, provider latency
  histograms, provider metrics status snapshots, provider degradation scoring/config/calibration records, market-data validators, and
  data-quality analyzers live in `Meridian.DataIntegration.Monitoring`. Runtime circuit-breaker
  status dashboards, pipeline backpressure alerting, alert dispatch, health aggregation, SLO
  definitions, and alert-runbook registries live in `Meridian.Platform.Monitoring`; shared alert and health-check contracts live
  in `Meridian.Core.Monitoring`; runtime error ring-buffer diagnostics and system-health snapshots
  live in `Meridian.Platform.Diagnostics`.
- `Http/` - core host-facing runtime services such as `ConfigStore`, `BackfillCoordinator`, and
  status response generation. ASP.NET endpoint adapter extensions for packaging, archive
  maintenance, and data-quality monitoring live in `Meridian.Ui.Shared.Endpoints`.
- `Composition/` - application feature registration and service wiring.

## Important workflows

Use this module when changing command behavior, workflow orchestration, feature registration, or
application service contracts consumed by host and UI surfaces.

Application consumes coordination through the shared contract ports and module-owned
implementations. Lease, leadership, scheduled-work ownership, subscription ownership, lease-record,
and coordination snapshot contracts live in `Meridian.Contracts.Coordination`; lease renewal,
cluster coordinator election, split-brain detection, scheduled-work ownership, and subscription
ownership services live in `Meridian.Platform.Coordination`; shared-storage lease persistence lives
in `Meridian.Storage.Coordination`. Application should wire and orchestrate those services rather
than owning cluster coordination primitives.

Application command handlers adapt design-module services to CLI flags. For example, `--selftest`
invokes the Data Integration-owned depth-buffer self-test runner instead of owning provider stream
validation logic in Application.

The interactive configuration wizard presents historical analysis and backtesting as the canonical
`Strategy` use case while retaining the older `Research` enum member only as a compatibility alias.
Backtest Studio run orchestration records accepted and terminal runs through the shared
`StrategyRunEntry` lineage model: `StrategyId`, `StrategyName`, run id, engine, dataset/feed
references, parameter set, sweep id, and canonical sweep-definition hash stay with the run evidence.
Studio run request, handle, status, engine contracts, and preflight trust-gate implementation now
live in `Meridian.Backtesting`; the shared Security Master validation gate abstraction lives in
`Meridian.Contracts.Services` while Application continues to provide the concrete Security Master
gate implementation and snapshot persistence.
Keep W6-BTSTUDIO-001 acceptance criteria in roadmap exit criteria and verify this lane with
`BacktestStudioRunOrchestratorTests` when changing backtesting evidence behavior.

## API contract notes

- Instruments-owned options-chain provider IDs are normalized with trim plus invariant lowercase
  before deduplication, health lookup, fallback detection, logging, and metrics.
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
