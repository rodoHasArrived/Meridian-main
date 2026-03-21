# Archived Documentation Index

**Last Updated:** 2026-03-20

This folder contains historical documentation that has been superseded by newer guides or is no longer actively maintained. These documents are preserved for historical reference and to understand the evolution of the project.

---

## Why Archive?

Documents are archived when:
- ✅ Implementation has been completed
- 🔄 Approach has changed or been superseded
- 📚 Information has been incorporated into other docs
- 📜 Value is primarily historical reference

---

## Archived Documents

### Planning & Reorganization Documents

| Document | Date Archived | Reason | Current Reference |
|----------|---------------|--------|-------------------|
| **DUPLICATE_CODE_ANALYSIS.md** | 2026-02-12 | Analysis complete, most items resolved | [ROADMAP.md](../status/ROADMAP.md) |
| **REPOSITORY_REORGANIZATION_PLAN.md** | 2026-02-12 | Superseded by new organization guide | [Repository Organization Guide](../development/repository-organization-guide.md) |
| **consolidation.md** | 2026-02-12 | Consolidation work completed | — |
| **desktop-ui-alternatives-evaluation.md** | 2026-02-12 | Decision made: WPF is primary desktop platform | [UI Redesign](../architecture/ui-redesign.md) |
| **IMPROVEMENTS_2026-02.md** | 2026-02-12 | Consolidated into unified tracker | [IMPROVEMENTS.md](../status/IMPROVEMENTS.md) |
| **STRUCTURAL_IMPROVEMENTS_2026-02.md** | 2026-02-12 | Consolidated into unified tracker | [IMPROVEMENTS.md](../status/IMPROVEMENTS.md) |
| **repository-cleanup-action-plan.md** | 2026-03-15 | All phases (1–6) complete; historical record of completed cleanup work | [EVALUATIONS_AND_AUDITS.md](../status/EVALUATIONS_AND_AUDITS.md) |

### UWP-Related Documents

| Document | Date Archived | Reason | Current Reference |
|----------|---------------|--------|-------------------|
| **uwp-development-roadmap.md** | 2026-02-12 | UWP deprecated in favor of WPF | [WPF Implementation Notes](../development/wpf-implementation-notes.md) |
| **uwp-release-checklist.md** | 2026-02-12 | UWP no longer primary desktop platform | — |
| **uwp-to-wpf-migration.md** | 2026-02-20 | Migration complete, UWP fully removed | [WPF Implementation Notes](../development/wpf-implementation-notes.md) |
| **desktop-app-xaml-compiler-errors.md** | 2026-02-20 | UWP XAML compiler issues, no longer applicable | — |

### Desktop Development Planning Documents

| Document | Date Archived | Reason | Current Reference |
|----------|---------------|--------|-------------------|
| **desktop-devex-high-value-improvements.md** | 2026-02-20 | Superseded by implementation guide and executive summary | [Desktop Platform Improvements](../evaluations/desktop-platform-improvements-implementation-guide.md) |
| **ROADMAP_UPDATE_SUMMARY.md** | 2026-02-20 | PR summary artifact, content in ROADMAP.md | [ROADMAP.md](../status/ROADMAP.md) |

### CI/CD & Build Fix Summaries (moved from `.github/`)

| Document | Date Archived | Reason | Current Reference |
|----------|---------------|--------|-------------------|
| **QUICKSTART_2026-01-08.md** | 2026-03-16 | Historical snapshot of CI/CD additions (Jan 2026); superseded by `.github/workflows/README.md` | [workflows/README.md](../../.github/workflows/README.md) |
| **WORKFLOW_IMPROVEMENTS_2026-01-08.md** | 2026-03-16 | Explicitly self-marked DEPRECATED; workflows since consolidated from 25→17; superseded by `workflows/README.md` | [workflows/README.md](../../.github/workflows/README.md) |
| **CS0101_FIX_SUMMARY.md** | 2026-03-16 | Specific bug fix record (duplicate type CS0101, Feb 2026); build issue resolved | — |
| **TEST_MATRIX_FIX_SUMMARY.md** | 2026-03-16 | Test infrastructure fix record (missing Integration trait, Feb 2026); issue resolved | — |

### PR and Change Summaries

| Document | Date Archived | Reason | Current Reference |
|----------|---------------|--------|-------------------|
| **2026-02_PR_SUMMARY.md** | 2026-02-12 | Historical PR summary from Feb 2026 | [CHANGELOG.md](../status/CHANGELOG.md) |
| **2026-02_UI_IMPROVEMENTS_SUMMARY.md** | 2026-02-12 | UI improvements completed | [IMPROVEMENTS.md](../status/IMPROVEMENTS.md) |
| **2026-02_VISUAL_CODE_EXAMPLES.md** | 2026-02-12 | Examples incorporated into dev guides | — |
| **CHANGES_SUMMARY.md** | 2026-02-12 | Change summary superseded by changelog | [CHANGELOG.md](../status/CHANGELOG.md) |

### Audit & Assessment Reports

| Document | Date Archived | Reason | Current Reference |
|----------|---------------|--------|-------------------|
| **UWP_COMPREHENSIVE_AUDIT.md** | 2026-02-21 | UWP fully removed from codebase | [EVALUATIONS_AND_AUDITS.md](../status/EVALUATIONS_AND_AUDITS.md) |
| **CONFIG_CONSOLIDATION_REPORT.md** | 2026-02-21 | Consolidation work complete, no duplicates found | [EVALUATIONS_AND_AUDITS.md](../status/EVALUATIONS_AND_AUDITS.md) |
| **CLEANUP_SUMMARY.md** | 2026-03-15 | All hygiene phases complete (H1–H3); historical reference only | [EVALUATIONS_AND_AUDITS.md](../status/EVALUATIONS_AND_AUDITS.md) |
| **H3_DEBUG_CODE_ANALYSIS.md** | 2026-03-15 | Complete — no action required; historical reference only | [EVALUATIONS_AND_AUDITS.md](../status/EVALUATIONS_AND_AUDITS.md) |
| **CLEANUP_OPPORTUNITIES.md** | 2026-03-15 | All platform cleanup items fully completed; historical reference only | [EVALUATIONS_AND_AUDITS.md](../status/EVALUATIONS_AND_AUDITS.md) |

### Desktop Evaluation Summaries

| Document | Date Archived | Reason | Current Reference |
|----------|---------------|--------|-------------------|
| **desktop-end-user-improvements.md** | 2026-03-20 | Full desktop UX assessment superseded by the active desktop evaluation set | [Desktop Improvements Executive Summary](../evaluations/desktop-improvements-executive-summary.md) |
| **desktop-end-user-improvements-shortlist.md** | 2026-03-04 | Subset of full desktop evaluation, superseded by the active desktop summary | [Desktop Improvements Executive Summary](../evaluations/desktop-improvements-executive-summary.md) |

### Design Documents

| Document | Date Archived | Reason | Current Reference |
|----------|---------------|--------|-------------------|
| **REDESIGN_IMPROVEMENTS.md** | 2026-02-12 | UI redesign completed | [UI Redesign](../architecture/ui-redesign.md) |
| **ARTIFACT_ACTIONS_DOWNGRADE.md** | 2026-02-12 | GitHub Actions artifact issue resolved | — |

---

## Document Summaries

### DUPLICATE_CODE_ANALYSIS.md

**Status:** Mostly complete, remaining work tracked in ROADMAP Phase 6

**Key Findings:**
- ~12 duplicate domain models identified (resolved)
- ~59 duplicate desktop services across WPF/UWP (partial resolution)
- HTTP client duplication (resolved)
- Provider implementation patterns (standardized)

**What Remains:**
- Service deduplication Phase 2-4 (ROADMAP 6C.2-6C.4)
- UWP platform decoupling decision (ROADMAP 6E)

---

### REPOSITORY_REORGANIZATION_PLAN.md

**Status:** Superseded by [Repository Organization Guide](../development/repository-organization-guide.md)

**Key Contributions:**
- Identified structural issues
- Proposed file organization patterns
- Documented cleanup procedures

**New Guide Improvements:**
- More comprehensive project structure documentation
- Clear dependency rules and enforcement
- Common pitfalls with solutions
- Quick reference sections

---

### consolidation.md

**Status:** Work completed

**Summary:**
- Documented need to consolidate duplicate service implementations
- Many items addressed in Phase 6 completion
- Remaining work tracked in ROADMAP Phase 6C

---

### desktop-ui-alternatives-evaluation.md

**Status:** Decision made: WPF is recommended, UWP legacy

**Key Decision:**
- WPF chosen as primary desktop platform
- UWP maintained for compatibility but not prioritized
- Cross-platform desktop (Avalonia/MAUI) deferred

**Current State:**
- WPF development active
- UWP receives maintenance only
- See [WPF Implementation Notes](../development/wpf-implementation-notes.md)

---

### uwp-development-roadmap.md

**Status:** UWP no longer primary platform

**Historical Value:**
- Documents original UWP feature planning
- Shows evolution from UWP to WPF focus
- Reference for understanding legacy UWP code

**Current Approach:**
- See [WPF Implementation Notes](../development/wpf-implementation-notes.md)
- See [Desktop Platform Improvements](../evaluations/desktop-platform-improvements-implementation-guide.md)

---

### 2026-02 PR Summaries

**Status:** Historical snapshots from February 2026

**Purpose:**
- Document PR contents for context
- Track implementation progress
- Provide audit trail

**Current Tracking:**
- See [CHANGELOG.md](../status/CHANGELOG.md) for version history
- See [ROADMAP.md](../status/ROADMAP.md) for current status

---

### REDESIGN_IMPROVEMENTS.md

**Status:** UI redesign completed and incorporated

**Key Achievements:**
- Document expansion from 296 to 840 lines
- 12 major sections added
- Production-ready UI specification

**Current Reference:**
- [UI Redesign](../architecture/ui-redesign.md) - Current design spec
- [Desktop Layers](../architecture/desktop-layers.md) - Architecture

---

### IMPROVEMENTS_2026-02.md & STRUCTURAL_IMPROVEMENTS_2026-02.md

**Status:** Consolidated into unified improvement tracker

**Background:**
- Two separate improvement tracking documents existed (functional and structural)
- Content overlapped with different organization schemes
- Made tracking progress difficult

**Consolidation:**
- Combined into single [IMPROVEMENTS.md](../status/IMPROVEMENTS.md)
- Organized by theme (Reliability, Testing, Architecture, API, Performance, UX, Operations)
- Cross-referenced to ROADMAP.md phases
- Tracks 33 items (14 completed, 4 partial, 15 open)

**Original Content:**
- IMPROVEMENTS: 10 completed, 3 partial, 2 open, 4 new items (19 total)
- STRUCTURAL_IMPROVEMENTS: 15 architectural/code improvements

**Archived Files Preserve:**
- Historical status snapshots from Feb 2026
- Original priority matrices
- Implementation notes and findings

---

## Accessing Archived Documents

All archived documents are still available in this folder for reference. To view:

```bash
# List all archived documents
ls -lh docs/archived/

# View a specific document
cat docs/archived/DUPLICATE_CODE_ANALYSIS.md
```

Or browse on GitHub: https://github.com/rodoHasArrived/Meridian/tree/main/docs/archived

---

## When to Archive a Document

Consider archiving a document when:

1. **Work is Complete**
   - Implementation finished
   - All action items resolved
   - Information captured in permanent locations

2. **Superseded**
   - Better/newer document covers the same topic
   - Approach has changed significantly
   - Document structure improved

3. **Historical Value Only**
   - No longer referenced in active development
   - Useful for understanding past decisions
   - Not needed for day-to-day work

### Archiving Procedure

1. **Update current documentation** to cover any valuable content
2. **Create entry in this INDEX.md** with context
3. **Move document** to `docs/archived/`
4. **Update links** in other docs to point to new locations
5. **Add note** at top of archived doc pointing to replacement

---

## Unarchiving

If an archived document becomes relevant again:

1. Review the document for accuracy
2. Update outdated information
3. Move back to appropriate active directory
4. Update this index
5. Update links throughout documentation

---

## Historical Context

The Meridian project has evolved significantly since its inception:

- **Early Phase:** Focus on basic data collection
- **Growth Phase:** Added multiple providers, desktop apps
- **Maturity Phase:** Architecture refinement, code cleanup
- **Current Phase:** Production readiness, optimization

These archived documents chronicle this evolution and provide insight into architectural decisions and trade-offs made along the way.

---

## Questions?

If you have questions about archived documents or need historical context:

1. Check this index for pointers to current docs
2. Open a [GitHub Discussion](https://github.com/rodoHasArrived/Meridian/discussions)
3. Reference the specific archived document in your question

---

*This index is maintained as part of repository cleanup efforts.*
