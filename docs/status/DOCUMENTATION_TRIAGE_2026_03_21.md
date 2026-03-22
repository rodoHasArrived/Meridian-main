# Documentation Triage

**Date:** 2026-03-21
**Scope:** Full markdown sweep of the active `docs/` tree after the archive reorganization
**Outcome:** Reviewed active docs for archive placement, obvious drift, and update markers

---

## Summary

This pass reviewed the active documentation tree and applied one of three outcomes:

- **Updated** when there was a clear, low-risk correction to make immediately
- **Archive** when a document was already historical and had been moved out of `docs/`
- **To Be Updated** when the document is still useful but should not be treated as authoritative without a refresh

One additional point-in-time audit snapshot was moved to the archive during this pass to keep the active audits area focused on current guidance while preserving historical traceability.

---

## Coverage

The following active markdown areas were reviewed:

| Area | Markdown Files Reviewed |
|------|--------------------------|
| `docs/` root | 3 |
| `docs/adr/` | 19 |
| `docs/ai/` and subfolders | 16 |
| `docs/architecture/` | 12 |
| `docs/audits/` | 5 |
| `docs/development/` and `policies/` | 19 |
| `docs/diagrams/` and `uml/` READMEs | 2 |
| `docs/docfx/` | 1 |
| `docs/evaluations/` | 17 |
| `docs/examples/provider-template/` | 1 |
| `docs/generated/` | 8 |
| `docs/getting-started/` | 1 |
| `docs/integrations/` | 4 |
| `docs/operations/` | 8 |
| `docs/plans/` | 11 |
| `docs/providers/` | 8 |
| `docs/reference/` | 7 |
| `docs/security/` | 2 |
| `docs/status/` | 10 |

---

## Updated In This Pass

| Document | Action | Reason |
|----------|--------|--------|
| `docs/development/build-observability.md` | Updated | Replaced an absolute local filesystem link with a correct relative link |
| `docs/plans/codebase-audit-cleanup-roadmap.md` | Rewritten | Converted from point-in-time audit prose into an active cleanup backlog aligned to the current repository |
| `docs/evaluations/high-impact-improvements-brainstorm.md` | Marked to be updated | Added an explicit warning that newer March 2026 planning docs may supersede it |
| `docs/README.md` | Updated earlier in the session | Linked active docs to the new project-level archive |
| `docs/audits/AUDIT_REPORT_2026_03_20.md` | Archived | Date-stamped audit snapshot moved to `archive/docs/assessments/` because `AUDIT_REPORT.md` remains the active report surface |
| `docs/status/FULL_IMPLEMENTATION_TODO_2026_03_20.md` | Rewritten | Normalized the broader non-assembly backlog around current open tracks instead of partial-item reconciliation notes |
| `docs/plans/meridian-6-week-roadmap.md` | Rewritten | Updated the six-week plan to reflect the current repo baseline rather than already-closed platform items |

---

## Archived In This Sweep Window

The former `docs/archived/` tree was already relocated out of `docs/` during this cleanup effort and now lives under `archive/docs/` with subfolders:

- `archive/docs/assessments/`
- `archive/docs/plans/`
- `archive/docs/summaries/`
- `archive/docs/migrations/`
- `archive/docs/assets/`

This means historical and deprecated documents are no longer mixed into the active docs tree.

Additionally archived in this pass:

- `docs/audits/AUDIT_REPORT_2026_03_20.md` -> `archive/docs/assessments/AUDIT_REPORT_2026_03_20.md`

---

## Active Docs Marked To Be Updated

These documents remain in `docs/` but should be refreshed before being treated as source-of-truth guidance:

| Document | Why It Needs Follow-Up |
|----------|------------------------|
| `docs/evaluations/high-impact-improvements-brainstorm.md` | Useful for idea history, but older than newer roadmap and planning artifacts |

---

## Reviewed And Kept Active

The remaining active documentation was reviewed for obvious archive misplacement and legacy archive-link drift and was kept in place.

These areas remain active:

- `docs/adr/`
- `docs/ai/`
- `docs/architecture/`
- `docs/audits/`
- `docs/development/`
- `docs/diagrams/`
- `docs/docfx/`
- `docs/evaluations/`
- `docs/examples/`
- `docs/generated/`
- `docs/getting-started/`
- `docs/integrations/`
- `docs/operations/`
- `docs/plans/`
- `docs/providers/`
- `docs/reference/`
- `docs/security/`
- `docs/status/`

Keeping a document active in this report means:

- it was not clearly historical enough to archive immediately
- it did not present a low-risk factual correction large enough to rewrite blindly
- it did not obviously contradict the new archive structure

---

## Recommended Next Pass

If we continue this cleanup, the next highest-value documentation refreshes are:

1. Review older brainstorm-style evaluations and either merge key items into `docs/status/IMPROVEMENTS.md` or archive them.
2. Refresh generated/status adjacency documents so `ROADMAP.md`, `FEATURE_INVENTORY.md`, and the normalized backlog docs stay aligned.
3. Continue trimming historical planning material out of active folders once it stops serving as current guidance.
