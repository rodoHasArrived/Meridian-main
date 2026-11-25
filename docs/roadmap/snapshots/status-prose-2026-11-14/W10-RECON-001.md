# W10-RECON-001 status-detail migration snapshot

Captured from `docs/roadmap/data/roadmap-items.yml` at `3503ae000e` on 2026-11-14.
This retains the original implementation detail, reservations and historical claims verbatim.
Read the current registry for status and decisions; this snapshot does not grant new acceptance.
Detailed boundaries remain applicable until a later scoped change replaces them.

```yaml
    current_summary: Implementation in PR 3004 separates stable business lineage from comparison policy, retains executed mapping fingerprints and completed recovery evidence, resolves named SLA calendars, and shows source state and occurrence age in browser and WPF. Source clearing never disposes governed casework. Reconstructed after workspace maintenance removed an earlier unpublished implementation; that earlier local test result is not acceptance evidence for this head. Focused verification ran on 2026-11-10 against source 471d651e - the lineage, SLA-calendar, statement-intake and statement-lineage test classes passed locally on Linux (39 tests), the same filter passed on hosted Linux (Targeted Test run 42910954556), and FundReconciliationWorkbenchServiceTests passed on hosted Windows with the full WPF build (Targeted Test run 42910959791, 12 tests). The browser source-observation test was not rerun in that pass, and rendered operator verification remains required. Legacy imports without mapping proof remain non-comparable, and historical policy forks require explicit review.
```
