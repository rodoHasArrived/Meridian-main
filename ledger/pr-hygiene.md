# PR hygiene ledger

## Latest snapshot (2026-11-26 05:52 MST, main d529d340)

Bucket key: R=ready to merge, D=ready but draft, F=needs fix, M=needs main merged, I=blocked by infra/flake, W=waiting, S=stale/superseded. "Reports" = consecutive runs in the current bucket (escalate at 3).

| PR | Head | Bucket | Diagnosis | Reports |
|---|---|---|---|---|
| 3823 | 6bcf3d6 | W | Dependabot System.Reactive 7; checks pending | 1 |
| 3822 | 4f734b3 | W | Dependabot batch; checks pending | 1 |
| 3821 | 913fc61 | W | Dependabot Serilog.Settings 11; checks pending | 1 |
| 3820 | fe6d92b | F | verify-desktop failed; log tail inconclusive | 1 |
| 3819 | 587c8e6 | W | Dependabot QuestPDF 2026; checks pending | 1 |
| 3818 | 4cef4a0 | W | Dependabot NSubstitute 6; checks pending | 1 |
| 3817 | 1d786b8 | W | Dependabot ModelContextProtocol 2; checks pending | 1 |
| 3816 | 83334e3 | F | verify-dotnet, verify-desktop, pipeline-budget failed; Parquet 6 major; cause not visible | 1 |
| 3815 | 12d9f6a | W | Dependabot OpenTelemetry 1.21; get data too large, mergeability unknown | 1 |
| 3814 | 4cba2de | F | 6 lanes failed; Npgsql 11 major; cause not visible | 1 |
| 3802 | 683c135 | W | Dependabot Moq 4.21; checks pending | 1 |
| 3795 | e8e12ca | F | quality-gate failed via verify-dotnet; testing-group bump; log not read | 1 |
| 3791 | ec67860 | I | quality-gate green; only repair-patch job failed | 1 |
| 3790 | 59e0f78 | W | Dependabot github-actions; checks pending | 1 |
| 3789 | 7239f75 | I | quality-gate green; only repair-patch job failed | 1 |
| 3614 | b5e5302 | F | quality-gate failed via verify-workflows; Docker SDK 11 base bump | 3 (escalate) |
| 3613 | b3cb59f | F | quality-gate failed via verify-workflows; Docker aspnet 11 base bump | 3 (escalate) |
| 3418 | 6b923f9 | I | integration-gate fails: service-backed lane cancelled, not PR code | 1 (was S) |
| 3813 | 4e6e641 | W | docs; checks pending | 1 |
| 3812 | 6766734 | W | integration stack of 11 PRs; checks pending | 1 |
| 3811 | 2c2c726 | M | dirty, behind; five-job CI change | 1 |
| 3809 | 38ccba6 | W | quality-gate queued; retires scope-gate | 1 |
| 3807 | 2d441f2 | W | draft; quality-gate queued; repair-patch failed | 1 |
| 3806 | d64e5ef | W | checks pending | 1 |
| 3804 | f7bb7ef | W | checks pending | 1 |
| 3787 | 457f86d | M | quality-gate green only on older run; behind | 1 |
| 3784 | 302be65 | W | checks pending; overlaps 3809 | 1 |
| 3782 | f96e0d7 | F | scope-gate failed (phase declaration likely) | 1 |
| 3776 | 48d33b3 | M | dirty; repair-patch failed | 1 |
| 3775 | 32abb17 | I | draft; repair-patch failed | 1 |
| 3774 | a1ddcb3 | M | behind; scope-gate and repair-patch failed | 1 |
| 3771 | 70de5cb | M | behind; scope-gate failed | 1 |
| 3770 | d136c0f | W | checks pending | 1 |
| 3769 | 8451860 | I | quality-gate green; repair-patch failed; behind | 1 |
| 3768 | 4ea3bce | W | checks pending | 1 |
| 3765 | 9ad2c5e | I | repair-patch failed; behind | 1 |
| 3763 | 0fad023 | M | all checks green; behind main | 1 |
| 3762 | 06e78a6 | I | repair-patch failed; behind | 1 |
| 3758 | f83523b | M | dirty | 1 |
| 3757 | 9f3dd08 | F | quality-gate failed via verify-browser (browser change) | 1 |
| 3756 | ef60908 | I | repair-patch failed; behind | 1 |
| 3755 | a6ffe9a | W | checks pending | 1 |
| 3751 | bef7a37 | M | behind; quality-gate queued | 1 |
| 3749 | 3d92f9e | M | draft; behind; checks unverified | 1 |
| 3748 | 942c27b | W | checks unverified | 1 |
| 3739 | 1e04708 | W | checks unverified; 4 COMMENTED reviews | 3 (escalate) |
| 3738 | f4d51a6 | W | checks unverified | 3 (escalate) |
| 3736 | fafb178 | I | quality-gate failed after cancelled verify-dotnet | 1 |
| 3733 | fd8b060 | I | draft; repair-patch failed | 3 (escalate) |
| 3679 | b90d6f1 | M | dirty; quality-gate failed by cancellation | 1 |
| 3678 | fd4fb7a | M | dirty; verify-desktop/verify-docs ambiguous | 1 |
| 3586 | 8b59068 | M | behind; checks unverified | 1 |
| 3528 | 0d079bd | M | behind; checks unverified | 3 (escalate) |
| 3523 | 16a1a2c | M | behind; checks unverified; archive-only | 3 (escalate) |
| 3491 | 2f233c2 | S | last push 2026-11-11 (15 days); repair-patch failed | 3 (escalate) |

Closed since last snapshot (merged): 3609, 3622, 3687, 3688, 3689, 3722, 3737, 3740, 3741, 3743, 3744, 3745, 3746-3747 (earlier run), 3723 (closed unmerged). Closed unmerged: 3610, 3556, 3721, 3723, 3729, 3728, 3730, 3734, 3712, 3692, 3691, 3675, 3662, 3655, 3623. 3716 and 3735 not re-verified.

## Run log

- 2026-11-25 04:28 MST | main cb5d7320 | R1 D0 F6 M4 I1 W24 S2 (38 open)
- 2026-11-25 05:51 MST | main adc8f97c | R0 D0 F7 M4 I1 W26 S2 (40 open)
- 2026-11-26 05:52 MST | main d529d340 | R0 D0 F8 M14 I10 W22 S1 (55 open)
