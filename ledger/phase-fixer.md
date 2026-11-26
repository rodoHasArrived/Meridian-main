# Phase-fixer ledger

First recorded run: 2026-11-25 (main be2baba0). Latest run: 2026-11-26.

Note: on current main, `roadmap-source-docs.yml` triggers `pull_request` on `edited`, `labeled`, `unlabeled` too, so a label add starts a fresh scope-gate run itself (draft PRs skip the job). The known-error entry AI-20260828 still says only the next push re-runs it.

## Actions

| PR | Head SHA | Phase | How determined | Method | Date | Gate result after next run |
|---|---|---|---|---|---|---|
| 3774 | a1ddcb3 | PR9 | Local enforce_phase_scope.py over 10 changed files: PR0-PR5 fail all 10, PR6-PR8 still fail on `.rgignore` and `scripts/check_workflow_docs_parity.py`, PR9 passes. No phase named in title/body. | label | 2026-11-25 | PASSED (scope-gate job 139492638675, 2026-11-25 23:26Z, success) |
| 3610 | f89551f | PR9 | Local enforce_phase_scope.py over 13 changed files: PR7/PR8 fail only on `.gitleaksignore`, PR9 passes. No phase named in title/body. | label | 2026-11-25 | unknown: PR no longer open as of 2026-11-26 (merged or closed, not checked) |
| 3728 | 9744ce3 | PR7 | Local enforce_phase_scope.py over 12 changed files (merge-base diff, matches API count): PR0-PR3 fail 9-10, PR4-PR6 fail 5 (src/Meridian.Application/**), PR7 passes. No phase in title/body. | label | 2026-11-26 | pending (non-draft; label event should start a run) |
| 3729 | af4bfa8 | PR7 | Local enforce_phase_scope.py over 63 changed files: PR4-PR6 fail 18 (src/Meridian.Application, Infrastructure, Ui.Shared), PR7 passes. No phase in title/body. | label | 2026-11-26 | pending (draft, so gate skips until ready for review; also dirty) |
| 3730 | 9d2e87c | PR9 | Local enforce_phase_scope.py over 149 changed files: PR7/PR8 fail only on `scripts/dev/capture-web-screenshots.mjs`, PR9 passes. No phase in title/body. | label | 2026-11-26 | pending (draft, so gate skips until ready for review; also dirty) |

## Skipped

| PR | Head SHA | Reason |
|---|---|---|

(2026-11-26: no PR skipped on its merits. Of 45 open claude/ or codex/ PRs without a phase label, only 3728/3729/3730 had a latest scope-gate failing with "No phase declaration found" (confirmed from job logs). Still queued/in progress at ~00:30Z, re-check next run: 3788 3776 3770 3767 3765 3760 3756 3754 3738 3679 3678. Scope-gate absent: 3787 3771 3769 3755 3733 3586 3523 3491. Cancelled: 3749. Not claude/codex: ccr-* 3675 and 3655, plus Dependabot PRs.)
