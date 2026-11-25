# PR hygiene ledger

## Latest snapshot (2026-11-25 05:51 MST, main adc8f97c)

Bucket key: R=ready to merge, D=ready but draft, F=needs fix, M=needs main merged, I=blocked by infra/flake, W=waiting, S=stale/superseded. Reports in bucket counts the consecutive runs a PR has sat in its current bucket (escalate at 3).

| PR | Head | Bucket | Diagnosis | Reports in bucket |
|---|---|---|---|---|
| 3729 | 71fc06f | F | quality-gate and integration-gate failed; verify-dotnet/verify-browser cancelled by a superseding run; integration-gate log not read | 1 (W→F) |
| 3728 | 2257766 | F | quality-gate failed; earlier diagnosis scope-gate no phase declaration (current cause not re-read) | 2 |
| 3721 | 34bc7ca | F | verify-browser failed; cause not read; overlaps npm lockfile with 3745 | 2 |
| 3730 | 7239ab9 | F | scope-gate: no phase declaration in PR body; body hosted-status names head af2e930 (≠ API head) | 2 |
| 3614 | b5e5302 | F | verify-workflows failed; quality-gate failed; SDK 11 alpine image vs repo net10 pin (hypothesis, unverified) | 2 |
| 3613 | b3cb59f | F | verify-workflows failed; quality-gate failed; same SDK 11 hypothesis (unverified) | 2 |
| 3586 | 0c0d705 | F | verify-browser, verify-docs, repair-patch failed; draft; last push 11-14; possibly superseded by 3744 | 2 |
| 3733 | fd8b060 | I | Prepare generated repair patch failed (bot job); quality-gate and integration-gate green on Nov 24 run; body lists outstanding Level 3 work | 2 |
| 3610 | f89551f | M | dirty; scope-gate no phase; verify-desktop failed | 2 |
| 3556 | 94ce9c0 | M | dirty; verify-dotnet and repair-patch failed | 2 |
| 3528 | da82d24 | M | dirty; verify-dotnet and verify-docs failed; last push 11-12 | 2 |
| 3523 | 797f4b9 | M | dirty (draft); scope-gate no phase; verify-dotnet failed; last push 11-12 | 2 |
| 3491 | 2f233c2 | S | dirty; last push 11-11 (14 days) | 2 |
| 3418 | a000a04 | S | FsUnit 8.0.0 bump; verify-dotnet failed; last push 11-10 (15 days) | 2 |
| 3745 | 8258c62 | W | new; deps batch; checks queued/running | 1 |
| 3744 | 768c603 | W | new; Security Master composition, 182 files; checks running | 1 |
| 3743 | bc4541d | W | new; WAL durability; checks queued/running | 1 |
| 3741 | 5d19210 | W | new; roadmap registry; checks running | 1 |
| 3740 | e94ef69 | W | verify-dotnet and integration-gate queued; others green | 2 |
| 3739 | 7a0bc36 | W | verify-browser, verify-docs, verify-workflows queued | 2 |
| 3738 | 0f1605c | W | checks queued; earlier scope-gate failure on a prior run; body cites 4 pre-existing failures in production_certification_workflow tests | 2 |
| 3737 | 829d5fd | W | quality-gate queued | 2 |
| 3736 | 9d9ee2c | W | head changed (was 8475859); 98 files; checks queued | 2 |
| 3734 | 7338a73 | W | checks running; body cites head 8f65e46 (≠ API head) | 2 |
| 3723 | 1856e8c | W | bot screenshot catalog; checks running | 2 |
| 3722 | 978da30 | W | quality-gate queued | 2 |
| 3712 | cc43cb8 | W | checks running; 66 commits | 2 |
| 3692 | 3e853f7 | W | checks running | 2 |
| 3691 | b4a0f44 | W | checks running | 2 |
| 3689 | 2f850df | W | checks queued (draft) | 2 |
| 3688 | 18dbcf9 | W | checks queued (draft) | 2 |
| 3687 | 774e6c9 | W | checks queued | 2 |
| 3679 | 940eeeb | W | checks running | 2 |
| 3678 | ca0a19c | W | head changed (was 8da1e29); checks running | 2 |
| 3675 | 58080d0 | W | checks queued | 2 |
| 3662 | 2b6c278 | W | checks queued | 2 |
| 3655 | 5330342 | W | checks running | 2 |
| 3623 | 79bfedd | W | checks queued | 2 |
| 3622 | 04d3c0a | W | checks running | 2 |
| 3609 | 84347fd | W | checks running | 2 |

Gone since last snapshot (not verified as merged or closed): 3716 (was R), 3735 (was W).

Notes: no R or D PRs confirmed this run. Failure diagnoses for unchanged heads carried from the prior snapshot where not re-read.

## Run log

- 2026-11-25 04:28 MST | main cb5d7320 | R1 D0 F6 M4 I1 W24 S2 (38 open)
- 2026-11-25 05:51 MST | main adc8f97c | R0 D0 F7 M4 I1 W26 S2 (40 open)
