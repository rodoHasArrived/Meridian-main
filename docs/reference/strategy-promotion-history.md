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
- `ApprovalChecklist`
- `EvidenceReferences`
- `ApprovedBy`
- `ManualOverrideId`
- `AuditReference`

Approvals require the canonical checklist for the target mode before a target run is created.
`Backtest -> Paper` approvals must include DK1 trust packet, run lineage, portfolio/ledger
continuity, and risk-control review. `Paper -> Live` approvals must also include live-override
review plus explicit evidence references for each live checklist item. Live evidence references
use the checklist item as a stable prefix, for example
`LIVE_OVERRIDE_REVIEWED:manual-override/{overrideId}`, so a restarted service can validate the
durable history before exposing it.

The `/api/promotion/history` endpoint returns this model directly, so these fields are exposed in endpoint payloads.
