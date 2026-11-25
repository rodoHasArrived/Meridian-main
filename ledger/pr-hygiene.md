# PR hygiene ledger

## Latest snapshot (2026-11-25 04:28 MST, main cb5d7320)

Bucket key: R=ready to merge, D=ready but draft, F=needs fix, M=needs main merged, I=blocked by infra/flake, W=waiting, S=stale/superseded. Reports in bucket: 1 (first recorded run).

| PR | Head | Bucket | Diagnosis | Reports in bucket |
|---|---|---|---|---|
| 3740 | e94ef69 | W | checks queued | 1 |
| 3739 | 7a0bc36 | W | checks queued (draft) | 1 |
| 3738 | 0f1605c | W | checks queued (draft) | 1 |
| 3737 | 829d5fd | W | checks queued (draft) | 1 |
| 3736 | 8475859 | W | checks queued (draft) | 1 |
| 3735 | 7af5c12 | W | quality-gate queued since 10:25Z | 1 |
| 3734 | 7338a73 | W | checks running (draft) | 1 |
| 3729 | 71fc06f | W | checks queued (draft) | 1 |
| 3723 | 1856e8c | W | bot screenshot catalog; checks running | 1 |
| 3722 | 978da30 | W | checks queued | 1 |
| 3712 | cc43cb8 | W | checks queued; 66 commits | 1 |
| 3692 | 3e853f7 | W | checks running | 1 |
| 3691 | b4a0f44 | W | checks running | 1 |
| 3689 | 2f850df | W | checks queued (draft) | 1 |
| 3688 | 18dbcf9 | W | checks queued (draft) | 1 |
| 3687 | 774e6c9 | W | checks queued | 1 |
| 3679 | 940eeeb | W | checks running | 1 |
| 3678 | 8da1e29 | W | checks running (draft) | 1 |
| 3675 | 58080d0 | W | checks queued | 1 |
| 3662 | 2b6c278 | W | checks queued | 1 |
| 3655 | 5330342 | W | checks running | 1 |
| 3623 | 79bfedd | W | checks queued | 1 |
| 3622 | 04d3c0a | W | checks running | 1 |
| 3609 | 84347fd | W | checks running | 1 |
| 3716 | 02a33f6 | R | quality-gate and integration-gate green; mergeable clean | 1 |
| 3721 | 34bc7ca | F | verify-browser failed (Run browser workstation lane), cause not read | 1 |
| 3730 | 7239ab9 | F | scope-gate: no phase declaration (PR body lacks marker) | 1 |
| 3728 | 2257766 | F | scope-gate: no phase declaration (draft) | 1 |
| 3614 | b5e5302 | F | verify-workflows failed (Run workflow hygiene lane); quality-gate failure | 1 |
| 3613 | b3cb59f | F | verify-workflows failed; quality-gate failure | 1 |
| 3586 | 0c0d705 | F | verify-browser, verify-docs, repair-patch failed; draft; last push 11-14 | 1 |
| 3733 | fd8b060 | I | Prepare generated repair patch failed; quality-gate and integration-gate green | 1 |
| 3610 | f89551f | M | dirty; scope-gate no phase declaration; verify-desktop failed | 1 |
| 3556 | 94ce9c0 | M | dirty; verify-dotnet failed; repair-patch failed | 1 |
| 3528 | da82d24 | M | dirty; verify-dotnet and verify-docs failed; last push 11-12 | 1 |
| 3523 | 797f4b9 | M | dirty (draft); scope-gate no phase; verify-dotnet failed; last push 11-12 | 1 |
| 3491 | 2f233c2 | S | dirty; last push 11-11 (14 days); only repair-patch job failing | 1 |
| 3418 | a000a04 | S | FsUnit 8.0.0 bump; verify-dotnet failed; last push 11-10 (15 days) | 1 |

Notes: 3734 body cites head 8f65e46, API head is 7338a73 (API used). Failing-step error lines not read for 3556, 3528, 3523, 3418, 3613, 3721, 3610, 3586.

## Run log

- 2026-11-25 04:28 MST | main cb5d7320 | R1 D0 F6 M4 I1 W24 S2 (38 open)
