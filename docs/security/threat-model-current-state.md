# Meridian Threat Model (Current State)

**Last Updated:** 2026-11-15

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
- Session auth uses random bearer tokens with only their hashes retained in the host's durable store, hashed credential verification, HttpOnly + SameSite=Strict cookies, and fail-closed behavior when auth is required but not configured. One strict auth-mode resolver governs both host startup and session composition.
- RBAC is now actively enforced in many sensitive API paths via `EndpointAuthorization` + `UserPermission` checks (including execution, credential, security-master, and lifecycle/admin-sensitive routes), but coverage is not uniform across all endpoint groups.
- MCP server trust boundary is still local stdio: the MCP client is treated as operator-equivalent.

## 3. Attack surface, mitigations, and attacker stories

### Authentication, sessions, and authorization
- `LoginSessionMiddleware`, `AuthEndpoints`, `LoginSessionService`, and `UserProfileRegistry` implement session auth and role/permission context propagation.
- `UserPermission` + `RolePermissions` are now operational (not purely descriptive), and many write/admin endpoints use permission gates.
- Residual concerns:
  - Session cookies set `Secure` except for proven loopback HTTP. Non-local required-auth production bindings require HTTPS or an explicitly configured TLS-terminating reverse proxy.
  - Operator credentials now require password hashes in `MDC_USERS` / `MDC_PASSWORD_HASH`, and governed user-account administration writes hashes plus audit evidence under the storage root; legacy plaintext password env bootstrap is no longer accepted.
  - Sessions and bounded username/client login-failure windows survive restart. Cooperating instances sharing one session store serialize reads and writes under a cross-process file lease and observe logout/revocation without cached-authority fallback. The fifth failed attempt returns `429` with `Retry-After` and rate-limit headers; timed lockout expires after 15 minutes. Corrupt/unreadable state refuses access. Separate data roots do not form a shared session authority; ADR-019 still limits the supported production envelope to one local node.
  - The failed-attempt table is capped at 1,024 username/client windows. When full it evicts its oldest window (preferring windows that are not locked out) instead of refusing new usernames, so throwaway-username churn neither locks out untracked accounts nor cheaply lifts an active lockout. `POST /api/auth/login` additionally carries a per-client-address `login` rate limit (10 requests per minute).
  - Sign-in follows `returnUrl` only when it is a path-absolute local URL (falling back to `/workstation/`), closing the open-redirect path. Because login is exempt from the CSRF token check, it refuses requests the browser marks as cross-site (`Sec-Fetch-Site: cross-site`, or a foreign `Origin` host when Fetch Metadata is absent).
  - A selected role profile that is missing or invalid refuses identity resolution instead of restoring the broader base role. Review actor and permission resolution continues through the authenticated request context.
  - Some endpoint groups still lack explicit permission checks (for example parts of the configuration routes), so authenticated overreach remains plausible where gates are absent. (Direct-lending routes are now permission-gated — group-level `RequireAnyPermission(View/ManageDirectLending)` plus `RequirePermission(ManageDirectLending)` on mutations — and additionally carry the SEC-005 fund-scoped write-tenant gate.)

### Tenant isolation (fund-scoped data) — SEC-005
- **Storage-enforced (was deployment-boundary-enforced).** Fund-scoped data is now partitioned by a stamped `tenant_id` at the storage layer, not solely by the single-company-per-deployment boundary:
  - Ledger stores (`ledger_books`, `accounting_periods`, journal entries) carry `tenant_id` (registry-backfilled + write-stamped) and filter reads by it, including the alternate-identifier paths (`ledgerBookId`/`periodId`/`fundStructureNodeId`).
  - The operations-continuity workflow store carries `tenant_id` (resolved through its ledger book to the authoritative `fund_profile_tenancy` registry, caller-tenant fallback for book-less workflows) and filters `ListAsync`/`GetAsync`.
  - The fund-account store (`account_definition`, a separate database with no in-DB registry linkage) carries `tenant_id` stamped trust-on-first-use from the writing operator and filters `GetAccount`/`QueryAccounts`.
  - `MERIDIAN_TENANT_SCOPE_ENFORCEMENT=fail-closed` rejects tenantless callers and hides unattributed/foreign rows. `TenantReadPredicate` centralizes the storage decision. The legacy deployment-boundary migration posture permits unattributed reads only within an isolated single-company deployment; multiple configured companies refuse startup and subsequent identity resolution under that posture.
- **Write side (4c-iii):** fund-scoped write/evaluate routes across `LedgerEndpoints`, `AccountingSystemEndpoints`, and `DirectLendingEndpoints` carry `RequireFundScopedWriteTenant()`. Strict tenant reads now automatically enable this write gate, so a separate absent/false write setting cannot leave tenantless writes enabled. `MERIDIAN_FUND_SCOPED_WRITE_TENANT_REQUIRED=true` may additionally tighten the single-company migration posture.
- **Deployment requirements:** every strict-mode operator needs a `CompanyId`; retained data must be attributed before switching from the migration posture. The unpartitioned fund-structure implementation refuses multiple companies even with strict mode selected. These controls and focused cross-tenant tests do not certify shared multi-tenant production hosting; the supported envelope remains ADR-019's isolated local workstation.

### Rate limiting and request abuse
- `UiServer` calls `UseRateLimiter()` after the session, API-key, CSRF, and authorization guards, so the shared policies registered by `UiEndpoints.AddMutationRateLimiter` are enforced in the host.
- Mutation routes commonly attach `RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)` (10 requests per minute per client address, small burst queue). Sign-in uses the separate `login` policy, and direct-lending mutations use a per-actor policy (20 per minute). Rejections return `429` with `Retry-After`.
- `MDC_DISABLE_RATE_LIMIT=true` bypasses these limiters only in an explicit Development/Test host outside production, packaged, and customer postures.
- Residual concern: not every mutating route attaches a rate-limit policy, and limits are per process rather than shared across cooperating instances.

### CSRF and browser security
- SameSite=Strict cookies reduce cross-site cookie send behavior and React surfaces avoid obvious unsafe HTML injection patterns.
- Cookie-authenticated mutations use a double-submit CSRF token: the host issues a random `mdc-csrf` cookie (SameSite=Strict, readable by the workstation) alongside the session, and `UseCookieCsrfProtection()` requires a matching `X-CSRF-Token` header, compared in constant time, on `POST`/`PUT`/`PATCH`/`DELETE` `/api/*` requests that carry a session cookie. The WPF and shared API clients echo the cookie automatically (`ApiClientSession`, `ApiClientService`).
- `POST /api/auth/login` is exempt from the token check so plain HTML forms can sign in. The cross-site refusal described under authentication covers that exemption. Requests without a session cookie (for example `X-Api-Key` clients) are not CSRF-checked.

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
- The provider-integration REST transport requires an approved HTTPS origin and revalidates DNS
  at each redirect. Its production client also resolves and checks addresses inside
  `SocketsHttpHandler.ConnectCallback`, then connects to those numeric addresses without another
  DNS lookup. TLS retains normal host/certificate validation. Automatic redirects and implicit
  system proxies are disabled; configured provider origins must be directly reachable.
  Private IPv6 unique-local addresses are rejected alongside private IPv4, loopback, and
  link-local addresses. The response limit applies while streaming even without Content-Length.
  Focused rebinding, redirect, private-address, and unknown-length-body tests are in
  `ProviderIntegrationHttpClientTransportTests`; this does not certify every vendor adapter or
  statement parser, and hosted validation remains required.
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
