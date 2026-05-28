# OMS/EMS Integration Contracts (v1)

## Versioning and Deprecation
- Current contract: `v1`.
- Deprecation policy: additive changes allowed in `v1`; breaking changes require `v2` route and a 90-day overlap.

## Endpoints
- `POST /api/oms/ingest`: idempotent ingestion with deduplication key.
- `GET /api/oms/messages`: canonical stored inbound messages.
- `GET /api/oms/adapters/diagnostics`: FIX and file-transfer adapter health/retry status.
- `POST /api/oms/excel/sync`: Excel bridge pull/push conflict resolution using timestamp precedence.
- `GET /api/oms/audit`: integration audit log.

## Security
- Intended scopes: `oms.ingest`, `oms.read`, `oms.sync`, `oms.audit`.
- Request signing: recommended for ingestion and sync requests where transport leaves trusted boundary.
- Key rotation hooks: rotate signer keys by key-id and overlap active+next key for at least one rotation window.

## Runbook
1. Validate adapter diagnostics and retry behavior.
2. Submit signed ingest payloads with correlation IDs.
3. Monitor dedup replay rates from audit stream.
4. For Excel conflicts, apply timestamp precedence and log manual overrides.
