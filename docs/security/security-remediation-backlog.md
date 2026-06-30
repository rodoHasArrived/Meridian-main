# Security Remediation Backlog

**Source:** `docs/security/threat-model-current-state.md` (last updated 2026-05-21).  
**Backlog generated:** 2026-05-27.

This backlog converts the threat-model residual concerns into tracked remediation work items with required implementation and verification evidence.

## Authz coverage gaps

### SEC-001 — Endpoint authorization parity for configuration and direct-lending routes
- **Affected module/path:** `src/Meridian.Ui.Shared/Endpoints/ConfigEndpoints.cs`; `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs`; `src/Meridian.Ui.Shared/Security/EndpointAuthorization.cs`.
- **Risk rating:** **High** (authenticated overreach to privileged mutations).
- **Required code/tests:**
  - Add explicit `UserPermission` checks to all mutation and sensitive read routes in config/direct-lending endpoint groups.
  - Add negative/positive API authorization tests for each hardened route (forbidden without permission, success with permission).
  - Add a coverage-oriented test ensuring all endpoint-map methods in both modules attach authorization metadata.
- **Owner:** `@platform-security` + `@shared-ui-services`.
- **Target date:** **2026-06-12**.
- **Done evidence:**
  - PR showing permission-gate additions.
  - Test run artifacts for focused endpoint authorization suites.
  - Updated threat-model note confirming parity closure.

## Cookie/TLS hardening

### SEC-002 — Secure-cookie enforcement and TLS deployment guardrails
- **Affected module/path:** `src/Meridian.Ui.Shared/Auth/LoginSessionService.cs`; `src/Meridian/UiServer.cs`; deployment docs under `docs/operations/`.
- **Risk rating:** **High** (session-cookie exposure on non-TLS/non-secure-cookie deployments).
- **Required code/tests:**
  - Set auth cookie `Secure=true` in non-development modes and document local-dev override behavior.
  - Add startup/deployment guard that warns or fails closed when auth-required mode runs without TLS in non-local environments.
  - Add integration coverage verifying `Set-Cookie` attributes include `Secure`, `HttpOnly`, and `SameSite=Strict` under required-auth production posture.
- **Owner:** `@platform-security` + `@ops-readiness`.
- **Target date:** **2026-06-14**.
- **Done evidence:**
  - Configured non-dev environment capture showing secure cookie attributes.
  - Regression tests for cookie flags and TLS guard behavior.
  - Operator runbook update proving deployment checks are executable.

## CSRF protections

### SEC-003 — Anti-forgery protections for cookie-authenticated mutations
- **Affected module/path:** `src/Meridian/UiServer.cs`; `src/Meridian.Ui.Shared/Endpoints/*` mutation routes; browser workstation request layer in `src/Meridian.Ui/dashboard/src/`.
- **Risk rating:** **Medium** (defense-in-depth gap for browser-mediated state changes).
- **Required code/tests:**
  - Introduce CSRF token issuance + validation middleware/policy for cookie-authenticated mutation endpoints.
  - Update browser client mutation helpers to send CSRF token headers.
  - Add endpoint integration tests that reject mutation requests missing/invalid token and accept valid token paths.
- **Owner:** `@platform-security` + `@trading-workstation`.
- **Target date:** **2026-06-19**.
- **Done evidence:**
  - API tests proving token-required mutation behavior.
  - Browser workstation tests proving token propagation for POST/PUT/DELETE workflows.
  - Updated threat-model CSRF residual concern marked remediated or narrowed.

## Rate-limiter enforcement validation

### SEC-004 — Enforce and prove runtime rate-limiter middleware activation
- **Affected module/path:** `src/Meridian/UiServer.cs`; shared registration in `src/Meridian.Ui.Shared/ServiceCollectionExtensions.cs`; mutation endpoints in `src/Meridian.Ui.Shared/Endpoints/*`.
- **Risk rating:** **High** (request-abuse and DoS risk if policies are registered but not enforced in runtime pipeline).
- **Required code/tests:**
  - Ensure `UseRateLimiter()` is wired in `UiServer` request pipeline before endpoint mappings.
  - Add integration test that sends repeated mutation calls and verifies policy-based throttling responses.
  - Add startup diagnostics log/event confirming active rate-limiter middleware and policy names.
- **Owner:** `@shared-ui-services` + `@ops-readiness`.
- **Target date:** **2026-06-10**.
- **Done evidence:**
  - Runtime log snapshot showing middleware activation and policy registration.
  - Reproducible throttling test artifacts (status codes + headers).
  - Threat-model rate-limiter residual concern closed with code-path reference.

## Tenant isolation (fund-scoped data)

### SEC-005 — Storage-enforced tenant isolation for fund-scoped data; single-company-per-deployment is the current boundary
- **Affected module/path:** `src/Meridian.Strategies/Storage/StrategyRunStore.cs`; `src/Meridian.Ui.Shared/Services/SecurityMasterWorkbenchQueryService.cs` (`LoadFundRunsAsync`); `src/Meridian.Storage/Ledger/*` (ledger books / journal); report-pack workflow + `ReportPackSecurityLineIndex`; `src/Meridian.Application/SecurityMaster/SecurityMasterWorkbenchCommandService.cs` (field-edit draft persists a body-supplied `FundProfileId`).
- **Risk rating:** **Medium** (deployment-conditional cross-tenant information disclosure; **not reachable** in the current runtime — see boundary below).
- **Current security boundary (documented, relied upon):**
  - Runtime today aliases `TenantId == CompanyId`, one company per authenticated session, and the platform is operated as **one company per deployment** (`src/Meridian.Ui.Shared/Endpoints/LoginSessionMiddleware.cs`; `WorkstationTenantContext.cs`). Multi-tenant separation is design-stage, not realized.
  - The fund-scoped stores (strategy runs, ledger books, report packs) partition by the free-form `FundProfileId` string **alone** — they carry no tenant/company column. `StrategyRunEntry` has `FundProfileId` but no tenant key, and `LoadFundRunsAsync` enumerates the process-wide run store filtering only by `FundProfileId`.
  - **Therefore tenant isolation is enforced by the single-company-per-deployment boundary, not by storage partitioning.** A shared-datastore multi-tenant deployment is out of the current security envelope until the work below lands. This assumption must be stated in deployment/operator docs and must not be mistaken for storage-enforced isolation.
- **In place now (slice 1 — the authority):** an authoritative fund-profile → tenant/company ownership registry, `IFundProfileTenancyRegistry` (`Meridian.Contracts.Tenancy`), backed by `FileFundProfileTenancyRegistry` in the workstation host and `PostgresFundProfileTenancyRegistry` + migration `V_ledger_019__fund_profile_tenancy.sql` for shared/multi-tenant deployments. It records the owning tenant of each fund (first-owner-wins), and the migration backfills ownership from existing tenant/company-attributed accounting audit history so a multi-tenant upgrade does not leave pre-existing funds claimable. `RegistryFundProfileTenantGuard` gates the Security Master workbench field-edit route with a **read-only** ownership check — denying only a fund already owned by a different tenant. It never binds (so a failed write cannot squat a fund), never denies an own/unknown fund (no false-deny), and is a no-op under the single-company runtime. This **replaces** the earlier presence-based `AccountingHistoryFundProfileTenantGuard` heuristic with an authoritative source of truth.
- **In place now (slice 2 — bind-on-write + read gate):** the Security Master workbench field-edit route binds the body-supplied fund to the caller's server-resolved tenant via `IFundProfileTenancyRegistry.BindAsync` only **after** the governed write succeeds (trust-on-first-use), so a rejected/failed write can never squat a fund id (`ClaimFundOwnershipAsync` in `WorkstationEndpoints.SecurityMasterWorkbench.cs`). The post-commit claim runs on a request-independent cancellation token so a client disconnect between commit and bind cannot leave the committed fund unbound. On the read side, `SecurityMasterWorkbenchQueryService.GetTrustSnapshotAsync` sanitizes the operator-supplied fund scope **once at entry** via `SanitizeFundScopeAsync` (resolving the request tenant via `IHttpContextAccessor`): a fund the registry positively attributes to a **different** tenant is treated as unscoped for **every** fund-scoped evidence path the snapshot and its instrument passport fan out to — downstream impact, open lots, Clearwater pricing (golden-copy/hierarchy), and entitlement applicability — so no cross-tenant runs, exposures, pricing, or entitlement metadata are returned. The separate `BulkResolveConflictsAsync` mutation path (which does not build a passport) is covered at the impact boundary: a foreign fund yields a **withheld/`Unknown`** impact (not `None`) via `BuildWithheldFundImpact`, and bulk resolution is eligible only on `None`/`Low`, so a foreign scope is non-eligible instead of looking *safe*. Unbound/legacy funds, a blank fund, a request without tenant scope, or an unavailable registry all **fail open** to the single-company-per-deployment boundary, so the restatement path never drops impact on mere uncertainty and single-tenant deployments are unaffected.
  - **Known residual (concurrent first-use race; closed by slice 4):** because the slice-2 claim is post-write and the field-edit guard is a read-only check, two *different* tenants issuing the **first** edit for the same previously-unbound fund concurrently can both pass the guard and both commit before either reaches `BindAsync`; first-owner-wins then settles ownership, but the losing tenant's edit has already committed against a fund now owned by the winner. This is **not reachable under the single-company-per-deployment boundary** (one tenant per deployment), and the read gate still closes the *disclosure* surface immediately afterward (the loser's subsequent reads of that fund are withheld). Closing the *write* race requires reserving/verifying ownership **atomically with the write commit** (deny the edit unless the caller becomes or already is the owner) — tracked under slice 4's storage-enforced reclassification. It is intentionally not patched in slice 2: binding *before* the write would reintroduce the squat-on-failed-write hole that bind-on-success exists to avoid.
- **In place now (slice 3 — ledger read boundary):** the fund-scoped ledger **read** routes (ledger books, periods, cross-period trial-balance / P&L reports, accounting-configuration workspace, accounting-configuration audit, accounting report packages, manual-journal-entry workbench, and the private-capital activity / fund-event / capital-account / report-output reads in `LedgerEndpoints.cs`) now carry the `RequireFundProfileTenantScope()` endpoint filter (`FundProfileScopeEndpointFilters`). It reuses the slice-1 `IFundProfileTenantGuard`: a request whose `fundProfileId` query value the registry positively attributes to another tenant is refused with `403` before any fund-partitioned ledger data is loaded. The filter evaluates **every** supplied `fundProfileId` query value (not the comma-joined `StringValues`) so a polluted `?fundProfileId=foreign&fundProfileId=mine` query cannot bypass the gate. It is **read-only and fail-open** — a blank fund, a caller with no tenant scope, a fund not positively foreign, or an unavailable guard all pass through, and it is a no-op under the single-company runtime. The filter is given each route's read permission and evaluates ownership **only** for callers who can already read the route (the gate runs before the handler's own permission check, so an unauthorized caller must get a uniform `403` rather than an ownership oracle). The body-scoped POST read/preview routes the query filter cannot see — accounting-configuration template preview, posting-rule dry-run, journal-candidate build, projection-set build, rule-test execution, ledger-book rollout assessment, manual-journal-entry draft validation, and accounting report-package build, whose fund scope arrives in the request body — are gated inline via `IsBodyFundScopeAccessibleAsync` **after** their `HasLedgerReadPermission` check, so a foreign `request.FundProfileId` (or `request.Draft.FundProfileId`) is refused with `403`. **Known residual (alternate fund identifiers; closed by slice 4):** the filter gates only the explicit `fundProfileId` scope. Several of these routes also reach fund-scoped data through *other* identifiers — `ledgerBookId`, `periodId`, `fundStructureNodeId`, `fundAccountId` — when `fundProfileId` is omitted, and the registry is fund-keyed (there is no book/period → tenant lookup), so a caller who knows another tenant's ledger-book GUID can still read that book's data. This is **not reachable under the single-company-per-deployment boundary** and is closed by slice 4's storage-enforced tenant partition columns on the ledger stores (a book/period row carries `tenant_id`, and reads filter by it), which makes alternate-identifier ownership uniform and testable rather than requiring ad-hoc book→fund resolution in each read path. The report-pack workflow-record reads (`ListRecordsForFundProfile`) and the `ReportPackSecurityLineIndex` cross-fund `Lookup` are **not endpoint-reachable** today — the restatement resolver consumes the fund-scoped `LookupByFund`, and the cross-fund `Lookup` seam has no caller — so there is no current cross-tenant read path there; they will be gated when a public cross-fund surface is introduced. _Tracked fast-follow (slice 3b):_ the remaining workstation fund-scoped reads outside the ledger boundary — accounting-system migration/import/reconciliation/mapping/export reads, operations-continuity financial-operations command center, workstation workflow-summary, and reporting structured-export — should carry the same filter.
- **Required code/tests (remaining slices to close the gap / enable multi-tenant deployment):**
  - **Slice 2 (done — read path + bind-on-write):** ✔ bind a fund to the caller's tenant on the first **successful** authoritative write (server-resolved tenant context); ✔ gate `LoadFundRunsAsync` / downstream-impact resolution by fund ownership via the registry, excluding runs for funds owned by another tenant and treating unbound/legacy funds as the ambient deployment tenant. Regression tests prove a fund owned by tenant B yields no runs/impact for a tenant-A caller, that an owned fund still surfaces its runs, and that ownership is bound only on a successful write. _Deferred (optional, low value while one-company-per-deployment holds):_ seed the file-backed registry from the workstation host's existing audit history for parity with the Postgres migration backfill.
  - **Slice 3 (done — ledger read boundary):** ✔ gate the fund-scoped ledger read routes by fund ownership via the `RequireFundProfileTenantScope()` endpoint filter, refusing a positively-foreign `fundProfileId` with `403` (fail-open otherwise). Regression tests prove a foreign fund is rejected, an own/blank fund passes, and a missing guard fails open. Report-pack workflow-record reads and the `ReportPackSecurityLineIndex` cross-fund `Lookup` are not endpoint-reachable today (internal callers use the fund-scoped `LookupByFund`), so they carry no current cross-tenant read path. _Fast-follow (slice 3b):_ apply the same filter to the remaining workstation fund-scoped reads (accounting-system, operations-continuity, workflow-summary, reporting structured-export).
  - **Slice 4a (done — stop client-body tenant overrides):** ✔ the accounting/ledger routes no longer fall back to a client-body `TenantId`/`CompanyId` — the server-resolved `WorkstationTenantContext` is authoritative. Removed every `tenantContext.TenantId ?? request.TenantId` / `?? request.CompanyId` (and the `request.Profile`/`request.Artifact`/`request.Plan` and nested per-test-case variants) across `LedgerEndpoints.cs`, `AccountingSystemEndpoints.cs`, and `AccountingConfigurationService.cs`, so a body-supplied tenant can no longer take effect when the session scope is absent. Existing endpoint tests already assert the server tenant wins over a spoofed body tenant; this makes that unconditional. (Behavior-preserving under the single-company runtime, where the session tenant is always populated.)
  - **Slice 4 (remaining: 4b atomic claim, 4c storage columns)** — make the trust-on-first-use ownership claim **atomic with the governed write commit** (reserve/verify ownership as a write precondition so a concurrent first-edit by another tenant cannot commit against a fund it does not win — see slice 2's "concurrent first-use race" residual); add `tenant_id` partition columns + read predicates to the fund-scoped stores (closes the slice-3 alternate-identifier / unscoped-list residual); reclassify this item from deployment-boundary-enforced to storage-enforced.
  - Optional hardening: stamp the resolved tenant onto `StrategyRunEntry` and add `tenant_id` partition columns on the fund-scoped stores as defense-in-depth alongside the registry gate. The same tenant partition columns close the slice-3 alternate-identifier residual (ledger reads keyed by `ledgerBookId` / `periodId` / `fundStructureNodeId` / `fundAccountId` rather than `fundProfileId`).
- **Owner:** `@platform-security` + `@shared-ui-services` + `@fund-operations`.
- **Target date:** **gated on a multi-tenant deployment decision** (not required while one-company-per-deployment holds).
- **Done evidence:**
  - PR adding tenant partition columns + predicates to the fund-scoped stores, with migration.
  - Tests proving a fund scoped to company B is invisible to a company-A caller across runs, ledger books, and report packs.
  - Threat-model update reclassifying the residual from "deployment-boundary-enforced" to "storage-enforced".

## Threat-model traceability

| Backlog ID | Threat-model section | Threat-model source lines | Residual concern excerpt |
| --- | --- | --- | --- |
| SEC-001 | `Authentication, sessions, and authorization` | `docs/security/threat-model-current-state.md` lines 50-54 | "Some endpoint groups still lack explicit permission checks ..." |
| SEC-002 | `Authentication, sessions, and authorization` | `docs/security/threat-model-current-state.md` lines 50-51 | "Session cookies still do not set `Secure` ... treat TLS + secure-cookie enablement as required ..." |
| SEC-003 | `CSRF and browser security` | `docs/security/threat-model-current-state.md` lines 62-63 | "No explicit anti-forgery token workflow is visible ..." |
| SEC-004 | `Rate limiting and request abuse` | `docs/security/threat-model-current-state.md` lines 57-58 | "`UseRateLimiter()` is not visible in `UiServer`; validate effective enforcement ..." |
| SEC-005 | `Authentication, sessions, and authorization` | `docs/security/threat-model-current-state.md` lines 50-54 | "Fund-scoped data partitions by `FundProfileId` alone; tenant isolation currently relies on the single-company-per-deployment boundary ..." |

## Weekly governance and status expectations

- Review all open security backlog items weekly using the workflow in `docs/operations/operator-runbook.md` (Security remediation cadence section).
- No item may move to `Done` without linked closure evidence that includes code/tests and threat-model residual update references.
- If any `High` item misses its target date, escalate in the weekly readiness review and record the mitigation/exception rationale.
