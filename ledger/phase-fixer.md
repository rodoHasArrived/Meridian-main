# Phase-fixer ledger

First recorded run: 2026-11-25 (main be2baba0).

Note: on current main, `roadmap-source-docs.yml` triggers `pull_request` on `edited`, `labeled`, `unlabeled` too, so a label add starts a fresh scope-gate run itself (draft PRs skip the job). The known-error entry AI-20260828 still says only the next push re-runs it.

## Actions

| PR | Head SHA | Phase | How determined | Method | Date | Gate result after next run |
|---|---|---|---|---|---|---|
| 3774 | a1ddcb3 | PR9 | Local enforce_phase_scope.py over 10 changed files: PR0-PR5 fail all 10, PR6-PR8 still fail on `.rgignore` and `scripts/check_workflow_docs_parity.py`, PR9 passes. No phase named in title/body. | label | 2026-11-25 | pending |
| 3610 | f89551f | PR9 | Local enforce_phase_scope.py over 13 changed files: PR7/PR8 fail only on `.gitleaksignore`, PR9 passes. No phase named in title/body. | label | 2026-11-25 | pending (PR is also dirty; needs main merged) |

## Skipped

| PR | Head SHA | Reason |
|---|---|---|

(No PR was skipped on its merits. Every other open claude/ or codex/ PR without a phase label had scope-gate queued, success, skipped (draft), cancelled, or absent, so none was a candidate. Queued as of 2026-11-25 ~20:30Z: 3783 3782 3779 3776 3775 3771 3770 3768 3767 3765 3762 3760 3757 3756 3755 3748 3744 3743 3738 3736 3734 3730 3729 3728 3712 3692 3691 3679 3678 3662 3609. Re-check these next run. Not claude/codex, so not considered: ccr-* branches 3675 and 3655, plus the Dependabot PRs.)
