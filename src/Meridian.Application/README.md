---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-APP
path: src/Meridian.Application
status: active
owner_lane: Runtime Host
last_reviewed: 2026-07-19
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
  `Meridian.DataIntegration.Etl` contracts, normalization services, and job service/orchestrator.
  Application composes Data Integration-owned ETL behavior through `IEtlIngestionJobCoordinator`
  and `IEtlEventPipeline` adapters over the concrete `IngestionJobService` and `EventPipeline`
  implementations that still own runtime job lifecycle and publish-queue wiring here. The ETL
  export service now lives in `Meridian.DataIntegration.Etl`. The ETL job-definition store and
  SFTP publisher port contracts live in `Meridian.Contracts.Etl`, and the local JSON-backed
  job-definition store implementation lives in `Meridian.Storage.Etl`.
  SFTP transaction-file onboarding now includes non-mutating preview/list/test CLI modes,
  schema-aware CSV sampling for `bank.statement.csv.v1` and `bank.transactions.csv.v1`, explicit
  source post-processing options, and runtime SFTP capability checks so operators can validate
  connectivity, host-key pinning, and file shape before committing an import.
  ETL command execution now consumes the Data Integration-owned `VerifiedOperationOutcome` receipt:
  every admitted run returns `Succeeded`, `CompletedWithWarnings`, `Failed`, or `Blocked` with
  postconditions, evidence, artifacts, and recovery guidance. Required normalization, pipeline, and
  export stages cannot be collapsed into a successful CLI exit when a terminal write or export
  fails; blocked and failed receipts map to non-zero command results. Runbook commands use the same
  receipt contract and no longer treat a message-only handler response as execution evidence.
- `Integrations/` - provider integration template catalog, setup persistence, dry-run
  orchestration, and activation readiness. The catalog seeds the first no-code template pack for
  manual CSV upload, custodian positions, brokerage transactions, and fixed income security master.
  The setup service saves draft manifests and connection instances through the Storage-owned
  integration manifest store, preserving tenant partitioning and returning readiness blockers before
  dry runs or activation. The OpenAPI import service parses OpenAPI/Swagger JSON into tenant-scoped
  draft `OpenApiRest` manifests, imports endpoint definitions, response record paths, query
  parameters, and schema-backed mapping suggestions, and keeps trading actions blocked behind
  certified-adapter activation readiness. The manual CSV dry-run service consumes contract-owned manifests and the
  Storage-owned integration manifest store, parses operator-uploaded samples, applies configured
  field mappings and safe transforms, writes raw payload evidence, stages accepted records,
  quarantines rejected records, and saves sync-run summaries without promoting directly into
  Portfolio, Security Master, or Ledger stores. Staged record lineage prefers an explicit
  `sourceRecordId` when configured, then falls back to capability-specific canonical identifiers
  such as provider transaction id, provider account id, CUSIP/ISIN/security identifiers, and
  account-security-as-of composites before using row ordinals. The dry-run and quarantine-replay
  staging paths fail closed on duplicate dedupe keys inside the same run, quarantining later
  duplicate records instead of letting repeated provider identities enter reconciliation staging.
  They also quarantine mapped money values that have an amount without a currency, or an invalid
  three-letter currency code, so financial values cannot reach reconciliation staging with
  ambiguous denomination. Position and holding records that carry quantity, price, and market
  value are also checked before staging; if `quantity * price.amount` differs from
  `marketValue.amount` by more than one cent, the record stays in quarantine for operator review.
  When a workstation endpoint supplies tenant context,
  setup, dry-run, readiness, activation, and monitoring services resolve a tenant-scoped
  provider-integration store before reading or writing manifests, connections, and retained
  evidence. The schema-drift service compares retained raw payloads against configured records
  paths, required response paths, and required mapping source paths, returning a pause
  recommendation when critical provider-shape drift would make the capability unsafe to sync. The
  sync-planning service reads the manifest schedule plus retained sync-run history and returns
  per-capability due, not-due, manual-only, unsupported, or activation-blocked planning state
  without starting provider calls. The sync-orchestration service composes that plan with the REST
  dry-run runtime, skips blocked/manual/not-due capabilities, and starts due read-only REST,
  OpenAPI REST, or hybrid capabilities while retaining raw payloads and writing only staging or
  quarantine evidence. For endpoint chains such as accounts before positions, orchestration can run
  or reuse the dependency endpoint, read the dependency output path from retained raw payload
  evidence, and fan out child endpoint calls with the resolved path parameter. The REST dry-run
  service executes a configured read-only endpoint through an injectable transport, resolves
  path/query parameters, follows cursor pagination, retains raw responses before mapping, and uses
  the same staging and quarantine boundary for accepted and rejected records. The default
  `HttpClient` transport supplies the concrete HTTP execution path while tests can still inject
  deterministic transports. The monitoring service composes
  connection-level monitor and sync-run history read models from durable sync-run summaries,
  integration staging counts, quarantine counts, and retained validation issues so workstation
  surfaces can show dry-run evidence without reading storage internals. The staging review service
  returns accepted records, reconciliation-ready counts, validation warning groups, and capability
  summaries from durable staging records without promoting them to canonical stores. The identity
  resolution preview service inspects those staged records, extracts provider account and security
  identifiers, resolves active Security Master matches when the query service is registered, and
  returns account/security review blockers before any canonical promotion. The promotion-readiness
  service composes that identity posture into a read-only reconciliation staging preview, labeling
  accepted rows as ready, review-required, or blocked without writing Portfolio, Security Master,
  Ledger, or Accounting records. The reconciliation handoff service rechecks that readiness,
  persists only operator-approved ready rows with approval evidence, actor, timestamp, and
  account/security identity, and still leaves Portfolio, Security Master, Ledger, and Accounting
  mutation to later reconciliation-owned promotion. Handoff requests are idempotent by staging
  record, so retrying a row that already has handoff evidence is blocked and surfaced as a
  retained-history review issue. The quarantine
  review service groups rejected records by operator-safe issue code and records durable review decisions
  without mutating the retained raw rejected records. The quarantine replay service remaps reviewed
  rejected records after mapping changes, writes a replay raw payload, stages accepted records, and
  re-quarantines records that still fail validation. The activation-readiness service evaluates
  those manifests before enablement, blocking unresolved required mappings, missing approval
  evidence, and order-preview/place/cancel capabilities unless they use a certified provider
  adapter with production-write activation policy. The activation service
  persists manifest and connection `Active` state only after readiness passes with retained
  approval evidence, leaving failed activation attempts in draft state for operator review.
- Canonicalization composition consumes `Meridian.DataIntegration.Canonicalization` contracts,
  provider condition-code mapping, venue normalization, parity metrics, the default
  `EventCanonicalizer`, and the Data Integration-owned `CanonicalizingPublisher` decorator.
  Application still owns the concrete event pipeline, dead-letter/quarantine implementation, and
  composition wiring that supplies the Domain-owned quarantine sink port.
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
  ledger writes before the posting guard accepts direct-lending instrument lines. The outbox
  dispatcher bounds environment-driven batch size and poll interval values before polling the
  database-backed outbox, so an invalid override cannot disable the worker or turn it into a
  zero-delay retry loop.
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
  composition consume those services. Direct-lending event projections also attach deterministic idempotency, typed source evidence, ledger-book scope from the resolved posting period, and an accounting posting command before handing loan journal impact to durable storage. Ledger posting commands also enforce line-level Security Master symbol, identity,
  explicit approval reference, provenance, and ledger-mapping evidence for every instrument-bearing
  journal line before the durable journal can be appended, including securities-style account lines
  that omit symbol metadata. Candidate and line-level provenance must reference the resolved
  Security Master id carried by the journal metadata or instrument line, line status must be
  re-read from the server-side Security Master and still be active, and instrument line symbols
  must match the journal-level Security Master symbol before posting. Ledger-mapping references must also identify the same resolved symbol or Security Master
  id instead of using a generic account mapping token. Direct-lending journal projection now stamps
  every generated ledger line with the resolved ledger book, borrower legal-entity/counterparty,
  Security Master instrument, and loan account dimensions before durable storage receives the
  posting candidate, so downstream trial balance, journal, close, and reporting filters do not have
  to infer direct-lending scope from journal-level tags.
  `DirectLendingServicerStatementService` owns the first servicer statement intake slice for
  Direct Lending operations: it previews CSV/manual JSON position and remittance statements, maps
  rows through existing loan and Security Master references, retains accepted imports through the
  existing servicer report batch command, and applies only operator-selected remittance rows through
  existing payment, fee, and penalty commands.
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
  owned by the Financial Operations design module rather than the application layer. Statement
  validation and import commands report missing, inaccessible, or unreadable local source files as
  structured CLI failures before connector parsing. The
  Security Master-enriched portfolio-vs-ledger reconciliation engine also lives in Financial
  Operations and consumes the contracts-owned Security Master query interface.
- `Backfill/` - historical backfill request orchestration and execution coordination. Shared run
  results and per-symbol validation signals live in `Meridian.Contracts.Backfill`, while durable
  last-run status, checkpoints, and bar-count sidecars live in `Meridian.Storage.Backfill`.
  Automatic gap-analyzer remediation batches same-provider, same-window symbol gaps into one
  deterministic request and retained execution-history entry; data-quality and quality-alert
  remediation paths remain single-symbol signals. Auto-remediation execution history also retains
  SLA tier, due-time, owner-assignment, downstream-workflow, and reason-code metadata, and exposes
  `EvaluateRemediationSla` snapshots for overdue, due-soon, failed, open, and completed remediation
  items so critical paper, reconciliation, accounting, and reporting gaps can be distinguished from
  standard gap repairs before a full provider-governance workflow owns escalation timers. `BackfillCostEstimator` exposes adaptive
  partition plans for intraday and multi-year daily ranges so preview and cost-estimate callers can
  size provider windows before execution, and `HistoricalBackfillService` executes bounded requests
  through the same plan before writing per-symbol validation signals and checkpoints. Cost previews
  and execution normalize multi-symbol requests by trimming, dropping blanks, and de-duplicating
  case-insensitively while preserving the first-seen order and spelling, so provider calls and
  evidence rows cannot be duplicated by casing or whitespace variants.
  `CrossSourceBackfillReconciliationService` compares bounded daily bars across a baseline provider
  and one or more comparison providers for one symbol or a de-duplicated multi-symbol batch. Batch
  reconciliation normalizes symbols to uppercase, preserves the first-seen request order, and
  returns per-symbol price/volume drift, missing-session, symbol-mismatch, provider-error,
  missing-evidence, closure-status, and ordered review-symbol evidence for data-confidence workflows
  without promoting it to full cross-provider SLA enforcement. Provider responses for the wrong
  symbol are retained as review-required contamination evidence and filtered out of the matching bar
  set so same-date fallback data cannot falsely close the requested symbol; zero symbol-scoped
  provider bars likewise block closure until retained evidence exists.
  Backfill storage placement now consumes the Storage-owned `AdaptivePartitionPlacementPlanner`.
  Request-scoped options use hourly symbol partitions for intraday runs, provider/source-aware
  partitions for composite provider evidence, and monthly date partitions for long-window archival
  backfills while preserving the caller's compression, retention, sink, quota, and manifest
  settings.
- `ProviderRouting/` - relationship-aware provider capability routing. Provider-ledger accounting
  workflows use these capability gates to block missing balance/position/reconciliation feeds and
  degrade corporate-action or factor-schedule support when the account's provider route cannot
  supply the required feed.
- `Config/Credentials/` - application-owned credential testing, OAuth refresh, legacy resolver
  compatibility, CLI validation adapters, and composition support. Shared
  configuration JSON options, JSON Schema generation, FluentValidation rules, validation pipeline
  stages, credential placeholder detection, default config-path resolution, environment overrides,
  template generation, and config file hot-reload watching live in Core under
  `Meridian.Core.Config`; conversion from shared `StorageConfig` into durable
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
  valuation evidence. The compatibility factor bridge delegates economics to Instruments, requires
  held face, and posts scaled monetary principal rather than a dimensionless factor delta. It remains
  an in-memory reconciliation bridge; governed production posting still uses the Financial
  Operations candidate, independent approval, and durable journal path. Asset-class mapping,
  instrument-kind mapping, the profile catalog contract,
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
  application-layer guidance does not expose legacy Governance workspace language. Corporate-action
  appends are routed through `SecurityMasterCorporateActionCommandService` so HTTP endpoints,
  imports, provider backfills, and future UI commands share the same validation, append, and
  structured audit path before the event store is touched. That shared validation is driven by
  the contract-owned `CorporateActionTypeDescriptorCatalog`: unknown event types are rejected
  with the accepted vocabulary, per-type required fields are enforced, and — when the security
  projection is available — asset-class validity is checked (fail-open on lookup faults, so a
  projection-store outage never blocks an otherwise valid append). Amendments and cancellations
  are superseding events validated by `CorporateActionValidation.ValidateSupersede` (chain-tip
  only, same event type, no lifecycle regression); an accepted supersede flows through
  `ICorporateActionRestatementTrigger` into the existing period-aware restatement resolver so a
  provider correction landing in a closed ledger period yields a governed restatement proposal
  on the append result rather than a silent mutation. Stored legacy event-type aliases stay
  readable through the projector's normalization; the one-time
  `--security-master-normalize-corporate-actions` CLI sweep (dry-run by default, `--apply` to
  rewrite) cleans the stored strings themselves.
  `SecurityMasterCashFlowService` now generates deterministic calculated bullet and sinker
  schedules from retained Security Master economic terms when provider-backed schedules are not
  selected, so downstream Asset Operations views can present expected coupon/principal dates with
  source-governed scenario posture instead of an empty calculated schedule.
  `SecurityMasterOperationalReadinessService` layers operational readiness on top of the shared
  asset-class catalog, validator registry, and governed profile catalog for equities, options,
  futures, FX, fixed income, direct loans, structured credit, private fund interests, private
  company equity, real estate holdings, commitment/guarantee exposures, governed `CustomAsset`,
  and `OtherSecurity` records. It declares required identifiers, economics, provider evidence, ledger classification,
  reconciliation signals, and close blockers while leaving missing live provider evidence as
  review-required/blocking evidence instead of fabricating completeness. It also projects the
  contract-owned `SecurityAssetPackRegistry` into the shared multi-asset coverage payload so
  browser and WPF clients see the same asset-pack schema, lifecycle, lifecycle-event automation
  coverage, valuation, accounting-rule, structured journal-template, registry-validation status,
  validation, taxonomy, admission-policy, and automation-depth metadata as the contract layer.
  Bond rows expose factor and corporate-action drill-through proof, including retained factor
  evidence blockers, while private-credit depth is represented on the canonical `DirectLoan` row
  with commitment, unfunded-commitment, paydown, covenant, obligation, and
  `Meridian.FSharp.DirectLending.Aggregates` rule-kernel evidence requirements. Alternative asset
  rows now declare class-specific retained evidence: structured credit needs trustee/servicer,
  factor, collateral-tape, valuation-source, and cash-remittance support; private fund interests
  need administrator/GP, capital-call, distribution, NAV, and capital-account support; private
  company equity needs cap-table, share-class, financing, valuation/409A, transaction, exit, and
  dividend support; real estate holdings need property-manager, rent-roll, lease, appraisal,
  debt-service, ownership, and SPV support; commitments and guarantees need agreements, draw/usage
  notices, fee/accrual schedules, collateral/covenants, and release/expiry support. Profile-backed
  `CustomAsset` records remain the compatibility fallback when old profile metadata cannot be
  upgraded to one of those first-class rows.
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
- `FundStructure/` - organization, fund, portfolio, account, ledger-group, and cash-flow
  orchestration. The shared `IFundStructureService` contract lives in
  `Meridian.Contracts.Services`; the fund-account traversal query contract also lives there while
  Identity owns the cached traversal implementation for scoped-access and fund-account endpoint
  lineage. The governance shared-data access contract also lives in
  `Meridian.Contracts.Services`; Application keeps the current Security Master, price, and
  backfill accessibility implementation, in-memory/PostgreSQL service implementations, and
  composition wiring. Local JSON and in-memory fund-structure state
  stores live in `Meridian.Storage.FundStructure` instead of Application. The PostgreSQL-backed service now supports the same shared
  governance cash-flow projection path as the local JSON/in-memory service, using stored
  structure rows plus fund-account snapshots, bank-statement rows, assignment metadata, and
  optional Security Master economic rules for realized/projected cash-flow evidence.
  Ownership-link policy validation is owned by `Meridian.Entities.FundStructure` and prevents
  invalid setup graphs by blocking self-parenting, active cycles, incompatible relationship types,
  overlapping primary links, invalid percentage ownership, sibling percentage over-allocation, and
  invalid effective windows before create, amend, expire, or replacement graph mutations are
  persisted. Ledger mapping workbench projection also lives in
  `Meridian.Entities.FundStructure` and consumes `LedgerGroupingRules` for ledger-group assignment
  normalization and resolution before falling back to account ledger references.
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
  `Meridian.Platform.Performance.CoLocationProfileActivator`. Application startup composition owns
  the host-side lifecycle state machine, readiness checks, shutdown stage coordination, supervisor
  named-pipe bridge, and participant ordering. The participants consume Core-owned
  `Meridian.Core.Services.IFlushable` and `IFlushableQueueDiagnostics` contracts, while shared DTOs
  remain in Contracts and the installed process/database owner remains the Lifecycle Supervisor.
  The former Platform Runtime graceful-shutdown services are compatibility-only and are not newly
  registered. Diagnostic bundle generation lives in
  `Meridian.Platform.Diagnostics`; Application composition and endpoints consume it with Core-owned
  redaction/masking helpers, Platform-owned error tracking, and friendly error formatting rather
  than Application-local utility services. Sample market-event
  generation is registered outside Production from
  `Meridian.DataIntegration.Testing.SampleDataGenerator`; packaged production composition omits the
  fixture generator entirely. Canonical
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
- `Http/` - core host-facing runtime services such as `ConfigStore` and status response
  generation. ASP.NET endpoint adapter extensions for packaging, archive maintenance, and
  data-quality monitoring live in `Meridian.Ui.Shared.Endpoints`. `BackfillCoordinator` lives
  in `Backfill/` alongside the rest of the backfill pipeline.
- `Composition/` - application feature registration and service wiring.
  `StorageFeatureRegistration` keeps production-safe governance composition explicit: production
  startup requires `MERIDIAN_DATABASE_URL` (or the per-domain
  `MERIDIAN_FUND_ACCOUNTS_CONNECTION_STRING` and `MERIDIAN_FUND_STRUCTURE_CONNECTION_STRING`)
  so fund account and fund structure workflows use persistence-backed services.
  `MeridianDatabaseEnvironment.ApplyUnifiedDatabaseUrl` (in `Meridian.Storage`) propagates
  `MERIDIAN_DATABASE_URL` into every unset per-domain connection-string variable at composition
  time; `PersistenceConfigurationStatus.Evaluate` reports the resulting NONE/PARTIAL/CONFIGURED
  posture for status endpoints and readiness checks. Local/dev launcher flows may set
  `MERIDIAN_USE_INMEMORY_GOVERNANCE=true` only with a non-production environment. Placeholder
  projection-reconciliation jobs are also omitted from production composition until real domain
  reconcilers replace them; production startup does not report a no-op comparison as assurance.
  `ProviderFeatureRegistration` supplies a non-secret empty `IConfiguration` fallback before
  registering provider adapters, preserving host-provided configuration when present while keeping
  credential-gated data providers resolvable in composition slices.

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
