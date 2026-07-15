# Meridian Production-Readiness Master TODO

**Status:** active; production certification blocked  
**Owner:** core-team  
**Reviewed:** 2026-07-11  
**Baseline:** `main` at `f0ac384a2`  
**Sources:** [Meridian Design Document (Version 0.25)](meridian-design-document.md), [Program State](../roadmap/data/program-state.yml), [Roadmap Registry](../roadmap/data/roadmap-items.yml), and the live source, test, workflow, deployment, security, and operator surfaces named below

This is Meridian's single active implementation list for turning the existing program into a supported production release. Roadmap rows marked `done` prove bounded product capabilities; they do not by themselves certify security, correctness, durability, operability, packaging, or recovery. Detailed product rationale remains in the design document, and roadmap status remains in the roadmap registry.

The review covered the modular-monolith host, contracts and shared services, accounting and financial operations, portfolio and reference data, execution and strategy, providers and ingestion, storage and audit, browser and WPF workstations, tests, release workflows, deployment manifests, security material, operator guidance, generated status output, and tracked duplicate/deprecation candidates.

## Production Definition

Meridian may be called production-ready only when all of the following are true:

- Every `P0` row below is complete on the same release commit.
- The supported deployment, authentication, tenancy, storage, provider, browser, and WPF envelope is explicit; unsupported modes fail closed or are removed from production guidance.
- Financial commands are authorized, idempotent, version-checked, period-safe, durable, replayable, and traceable to immutable evidence.
- Release artifacts are built from the current source and UI bundle, signed, scanned, installed, launched, upgraded, rolled back, restored, and smoke-tested in clean environments.
- Deterministic integration and recovery lanes run without silent skips, and required GitHub Actions checks are green.
- Operator runbooks, SLOs, alerts, diagnostics, backup/restore procedures, and incident ownership are executable rather than aspirational.
- No sample, placeholder, in-memory, null, no-op, or plaintext implementation can be selected by a supported production profile unless it is explicitly safe for that role.

Priority meanings:

- `P0`: release blocker. No supported production release while open.
- `P1`: required production hardening or acceptance work. A row can be deferred only by narrowing the signed production envelope.
- `P2`: cleanup and maintainability work that follows the critical migrations; it must not preserve a second policy implementation.

## Master Backlog

### P0 — Release Blockers

| ID | Owner | TODO | Completion evidence |
| --- | --- | --- | --- |
| `PRD-000` | Runtime Host + Architecture + domain owners | **Declare one supported production envelope and make its final dependency graph valid.** Choose the supported desktop/local, browser-hosted, or hardened-container topology; define OS/runtime, auth, tenancy, storage, provider, and UI support; archive or label all other manifests experimental. Replace the split `ProductionApi`/environment policy and validate the graph after every registration so later in-memory bindings cannot bypass production checks. | A signed support matrix; one typed deployment posture; a `ProductionApi` integration test that starts successfully; startup rejection tests for every prohibited in-memory/null/no-op store. **Progress 2026-07-12:** [ADR-019](../adr/019-production-support-matrix-and-deployment-posture.md) (proposed, awaiting sign-off) drafts the support matrix; the typed posture (`MeridianDeploymentPosture`), one unified production resolution, and the final-graph `ProductionRegistrationGuardService` (first hosted service; static descriptor scan plus factory-resolved singleton checks) now fail startup closed, and `ApiHostOptions` rejects unrecognized deployment modes instead of falling back. Startup-rejection coverage: `ProductionRegistrationGuardServiceTests`, `ProductionServiceRegistrationPolicyTests`, `ApiHostOptionsDeploymentModeTests`. **Remaining:** sign ADR-019; archive or label the experimental `deploy/` manifests per the matrix; add the supported-posture startup integration test. |
| `PRD-001` | Identity + Security + Storage | **Make authentication, sessions, authorization, and tenancy fail closed.** Use one strict auth-mode parser, throttle and lock out repeated login attempts, derive review actors/roles from authenticated identity, provide durable/revocable sessions for any multi-node topology, and either enforce one company per isolated deployment or finish tenant partitioning. | Invalid auth modes fail startup; login receives deterministic `429`/lockout behavior; restart/revocation/multi-node tests pass; cross-tenant reads and writes are rejected. Evidence anchors: `AuthenticationMode.cs:17`, `UiServer.cs:471`, `AuthEndpoints.cs:34`, `LoginSessionMiddleware.cs:107`, `LoginSessionService.cs:15`, `docs/security/threat-model-current-state.md:46`, and `WorkstationServiceCollectionExtensions.cs:109`. |
| `PRD-002` | Platform Security + Data Integration | **Eliminate plaintext provider credential persistence and unify credential ownership.** Migrate every consumer from the Core plaintext JSON store to the encrypted/audited vault, scope secrets to tenant/connection/account/environment, protect non-Windows vault keys with an OS keyring or KMS-backed mechanism, rotate safely, and securely remove the legacy sidecar. | No supported runtime writes `provider-credentials.json`; migration/rollback/rotation/two-account-isolation tests pass; raw secrets never appear in files, logs, or APIs. Current active plaintext path: `Meridian.Core/Contracts/IProviderCredentialStore.cs:5`, `ProviderCredentialStore.cs:9`, and `WorkstationServiceCollectionExtensions.cs:531`; encrypted alternative: `DataIntegration/Credentials/FileProviderCredentialStore.cs:13`. |
| `PRD-003` | Ledger + Storage + Financial Operations | **Close the ledger append boundary.** Require a typed posting command, matching provenance, approval/evidence, a non-null idempotency key, and atomic expected-version enforcement for every material production append; isolate any legacy import boundary. | Null/mismatched command, stale version, concurrent duplicate, manual-journal, Operations Continuity, and direct-lending tests fail closed. Evidence anchors: `AccountingPostingCommandValidator.cs:14`, `ILedgerJournalStore.cs:86`, `OperationsLedgerPostingService.cs:210`, `AccountingPostingCommandDtos.cs:52`, `PostgresLedgerJournalStore.cs:91`, and `V_ledger_013__journal_idempotency_guards.sql:1`. |
| `PRD-004` | Ledger + Accounting Close | **Make hard-close final and race-safe.** Commit the authorized closing journal before an atomic hard-close transition, then reject every later append—including fabricated `ClosingEntry` records. | Concurrent close/post and post-close fabricated-entry tests reject all late writes; the permissive behavior in `LedgerPeriodPostingGuard.cs:30` and `LedgerJournalStoreTests.cs:353` is removed. |
| `PRD-005` | Reporting + Portfolio + Security Master + WPF | **Remove non-authoritative financial outputs.** Make historical reports truly as-of across balances, dimensions, and reference identity; make the cash ladder block when fund scope, holdings, base currency, FX-as-of, or valuation evidence is missing; replace WPF Reporting dashboard sample holdings/metrics with shared read models or a persistent, accessible demo label. | Future postings/classifications are excluded from historical reports; placeholder cash cannot emit breach flags; normal WPF mode contains no synthetic financial records. Evidence anchors: `ReportGenerationService.cs:26`, `FundLedgerBook.cs:48`, `PortfolioCashLadderReadService.cs:54`, `PortfolioCashLadderEngine.cs:137`, and `DashboardViewModel.cs:417`. |
| `PRD-006` | Execution + Risk + Storage | **Unify order truth and enforce pre-trade risk.** Register one mandatory production risk validator, fail closed on missing configuration/positions, converge `IOrderGateway` and `IExecutionGateway` behind one gateway state, consume execution-report streams, persist transitions atomically, and serialize/version operator-control state. Corrupt or missing live-control state must open the breaker or fail startup. | Exact-host composition plus duplicate/out-of-order/partial-fill/reconnect/cancel-race/risk/corruption/restart tests pass. Evidence anchors: `UiServer.cs:179`, `OrderManagementSystem.cs:200`, `IExecutionGateway.cs:5`, `OrderLifecycleManager.cs:5`, both `PaperTradingGateway.cs` implementations, and `ExecutionOperatorControlService.cs:166,512`. |
| `PRD-007` | Audit + Compliance + Strategy + Workflow + Storage | **Make production evidence durable and authoritative.** Replace process-local compliance audit, strategy-run, promotion scaffold, operator inbox, and reconciliation-governance state with durable idempotent repositories; bind actors to authenticated identity; make action and audit/evidence atomic through a transaction or outbox. | Restart, tamper, concurrent append, failed-action, access-revocation, retention, pagination, and promotion-lineage tests pass. Current gaps include `ComplianceServices.cs:56`, `StrategyRunStore.cs:6`, `WorkstationServiceCollectionExtensions.cs:147,155`, and `ReconciliationGovernanceService.cs:27`. |
| `PRD-008` | Data Integration + Storage | **Enforce single ETL ownership and safe commit ordering.** Acquire/renew an ownership token before any work; honor rejected state transitions; do not checkpoint, export, move, or delete source data until every required durable catalog/export commit succeeds; return explicit failed/partial states and preserve idempotent retry. | Concurrent-run, lease transfer, catalog/export `Success=false`, and crash-between-stage tests prove exactly one publisher and retained source on failure. Evidence anchors: `EtlServices.cs:126,210`, `IngestionJobService.cs:150`, and `EtlJobOrchestratorTests.cs:103`. |
| `PRD-009` | Storage + Audit | **Honor durability promises under concurrency and low volume.** Give `MaxFlushDelay` a lifecycle-owned delayed WAL flush; serialize/CAS audit-chain appends; preserve the last-known-good catalog by building candidates off-side; make metadata/lineage persistence return explicit success or failure. | Idle-single-append/process-termination/rotation/dispose tests pass; concurrent and multi-process audit appends verify one chain; failed catalog scans do not replace live state; slow-disk/restart mutation tests show no silent loss. Evidence anchors: `WriteAheadLog.cs:190,1003`, `AuditChainService.cs:91`, `StorageCatalogService.cs:108`, `SourceRegistry.cs:242`, `MetadataTagService.cs:299`, and `DataLineageService.cs:216`. |
| `PRD-010` | Provider Platform + Security + Reconciliation | **Harden provider and statement ingress.** Require an approved HTTPS connection base URI; block loopback/private/link-local/metadata egress across redirects and DNS changes; stream with byte/record/page/timeout limits and quotas. Replace whole-file/ad-hoc CSV/XML parsing with bounded schema-aware invariant parsing and atomic import uniqueness. | SSRF/rebinding/redirect/oversize/chunked-body tests pass; quoted-comma, malformed-row, locale, concurrent-duplicate, and interrupted-write statement tests pass. Evidence anchors: `StorageFeatureRegistration.cs:103`, `ProviderIntegrationHttpClientTransport.cs:39`, `ProviderIntegrationRestDryRunService.cs:136`, `BrokerStatementInfrastructure.cs:20,70`, and `IbFlexStatementService.cs:79`. |
| `PRD-011` | Direct Lending + Shared API + Security | **Close mutation abuse and post-commit ambiguity.** Attach an owned limiter to every direct-lending write route; make durable state plus ledger commit authoritative; publish asset-operation events through the outbox so a post-commit publication failure cannot be reported as a failed command that is safe to repeat. | Endpoint metadata covers every write; runtime returns `429` with expected headers; crash-after-commit and retry tests prove exactly-once observable effects. Evidence anchors: `DirectLendingEndpoints.cs:32`, `UiServer.cs:319`, and `PostgresDirectLendingCommandService.cs:1421`. |
| `PRD-012` | Strategy Analytics + Runtime Security | **Keep QuantLab disabled in production until scripts run in a real isolation boundary.** Move arbitrary C# execution to a killable worker/process with CPU, memory, process, file, network, queue, cache, output, and time limits plus retained audit/artifacts. | Supported production profiles reject in-process execution; adversarial infinite-loop, cancellation-resistant, escape, fork, file/network, and large-output tests prove containment. Evidence anchors: `UiServer.cs:208`, `QuantLabEndpoints.cs:35`, `RoslynScriptCompiler.cs:146`, and `ScriptRunner.cs:135`. |
| `PRD-013` | Release Engineering + Browser + SRE | **Make the selected deployment start securely with the current browser bundle.** Establish one canonical workstation asset tree; build or verify it during publish; include it in output; start the published host and fetch `/workstation/` plus referenced JS/CSS. For container/systemd support, align HTTPS/trusted-proxy startup, non-root execution, immutable image digest, migrations, secrets, health/readiness, rollback, and publishing. | Publish smoke starts the actual artifact and proves asset freshness; the chosen manifest passes auth-required Production startup. Unsupported manifests are archived. Evidence anchors: `publish.ps1:284,387`, dashboard `vite.config.ts:173`, `.gitignore:269`, `UiServer.cs:80,452`, `WorkstationEndpoints.cs:5062`, `publish-smoke.yml:55`, and `deploy/k8s/deployment.yaml:38`. |
| `PRD-014` | Release Engineering + Security | **Certify every shipped artifact and its supply chain.** Build, sign, generate checksums/SBOM/provenance, scan NuGet/npm/container/Actions dependencies, install, launch, update from N-1, repair, roll back, and uninstall on clean x64/ARM64 environments. Align installer product claims with .NET 10 and WPF. | Versioned release artifacts carry verified publisher identity, SBOM, provenance, and clean-VM evidence; no `-SkipInstall` certification shortcut. Evidence anchors: `desktop-installer-packaging.yml:145`, `install.ps1:237,1115`, `global.json:3`, `Meridian.Wpf.csproj:25`, `known-vulnerabilities.md:15`, and `generate-sbom.ps1:47`. |
| `PRD-015` | Storage + SRE/Operations | **Implement and drill backup, restore, migration rollback, replay, and disaster recovery for every supported store.** | Encrypted scheduled backups, retention/integrity checks, clean-environment restore, replay/reconciliation, destructive-migration rollback, measured RPO/RTO, and a dated drill artifact exist. The current `docs/operators/failover-and-recovery.md:9` is guidance, not proof. |
| `PRD-016` | Test Architecture + DevEx + domain owners | **Create a deterministic production-certification lane.** Run API/auth/PostgreSQL/storage/integration tests with required services enabled; separate live-provider tests; fail on unowned skips; perform current dependency scans; attach coverage in a real coverage format. | Release/tag and scheduled lanes execute all deterministic integrations with zero silent skips, upload migration/coverage/skip evidence, and enforce accepted-risk policy. Current exclusions: `scripts/ci.sh:12,147` and `.github/workflows/ci.yml:26,302`; the reviewed nightly run passed unit slices but skipped Docker integrations and emitted no coverage document. |
| `PRD-017` | Documentation + Roadmap + module owners | **Restore one trustworthy product/operations evidence chain.** Keep documentation automation, source hashes, generated diagrams, program-state summaries, security claims, roadmap status, and this tracker current on the same commit; retire or rewire stale 1970/zero-value dashboards and separate documentation coverage from code coverage. | All documentation validators are green and deterministic; generated status is source-backed; security/operations links target active docs; no dashboard is presented as release proof without real acceptance artifacts. |

### P1 — Required Hardening and Acceptance

| ID | Owner | TODO | Completion evidence |
| --- | --- | --- | --- |
| `PRD-100` | Product + owning lanes | **Finish or explicitly remove active roadmap work from the supported release envelope.** Reconcile `W5X-EVIDENCE-001`, `W5X-STMT-ONBOARD-001`, and `W8-WPF-PARITY-001` against their live exit criteria. | Each included row is `done` with complete evidence on the release commit; each excluded row is named in the support matrix without false parity/product claims. |
| `PRD-101` | Financial Operations + Platform Persistence | **Make reconciliation and projections deterministic and real.** Dedupe by event identity, reject conflicting duplicates, add stable order tie-breakers, inject time/ID creation, implement actual projection reconciliation/checkpoints, and expose not-ready health instead of empty success. | Duplicate/out-of-order/byte-identical replay property tests and seeded projection-drift tests pass. Evidence anchors: `Reconciliation.fs:219`, `ReconciliationRules.fs:187`, `CashFlowRules.fs:101`, `StorageFeatureRegistration.cs:530`, and `CanonicalProjectionSchemas.cs:9`. |
| `PRD-102` | Storage + Provider Platform | **Make catalogs, lineage, and provider manifests immutable/versioned.** Preserve last-known-good catalogs; store every `(manifestId, version)` with a CAS current pointer; attach exact manifest hash/version to raw payload and replay; make metadata and lineage mutations durable or explicitly failed. | Candidate-promotion, concurrent-version, replay, restart, slow-disk, and migration tests pass. Evidence anchors: `FileProviderIntegrationManifestStore.cs:38,217` and `provider-integration-manifest-runtime.md:192`. |
| `PRD-103` | Provider Platform | **Make provider failover subscription-complete and lifecycle-safe.** Replace `async void` handlers with a serialized task loop; complete all required subscriptions or roll back/degrade; drain in-flight work on disposal. | Partial resubscribe, cancellation, racing failover/recovery, and disposal-during-switch tests pass. Evidence: `FailoverAwareMarketDataClient.cs:282,316,426`. |
| `PRD-104` | Data Integration + Release | **Decide and certify the SFTP capability.** Either enable and package the real adapter with shared vault resolution, host-key validation, cancellation, timeout/retry, and integration evidence, or hide/reject SFTP in production. | No runtime-throwing stub is advertised as supported; source and destination resolve the same secret model. Evidence anchors: `Meridian.Infrastructure.csproj:25,68`, `ISftpClientFactory.cs:98`, `SftpFileSourceReader.cs:27`, and `SftpFilePublisher.cs:15`. |
| `PRD-105` | Documents + Evidence + Storage | **Bound Evidence Vault resource use and finish document-runtime ownership.** Add file/count/package/tenant/disk quotas with reservations and cleanup, streaming hash/copy, retention policy, and failure-safe concurrency; move non-UI policy toward `Meridian.Documents`. | Concurrent quota/disk-pressure/large-artifact tests pass; UI Shared is an adapter rather than the document policy owner. Evidence: `FileEvidenceArtifactStore.cs:62,1722,2434` and `Meridian.Documents/README.md:28`. |
| `PRD-106` | Backtesting + Strategy Analytics | **Unify and harden backtest orchestration.** Route browser and WPF through `BacktestStudioRunOrchestrator`; persist intent before launch; recover/retire runtime state; bound batch concurrency; preserve input order; propagate cancellation; use an outbox/retry for terminal state. | Browser/WPF parity, large-grid, cancellation, repository-failure, crash/restart, and recovery tests pass. Evidence anchors: `WorkstationEndpoints.Strategy.cs:162`, `BacktestService.cs:15`, `BatchBacktestService.cs:146`, and `BacktestStudioRunOrchestrator.cs:43,156`. |
| `PRD-107` | Runtime + Workflow | **Use full host lifecycles and honest runbooks.** Replace raw service-provider CLI/backfill/collector composition with `IHost`; start/stop hosted coordination cleanly; make runbook `execute` invoke an authorized idempotent step registry or fail closed rather than report simulated success. | Initialization, leader-election, split-brain, shutdown, runbook authorization/idempotency, crash/retry, and audit tests pass. Evidence: `HostStartup.cs:57`, `CoordinationFeatureRegistration.cs:42`, `RunbookExecutor.cs:10`, and `RunbookCommands.cs:59`. |
| `PRD-108` | Architecture + DevEx | **Repair the physical dependency graph and its enforcement.** Extract ports/contracts downward; remove documented forbidden references such as Infrastructure→Storage and upper-layer dependencies from Execution/Backtesting/QuantScript/Strategies; teach the checker every module kind and direct project-reference rule. | An architecture test rejects each forbidden edge and correctly classifies DataIntegration, ReferenceData, Instruments, Documents, dashboard, and other non-host modules. Evidence anchors: `layer-boundaries.md:40`, `Meridian.Infrastructure.csproj:72`, `Meridian.Execution.csproj:13`, and `Meridian.Strategies.csproj:14`. |
| `PRD-109` | Browser Workstation + Contracts | **Consolidate TypeScript contracts and make dense explorers accessible.** Give each DTO/type one declaration behind a stable barrel with C#/TypeScript compatibility checks; add keyboard focus, activation, selection semantics, and `aria-selected` to Financial Record Explorer rows. | Duplicate-export gate is zero; representative serialization parity passes; keyboard and `jest-axe` coverage passes. Current split: dashboard `types.ts:11,6943` and `types/workstation-5.ts:934`; accessibility gap: `financial-record-explorer.tsx:465`. |
| `PRD-110` | WPF + Shared UI Services | **Remove polling/lifecycle hazards and reconcile the parity inventory.** Assign one poll owner per resource, enforce single-flight and awaited cancellation/disposal, remove sync-over-async construction, and classify every WPF route as full page, embedded equivalent, compatibility alias, or real gap. | Timer-overlap/cancellation/disposal tests pass; no UI-thread blocking composition remains; the alignment plan matches `ShellNavigationCatalog` and `NavigationService`. Evidence anchors: `DashboardViewModel.cs:409,624`, `ConnectionService.cs:42,117`, `StatusServiceBase.cs:214`, and `wpf-web-ui-alignment-plan.md:57`. |
| `PRD-111` | SRE + Support + Observability | **Create executable service objectives and incident operations.** Define SLIs/SLOs and RTO/RPO, validate metric names and alerts, provision dashboards without default credentials, link alerts to active runbook anchors, and drill support diagnostics/incident response. | A dated alert, incident, diagnostics-bundle, and recovery drill proves the system. Current stubs/mismatches: `service-level-objectives.md:1`, `operator-runbook.md:1`, `alert-rules.yml:18`, and `docker-compose.yml:55`. |
| `PRD-112` | Performance + Test Architecture | **Restore resource, soak, and test-inventory gates.** Establish representative latency/throughput/allocation budgets on stable hosts; fail when benchmark results are absent; inventory every project/category/skip under an owned lane. | Missing results fail; regressions cross an owned threshold; machine-readable lane and skip evidence is attached. Evidence anchors: `validate_budget.py:149`, `BOTTLENECK_REPORT.md:8,389`. |
| `PRD-113` | Security + Compliance | **Refresh the security register and complete an evidence-backed SOC 2 readiness assessment.** Correct stale control sources, close or rescope overdue items, name control owners, and maintain an evidence calendar. | Current control matrix, readiness report, owner roster, and residual-risk approvals exist. Evidence anchors: `soc2-roadmap.md:7` and `soc2-control-matrix.md:10`. |
| `PRD-114` | Documentation + DevEx | **Repair repository audit and generated-status tooling.** Make duplicate scanning begin from `git ls-files`, finish within a budget, and emit owner-classified results; rewire or archive stale doc-health/pilot/governance/metrics generators; make code coverage a separate artifact from documentation coverage. | Audit finishes deterministically; no generated status uses archive stubs or epoch timestamps; current code coverage is uploaded and parsed. Current duplication traversal: `duplication-audit.ps1:88`. |

### P2 — Cleanup After Critical Migrations

| ID | Owner | TODO | Completion evidence |
| --- | --- | --- | --- |
| `PRD-200` | Financial Operations | Remove or reduce `FundWorkflowCommandHandler` to a thin adapter over the governed Financial Operations workflow after proving no production consumers; do not retain its independent post-before-approval policy. | Reference inventory is empty or adapter contract tests pass; duplicate policy is gone. |
| `PRD-201` | Contracts + Domain | Migrate the non-equivalent Domain/Contracts `MarketEventPayload` hierarchies behind serializer-parity tests, then rename/archive the legacy base. | Consumer/serialization inventory and round-trip compatibility evidence exist before removal. |
| `PRD-202` | Provider Platform | Stop dual writes to legacy `DataSources` and current `ProviderConnections` after every reader is migrated; move obsolete brokerage templates out of runtime compilation while retaining tested SDK examples. | Reader inventory is zero; migration and rollback tests pass; `TemplateBrokerageGateway` is not compiled into production. |
| `PRD-203` | UI owners | Archive the unused inline workstation dashboard template and deliberately wire or archive `ArchiveHealthService`, `ProviderHealthService`, and `ScheduledMaintenanceService`; preserve the active login template and required styles. | Reference and startup-graph checks prove no removed production consumer. |
| `PRD-204` | Execution + Strategy | After `PRD-006`/`PRD-106`, remove the orphan lifecycle manager, in-memory promotion scaffold, static WPF backtest service, and obsolete gateway/promotion compatibility surface. | All callers use canonical orchestration and durable stores; contract and migration tests pass. |
| `PRD-205` | AI/DevEx | Remove stale active references to the retired MCP server, retain its archive tombstone, and make current MCP help/handshake/subprocess termination accurate and bounded. | Maintenance scripts no longer invoke missing projects; timed-out child process trees are terminated; help/handshake tests pass. |

## Duplicate and Deprecation Register

The word intended here is **deprecated**. Repeated filenames or concepts are not enough evidence for deletion; each family has an explicit disposition.

### Removed in this audit

| Artifact | Reason | Verification |
| --- | --- | --- |
| `src/Meridian.Ui/dashboard/src/Meridian.Ui/dashboard/package-lock.json` | Accidental 88-byte nested empty lockfile; the canonical dashboard lockfile remains. | No tracked reference; deletion leaves the real dashboard package graph intact. |
| `tests/fixtures/workstation/security-instrument-explorer-parity.json` | Exact unused copy of the canonical parity fixture. | Browser, WPF, roadmap, plan, and tracker references all target `tests/fixtures/security-instrument-explorer-parity.json`. |

### Consolidate through migration

| Family | Disposition | Governing row |
| --- | --- | --- |
| Plaintext Core provider credential store vs encrypted DataIntegration vault | Migrate all callers, securely remove plaintext data, then delete the legacy contract/store. | `PRD-002` |
| `IOrderGateway`/`IExecutionGateway`, two paper gateways, orphan lifecycle manager | One policy/state engine with temporary interface adapters only. | `PRD-006` |
| `Meridian.Risk`, `OperatorRiskRuleService`, and `RiskRuleRuntimeService` | Put enforcement in one domain policy; keep UI runtime only as configuration/read adapter. | `PRD-006` |
| In-memory strategy/promotion/workflow evidence vs durable stores | Keep only explicit non-production adapters; production must reject them. | `PRD-007` |
| Browser duplicate TypeScript DTO declarations | One declaration per contract behind stable exports. | `PRD-109` |
| Provider `DataSources` and `ProviderConnections` writes | Migrate readers, then end legacy writes. | `PRD-202` |
| Fund workflow handler and governed Financial Operations workflow | Remove the unregistered independent policy or reduce it to a thin adapter. | `PRD-200` |
| Domain and Contracts `MarketEventPayload` bases | Inventory consumers and serializers, migrate with compatibility proof, then retire one. | `PRD-201` |

### Retain intentionally

- Browser and WPF workstations are active co-equal presentation lanes; shared contracts/read services prevent policy forks.
- C# domain/orchestration over F# rule kernels is intentional; consolidate duplicated policy, not language-specific engines.
- Native and Lean backtest engines are adapters; orchestration and result normalization should be shared.
- In-memory and PostgreSQL implementations are legitimate test/development and production adapters when production selection is explicit and fail-closed.
- `GlobalUsings.SecurityMasterConcerns.cs`, `DesignModule`, interop files, cross-surface brand assets, and stable browser `components/ui/*` re-export adapters are conventions or compatibility surfaces.
- WPF services that extend shared bases, type forwards, route aliases, obsolete persisted enum aliases, and the reconciliation API wrapper remain until consumer and persistence migrations prove safe removal.
- `Meridian.Documents` is an extraction target, not dead code; Evidence Vault policy should move toward it.
- Null/no-op providers and disabled SFTP/IB implementations may remain as explicit non-production seams, but a supported production profile must reject or hide them.

### Investigate before deletion

- Duplicate icon content under different semantic names; require design-owner review before consolidation.
- `ConfigStore` compatibility wrapper and ProviderSdk types retaining `Meridian.Infrastructure.*` namespaces; inventory public callers before a versioned rename.
- Generated archive-migration stubs and readiness dashboards; repair incoming links/generators before archiving.
- Tracked browser bundle trees; resolve the publish contract in `PRD-013` before removing either output.

## Roadmap and Scope Reconciliation

| Roadmap posture | Rows | Production interpretation |
| --- | --- | --- |
| Bounded capability evidence complete | `W1-DATA-001`, `W2-TRD-001`, `W2-PROMO-001`, `W3-CONT-001`, `W4-RECON-001`, `W4-RPT-001`, `W5-ACCT-001`, `W5-MASSET-001`, `W5X-FREX-001`, `W5X-FINOPS-001`, `W5X-CONNECT-001`, `W7-LIVE-001` | Preserve the evidence; do not reinterpret these rows as production certification. `W7-LIVE-001` closes governance only, not broad live execution. |
| Active productization | `W5X-EVIDENCE-001`, `W5X-STMT-ONBOARD-001`, `W8-WPF-PARITY-001` | Finish their registered exit criteria if they are inside the supported release envelope; otherwise explicitly scope them out without claiming full parity/productization. |
| Planned | `W6-BTSTUDIO-001` | Not a blocker unless the supported release advertises Backtesting Studio; the shared orchestration/durability defects in `PRD-007` and `PRD-106` still apply to any existing production entrypoint. |

The following remain deferred and are not production blockers unless the signed support envelope reopens them: broad live execution and live portfolio operations beyond bounded W7 governance, treasury payment execution, broad alternative-asset expansion beyond current evidence, forecasting/scenario engines, enterprise risk platform, capital-structure modeling, client portal, no-code workflow designer, broad collaboration productization, and all native mobile lanes.

## Recommended Execution Order

1. Complete `PRD-000` and freeze the supported topology and release scope.
2. Close identity, secrets, ledger, financial truth, risk/execution, durable evidence, ingestion, and runtime-isolation blockers (`PRD-001` through `PRD-012`).
3. Build and certify the actual deployment, artifacts, recovery, test lane, and documentation evidence chain (`PRD-013` through `PRD-017`).
4. Finish the included roadmap acceptance and P1 operability/architecture work (`PRD-100` through `PRD-114`).
5. Remove migrated compatibility implementations and archive verified dead code (`PRD-200` through `PRD-205`).

## Completion Rules

- Check off a row only when its named acceptance evidence exists on the current release commit.
- Add or update focused automated tests for every control change; financial, security, concurrency, recovery, and migration work requires negative-path evidence.
- Run the narrowest relevant checks while iterating, then the repository-required validation and GitHub Actions lanes before claiming completion.
- Do not weaken, delete, skip, or rewrite expected behavior merely to obtain a green result.
- Do not hand-edit generated roadmap/status files; update their source registry or generator and render them.
- Record owner, pull request, commit, validation commands, results, migration/rollback notes, and residual risk when closing an item.
- All `P0` rows must be complete for production certification. An accepted scope reduction must be recorded in `PRD-000`; it cannot silently turn a failing path into a supported one.

## Review Snapshot

The 2026-07-11 audit began on `eadd95df6`, refreshed to `f0ac384a2`, and found no incoming changes to the critical host, execution, provider, storage, finance, or release findings. The refresh added Reference Data/WPF asset-profile work and regenerated documentation outputs.

Evidence collected during the audit:

- Core GitHub lanes on the reviewed pre-refresh commit were green: Meridian CI, CI, Golden Path, WPF route/dev-loop, Windows Desktop Build, and CodeQL; Documentation Automation was red there and green after the refreshed generated-doc commit.
- Roadmap registry, design adaptation, source README, source TODO policy, and the existing architecture-surface checks passed during the review.
- The architecture checker reported zero violations but missed known forbidden project references because its module taxonomy is incomplete; its pass is structural evidence, not production proof.
- The maintained full duplication scan did not finish reliably; a tracked-file scan and reference verification were used for the two removals above. `PRD-114` owns the tooling repair.
- The reviewed npm production dependency audit reported zero vulnerabilities. The NuGet vulnerability command timed out and must be rerun through `PRD-016`; no clean NuGet result is claimed.
- Scheduled tests showed strong unit coverage but excluded deterministic integration/performance categories, skipped Docker-backed accounting tests, and produced no parsed code-coverage document. Those gaps are release work, not evidence of failing unit behavior.

This snapshot is an audit baseline, not a completion claim. Live source and authoritative GitHub Actions results take precedence when a row is implemented.

### 2026-07-12 addendum — PRD-000 enforcement seam

The split-policy defect named in `PRD-000` was addressed on top of the audit baseline:
[ADR-019](../adr/019-production-support-matrix-and-deployment-posture.md) (proposed) carries the
support-matrix decision for sign-off, and the registration policy, typed posture, final-graph
guard, and strict deployment-mode parsing landed with startup-rejection tests under
`tests/Meridian.Tests/Application/Composition/` and `tests/Meridian.Tests/Ui/`. `PRD-000` stays
open until the ADR is signed, the experimental `deploy/` manifests are archived or labeled per the
matrix, and the supported-posture startup integration test exists. The evidence anchors quoted in
the P0 table remain relative to the `f0ac384a2` audit baseline.
