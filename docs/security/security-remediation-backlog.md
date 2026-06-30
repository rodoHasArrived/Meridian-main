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
- **Required code/tests (remaining slices to close the gap / enable multi-tenant deployment):**
  - **Slice 2** — bind a fund to the caller's tenant on the first **successful** authoritative write (using the server-resolved tenant context), and seed the file-backed registry from the workstation host's existing audit history for parity with the Postgres migration backfill. Gate `LoadFundRunsAsync` and the downstream-impact resolution by fund ownership via the registry: exclude runs for funds owned by another tenant; treat unbound/legacy funds as the ambient deployment tenant so single-tenant deployments are unaffected. Regression test: a fund owned by company B yields no runs/exposures for a company-A caller.
  - **Slice 3** — apply the same ownership gate at the other fund-scoped read boundaries (ledger books/journal, report-pack workflow records, `ReportPackSecurityLineIndex` cross-fund `Lookup`).
  - **Slice 4** — stop accepting client-body tenant overrides (`?? request.TenantId` / `?? request.CompanyId`) on accounting routes once an ambient server tenant context exists; reclassify this item from deployment-boundary-enforced to storage-enforced.
  - Optional hardening: stamp the resolved tenant onto `StrategyRunEntry` and add `tenant_id` partition columns on the fund-scoped stores as defense-in-depth alongside the registry gate.
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
