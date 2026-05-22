# Chief of Staff runtime integration

**Last Updated:** 2026-05-22

## Purpose

The Chief of Staff (CoS) runtime is an additive orchestration layer. It does not replace Meridian ledger, reconciliation, readiness, or evidence source-of-truth services.

## Shared workstation API routes

- `POST /api/workstation/chief-of-staff/sessions`
- `GET /api/workstation/chief-of-staff/sessions`
- `GET /api/workstation/chief-of-staff/sessions/{sessionId}`
- `POST /api/workstation/chief-of-staff/sessions/{sessionId}/decisions`
- `POST /api/workstation/chief-of-staff/sessions/{sessionId}/export-trace`
- `GET /api/workstation/chief-of-staff/health`

## Configuration

Configure under `ChiefOfStaff`:

- `RuntimeBaseUrl`
- `RequestTimeout`
- `MaxConcurrentSessions`
- `EnableTraceRetention`
- `AllowedIntentKinds`
- `AllowedMcpServers`
- `EnableDecisionRouting`

## Evidence integration

- Evidence subject kind: `chief-of-staff-session`
- CoS trace exports use existing evidence manifest retention via `/api/workstation/evidence/.../export-manifest`.

## Runtime scaffold

The in-repo ADK scaffold lives at `tools/chief-of-staff-runtime/` and defines node boundaries for:

- intent analysis
- context assembly
- evidence aggregation
- recommendation synthesis
- decision preparation
- trace emission
