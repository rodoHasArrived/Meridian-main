# W10-SEAM reviewed-source evidence — candidate 12565b9

**Status:** browser supporting checks and WPF compilation passed; current full and hosted gates belong to PR #3108

**Owner:** Workstation Shell and UX / designated operator

**Reviewed:** 2026-10-07

This packet binds supporting checks to application candidate
**`12565b938fff633cac2060b844487c4cf9454099`** on
[PR #3108](https://github.com/rodoHasArrived/Meridian-main/pull/3108).
The [binding](candidate-binding.json) checks source, test, fixture and dependency hashes and
records the `src`, `tests` and workstation bundle tree IDs for this candidate. Those tree
identities do not certify later source or test changes. Current full CI and required hosted
results belong to the PR's current head. This packet asserts no full-gate pass.

| Observed support | Result and retained evidence |
| --- | --- |
| Focused browser regressions | **74 passed, zero failed/pending**, across three files: 23 component, 37 view-model and 14 workflow-selection tests. [Aggregate](browser/focused-test-summary.json), [60-test report](browser/focused-tests.json.gz), [14-test report](browser/snapshot-tests.json.gz). The latter filename describes publication-evidence snapshot tests, not an additional 14 tests beyond the total. |
| Rendered response simulation | **24/24 automated observations passed**, at 1440×900 and 1366×768; **105 request/response records** retained. [Compact cases](browser/cases.json), [full cases](browser/cases-full.json.gz), [requests](browser/requests-responses.json.gz), [provenance](browser/provenance.json). |
| Official screenshot route | **W03G passed on its first attempt**, one selected route from the 80-route catalog. [Original manifest](browser/official-w03g/manifest.json), [screenshot](browser/official-w03g/screenshots/web-accounting-operations-continuity.png), [log](browser/official-w03g.log.gz). The binding records its repository harness, route configuration and fixture hashes. |
| Dependency identity | Dashboard `npm ls --all --json` exited **0**, with empty stderr. [Dependency record](browser/dependencies.json), [full npm tree](browser/npm-ls-all.json.gz), [verification](browser/verification.json). Root manifests are hashed; no separate root npm-ls run is claimed. |
| WPF compilation | Full Windows-target source, XAML and test-assembly compilation exited **0**, with **0 warnings/0 errors**. Assembly metadata includes 11 regression methods representing **27 source-declared xUnit cases**; **0 runtime tests** were executed. [Original record](wpf/working-tree-compilation.json), [log](wpf/build.log.gz), [compiled page BAML](wpf/OperationsContinuityPage.baml.gz). |

The B1 continuation checks reject selected detail outside the current workflow/scope or
publication-evidence snapshot, including disagreement at the same workflow version. The
browser exercise retains refusal, repair and simulated publication using refreshed evidence.
Representative captures show [matching repair](browser/screenshots/S19-same-version-matching-repair.png)
and [publication](browser/screenshots/S20-same-version-repaired-publication.png): the command
uses expected workflow version 18 with repaired report/evidence identities v20, returning
closed workflow 19. D1's WPF source and compiled regressions retain shared blocker type,
count, severity, owner and causing records, plus selection/scope and evidence consistency.

Capture ran **2026-10-07 16:56:17.437–16:56:53.353 UTC** with Chromium `151.0.7922.173`,
Playwright `1.63.0`, Node `v24.19.0` and npm `11.9.0`. Installed dependencies include
Tailwind `3.4.19`, PostCSS `8.5.23`, Vite `8.0.16` and Vitest `4.1.11`.
The browser executed **Vite-served source components**; the bundle tree is identity only.
The deliberate 409 refusals and 503 detail failure remain in the provenance. Plan/evidence
revisions are simulated contributor record identities, not independently retained versioned
server records. Intercepted responses establish no backend persistence or live publication.

WPF compilation occurred against staged working-tree changes while HEAD was `faf90ecf`.
The original record preserves that HEAD and its timestamps; all seven recorded owned-file
hashes match candidate `12565b9`. A first attempt exhausted disk during dependency copying;
its [failure log](wpf/capacity-failed-build.log.gz) and [cleanup record](wpf/capacity-cleanup.json)
are retained. The initial metadata verifier missed quiet incremental output; the
[incomplete record](wpf/metadata-verification-incomplete.json.gz) is preserved alongside
corrected PE/BAML verification. Large DLLs are represented by hashes in the compilation
record and are not retained in this compact packet. Scratch paths in original records
describe the capture environment.

Historical Windows evidence is separately summarized in [the prior-run record](wpf/historical-windows.json):
the run associated with PR head `faf90ecf` reports tested commit `82118d79` and passed
2,360 tests, failed zero and skipped one, including shared-blocker rendering. That earlier run does not execute this
candidate's subsequent selection/evidence fixes. Current Windows outcomes must be read
from [PR #3108](https://github.com/rodoHasArrived/Meridian-main/pull/3108).

The exact [browser commands](browser/commands.json), [capture harness](browser/capture.mjs),
[dependency recorder](browser/record-dependencies.mjs) and [WPF build helper](wpf/run-build.py)
retain their environment paths. Reproduce in an isolated checkout and fresh output location;
preserve this packet. The [manifest](manifest.json) hashes every retained artifact except itself,
including compressed and original payload identities. Unretained simulation screenshots
are explicitly marked in the compact cases.

No live accounting population, installed Windows operator session, authorization or human
acceptance is established. **All operator decisions remain pending**, and the live-session
candidate remains unset. Implementation order remains **LOT → MARK → SEAM → RECON**;
the joint live session remains **SEAM before MARK**. LOT/MARK completion is not established.
The [7cbdaff browser packet](../w10-seam-merge-20261007/README.md),
[2026-10-06 continuation](../w10-seam-refresh-20261006/README.md) and
[615abde9 packet](../w10-615abde9/README.md) remain unchanged.
