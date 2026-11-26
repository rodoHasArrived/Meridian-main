# Generated Repair ledger

## Current status

- **Status:** Recovered. Last failure 2026-11-26 10:20:32Z; none since across 100 completed runs (08:54–13:09Z). Runs created after that are skipped, cancelled or successful.
- **Since:** Failing 2026-11-26 10:03Z to 10:20Z (15 failures, all `pull_request_target` runs). Earlier 2026-11-25 failures (`review_required`, merge step) were not seen in the last 24h.
- **Error signature (current):** job `Prepare generated repair patch`, step `Pin request and fetch revisions` exits 1 with `Generation request is stale; publisher will dispatch a bounded retry`.
- **Root cause (high confidence on mechanism, medium on full queue timing):** a `pull_request_target` run checks out `github.workflow_sha`, which is the default-branch commit at event time, so it runs the generator from that older commit. Runs waited roughly 1.5–2 h for a runner (runner saturation). Main advanced during the wait (e.g. c326b873 to d529d340), and the pre-#3810 generator failed closed on base drift instead of re-pinning to the PR's current base.
- **Fix status:** #3810 (`d529d340`, "ci: cut runner demand and job hand-offs", committer time 2026-11-26 10:21Z) on main re-pins relay-event runs to the current PR base (`generated_repair.py` line ~646). Runs created after it did not fail.
- **Not confirmed:** whether the promised "publisher will dispatch a bounded retry" happens. No `workflow_dispatch` generation runs appear in the 100 most recent runs.
- **Earlier signature (2026-11-25):** `review_required` for `SRC-UI` and `SRC-UI-DASHBOARD`. Fixes #3778 (`9293ac60`) and #3781 (`be2baba0`) are on main. Not re-observed in the last 24h.
- **#3556 (LFS isolation):** closed unmerged 2026-11-25 21:09Z. It is no longer pending owner review.
- **Affected PRs still showing the failed check on their current head (open):** #3756, #3678, #3679, #3771; draft #3807. Other PRs in the 15-failure list have since pushed new heads.

## Incidents

- **generated-repair-001**: start 2026-11-25 16:44Z; end: not confirmed (no failure of this signature since 2026-11-25 19:17Z). Signature: `review_required` for `SRC-UI` and `SRC-UI-DASHBOARD`. Cause: merged dashboard modules differ from reviewed hash entries. Resolution: #3778 and #3781 on main, not yet confirmed by a PR run.
- **generated-repair-002**: start 2026-11-26 10:03Z; end 2026-11-26 10:20Z (last failure; recovered). Signature: `Prepare generated repair patch` / `Pin request and fetch revisions`, `Generation request is stale; publisher will dispatch a bounded retry`. Cause: queued `pull_request_target` runs ran pre-fix generator code while main advanced during runner queueing. Resolution: #3810 (`d529d340`) on main. Open: confirm publisher retry dispatch.

## Daily log

- 2026-11-25: runs checked 100 completed Generated Repair runs (16:40–20:15Z) and 100 completed Generated Repair Publisher runs (14:53–20:24Z). Failures: 14 Generated Repair, 0 Publisher. Status: failing (first recorded run).
- 2026-11-26: runs checked 100 completed Generated Repair runs (08:54–13:09Z); 15 failures, all in 10:03–10:20Z, all `pull_request_target` (stale-base prepare error). Status: failing until 10:20Z, then recovered.
