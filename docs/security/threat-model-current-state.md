# Meridian Threat Model (Current State)

**Last Updated:** 2026-05-21

## 1. Overview

Meridian is a self-hosted .NET 10 fund-management and trading-operations platform. It ingests market data, runs backfill/research workflows, stores JSONL/Parquet data, serves a browser workstation and WPF desktop workstation, and exposes MCP tooling for AI-assisted workflows. Active domains include execution (paper/live-gated), security master, ledger, reconciliation, direct lending, ETL, and packaging.

Primary assets include provider/broker credentials, order-entry controls, operator sessions, historical market data, portfolio/ledger/security-master records, local files under `DataRoot`, PostgreSQL-backed state, audit trails, and generated reports/packages.

The primary network surface remains the local API host (`src/Meridian/UiServer.cs`) with endpoint mappings under `src/Meridian.Ui.Shared/Endpoints/*` and additional packaging routes from `src/Meridian.Application/Http/Endpoints/PackagingEndpoints.cs`. It binds to `http://localhost:{port}` by default, but deployment manifests commonly publish `8080`; production environments should treat the API as reachable unless explicitly isolated by network policy.

## 2. Threat model, trust boundaries, and assumptions

**Attacker-controlled inputs**
- HTTP requests to `/api/*`, `/workstation/*`, and auth endpoints when the host is exposed.
- Browser-originated mutation payloads.
- Symbols, provider names, order requests, export/backfill/replay/package parameters.
- Package files, security-master imports, partner CSV/SFTP content, and provider HTTP/WebSocket responses.
- MCP tool arguments supplied by an attached LLM client.

**Operator-controlled inputs**
- `appsettings.json`, `MDC_*` environment variables, provider credentials, DB connection/schema values, `DataRoot`, and deployment topology.
- Live execution enablement and brokerage wiring.

**Developer-controlled inputs**
- Tests/fixtures, docs/scripts, local dev proxy/tooling, and CI/AI automation metadata.

**Current security assumptions**
- Authentication defaults to required outside Development/Test (`AuthenticationModeResolver`), optional in Development/Test unless overridden by `MDC_AUTH_MODE`.
- Session auth uses in-memory random tokens, fixed-time credential comparison, HttpOnly + SameSite=Strict cookies, and fail-closed behavior when auth is required but not configured.
- RBAC is now actively enforced in many sensitive API paths via `EndpointAuthorization` + `UserPermission` checks (including execution, credential, security-master, and lifecycle/admin-sensitive routes), but coverage is not uniform across all endpoint groups.
- MCP server trust boundary is still local stdio: the MCP client is treated as operator-equivalent.

## 3. Attack surface, mitigations, and attacker stories

### Authentication, sessions, and authorization
- `LoginSessionMiddleware`, `AuthEndpoints`, `LoginSessionService`, and `UserProfileRegistry` implement session auth and role/permission context propagation.
- `UserPermission` + `RolePermissions` are now operational (not purely descriptive), and many write/admin endpoints use permission gates.
- Residual concerns:
  - Session cookies still do not set `Secure` (open remediation item; treat TLS + secure-cookie enablement as required for non-local deployments).
  - Operator credentials now require password hashes in `MDC_USERS` / `MDC_PASSWORD_HASH`, and governed user-account administration writes hashes plus audit evidence under the storage root; legacy plaintext password env bootstrap is no longer accepted.
  - Sessions are in-memory and reset on process restart.
  - Some endpoint groups still lack explicit permission checks (for example parts of the configuration routes), so authenticated overreach remains plausible where gates are absent. (Direct-lending routes are now permission-gated — group-level `RequireAnyPermission(View/ManageDirectLending)` plus `RequirePermission(ManageDirectLending)` on mutations — and additionally carry the SEC-005 fund-scoped write-tenant gate.)

### Tenant isolation (fund-scoped data) — SEC-005
- **Storage-enforced (was deployment-boundary-enforced).** Fund-scoped data is now partitioned by a stamped `tenant_id` at the storage layer, not solely by the single-company-per-deployment boundary:
  - Ledger stores (`ledger_books`, `accounting_periods`, journal entries) carry `tenant_id` (registry-backfilled + write-stamped) and filter reads by it, including the alternate-identifier paths (`ledgerBookId`/`periodId`/`fundStructureNodeId`).
  - The operations-continuity workflow store carries `tenant_id` (resolved through its ledger book to the authoritative `fund_profile_tenancy` registry, caller-tenant fallback for book-less workflows) and filters `ListAsync`/`GetAsync`.
  - The fund-account store (`account_definition` **and all its child tables** — balance snapshots, custodian/bank statements, reconciliation runs + breaks, sync history, margin snapshots — a separate database with no in-DB registry linkage) carries `tenant_id` stamped trust-on-first-use from the writing operator. Reads filter by it (`GetAccount`/`QueryAccounts` plus every child read), and child writes are refused for a tenant-scoped caller targeting a missing or foreign account (`FundAccountChildWriteTenantGuard`), so the `account_id` / reconciliation-run-id alternate-identifier routes on `FundAccountEndpoints` (scope-authorized, not tenant-authorized) can no longer read or inject cross-tenant rows.
  - Reads are **fail-open**: a null (unbound/legacy) `tenant_id` or a tenantless caller passes, so single-company-per-deployment behavior is unchanged. `TenantReadPredicate` (`Meridian.Contracts.Tenancy`) centralizes the decision and is unit-tested.
- **Write side (4c-iii):** fund-scoped write/evaluate routes across `LedgerEndpoints`, `AccountingSystemEndpoints`, and `DirectLendingEndpoints` carry `RequireFundScopedWriteTenant()`. Enforcement is **off by default** (detection-first: a tenantless write is logged, not blocked) and is enabled per shared-multi-tenant deployment via `MERIDIAN_FUND_SCOPED_WRITE_TENANT_REQUIRED=true`; the Security Master governed-write group is already unconditionally tenant-scoped.
- **Residual (deployment-gated):** enabling enforcement requires that every authenticated session carries a tenant (the legacy tenantless `MDC_USERNAME` admin must be given a `CompanyId` before enabling, or it will be refused fund-scoped writes). The fund-account child tables (balances, statements, reconciliation, sync, margin) are now tenant-partitioned alongside `account_definition`; the remaining un-partitioned surface is the fund-structure store (`fund_structure_assignment`, reached by node id) — further defense-in-depth with no endpoint-reachable cross-tenant path today, tracked in `docs/security/security-remediation-backlog.md` (SEC-005 slice 4c). Single-company deployments remain safe with enforcement off.

### Rate limiting and request abuse
- Mutation routes commonly attach `RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)`.
- `AddRateLimiter` is wired in shared services, but `UseRateLimiter()` is not visible in `UiServer`; validate effective enforcement in deployed hosts.

### CSRF and browser security
- SameSite=Strict cookies reduce cross-site cookie send behavior and React surfaces avoid obvious unsafe HTML injection patterns.
- No explicit anti-forgery token workflow is visible for cookie-authenticated mutations (known defense-in-depth gap; treat as an accepted risk only for tightly local/isolated deployments until token-based CSRF protection is added).

### Secrets and configuration exposure
- Sensitive masking is now applied in config payloads (`ConfigEndpoints` uses `SensitiveValueMasker` for Alpaca key/secret fields).
- Credential endpoints require `ManageCredentials` permission, improving separation for credential mutation/read paths.
- Provider secrets are stored through the local encrypted provider credential vault with rotation and verification metadata. Environment fallback is limited to Development/Test or an explicit migration override and is disabled for packaged/customer builds.
- Risk remains where broad config read/write endpoints are accessible without fine-grained authorization checks.

### Filesystem, packaging, and storage
- Path hardening is present in key areas:
  - `PathValidation` for base/path checks.
  - Workstation static asset serving now enforces full-path containment before file reads.
  - Packaging delete/download enforce package-directory containment against traversal.
- Residual review targets:
  - Packaging list endpoint accepts a user-supplied `directory` value; current checks reject traversal (`..`, null-byte) but do not constrain reads to a fixed base directory.
  - Storage migration and admin maintenance accept user-supplied path arrays.
  - Replay and import flows still process user-selected file paths and should be treated as high-value abuse surfaces.

### Trading and execution
- `ExecutionEndpoints` include permission checks, actor derivation from server-side session context, phase-gate checks, and routing-control gating.
- In paper mode, compromise primarily impacts simulation integrity; in live mode, execution-path abuse remains high impact.

### External data, SSRF, and integration boundaries
- Most adapters use fixed vendor endpoints and resilient HTTP clients, reducing generic SSRF exposure.
- Operator-configured external hosts/ports (e.g., IB/SFTP/other integration settings) still require strict governance because misconfiguration can become internal network reachability.

### Database and business-state integrity
- Parameterized DB access is common; dominant risks are authorization gaps, business-logic abuse, and audit/concurrency integrity rather than classic SQL injection from API fields.

### Scripting, MCP, and native adapters
- QuantScript remains opt-in and should be treated as trusted-host code execution, not a strong sandbox for hostile code.

## 4. Criticality calibration (current)

**Critical**
- Unauthenticated access to privileged API surfaces in non-development deployments.
- Auth/session bypass or forgery.
- Arbitrary file read/write/delete outside intended roots leading to credential theft, host compromise, or destructive tampering.
- Unauthorized live order placement/cancel-all pathways.
- Secret exfiltration from credential/config pathways.

**High**
- Authenticated users reaching privileged config, credential, execution, security-master, ledger, or direct-lending mutations without proper permission gates.
- SSRF/internal network probing via mis-governed operator-configurable integration endpoints.
- Persistent DoS via unbounded replay/export/backfill/package/native adapter resource usage.
- Bypass or unauthorized changes to risk/circuit-breaker/order-routing controls.

**Medium**
- Sensitive metadata/config leakage to authenticated users.
- CSRF or browser-mediated state change where cookie semantics or deployment topology reduce SameSite protection.
- Data poisoning from provider/CSV/package imports affecting research, readiness, or reporting.
- Spreadsheet formula injection risk in export workflows.

**Low**
- Findings requiring developer-controlled CI/test/script context.
- Self-XSS in local-only operator environments.
- Health/liveness metadata disclosures without privileged state leakage.
- Local-shell-only unsafe CLI paths requiring operator host access.
