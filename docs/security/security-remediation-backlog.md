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

## Threat-model traceability

| Backlog ID | Threat-model section | Threat-model source lines | Residual concern excerpt |
| --- | --- | --- | --- |
| SEC-001 | `Authentication, sessions, and authorization` | `docs/security/threat-model-current-state.md` lines 50-54 | "Some endpoint groups still lack explicit permission checks ..." |
| SEC-002 | `Authentication, sessions, and authorization` | `docs/security/threat-model-current-state.md` lines 50-51 | "Session cookies still do not set `Secure` ... treat TLS + secure-cookie enablement as required ..." |
| SEC-003 | `CSRF and browser security` | `docs/security/threat-model-current-state.md` lines 62-63 | "No explicit anti-forgery token workflow is visible ..." |
| SEC-004 | `Rate limiting and request abuse` | `docs/security/threat-model-current-state.md` lines 57-58 | "`UseRateLimiter()` is not visible in `UiServer`; validate effective enforcement ..." |

## Weekly governance and status expectations

- Review all open security backlog items weekly using the workflow in `docs/operations/operator-runbook.md` (Security remediation cadence section).
- No item may move to `Done` without linked closure evidence that includes code/tests and threat-model residual update references.
- If any `High` item misses its target date, escalate in the weekly readiness review and record the mitigation/exception rationale.
