# Generated Repair ledger

## Current status

- **Status:** Failing as of 2026-11-25 (first recorded run); no completed failure seen since 19:17Z, but recent runs are still queued or pending (runner saturation), so recovery is not yet confirmed.
- **Since:** 2026-11-25 16:44Z (earliest failure in the 100 most recent completed runs checked).
- **Error signature:** job `Prepare generated repair patch`, step `Merge and regenerate in an isolated container` exits 1. The container log reads `Repair: review_required; report: /output/report.json`. The report has `status: review_required` with `review_required` entries for `SRC-UI` and `SRC-UI-DASHBOARD` ("Merged module/README differs from the selected reviewed entry; module-specific review required").
- **Root cause (high confidence for the run read; medium for the rest):** the merged dashboard UI modules no longer match the reviewed source-hash entries the PR branch carries. The generator fails closed and produces no patch. The step reports a red check for a needs-review outcome, not a crash.
- **Fix status:** #3778 (9293ac60, 19:49Z) split reviewed hashes into per-module files. #3781 (be2baba0, 20:24Z) refreshed the `SRC-UI` and `SRC-UI-DASHBOARD` reviewed hashes on main. Neither has yet been confirmed by a completed run on a PR.
- **Not the cause:** PR #3556 (LFS isolation, open, `mergeable_state: dirty`, needs owner governance review) addresses a different signature (LFS pointer smudge) and is not the current failure.
- **Affected PRs (open, per the 19:17Z window):** #3774, #3752 (draft), #3739, #3762, #3743, #3750 (draft), #3678, #3771, #3748, #3755, #3751, #3765, #3768. Not checked: whether this is their only failing check.

## Incidents

- **generated-repair-001**: start 2026-11-25 16:44Z; end: none yet (last completed failure 19:17Z). Signature: `review_required` for `SRC-UI` and `SRC-UI-DASHBOARD` from the Generated Repair prepare step. Cause: merged dashboard UI modules differ from reviewed hash entries carried by the PR branch. Resolution: pending verification after #3778 and #3781 land on main.

## Daily log

- 2026-11-25: runs checked 100 completed Generated Repair runs (16:40–20:15Z) and 100 completed Generated Repair Publisher runs (14:53–20:24Z). Failures: 14 Generated Repair, 0 Publisher. Status: failing (first recorded run).
