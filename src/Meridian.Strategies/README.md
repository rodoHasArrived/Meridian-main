---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-STRATEGIES
path: src/Meridian.Strategies
status: active
owner_lane: Strategy and Research
last_reviewed: 2026-05-27
---

# src/Meridian.Strategies

## Purpose

Strategies owns strategy lifecycle, run storage, promotion records, and strategy read services.

## Layer responsibility

This layer should preserve strategy lineage from research through paper validation and promotion review.

## Key folders and files

- `Interfaces/` - strategy lifecycle contracts.
- `Models/` and `Serialization/` - strategy run payloads and serialization support.
- `Promotions/` - promotion evidence and review records.
- `Services/` and `Storage/` - strategy read and persistence services.

## Important workflows

Use this module for strategy run evidence, promotion lineage, and research-to-paper continuity.
Reconciliation break queue persistence also owns shared Accounting casework lifecycle enforcement: assignment before investigation, evidence notes for awaiting-evidence, taxonomy before resolution, dual-control sign-off, privileged reopen, SLA computation, immutable audit events with source metadata, threaded comments, and idempotent bulk triage.
Reconciliation also projects Security Master accounting inputs for Operations Continuity: fixed
coupon accruals, expected journal previews, and factor-schedule principal paydowns are generated
from resolved Security Master economic definitions before ledger/reconciliation gate posture is
reported. Factor-based instruments distinguish missing schedules from stale prior-period factor
evidence so accounting operations can route principal-paydown blockers precisely. The real
Security Master adapter preserves mortgage-backed, asset-backed, and amortizing-loan asset classes
when normalizing economic definitions so factor paydowns stay principal events instead of being
collapsed into generic unsupported instruments.

## Diagrams

See `DIA-ASSURANCE-LOOP` in `docs/source/data/diagram-index.yml`.

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-STRATEGIES -->
| Roadmap item | Title |
| --- | --- |
| `W2-PROMO-001` | Paper promotion evidence and operator acceptance |
| `W3-CONT-001` | Research to paper continuity |
| `W5-BTSTUDIO-001` | Backtesting studio evidence loop |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-STRATEGIES -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```bash
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "Category!=Integration" --logger "console;verbosity=normal"
```

## Change rules

Preserve evidence lineage and avoid breaking promotion compatibility across browser and retained desktop consumers.

## Related docs

- `docs/plans/waves-2-4-operator-readiness-addendum.md`
- `docs/source/generated/source-roadmap-traceability.md`
