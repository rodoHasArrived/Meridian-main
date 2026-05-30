# Audit Source-Material Index

**Status:** migration-source
**Owner:** core-team
**Reviewed:** 2026-05-30

This folder is retained as source material for historical code-quality, simplification, and architecture-hygiene reviews. It is no longer the canonical governance front door in the documentation rebuild.

Use current guidance first:

- [Documentation Front Door](../README.md)
- [Engineering Documentation](../engineering/README.md)
- [Documentation Ownership Contract](../documentation-ownership.md)
- [Documentation Inventory](../documentation-inventory.md)
- [Assessment Archive](../../archive/docs/assessments/README.md)

## Current Role In The Rebuild

- Keep active only when a file is explicitly linked as current evidence by the rebuilt docs model.
- Treat older point-in-time reviews as historical source material.
- Move or redirect superseded audit snapshots into `archive/docs/assessments/` in small batches.
- Do not use this folder as a new destination for documentation governance work; route new docs through `docs/engineering/`, `docs/product/`, `docs/operators/`, `docs/reference/`, or `docs/ai/`.

## Active Or Source-Material Files

| Document | Migration status | Notes |
| --- | --- | --- |
| [AUDIT_REPORT.md](AUDIT_REPORT.md) | redirect | Redirects to the retained archived copy. |
| [BACKTEST_ENGINE_CODE_REVIEW_2026_03_25.md](BACKTEST_ENGINE_CODE_REVIEW_2026_03_25.md) | redirect | Redirects to the retained archived copy; durable backtesting rules are summarized in engineering docs. |
| [FURTHER_SIMPLIFICATION_OPPORTUNITIES.md](FURTHER_SIMPLIFICATION_OPPORTUNITIES.md) | redirect | Redirects to the retained archived copy; current cleanup handling is summarized in engineering docs. |
| [workspace-visual-audit-checklist-2026-04-22.md](workspace-visual-audit-checklist-2026-04-22.md) | redirect | Redirects to the retained archived copy; current visual consistency rules are summarized in engineering docs. |
| [CODE_REVIEW_2026-03-16.md](CODE_REVIEW_2026-03-16.md) | redirect | Redirects to the retained archived copy. |

## Machine-Readable Outputs

Historical machine-readable audit outputs have moved to [archive/docs/assessments/](../../archive/docs/assessments/README.md):

- `archive/docs/assessments/audit-architecture-results.txt`
- `archive/docs/assessments/audit-code-results.json`
- `archive/docs/assessments/audit-results-full.json`
- `archive/docs/assessments/prompt-generation-results.json`

Do not treat these point-in-time outputs as current findings without rerunning the owning audit command against the current checkout.

## Archived Audit Snapshots

Historical point-in-time audit documents live in [archive/docs/assessments/](../../archive/docs/assessments/README.md).

## Migration Rules

1. Confirm whether a file is still referenced by rebuilt canonical docs.
2. Extract current findings into [Engineering](../engineering/README.md), [Product](../product/README.md), [Operators](../operators/README.md), or [Reference](../reference/README.md).
3. Replace high-traffic active paths with redirect stubs when the historical copy moves to archive.
4. Update [Documentation Inventory](../documentation-inventory.md) and archive indexes in the same batch.
5. Run targeted docs validation after each batch.
