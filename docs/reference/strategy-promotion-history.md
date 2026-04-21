# Strategy Promotion History Persistence

`PromotionService` now records promotion decisions through an injected persistence seam (`IPromotionRecordStore`) instead of relying on process-memory history.

## Durable Record Store

- Default implementation: `JsonlPromotionRecordStore` in `src/Meridian.Strategies/Storage/`.
- File format: append-only JSONL (`promotion-history.jsonl`).
- Startup behavior: historical records are loaded when `PromotionService` is constructed.
- Write behavior: approvals and rejections both append atomically.
- Read behavior: history is read from durable storage, so records survive process restarts.

## Promotion Record Fields

`StrategyPromotionRecord` now includes explicit promotion-chain fields:

- `SourceRunId`
- `TargetRunId`
- `Decision` (`Approved` or `Rejected`)
- `ApprovalReason`
- `ReviewNotes`
- `ApprovedBy`
- `ManualOverrideId`
- `AuditReference`

The `/api/promotion/history` endpoint returns this model directly, so these fields are exposed in endpoint payloads.
