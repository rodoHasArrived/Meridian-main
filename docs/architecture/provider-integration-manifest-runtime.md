# Provider Integration Manifest Runtime

**Status:** accepted planning baseline
**Owner:** core-team
**Reviewed:** 2026-06-16

## Summary

Meridian should support no-code setup for read-oriented financial APIs by letting operators configure
provider capabilities, authentication, endpoints, mappings, schedules, and validation gates in guided
screens. Those screens produce a versioned `ProviderIntegrationManifest` that a generic connector
runtime can execute without adding custom code for every provider.

This design extends the [Core Extensibility Model](core-extensibility-model.md). Provider
integrations and data mappings are governed configuration layers around Meridian's stable financial
operations core; they contribute source evidence and canonical records, but they do not weaken audit,
lineage, approval, identity, or financial-calculation controls.

## Scope

In scope:

- REST, OpenAPI-described REST, GraphQL, webhook, SFTP, file, manual upload, and hybrid data intake.
- Read-only financial capabilities such as accounts, balances, positions, holdings, transactions,
  tax lots, security reference data, market prices, corporate actions, documents, alerts, and events.
- Versioned provider templates and tenant-specific connection instances.
- Raw payload retention, visual field mapping, safe transformations, validation, quarantine,
  identity resolution, canonical loading, sync monitoring, schema drift detection, and audit history.
- Browser and WPF operator surfaces backed by shared contracts and read models under `Data` and
  `Settings`.

Out of scope:

- Fully generic production trade submission from user-defined mappings.
- Arbitrary user-authored code in mappings or transforms.
- Mobile-specific setup or mobile-first integration workflows.
- Replacing certified provider adapters for FIX, complex streaming, or production execution APIs.

Trade execution remains a certified capability. Order preview, placement, cancellation, order status,
and fills must route through provider-specific adapters, entitlement checks, idempotency, sandbox
tests, approval workflows, kill switches, audit evidence, and reconciliation.

## Planning Position

This work is a W1-W5 operational-record accelerator, not a general no-code workflow product. It is
in scope only when it improves data confidence, retained source evidence, replayable imports,
reconciliation inputs, accounting records, governed reporting, or provider monitoring.

Planning assumptions:

- The first release should prove one read-only intake path end to end before broadening provider
  types.
- The first canonical data path should land in a staging or evidence store until validation,
  identity resolution, and downstream ownership are proven.
- Browser and WPF screens should share endpoint contracts and read models; the browser workstation
  can lead the guided setup flow, while WPF can initially consume monitoring and review surfaces.
- Provider templates are reusable product assets; connection instances are tenant or account-group
  configuration and carry credentials, schedules, approvals, and sync state.
- Any capability that can submit, cancel, or amend an order is blocked until a certified adapter
  path declares support and activation evidence proves sandbox, entitlement, approval, idempotency,
  and recovery controls.

Near-term non-goals:

- No generic production trade writer.
- No open-ended user scripting.
- No new mobile surface.
- No promise that every provider can be supported without certified adapter work.
- No roadmap completion claim until registry rows and acceptance evidence are updated.

## Resolved Planning Decisions

The following decisions are no longer open planning questions. They are the baseline for the first
implementation wave unless a later architecture decision record explicitly supersedes them.

| Question | Decision |
| --- | --- |
| First durable manifest store | Use a new integration-specific durable store under `src/Meridian.Storage/Integrations/`. Do not make the existing workstation file-backed configuration store the authoritative manifest store. |
| First template release | Ship all four templates in the first `ProviderIntegrationTemplateCatalog`: manual CSV upload, custodian positions, brokerage transactions, and fixed income security master. |
| OpenAPI import sequencing | Build the first setup slice around sample responses and guided manual endpoint definitions. OpenAPI import is a draft-seeding accelerator after mapping, validation, dry-run, replay, and activation gates exist; it is not the core runtime path. |
| First accepted-record writer | Write accepted records into integration staging first, then reconcile and promote into Portfolio, Security Master, Ledger, Reporting, or Accounting-owned stores only after lineage, dedupe, validation evidence, and ownership are proven. |

The workstation store may cache draft UI state, but approved manifests, raw payloads, sync runs,
quarantine records, quarantine review decisions, quarantine replay payloads, schema snapshots, and
replay evidence are operational records and need a storage-owned boundary with WAL or
`AtomicFileWriter` durability.

The first template release ships all four templates as a coherent pack:

- Manual CSV upload.
- Custodian positions.
- Brokerage transactions.
- Fixed income security master.

Manual CSV upload should be the first execution proof because it exercises sample ingestion,
visual mapping, transformations, validation, quarantine, replay, and staging without external API
fragility. Custodian positions should be the first API proof, brokerage transactions should follow
once enum/sign handling is proven, and fixed income security master should validate the richer
identifier and instrument-attribute path.

The first setup slice should use sample responses plus guided manual endpoint definitions before
operator-facing OpenAPI import. Visual mapping, transformation, validation, and replay are the core
product risks; OpenAPI import can seed endpoint, response-shape, query-parameter, and
mapping-suggestion drafts into the same manifest model after the manual runtime, dry-run,
validation, replay, and activation gates are in place.

Accepted records should first load into an integration staging store feeding reconciliation, not
directly into Portfolio, Security Master, or Ledger. Reconciliation and identity resolution should
promote staged records into source-owned canonical services only after accepted-record lineage,
dedupe keys, validation evidence, and downstream ownership are proven.

### First-Wave Commitments

| Area | Baseline commitment | Why |
| --- | --- | --- |
| Manifest store | Implement `src/Meridian.Storage/Integrations/` as the first durable store. | Manifests, raw payloads, quarantine records, quarantine review decisions, quarantine replay payloads, staging records, and replay evidence are operational records, not workstation preferences. |
| Template catalog | Implement manual CSV upload, custodian positions, brokerage transactions, and fixed income security master in the first catalog. | The four templates jointly prove file intake, REST intake, transaction semantics, and institutional reference-data richness. |
| First execution proof | Execute manual CSV first; execute custodian positions as the first REST proof. | CSV proves mapping and validation without network variance; custodian positions proves endpoint dependencies, pagination, and raw API payload retention. |
| Scheduled execution | Run due read-only REST/OpenAPI/hybrid capabilities through the sync-orchestration service, which composes sync planning with the REST dry-run runtime and resolves configured endpoint dependencies. | Scheduled execution should reuse raw payload retention, staging, quarantine, validation, dependency evidence, and sync-run evidence instead of introducing a second connector path. |
| OpenAPI import | Seed draft `OpenApiRest` manifests from OpenAPI/Swagger JSON after manual endpoint definition, sample response mapping, dry-run, validation, replay, and activation gates are stable. | OpenAPI is an accelerator over the manifest model, not a separate runtime or a substitute for mapping approval. |
| Accepted record writer | Write accepted records to integration staging first, then reconcile and promote into Portfolio, Security Master, Ledger, or Reporting-owned stores. | Staging preserves replay, identity review, dedupe, and downstream ownership before canonical mutation. |
| Trading boundary | Keep production trading out of the generic no-code runtime. | Order placement/cancel/amend flows require certified adapters, sandbox proof, idempotency, entitlement, approval, kill switch, audit, and reconciliation controls. |

## Architecture

```text
Connector Designer UI
    -> Versioned Provider Integration Manifest
    -> Generic Connector Runtime
    -> Raw Payload Store
    -> Record Extractor
    -> Mapping Engine
    -> Normalization Engine
    -> Validation Engine
    -> Identity Resolution Engine
    -> Canonical Writer
    -> Event Pipeline
    -> Portfolio / Accounting / Reporting / Monitoring workflows
```

The runtime supports a controlled set of integration patterns instead of truly unconstrained APIs:

| Integration type | No-code posture | Boundary |
| --- | --- | --- |
| REST API | Supported | Preferred generic path. |
| OpenAPI-described REST API | Supported | Can seed endpoints, schemas, and mappings. |
| GraphQL API | Supported with schema import | Requires introspection or documented samples. |
| Webhooks | Supported with templates | Requires signature verification and replay-safe event handling. |
| SFTP and file drops | Supported | Must preserve file hash, layout version, and receipt metadata. |
| CSV, Excel, and API hybrid | Supported | Common for custodians, administrators, accounting feeds, and tax lots. |
| Streaming APIs | Template-driven only | Complex streams should graduate to certified adapters. |
| FIX and production execution | Certified adapter only | Not a free-form no-code runtime path. |

## Interfaces And Models

New shared contracts should live under `src/Meridian.Contracts/Integrations/` and use source-generated
JSON serialization:

- `ProviderIntegrationManifestDto`
- `ProviderTemplateDto`
- `ProviderConnectionDto`
- `ProviderCapabilityDto`
- `EndpointDefinitionDto`
- `EndpointDependencyDto`
- `EndpointPaginationDto`
- `EndpointResponseShapeDto`
- `FieldMappingDto`
- `TransformRuleDto`
- `ValidationRuleDto`
- `SyncScheduleDto`
- `RawIngestionPayloadDto`
- `MappedRecordPreviewDto`
- `ValidationIssueDto`
- `QuarantinedRecordDto`
- `SchemaDriftIssueDto`
- `IntegrationActivationReadinessDto`

Core enums should be explicit:

- `IntegrationType`: `Rest`, `OpenApiRest`, `GraphQl`, `Webhook`, `SftpFile`, `ManualUpload`,
  `Hybrid`, `StreamingTemplate`, `CertifiedTradingAdapter`.
- `ProviderCapability`: `Accounts`, `Balances`, `Positions`, `Holdings`, `Transactions`, `TaxLots`,
  `SecurityReferenceData`, `MarketPrices`, `CorporateActions`, `Documents`, `Alerts`, `Events`,
  `OrderPreview`, `OrderPlacement`, `OrderCancellation`, `OrderStatus`, `Executions`.
- `ActivationState`: `Draft`, `Tested`, `DryRunPassed`, `PendingApproval`, `Active`, `Paused`,
  `Failed`, `Retired`.
- `ProcessingStatus`: `Received`, `Parsed`, `Mapped`, `Validated`, `Quarantined`, `Loaded`,
  `Published`, `Blocked`.

Application and infrastructure seams:

- `IProviderIntegrationManifestStore` stores templates, connection instances, manifest versions,
  approval state, and rollback metadata.
- `IGenericConnectorRunner` executes a manifest capability for a connection and produces a
  `SyncRunResult`.
- `IConnectorRequestBuilder` resolves auth, path parameters, query parameters, headers, request
  bodies, dependency outputs, cursors, and rate-limit policy.
- `IRawIngestionPayloadStore` preserves unchanged provider requests, responses, files, webhook
  events, source hashes, and mapping versions.
- `IRecordExtractor` applies JSONPath, GraphQL path, CSV/Excel layout, or file parser rules to
  produce source records.
- `IProviderMappingEngine` maps provider fields into canonical financial DTOs with confidence
  scores and transform evidence.
- `IProviderNormalizationEngine` parses dates, decimals, currencies, enum values, identifiers, and
  amount signs through approved functions.
- `IProviderValidationEngine` applies required field, type, range, duplicate, freshness, tolerance,
  and business-rule gates.
- `IProviderIdentityResolutionService` matches accounts, securities, orders, transactions, tax lots,
  portfolios, and legal entities.
- `IIntegrationStagingReviewService` exposes accepted, validation-passed records, warning groups,
  dedupe keys, and capability summaries so operators and reconciliation workflows can inspect the
  staging handoff before canonical promotion.
- `ProviderIntegrationIdentityResolutionPreviewService` reads staged records, extracts provider
  account ids and security identifiers, resolves active Security Master matches when that query
  service is registered, and returns review-required issue rows without mutating canonical stores.
- `ProviderIntegrationPromotionReadinessService` composes staging identity posture into a read-only
  reconciliation promotion preview. It labels each staged record as ready for reconciliation,
  review-required, or blocked, and keeps the first accepted-record writer pointed at integration
  staging rather than Portfolio, Security Master, Ledger, or Accounting stores.
- `ProviderIntegrationReconciliationHandoffService` persists operator-approved handoff evidence
  only for rows that remain ready for reconciliation. It records selected staging record ids,
  approval evidence, actor, timestamp, account/security identity, and the
  `reconciliation-staging` target without mutating downstream canonical stores. Handoffs are
  idempotent at the staging-record level: a row with retained handoff evidence is rejected on
  retry and the operator is directed back to handoff history.
- `ICanonicalFinancialDataWriter` promotes staged accepted records to the owned domain store only
  after reconciliation, identity, lineage, dedupe, and ownership gates pass.
- `IIntegrationQuarantineService` stores rejected records, issue groups, suggested fixes, replay
  state, and reviewer decisions.
- `IProviderSchemaDriftDetector` compares the latest response shape against the approved manifest
  shape and pauses affected capabilities when required fields disappear.
- `IIntegrationActivationService` evaluates safe activation gates and exposes readiness issues.
- `ProviderIntegrationSyncOrchestrationService` evaluates the current sync plan, skips blocked,
  manual-only, not-due, or unsupported capabilities, and starts due read-only REST/OpenAPI/hybrid
  capabilities through the same dry-run runtime used by setup tests. When an endpoint depends on a
  previous endpoint output, it runs or reuses the dependency endpoint, reads the configured output
  path from retained raw payload evidence, and fans out child endpoint calls with the resolved path
  parameter.

The first storage implementation should be local and self-hosted under
`src/Meridian.Storage/Integrations/`, but it must preserve the same semantic boundaries a
database-backed implementation would need:

```text
provider_templates
provider_connections
connection_capabilities
endpoint_definitions
field_mappings
sync_runs
raw_ingestion_payloads
quarantined_records
integration_audit_events
schema_drift_snapshots
integration_staging_records
reconciliation_handoff_records
```

Durable local storage should use existing Meridian durability patterns such as WAL or
`AtomicFileWriter`; source-generated JSON contexts must be added for any new retained manifest or
payload DTOs.

## Manifest Shape

The manifest should be declarative and versioned. Operators edit it through guided UI screens, not
raw YAML or JSON.

```yaml
manifestVersion: 1
providerId: custodian_abc
displayName: Custodian ABC
integrationType: rest
environment: production
auth:
  type: oauth2
  tokenUrl: https://api.example.com/oauth/token
  scopes:
    - accounts.read
    - positions.read
    - transactions.read
capabilities:
  - accounts
  - positions
  - transactions
endpoints:
  accounts:
    method: GET
    path: /v1/accounts
    response:
      recordsPath: $.accounts
    pagination:
      type: cursor
      cursorPath: $.nextCursor
      cursorParam: cursor
  positions:
    method: GET
    path: /v1/accounts/{accountId}/positions
    dependsOn: accounts
    response:
      recordsPath: $.positions
mappings:
  Position:
    providerAccountId:
      sourcePath: $.account_id
      required: true
    security.cusip:
      sourcePath: $.cusip
      transform: trimUppercase
    quantity:
      sourcePath: $.quantity
      transform: parseDecimal
    marketValue.amount:
      sourcePath: $.market_value
      transform: parseDecimal
    marketValue.currency:
      sourcePath: $.currency
      defaultValue: USD
    asOf:
      sourcePath: $.as_of_date
      transform:
        type: parseDate
        format: yyyy-MM-dd
sync:
  mode: incremental
  frequency: daily
  time: "06:00"
  timezone: America/New_York
  fullRefresh:
    frequency: monthly
  cursor:
    type: timestamp
    field: updated_at
validation:
  requiredFields:
    Position:
      - providerAccountId
      - quantity
      - asOf
  tolerances:
    marketValue: 0.01
activation:
  requiresDryRun: true
  requiresApproval: true
  productionWriteCapabilitiesAllowed: false
```

## Connector Designer UI

The operator experience should be progressive:

1. Provider details and integration type.
2. Capability selection.
3. Authentication setup using secret-safe fields.
4. API discovery through existing template, guided endpoint builder, sample response upload,
   assisted mapping, later OpenAPI import, or certified developer plugin.
5. Endpoint configuration for method, path parameters, query parameters, headers, body, pagination,
   rate limits, retries, dependency chains, and incremental sync.
6. Visual field mapping from provider sample fields to canonical financial fields.
7. Safe transformation builder.
8. Validation preview.
9. Identity matching policy.
10. Schedule and cursor policy.
11. API test console.
12. Activation readiness and approval.
13. Sync monitor and quarantine review.

The UI should start from the first template pack:

- Manual CSV upload for any supported read-only capability.
- Custodian positions.
- Brokerage transactions.
- Fixed income security master.

Additional templates should follow once the shared runtime is proven:

- Cash balances.
- Tax lots.
- Market quotes.
- Security reference data.
- Corporate action events.
- Trade order status.
- Documents and statements.

Mapping suggestions should carry confidence:

| Provider field | Suggested canonical field | Confidence |
| --- | --- | --- |
| account_number | providerAccountId | High |
| cusip | security.cusip | High |
| qty | quantity | High |
| mkt_value | marketValue.amount | Medium |
| ccy | marketValue.currency | High |
| as_of_dt | asOf | Medium |

High-confidence mappings can be preselected. Medium-confidence mappings require review. Required
fields with no approved mapping block activation.

## Safe Transform Library

The transform library should be configuration-driven and intentionally constrained:

- Parse date.
- Parse decimal.
- Default currency.
- Trim text.
- Uppercase identifiers.
- Map enum values.
- Apply amount sign by transaction type.
- Prefer identifiers by priority.
- Set constant values.
- Apply conditional mapping from approved predicates.

Free-form script transforms should require an admin or developer role, be disabled for production by
default, and run only through a separately reviewed execution sandbox if introduced later.

## Data Flow

1. `IIntegrationActivationService` confirms the connection has valid credentials, endpoint tests,
   required mappings, validation evidence, dry-run results, and approval evidence.
2. `IGenericConnectorRunner` starts a `sync_run` for a connection and capability.
3. `IConnectorRequestBuilder` builds the request from the manifest, connection instance, dependency
   outputs, and auth context resolved through the existing provider credential store.
4. The runtime calls the provider, receives a webhook, or reads a file.
5. `IRawIngestionPayloadStore` stores the unchanged request metadata, response or file payload,
   provider, endpoint, timestamp, connection, sync run, API version, mapping version, and processing
   status.
6. `IRecordExtractor` extracts source records from the configured response path or file layout.
7. `IProviderMappingEngine` maps records to canonical capability contracts.
8. `IProviderNormalizationEngine` applies approved transforms.
9. `IProviderValidationEngine` separates accepted and rejected records.
10. `IIntegrationQuarantineService` stores rejected records with issue groups and suggested fixes.
11. `IProviderIdentityResolutionService` resolves accepted records to internal accounts, securities,
    portfolios, transactions, and legal entities.
12. `ICanonicalFinancialDataWriter` writes accepted data first to integration staging records, then
    promotes reconciled records through existing source-owned services when identity and downstream
    ownership are proven.
13. The event pipeline publishes sync completion, validation, quarantine, and freshness events.
14. Monitoring read models update the Data workspace and downstream Accounting, Portfolio, Reporting,
    and Settings blockers.

## Canonical Capability Contracts

Each capability maps into a stable contract before it can affect operational workflows. The first
implementation should cover these read-oriented canonical records:

- `CanonicalAccountRecord`
- `CanonicalBalanceRecord`
- `CanonicalPositionRecord`
- `CanonicalTransactionRecord`
- `CanonicalTaxLotRecord`
- `CanonicalSecurityReferenceRecord`
- `CanonicalPriceRecord`
- `CanonicalCorporateActionRecord`
- `CanonicalDocumentRecord`
- `CanonicalProviderEventRecord`

Records should include provider id, connection id, source record id, source payload id, mapping
version, received timestamp, validation result, and optional internal identity references. These
fields are part of the operational proof chain, not optional UI metadata.

## Validation And Quarantine

Validation is capability-specific:

- Accounts require provider account id, recognized account type where supplied, valid base currency,
  and duplicate detection.
- Positions require numeric quantity, as-of date, at least one security identifier, currency when
  money fields are supplied, and market value tolerance checks when price is available.
- Transactions require transaction id, amount, mapped transaction type, date, currency, sign policy,
  and security identity for security-linked activity.
- Tax lots require account identity, security identity, acquisition date when supplied, quantity,
  cost basis, and as-of date.
- Prices require security identity, price, currency, as-of timestamp, source, and stale-data policy.

Rejected records move to quarantine rather than silently entering canonical stores. The review UI
groups issues by fix path, such as missing security identifier, unmapped transaction type, invalid
date format, unexpected enum value, duplicate source key, or unmatched account.

Replay must run from the retained raw payload and the selected manifest version so an operator can
repair mappings without reacquiring source data.

## Identity Resolution

Account matching should allow ordered strategies:

- Provider account id.
- Account number.
- Account name.
- Legal entity.
- Portfolio code.
- Custodian account number.
- Manually selected internal account.

Security matching should use priority rules:

1. Internal security id.
2. CUSIP.
3. ISIN.
4. SEDOL.
5. FIGI.
6. Provider security id.
7. Ticker plus exchange plus currency.
8. Manual review.

Ticker alone must not be the default institutional matching key because it is ambiguous, reused, and
often insufficient for fixed income or private assets.

## Activation And Permissions

Permissions should be role-based:

| Role | Capabilities |
| --- | --- |
| Viewer | View sync status, manifests, and redacted previews. |
| Operator | Run syncs and review quarantined records. |
| Configurer | Edit mappings, endpoints, schedules, and identity policies. |
| Approver | Approve production activation and promoted mapping versions. |
| Admin | Manage credentials and provider templates. |
| Developer | Add certified adapters or advanced transforms. |

Production activation requires:

1. Authentication test passed.
2. Endpoint test passed.
3. Sample data loaded and retained.
4. Required mappings approved.
5. Validation passed for representative samples.
6. Dry-run sync completed.
7. Reconciliation impact reviewed where the data affects close or reporting.
8. Approval evidence retained.
9. Scheduled sync explicitly enabled.

Activation readiness should fail closed using stable issue codes, including:

- `integration.<connectionId>.credential-state`
- `integration.<connectionId>.endpoint-test`
- `integration.<connectionId>.required-mapping`
- `integration.<connectionId>.validation-result`
- `integration.<connectionId>.dry-run-evidence`
- `integration.<connectionId>.approval-evidence`
- `integration.<connectionId>.trading-certification`

## Monitoring And Drift Detection

Every connection should expose a status read model:

- Connection health.
- Last successful sync.
- Next scheduled sync.
- Records received, accepted, loaded, and quarantined.
- Average sync duration.
- Enabled capabilities.
- Data freshness.
- Credential verification state.
- Mapping version.
- Schema drift state.
- Downstream blockers for accounting, reporting, portfolio, and close workflows.

Schema drift detection compares retained raw payloads with the approved manifest shape. The first
backend check resolves a stored raw payload and reports operator-safe drift issues when the
configured records path, required response paths, or required mapping source paths are missing; any
critical issue recommends pausing the affected capability before the next sync. Later drift checks
should extend this same result model to date-format, enum-value, and pagination-shape changes.

Operator messages should be business-readable:

- Authentication expired.
- Provider rate limit reached.
- Provider returned no positions.
- Required field is missing.
- Mapping no longer matches provider response.
- Security could not be matched.
- Transaction type is unmapped.

Raw stack traces belong in diagnostics and logs, not the non-technical setup flow.

## Existing Meridian Anchors

- `docs/product/meridian-design-document.md` already defines the data and integration flow:
  connect, acquire, validate, normalize, store, and publish with retained source evidence.
- `docs/architecture/core-extensibility-model.md` owns the stable-core boundary and names
  integrations plus data mappings as governed configuration layers.
- `docs/architecture/provider-management.md` documents the encrypted provider credential store,
  provider setup service, provider routing API, provider health, and data-quality monitoring seams.
- `src/Meridian.ProviderSdk/` and `src/Meridian.Infrastructure/` remain the right path for certified
  adapters and provider-specific code.
- `src/Meridian.Ui.Shared/` and `src/Meridian.Ui.Services/` should own shared workstation endpoint
  and read-model support before browser or WPF surfaces render the setup flow.
- `src/Meridian.Storage/` owns WAL, archival, and retained payload durability patterns.
- `src/Meridian.Execution/` and `src/Meridian.Risk/` own certified execution and pre-trade controls.

## Delivery Plan

The delivery plan should proceed by evidence gates, not by provider count. Each phase should leave a
usable operator artifact and a replayable proof path.

| Phase | Goal | Exit criteria |
| --- | --- | --- |
| 0. Decision record | Lock the boundary and template pack. | Storage owner, four-template pack, manual CSV first proof, custodian-position first API proof, integration-staging writer target, and activation gate issue codes are accepted. |
| 1. Manifest foundation | Persist draft provider templates and connection instances without secrets. | Manifest DTOs round-trip through source-generated JSON, credentials stay in the provider credential store, and activation readiness fails closed. |
| 2. Dry-run runner | Execute manual CSV upload and one REST pull-mode endpoint without loading canonical stores. | Raw payload retention, record extraction, cursor pagination, durable sync-run summaries, and dry-run summary output work for manual CSV and one custodian-position sample provider. |
| 3. Mapping and validation | Map one capability into canonical records and quarantine rejects. | Required-field mapping, safe transforms, validation issues, confidence scores, and replay from raw payload are tested. |
| 4. Operator setup | Expose guided setup and review through shared workstation endpoints. | Template catalog, manifest detail, OpenAPI import, setup-save, activation-readiness, activation, manual CSV dry-run, REST dry-run, quarantine review, quarantine replay, connection monitor, sync planning, sync-run history, staging review, identity-resolution preview, promotion readiness, and reconciliation-handoff endpoints now adapt starter manifests, tenant-scoped draft persistence, fail-closed blockers, active-state promotion, durable sync-run, staging, quarantine, review decisions, replay evidence, and validation evidence for WPF/browser consumers; the Settings Provider Connection Center now loads runtime evidence for routed connections, records review-only, replay-after-mapping, ignore-provider-record, and mark-as-cash-position quarantine decisions for retained records, shows backend-computed quarantine decision posture counts, and can trigger quarantine replay for retained batches, while full guided setup and quarantine-resolution screens still need to bind draft editing, tests, mapping preview, and corrective mapping/data-rule actions. |
| 5. Controlled load | Write accepted read-only records into integration staging and promote only after reconciliation. | Identity resolution, idempotent staging, downstream blockers, audit events, and reconciliation handoff are proven. |
| 6. Template expansion and OpenAPI import | Add more templates without changing the runtime contract; OpenAPI import is now available as a draft-seeding backend capability. | OpenAPI import seeds endpoint and schema drafts; additional providers or file modes reuse the same manifest, mapping, validation, and activation seams. |
| 7. Certified action boundary | Add controlled write capabilities only through certified adapters. | Sandbox tests, approval evidence, kill switch, idempotency, entitlement checks, and reconciliation are mandatory before production write activation. |

## Implementation Slices

1. Contracts and JSON context:
   Add manifest, endpoint, mapping, validation, sync, setup-save, quarantine, and
   activation-readiness DTOs.
2. Manifest store:
   Add `Meridian.Storage.Integrations` with local durable stores for provider templates, connection
   instances, manifest versions, raw payloads, quarantine records, quarantine review decisions,
   quarantine replay payloads, integration staging records, and activation evidence.
3. Runtime skeleton:
   Implement manual CSV upload and one REST pull-mode runner with raw payload retention, record
   extraction, and dry-run output only.
4. Mapping and validation:
   Implement manual CSV mapping, custodian positions, brokerage transactions, and fixed income
   security master templates with safe transforms and quarantine.
5. UI services and endpoints:
   Surface setup-save, OpenAPI import, sync planning, quarantine review/replay, and the connection
   monitor read model through shared workstation endpoints, then bind browser and WPF setup screens
   to dry-run evidence, activation blockers, due/manual/blocked sync state, sync runs, and
   quarantine groups. Add shared endpoints for catalog, connection draft, test auth, test endpoint,
   sample preview, mapping preview, dry run, activation readiness, activation, sync planning, sync
   runs, and quarantine.
6. Browser and WPF operator surfaces:
   Render the guided setup flow in the Data or Settings workspace using shared view models and
   route contracts.
7. Drift and monitoring:
   Add schema snapshots, drift issues, sync health, due-sync planning, and downstream blocker read
   models. The first sync-planning backend reports whether enabled capabilities are due, not due,
   manual-only, unsupported, or activation-blocked from the manifest schedule and retained sync-run
   history; background job orchestration and provider execution remain separate slices.
8. OpenAPI import:
   OpenAPI import now exists as a draft-seeding feature after manual endpoint definition, visual
   mapping, validation, and replay are proven. Browser and WPF setup screens still need to expose
   the route as a paste/upload action with mapping-review follow-through.
9. Certified trading boundary:
   Expose write capabilities only when a certified adapter declares support and activation evidence
   includes sandbox, approval, entitlement, idempotency, and kill-switch checks.

## Planning Gates

Before the next implementation slice:

- Use `src/Meridian.Storage/Integrations/` as the first durable storage owner for manifests, raw
  payloads, quarantine records, quarantine review decisions, quarantine replay payloads, staging
  records, and replay evidence.
- Keep the first template pack complete: manual CSV upload, custodian positions, brokerage
  transactions, and fixed income security master.
- Use manual CSV upload as the first execution proof and custodian positions as the first API
  proof.
- Use integration staging records as the first accepted-record write path feeding
  reconciliation.
- Keep expanding activation issue-code coverage as new templates and controlled actions are added.
- Identify which existing provider credential flows can be reused without adding new secret
  persistence.

Before scheduled sync is enabled:

- Raw payload retention works for successful and failed provider responses.
- Required mappings are complete and versioned.
- Dry-run evidence exists for representative records.
- Validation failures are visible in quarantine and can be replayed after mapping changes.
- Identity matching has a manual-review path for unresolved accounts and securities.
- Schema drift on required fields pauses the affected capability.
- Operator-facing monitoring shows freshness, counts, failures, and downstream blockers.

Before any write capability appears in production UI:

- A certified adapter owns the provider-specific request contract.
- Sandbox order preview, placement, cancellation, rejection handling, and recovery are tested.
- Entitlement, approval, pre-trade, idempotency, kill-switch, and reconciliation checks are active.
- Production activation requires explicit approver evidence and cannot be inherited from read-only
  sync approval.

## Acceptance Evidence

Minimum evidence for a read-only production activation:

- Manifest version and connection instance id.
- Credential verification result with redacted metadata only.
- Endpoint test transcript with request metadata and response shape.
- Raw payload id and source hash for sample records.
- Mapping version with approved required fields.
- Validation report with accepted, warned, and quarantined counts.
- Identity resolution report for accounts and securities.
- Integration staging record ids and dedupe keys.
- Dry-run sync result.
- Reconciliation impact statement when records feed close, accounting, or reporting.
- Approver, timestamp, reason, and retained approval evidence.

Staging source identity must not rely on row number when the mapped canonical record has a stronger
identifier. The runtime first uses an explicit `sourceRecordId`, then capability-specific
identifiers such as `providerTransactionId`, `providerAccountId`, security identifiers, or
account-security-date composites. Row ordinals are only a last-resort fallback for records that do
not expose a durable provider identity. This keeps retry, replay, quarantine review, promotion
readiness, and reconciliation handoff evidence tied to the provider's own record identity whenever
one exists. Duplicate dedupe keys within a dry run or replay are critical validation failures:
the first valid record may stage, while later records with the same provider identity are
quarantined for operator review before they can affect reconciliation staging.
Mapped money objects also fail closed when an amount is present without a currency or when the
currency is not a three-letter code, because accepted records must carry explicit denomination
before reconciliation, accounting, reporting, or portfolio promotion.
For positions and holdings, the first backend tolerance gate compares `quantity * price.amount`
against `marketValue.amount` when all three values are mapped; records outside the one-cent
tolerance are quarantined until the provider units, price, or market value are corrected.

Minimum evidence for promotion of a new template:

- Template manifest version.
- Supported capabilities.
- Required and recommended mappings.
- Provider-specific rate-limit and pagination assumptions.
- Sample payload or sanitized fixture.
- Mapping confidence fixture.
- Validation fixture.
- Drift-detection fixture.
- Activation-readiness fixture.
- Reconciliation-staging fixture.

## Test Plan

Contract tests:

- Manifest round-trip serialization through source-generated JSON.
- Validation of required fields, enum values, and activation issue codes.
- Backward-compatible manifest version migration.

Runtime tests:

- REST request build from path, query, headers, auth context, and dependency outputs.
- Pagination by page, offset, cursor, next URL, and timestamp cursor.
- Raw payload retention before mapping.
- Mapping preview with confidence scores.
- Transform library behavior for dates, decimals, currency defaults, enum maps, and amount signs.
- Validation split into accepted and quarantined records.
- Replay from raw payload using a new mapping version.
- Schema drift detection for missing fields, path changes, enum changes, and date-format changes.

UI service tests:

- Setup wizard draft state never returns secret values.
- Activation readiness blocks missing credentials, missing mappings, failed validation, missing dry
  run, missing approval evidence, and uncertified trading capability.
- Monitoring read models expose plain-language issue summaries.

Validation commands should start narrow:

```powershell
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~ProviderIntegrationManifest" /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~GenericConnectorRunner" /p:EnableWindowsTargeting=true
npm --prefix src/Meridian.Ui/dashboard run test:vitest -- src/screens/<integration-screen>.test.tsx
git diff --check -- docs/architecture/provider-integration-manifest-runtime.md docs/architecture/README.md docs/architecture/core-extensibility-model.md
```

If local MSBuild contention blocks proof, use the contention-aware `buildctl.py` runner or the
GitHub-hosted `Targeted Test` workflow with the same filters.

## Risks

- A generic connector can look more complete than it is. Activation readiness must distinguish
  draft, dry-run, active, blocked, and certified execution states.
- Weak mapping controls can pollute canonical stores. Required mappings, validation gates,
  quarantine, and replay must ship before scheduled production sync.
- Storing only normalized records would break auditability. Raw payload retention is mandatory.
- Secrets can leak through previews or manifests. Credentials must stay in `IProviderCredentialStore`
  or a stronger host-provided secrets manager, never in manifest JSON, endpoint responses, logs, or
  UI read models.
- Schema drift is normal for external APIs. Required-field drift should pause the affected
  capability instead of producing plausible-looking data.
- Trading requires stricter controls than read-only ingestion. Generic manifests must not be allowed
  to submit production orders.

## Remaining Open Questions

- After integration staging and reconciliation are proven, which promoted record type should be
  first to mutate its source-owned canonical store: positions into Portfolio, security reference
  data into Security Master, transactions into Portfolio and Ledger evidence, or accounting
  evidence into close/reporting workflows?
- Which provider-sanitized external fixtures should supplement the seeded manual CSV upload,
  custodian positions, brokerage transactions, and fixed income security master template tests?
