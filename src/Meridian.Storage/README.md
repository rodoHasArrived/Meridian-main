---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-STORAGE
path: src/Meridian.Storage
status: active
owner_lane: Accounting and Ledger
last_reviewed: 2026-11-13
---

# src/Meridian.Storage

## Purpose

Provide market-data storage and durable IO primitives: WAL, sinks, archives, replay, catalogs,
maintenance, packaging, backfill checkpoints and shared-storage coordination.

## Layer responsibility

The `Meridian.Storage` assembly owns storage primitives and does not depend on Ledger or Reporting.
Business adapters whose source remains under `Ledger/`, `Reporting/`, Security Master and other
business folders compile through linked items in `Meridian.Storage.Accounting`. The workflow
notes below cover these retained source paths; they do not move business ownership back into
Storage primitives.

## Key folders and files

- `Meridian.Storage.csproj` owns primitive compile items and explicitly excludes linked business adapters.
- `Archival/`, `Sinks/`, `Replay/`, `Catalog/` and `Maintenance/` implement durable market-data storage.
- `Export/` contains shared format writers; ledger projections compile in the Accounting adapter assembly.
- `../Meridian.Storage.Accounting/Meridian.Storage.Accounting.csproj` owns business adapters and migration content.

## Important workflows

Position snapshots preserve reviewed preparation lineages alongside economic-state history.
Historical reads still exclude lineage effective after their requested date.

`FileBondMarketDataStore` retains host-local typed bond market observations in hashed tenant
partitions with process-wide file leases, atomic publication, content validation and immutable
source revision identity. This store does not publish prices or post journals. Signed curves use
the existing PostgreSQL curve authority: each definition's immutable `rate_policy` is included in
identity comparisons and governance evidence; legacy definitions retain their default policy.

`FileBondProviderResponseStore` archives exact provider JSON text as UTF-8, addressed by its SHA-256
inside hashed tenant/company partitions. Immutable atomic publication supports concurrent retries;
scoped reads verify original content and metadata hashes and refuse corruption. Typed FRED inputs
pin their first retained response while later overlapping fetches retain their own response evidence.

Fixed-income stores retain scoped monitoring, saved runs and reviewed performance transaction
populations. Account revision authority and curve-policy migrations preserve earlier rows; coupon
recognition receipts participate in the existing governed journal/lot transaction. Restore retained
evidence on corruption rather than recreating empty history. See [bond operations](../../docs/operators/bond-workflows.md).

Coupon and terminal-principal writers take a history lock before any authority read. Ordinary
journal writers take the compatible writer mode, so unrelated ordinary writes can proceed while
coupon history remains stable through its validation and commit. Both ordinary and atomic entry
paths select the lock mode before reading; stale serializable snapshots still fail explicitly.

`AnalysisExportService.WriteInvariantTextTableAsync` writes schema-preserving text tables as CSV,
XLSX, Parquet or Arrow. It owns format IO, formula-safe spreadsheet escaping and checksums without
interpreting accounting records. Ledger-backed `ExportLedgerAsync` projections are extension methods
compiled by `Meridian.Storage.Accounting`, keeping the primitives assembly independent of Ledger.

The business adapters in the linked FundStructure and Ledger folders compile into
`Meridian.Storage.Accounting`; generic storage primitives remain in `Meridian.Storage`.

The linked ledger adapter commits immutable native balance checkpoints with governed book
hard-close events and their audited digests. Its as-of balance capability combines the retained
prefix with a verified journal tail in one repeatable-read transaction, preserving reopened and
backdated activity, future-dated postings, exact dimensions and known zero accounts. See the
[accounting adapter guide](../Meridian.Storage.Accounting/README.md#durable-balance-checkpoints)
for the audit boundary and compatibility-store fallback.

Intercompany accounting profiles link to the canonical legal registry through exact approved
GUID/version/payload-hash pins. Legal terms and independent legal approval remain in that registry;
profiles own operational templates, party-book/account mappings and bounded validity only. Native
legal line dimensions and accounting-profile dimensions remain separate through storage, review,
source evidence and both workstations. See [the implementation plan](../../docs/engineering/plans/intercompany-entity-controls.md).

Reporting cutovers retain immutable previews/receipts in ledger migration 065 and fence activation
against the ledger append audit head. Migration 066 preserves ordinary/source book uniqueness while
allowing distinctly named Fund/Primary books only for exact governed consolidation policy pairs.

Intercompany storage retains append-only account-role revisions, atomic source-journal proposal
intent, approved FX revisions and legacy-attribution sidecars over exact immutable journal digests.
PostgreSQL supplies authority leases through financial commit; file stores provide durable local/demo
parity without a production or cross-process fencing claim. Migration keeps old journal/evidence
bytes and refuses unresolved history. See [the operator procedure](../../docs/operators/intercompany-entity-controls.md).

Entity lifecycle history uses immutable tenant-bound timeline revisions and authority leases.
Ledger migration 061 initializes existing books in `LegacyWarning` and stamps new books
`Enforced` at insertion. Activation recomputes migration preflight while holding its authority
leases and retains an immutable receipt. Every booked append, including ambient transactions
and atomic lot posting, requires a matching posting-authority scope retained through commit.
Controlled-close authorizations bind an approved draft's economics, evidence and version;
they permit no edits to posted journals. Unmapped historical periods retain explicit warnings.

JSONL and Parquet sinks retire empty partition buffers after successful flushes. Admission and
retirement share the buffer lock, so a producer holding an evicted buffer retries admission to
the current buffer. Failed flushes restore their batches and retain the buffer. Parquet filenames
and policy directories both use the event's UTC date. `CompositeSink` in `FailOnAnyFailure` mode
retains per-child acknowledgements for the same in-process event admission, preventing retries
from re-appending to successful children; this is not cross-process replay deduplication.
`CatalogSyncSink` carries whole-file counts and ranges across flush windows and restarts, with
directory indexes persisted when a file first enters the catalog.

`ListTaxLotAccrualSnapshotsAsync` reads one tenant/book/security/position scope under repeatable
read. It reconstructs daily open shares and pre-paydown interest factors from retained mutations,
including the final interest day of a fully paid-down lot. It preserves same-day disposals and
refuses incomplete quantity histories or missing acquisition face terms. The read does not
change posted journals, lot facts or current concurrency versions.

Ledger-period reopen and shared instrument-role approval validation reject reserved W14 preparer
assertion references before governed writes. Instrument-role validation applies to both in-memory
and PostgreSQL projection stores. A preparer assertion cannot supply ordinary write approval.

`ILedgerSecurityEntitlementInventory` adds a complete, unpaged book/security inventory for bounded
cash-dividend eligibility. The PostgreSQL reader requires exact authenticated tenant/company book
ownership, inspects all book lots for unidentified legacy positions, verifies acquisition journals,
and reconstructs dated quantities with the existing retained mutation-history rules in a repeatable
read transaction. Its retained population and hash include later-disposed lots when held at the
cutoff; missing identity, acquisition or historical quantity facts fail closed. Existing account and
position lot-read APIs keep their behavior.

The separate economic-attestation store also reads every retained processing option and evidence
record for the exact tenant/company case version. That repeatable-read snapshot supports explicit
case-fact exclusions without inferring tax or election terms from freeform document prose. Its
content digest excludes workflow-only case-version increments so recovery after an attestation
stage can reuse unchanged source facts.

The bounded W14 cash-dividend path uses a native PostgreSQL book declaration, separate immutable
capacity reservations, and a sealed invocation lease. Migration 066 preserves ordinary approval
rows and adds distinct preparer receipts and posted proof. Source ex-date fixes the monthly bucket;
provider aliases cannot move a retained event into another bucket. Exact reaffirmation reuses its
reservation, while rollback or withdrawal never releases uncertain capacity. Declaration withdrawal
serializes with the journal, both lifecycle stages and the case receipt in one database transaction.
Posting locks run in this order: economic owner scope, complete-book entitlement advisory lock,
ledger book row, Security Master parent row, canonical security chain, and exact case. Parent-row
share authority freezes current security metadata; the existing chain lock prevents cancellation
or supersession. Ordinary journal, lot, backfill, position and book-specific accounting-configuration
writers take the matching shared lock first, keeping the exact promoted account/rule binding
stable through posting as well as the inventory.
The producer also rereads the retained candidate's dated ticker against the current native
Security Master identity and version in that held transaction; missing, ambiguous or changed
instrument authority refuses before the journal append.
Only that lease permits the native preparer-attested journal and spine append. Host policy defaults
off, and enabling the declaration requires a separately authorized explicit existing account/rule
binding. All receipts retain the original declaration, reviewed projection and evidence digest.
These new economic transactions use ReadCommitted after acquiring the exclusive tenant/company
scope lock: a queued command sees the preceding budget commit rather than retaining a snapshot
from before its lock wait. Exact case versions, the complete-book inventory lock, period-row
locking and the event-stream compare-and-swap remain enforced through the final commit. Ordinary
transaction isolation is unchanged.

Maintenance windows and trading-session checks use the configured `PrimaryTimeZone`,
including the starting weekday of overnight windows and the offset at each window boundary.
The scheduler scans deferred queue entries without blocking other due jobs, and counts running
admissions before case-history writes complete when enforcing scheduled-job concurrency.
Explicit `ExecuteJobAsync` calls retain their existing manual-execution bypass of window admission.

Date-only natural-language search filters use UTC midnight boundaries. Parquet timestamp-column
conversion shares the export timestamp parser, treating date-only strings and unspecified
`DateTime` values as UTC while preserving the instant carried by explicit timestamp offsets.

`PostgresIntercompanyAgreementStore` uses fund-structure migration `006`: a tenant-qualified head
points to immutable command snapshots, approved revisions, audit decisions and command receipts.
One command atomically checks its expected head and retains its result; command identities are
unique within a tenant. Database guards refuse history rewrites and tenant reassignment. Party
reads remain stable under entity authority locks; sorted shared agreement leases survive through
the ledger store's owned commit. Explicit references require that authority, while unsupported
caller-owned append paths refuse them. See [intercompany agreements](../../docs/domain/intercompany-agreements.md).

`LedgerOpenLotProjection` uses shared successor quote validation without rounding retained basis.
The exception still requires unit-successor lineage, an originating atomic mutation batch and a
retained basis adjustment; ordinary acquisitions retain exact quantity/price/basis checks.

`StorageCatalogService` publishes content-addressed immutable revisions containing the complete
manifest and directory indexes. Its current manifest remains compatible with catalog readers and
pins the exact retained revision digest. Publication compares the retained current generation under
a cross-process file lease; stale publishers fail explicitly. Restart verifies the referenced bytes
and ignores loose `_index.json` compatibility exports and unpublished candidates. Legacy migration
retains only the surviving complete catalog; corruption or missing revisions require recovery.
Mutations commit a complete generation before one in-memory publication, and file/metadata getters
return detached snapshots. Integrity verification is bound to the generation it inspected.
Compatibility exports rewrite only directories affected by a mutation. Root scans use the same
empty directory identity as updates and rebuilds; trailing root separators preserve containment checks.

`DataLineageService` and `MetadataTagService` fence read-modify-replace across instances and processes,
reload retained state under that lease, and publish only after durable persistence. Corrupt,
unsupported, incomplete or missing previously observed snapshots fail closed instead of erasing
history. Contention and cancellation do not publish unfinished writes. Focused synthetic coverage
lives in `StorageCatalogServiceTests`, `DataLineageServiceTests`, and `MetadataTagServiceTests`.
The catalog and provider-manifest/replay fixtures run in `tests/Meridian.Tests`; metadata, lineage,
and catalog-sink fixtures run in `tests/Meridian.Storage.Tests`. Shared compile ownership includes
both metadata/lineage partial files, so discovery does not split their fixture state across assemblies.

`FileProviderIntegrationManifestStore` stores identity decisions under each sync run's
`identity-decisions` directory. Entity locking protects append-only retention: identical retries
reuse the existing decision, while a changed payload under the same decision ID is refused.
Staged source records remain unchanged.

Security Master migration 065 adds nullable quotation evidence and an unconstrained `price_exact`
column for new CLR-decimal observations. The existing `price numeric(28,10)` column remains a
compatibility mirror, including its limit of eighteen integer digits; new values outside that
range are still refused. Reads and same-timestamp idempotence use `COALESCE(price_exact, price)`
so quotes that share a rounded mirror remain distinct. Existing rows keep null exact prices and
unknown quotation metadata: no missing historical digits are backfilled. Nullable metadata is
omitted from legacy canonical serialization, and retained receipt payloads and hashes stay unchanged.
Curve point JSON can retain typed rate observations and reviewed construction
evidence; only evidence-bearing versions add it to content hashes, preserving legacy curve hashes.
Typed-rate retries compare all retained source and construction facts except retrieval time under
the curve row lock. A retry returns its original immutable version, hash and evidence, including
after a newer distinct publication; it never makes the older observation latest again.

Hard close now retains `LedgerPeriodCloseSnapshot` in the same PostgreSQL transaction as the
period transition, close event and audit fact. Migration 055 adds immutable, versioned snapshots.
The canonical source-generated payload includes the scoped book journal history through the closing
effective date, including opening history, closing entries, currencies, dimensions and source lineage.
SHA-256 verification precedes every read. Earlier-period fences and the audit boundary prevent a
concurrent posting from producing a partly observed close population. An exact lost-acknowledgement
retry returns the original close version; altered close commands conflict. Reopen/reclose retains
earlier snapshots. Pre-migration closes explicitly have no snapshot; they are never fabricated.

Focused coverage: `LedgerPeriodCloseSnapshotTests` includes deterministic hashing, scope/content
corruption, restart replay, immutable retention and rollback when snapshot persistence fails.

Portfolio opening balances recheck their account cutoff inside the journal append transaction,
after the shared ledger audit-head lock. Prior account activity at or before the snapshot date
blocks the opening; once an opening posts, backdated originating activity at or before that
cutoff requires a governed adjustment. Later activity and independently scoped accounts remain
eligible. This protects the approval interval and concurrent postings as well as draft preparation.
Declared line functional currencies must match the retained ledger book's base currency.
Mixed-security prior-period adjustments require the retained hard-closed root, exact same-book
predecessor, approved typed correction and complete native inverse. Rebook replacements balance
separately. The append holds the target update fence and a shared root fence through commit;
ordinary callers cannot enable per-line lineage using a prior-period key prefix alone.

`Export/LedgerJournalExporter` produces CSV and Parquet rows from immutable journal lines. It
preserves audit metadata, dimensional scope and transaction-currency evidence through source-generated
JSON envelopes; exports from retained `LedgerJournalEntryRecord` populations additionally retain
posting policy, rule, source event, period and approval metadata. Monetary columns use invariant
decimal text in both formats to preserve full decimal precision across readers. An optional external
account map must cover every exported source account; it never changes the original journal.

Security Master migration 062 now checks its apply conditions in the runner without changing
the shipped SQL: upgrades require explicit stopped-writer and retired-legacy-writer attestations,
hold source-table write locks through commit, and refuse the usual thirty-second command timeout
above one million identifier rows. Fresh installations and already-applied restarts are exempt
from upgrade attestations. See the [migration procedure](../../docs/operators/security-master-governed-edit-rollout.md#identifier-history-migration-062).

`BackfillStatusStore` keeps a verified coverage sidecar (`_status/backfill-symbol-coverage.json`, atomic write): per symbol, granularity, provider and feed, the session ranges whose bars were written. Backfill resume reads only this sidecar; the date-only checkpoint and bar-count sidecars remain for status surfaces and are not evidence for skipping. An unreadable or newer-format coverage file is treated as no coverage. `BackfillCoverageMath` owns the range merge and missing-range arithmetic.

`JsonlBackfillBarWriter` gives every granularity its own file prefix (`bar_4hour`, `bar_weekly`, `bar_monthly` alongside the existing ones) and throws for an unknown granularity instead of writing it as `bar_daily`.

`SharedStorageCoordinationStore` accepts an optional `TimeProvider` for lease expiry and takeover decisions (system clock by default).

Maintenance schedule and history stores bound their cross-process lock acquisition (default 30 seconds) and throw a `TimeoutException` naming the lock, instead of retrying forever during construction.

Archive maintenance status counts today's executions from the start of the UTC day rather than a host-offset-shifted midnight.

`CompositeSink` tolerates individual sink failures under `ContinueOnPartialFailure` only while at least
one sink accepts the write. When no sink accepts an append, or no sink can be flushed because every
circuit is open, it throws, so the pipeline never treats an unpersisted event as durable or truncates
the WAL holding it. Under `FailOnAnyFailure` an append also throws while any sink's circuit breaker
is open, instead of skipping that sink and writing to the healthy ones only.

Native capital-call journals require approved commitment/call authority at the final PostgreSQL
append boundary. Posting serializes with commitment amendments, rechecks the effective revision,
unchanged allocation basis and remaining capacity, and commits one receipt with the journal.
Commitment approval, call retention, and final issuance validate cumulative called capital at every
posted issuance and effective-revision date under that same lock. Allocation is measured as of the
notice date while approved future reductions and later posted calls remain binding on the timeline.
External capital-call append transactions must use `ReadCommitted` or `Serializable`; repeatable-read
and other unsupported isolation levels refuse before any capital write because their retained
snapshot can hide a successor approval after the commitment lock is acquired. Ordinary journals
retain their existing transaction contract.
Competing drafts reserve no capacity. Funding additionally locks the retained `CapitalCallFunding`
bank transaction in the same database and enforces unique receipt/bank-line consumption. The
`PrivateCapitalBankingSchema` ledger option defaults to `banking`; absent or separate-database
banking authority refuses funding. Unreconciled historical capital activity blocks native posting.

Ledger migration `054` adds private-capital commitment revisions, immutable call plans, and posting
receipts in the same PostgreSQL database as journals. `PostgresPrivateCapitalStore` checks retained
book/entity/currency ownership and serializes commitment amendments with final posting through the
same commitment advisory-lock identity. Historical capital journals without native receipts require
opening reconciliation. Funding receipt and bank-line uniqueness span the owning tenant/company,
and database triggers protect approved authority, retained calls, and posting receipts from rewrite.

Holder-side private-fund asset observations use the separate `IPrivateCapitalRecordStore`, not
issuer-side `PostgresPrivateCapitalStore` call authority. Fund Accounts migration `009` adds
`private_capital_record`: immutable register entries, original manual/CSV import inputs, reviewed
previews, source-reported snapshots and command receipts, partitioned by tenant and company.
Amounts remain exact decimal strings inside retained JSON; missing source values remain null.
Explicit snapshot supersession records corrections, and the unique correction index refuses a
second successor for the same retained observation. No legacy assignment is promoted to a
measured balance, and no historical monetary digits are invented or backfilled.

`PostgresPrivateCapitalRecordStore` uses the existing Fund Accounts connection, schema and
migration runner. A transaction-scoped partition advisory lock serializes validation against
current correction heads, append and receipt retention; failure rolls back the complete batch.
`FilePrivateCapitalRecordStore` is the durable alternative when that connection is absent. It
hashes the tenant/company identity into the partition path, checks the retained envelope scope,
holds an exclusive cross-process writer lock, reloads current records under that lock and uses
`AtomicFileWriter` to publish the complete replacement. Neither implementation silently replaces
or deletes prior records. Repeating a command with identical reviewed inputs returns its retained
outcome after restart; changed inputs under the same command identity conflict. Focused coverage
is in `PrivateCapitalRecordServiceTests`, `PrivateCapitalRecordAuthorityTests`,
`PrivateCapitalRecordEndpointTests` and `PostgresPrivateCapitalRecordStoreTests`.

`JsonlAppendWriter` appends lines to an append-only JSONL log in place and fsyncs them, truncating a
torn final line first; `JsonlStorageSink` uses the same torn-tail repair for its day files. Use it
for logs that grow with every event. `AtomicFileWriter.AppendLinesAsync` still copies the whole
destination per call and remains for small, low-frequency artifacts.

Workstation SQL queries retain a completed result once for export. `DataQuery.MaxRows` limits
the preview; SQL-authored limits remain part of the result. Retention defaults are 250,000 rows,
32 MiB per execution, 128 MiB across retained executions and 30 minutes. The optional
`RetainedResultsRoot` must resolve outside the queryable storage tree. The private default path
and owner binding include the canonical source identity; tokens also bind tenant, company and
operator. An incomplete, expired, corrupt, over-quota or foreign result cannot export. Wide decimal
values retain their exact digits; unsupported complex/time types require an explicit SQL cast.

Corporate-action inbox exports read every actionable provider-economic reference observation and
the authenticated tenant/company's processing cases within one repeatable-read cut. The normal
inbox retains its browse limits. The shared export adapter distinguishes global source references
from scoped cases and includes only fields already exposed by the corresponding browse projection.

`Workstation/` retains operator triage metadata separately from financial source workflows.
`PostgresOperatorTriageStore` uses the shared migration runner and a tenant/company/item advisory
lock to commit state, activity and idempotency receipts together. Production triage defaults to the
`operator_triage` schema (`MERIDIAN_OPERATOR_TRIAGE_SCHEMA` overrides it). `FileOperatorTriageStore`
supports deterministic synthetic hosts with cross-process writer leases and atomic replacement;
it is not selected as a production fallback.

Ledger migrations 069–071 retain operator-delegated synthetic fill authority, the immutable policy
snapshot used after restart, and a shared unique fee identity across automatic fills and reviewed
reconciliation corrections. The write boundary validates exact authority, typed USD economics and
scope in the posting transaction. Fee uniqueness is independent of run/draft/fill identities, so
competing handoffs cannot double-book an expense. Exact receipt recovery remains idempotent; linked
human reversal/rebook flows retain their separate correction lineage.

`GeneralLedgerExportBuilder` freezes one `general-ledger.v1` journal-line dataset into a ZIP of
CSV, exact text-cell XLSX, typed-decimal Parquet, symbol P&L CSV/JSON, and an agreement/evidence
manifest. Every artifact is hashed; the snapshot digest binds book, period/version, ordered lines,
retained disposal history, historical open positions and any selected approved marks/reference data.
Legacy currency detail remains explicitly `book-base-currency-only`; transaction currency is never
invented. The bounded export accepts at most 100,000 journal lines/history rows and refuses decimal
values that cannot be encoded exactly. CSV neutralizes formula-looking text; XLSX retains source
text in inline-string cells so neither formulas nor floating-point coercion alter it.

`ILedgerOpenPositionReportingSnapshotSource` is an opt-in reporting capture. PostgreSQL reads full
immutable lot after-snapshots in the journal's repeatable-read transaction, restoring historical
quantity **and basis**; it does not substitute the current basis used by the quantity-only historical
helper. An explicit query `EffectiveTo`/`OccurredTo` also cuts the lot versions at that exact
effective date/UTC occurrence time; an admitted mutation cannot depend on an excluded prior state.
Missing, discontinuous or retrodated mutation chains block unrealized P&L explicitly.
Inventory coverage failures are scoped to their financial account and symbol. Canonical historical
acquisition accounts establish lot-carrying roles; typed, source-backed dividend receivables and
Direct Lending subledger assets retain their independent accounting roles. Unknown instrument
assets still block their own scope, so renamed journal-only equities cannot masquerade as zero lots.
Native cash-dividend preparer receipts explain only their exact retained USD receivable role and
source binding; this classification does not convert preparer attestation into independent approval.
`GeneralLedgerSymbolPnlBuilder` reconciles canonical disposal proceeds/basis and booked/economic
realized results. Unrealized v1 supports long unit common equity with same-currency basis, historical
instrument currency identity and an independently approved per-unit close snapshot. Other asset,
multiplier or FX cases remain unavailable with reasons, never fabricated zeroes. Exports do not
certify close or tax finality, post journals, or claim compatibility with a named external GL.

Automatic fill accounting has a separate, explicitly delegated system origin. Ledger migration 069
retains operator-activated grants for one tenant, financial account, USD book, open period, policy
version, accounting-mapping revision, and synthetic source mode. The journal append transaction
locks and rechecks the retained grant and mapping revision; revocation blocks new appends while
exact retained replay remains available. Ordinary fills require atomic canonical-lot publication;
only commission and identified fee journals use the journal-only path. Assistant origins and
human approval workflows retain their existing guards. Grants are never activated at startup.
Focused proof is `AutomatedPostingGrantTests`, including PostgreSQL-backed activation, revocation,
restart, tenant isolation, stale mapping, and replay scenarios.

## Diagrams

See `DIA-ASSURANCE-LOOP`, `DIA-STORAGE-TOPOLOGY` in the [diagram index](../../docs/source/data/diagram-index.yml).

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-STORAGE -->
| Roadmap item | Title |
| --- | --- |
| `W14-ATTEST-001` | Single-principal attestation for governed approvals in the single-operator envelope |
| `W9-GOV-008` | Route-level authorization, fail-closed tenancy, and hash-chained accounting audit |
| `W1-DATA-001` | Provider trust gate and data confidence baseline |
| `W2-TRD-001` | Paper trading cockpit reliability |
| `W4-RECON-001` | Portfolio ledger reconciliation readiness |
| `W4-RPT-001` | Governed report pack readiness |
| `W5-ACCT-001` | Accounting records and operational evidence |
| `W9-ASSET-010` | Asset Accounting Event Spine and atomic lot posting |
| `W10-JRNL-001` | Durable recurring journal schedules and draft runner |
| `W12-ICAGREE-001` | Intercompany accounting profiles and legacy attribution |
| `W12-ENTITY-001` | Effective entity lifecycle and audited book-control migration |
| `W12-ICPAIR-001` | Durable reviewed counterpart proposals and aged intercompany breaks |
| `W12-ICINCOME-001` | Policy-governed intercompany revenue and expense elimination |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-STORAGE -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```bash
dotnet build src/Meridian.Storage/Meridian.Storage.csproj
dotnet test tests/Meridian.Storage.Tests/Meridian.Storage.Tests.csproj --filter "FullyQualifiedName~Parquet|FullyQualifiedName~WriteAheadLog"
# Linked business adapters and accounting changes have separate compile/test ownership.
dotnet build src/Meridian.Storage.Accounting/Meridian.Storage.Accounting.csproj
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~GeneralLedgerExport|FullyQualifiedName~ReportingOpenPositions"
```

## Change rules

Preserve durable IO, tenant isolation, migration history and transaction boundaries. Keep business
adapters in the Accounting assembly and never add a reverse Ledger/Reporting dependency to Storage.
Validate linked business changes through their owning project and relevant financial invariants.

## Related docs

- [Accounting adapter source guide](../Meridian.Storage.Accounting/README.md)
- [Module map](../../docs/architecture/module-map.md)
- [Governed reporting operations](../../docs/operators/governed-reporting-operations.md)
- [Reconciliation operations](../../docs/operators/reconciliation-operations.md)
