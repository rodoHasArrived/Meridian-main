# OMS/EMS Integration Contracts and Runbook

**Owner:** Workstation Shell and UX
**Status:** Active
**Contract version:** `v1`
**Last reviewed:** 2026-05-28

## Scope

Meridian exposes shared OMS/EMS integration contracts from `src/Meridian.Ui.Shared/Contracts/Integrations/` and wires the runnable API handler from `src/Meridian.Ui.Services/Services/Integrations/`. The same contracts are intended for browser workstation, WPF desktop shell, local API host, and integration-slice tests.

## Endpoint Surface

Base path: `/api/oms`

| Method | Route | Scope | Permission | Purpose |
| --- | --- | --- | --- | --- |
| `POST` | `/ingest` | `OmsIntegrationScope.Write` | `ManageOrders` | Accept inbound OMS/EMS events with replay-safe deduplication. |
| `GET` | `/messages` | `OmsIntegrationScope.Read` | `ViewTrades` | Return the current in-memory canonical event snapshot. |
| `GET` | `/adapters/diagnostics` | `OmsIntegrationScope.Diagnostics` | `ViewDiagnostics` | Return FIX, SFTP, file-drop, and Excel bridge boundaries with retry posture. |
| `POST` | `/excel/sync` | `OmsIntegrationScope.Write` | `ManageOrders` | Resolve Excel pull/push synchronization conflicts. |
| `POST` | `/auth/signing-keys/rotate` | `OmsIntegrationScope.Admin` | `ManageCredentials` | Run the signing-key rotation hook and audit the change. |
| `GET` | `/audit` | `OmsIntegrationScope.Diagnostics` | `ViewDiagnostics` | Return integration audit events, newest first. |

## Inbound Mapping Contract

`OmsInboundMessage` is the canonical inbound event envelope:

- `sourceSystem` identifies the OMS/EMS or adapter.
- `externalOrderId` is the upstream order identifier.
- `eventType` is the upstream lifecycle event type, such as `new`, `fill`, `cancel`, or `replace`.
- `eventTimestampUtc` is the upstream event timestamp.
- `payloadHash` is the adapter-computed payload fingerprint.
- `deduplicationKey` is optional. If omitted, Meridian computes a stable SHA-256 key from source system, external order id, event type, timestamp, and payload hash.
- `correlationId` ties ingest, audit, diagnostics, and replay evidence together.

Processing is idempotent: the first event for a deduplication key becomes canonical, and later replays are acknowledged with `replayDetected = true` without overwriting the original message.

## Adapter Boundaries and Retry Policy

The diagnostics endpoint declares these active boundaries:

- `fix`: TCP/FIX session boundary, request signing required, `Write` and `Diagnostics` scopes.
- `sftp`: file-transfer boundary for SFTP pulls/drops, request signing required, exponential retry policy.
- `file-drop`: local drop-folder boundary, no request signing by default, exponential retry policy.
- `excel-bridge`: workbook synchronization boundary for pull/push sync, request signing required, `Read` and `Write` scopes.

Retry policy fields are `maxAttempts`, `initialDelay`, `maxDelay`, `backoffMultiplier`, and `useJitter`. Handlers should record retry status in audit events rather than performing fire-and-forget retries inside endpoint delegates.

## Excel Bridge Synchronization Policy

`POST /api/oms/excel/sync` accepts an `OmsSyncRequest` with `mode`, `entityId`, `correlationId`, `pullRecord`, and `pushRecord`.

- `mode = pull`: Meridian treats the workbook as requesting the canonical platform value.
- `mode = push`: Meridian treats the workbook as proposing an update.
- Conflict policy is `timestamp-precedence`.
- The record with the newest `updatedAtUtc` wins.
- If timestamps tie, the pull/platform record wins as a deterministic tie-break.
- Each resolution is audit logged as `excel.sync-conflict-resolved`.

## Request Signing and Key Rotation

Signed requests use these headers when an external adapter requires signing:

- `X-Meridian-Key-Id`
- `X-Meridian-Timestamp`
- `X-Meridian-Signature`

The canonical payload for the current minimal API endpoints is the request `correlationId`. Handlers validate HMAC-SHA256 signatures when all signing headers are present and audit rejection as `auth.signature-rejected`. Missing signatures are allowed for local fixture/dev flows unless the adapter boundary marks request signing as required and the calling host enforces that requirement.

Signing keys are rotated through `POST /api/oms/auth/signing-keys/rotate`. The hook records `auth.signing-key-rotated` audit evidence and replaces the active key material for the supplied key id.

## Versioning and Deprecation Policy

- Current contract version is `v1` and is represented by the `Oms*` DTO names in `Meridian.Ui.Shared`.
- Additive fields are allowed when defaults preserve existing clients.
- Breaking request/response changes require a new route prefix or DTO version suffix.
- Deprecated fields must remain readable for at least one minor release cycle and be called out in this document before removal.
- Contract tests must cover deduplication, replay behavior, adapter diagnostics, signing rejection, and Excel conflict resolution before a contract is promoted.

## Operational Runbook

1. Confirm adapter health with `GET /api/oms/adapters/diagnostics`.
2. For duplicate inbound events, inspect `replayDetected` and audit action `ingest.replay-ignored`.
3. For rejected signed requests, inspect audit action `auth.signature-rejected`, key id, and adapter boundary signing requirement.
4. For Excel workbook conflicts, inspect `excel.sync-conflict-resolved` audit events and compare `pullRecord.updatedAtUtc` with `pushRecord.updatedAtUtc`.
5. Rotate signing keys with `/auth/signing-keys/rotate` during planned maintenance and keep the resulting correlation id in the operator change ticket.
