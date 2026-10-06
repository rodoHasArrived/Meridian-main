# W10-SEAM-001 refresh and blocker-detail continuation

**Status:** active; browser supporting checks passed; server/WPF/full CI and live acceptance pending

**Owner:** Workstation Shell and UX / Accounting and Ledger / designated operator

**Reviewed:** 2026-10-06

This continuation records the B1 and D1 repair work under the
[W10 operator acceptance procedure](../../w10-mark-seam-operator-acceptance.md).
Source base: `9c96c3f72dd60111b62b8368d29e553ec58fd7a0`; work branch:
`codex/w10-seam-refresh`. The final application candidate and outstanding validation results
are pending in [continuation.json](continuation.json). A base commit plus uncommitted changes
does not identify an exercised release candidate.

The [615abde9 evidence packet](../w10-615abde9/README.md) is preserved unchanged. Its failed
B1 observation, source-supported D1 finding, failed/cancelled hosted checks and undecided
operator records remain attributable to that historical candidate. New engineering results
must be recorded separately; they cannot change the historical result or supply a human verdict.

## Queue and session boundaries

The user-requested implementation order remains **LOT → MARK → SEAM → RECON**.
[PR #3050](https://github.com/rodoHasArrived/Meridian-main/pull/3050) merged a partial LOT
slice; [PR #3070](https://github.com/rodoHasArrived/Meridian-main/pull/3070) is a pending LOT
follow-up. These facts do not establish completion of the LOT or MARK implementation queues.
This packet makes no roadmap status change or queue-completion claim.

The joint live acceptance session retains its separate order: **SEAM before MARK**.
Complete and retain every SEAM case for both clients, including refusal, owned repair,
publication and reload, before beginning MARK. Preserve the
[desktop sequencing receipt](../../../engineering/plans/wpf-web-ui-alignment-plan.md#w10-shared-close-sequencing-receipt---2026-09-04).

## Findings and required rechecks

| Finding | Historical observation | Required repair and proof | Current result |
| --- | --- | --- | --- |
| B1 | Refresh workflows reloaded the shared projection/list while selected workflow detail remained at v4 after the same workflow advanced to v5. Repaired readiness left publication disabled. | Refresh the selected workflow detail and current shared plan/evidence references; the server independently validates plan/evidence versions. Refuse mismatches and delayed old responses. Retain refusal, repair, publication and reload evidence. | 58 focused browser tests and 16 rendered response-simulation observations passed. The simulation exercises refusal, repaired detail and subsequent publication response; retained server and live client proof remain pending. |
| D1 | WPF shared blockers rendered contributor/message only. Legacy blocker rows did not establish visibility of shared blocker fields. | Render shared blocker type, count, severity, owner and causing record IDs in WPF. Verify each field against the shared response, including an absent owner/record list without implying complete evidence. | Structured shared blocker bindings are present; eight general WPF static structure checks passed. Dedicated WPF tests/build and actual Windows rendering remain pending. |

The current shared close decision remains the authority in both clients. Asset-class coverage
is diagnostic; selected workflow detail and client presentation cannot authorize publication
when scope, workflow, plan or evidence support is stale or inconsistent.

## Validation record

| Evidence lane | Result | Acceptance limit |
| --- | --- | --- |
| Browser component regression, lint and build | Passed: 58/58 tests in two Vitest files; targeted ESLint passed for four changed files; TypeScript and Vite build passed. | Automated support cannot establish a live retained host or human decision. |
| Rendered browser response simulation | Passed: 16/16 observations at 1440×900 and 1366×768; [cases](browser/cases.json), [provenance](browser/provenance.json). | All governed API responses are intercepted; plan/evidence revisions are simulated record references. |
| Shared publication refusal/repair regression | Attempt 1 stopped before tests: a concurrent browser bundle rebuild removed 57 asset filenames already evaluated by host MSBuild (`MSB3030`). Attempt 2 timed out awaiting the WPF validation lock (exit 3; no tests). Attempt 3 is pending after WPF completes. | Validation sequencing failures; no product regression inferred or source workaround applied. Fixtures cannot establish installed population or operator authorization. |
| WPF presentation/view-model regression and build | Passed full Windows-target application/XAML and test-assembly cross-compilation, including eight new regression cases (0 errors; 8 existing warnings). Initial assertion-type errors were corrected before the passing retry. | Compiled on Linux with `IsWindows=true`, `EnableWindowsTargeting=true`, and `EnableFullWpfBuild=true`; zero Windows runtime tests or rendered cases executed. |
| WPF general static structure | Passed: 8/8; [retained report](wpf-static.txt). | These general checks do not execute the new blocker presentation or render Windows. |
| Core documentation automation | Passed: 23 steps. | Documentation validation supplies no application or live acceptance verdict. |
| Repository `bash scripts/ci.sh` | First attempt failed the file-size ratchet by five lines in the interim browser view-model. Helper extraction fixed that check; full rerun pending. | Preserve the failed attempt; local success remains preliminary to required hosted checks. |
| Required same-candidate GitHub Actions | Pending | A prior commit's results cannot certify the changed application candidate. |
| Live browser refusal → repair → publication → reload | Pending | Retain authenticated requests, owned repair evidence, command/approval IDs and operator decision. |
| Live WPF refusal → repair → publication → reload | Pending | Retain installed Windows/client identity, rendered fields, the same shared responses and operator decision. |

Independent code review reported no remaining implementation blockers. This is an engineering
review result; live rendering and human acceptance remain separate gates.

## Retained browser evidence

The simulation ran on Chromium `151.0.7922.173` / Playwright `1.63.0` from
`2026-10-06T21:27:05.765Z` to `2026-10-06T21:27:24.809Z`. Its 16 observations include
HTTP 409 refusal, v8→v9 refresh, simulated v10 publication, mismatched detail refusal,
scope recovery, selected-detail failure, same-workflow-version evidence refresh, keyboard
activation and delayed older-response rejection. No framework overlay or page exception was
observed; the recorded 409 and 503 responses were controlled scenario inputs.

| Retained artifact | Scope |
| --- | --- |
| [Compact cases](browser/cases.json) | All 16 expected/observed automation results and pending operator decisions; omitted full body text is retained separately. |
| [Full cases, compressed](browser/cases-full.json.gz) | Byte-preserving original case JSON, including captured body text and original screenshot paths. |
| [Requests and responses, compressed](browser/requests-responses.json.gz) | All 71 simulated request/response records, including refusal and publication command bodies. |
| [Provenance](browser/provenance.json) | Original environment, times, source hashes and evidence limitations. Its abbreviated implementation-order string does not revise the four-step queue above. |
| [Source hashes](source-hashes.json) | Verified capture source hashes plus the extracted workflow-selection helper's supplemental SHA-256 and executed harness hash. |
| [Capture harness](browser/capture.mjs) | Exact script used; its repository/output/browser paths describe the capture environment. Copy and adjust paths when reproducing in an isolated checkout; preserve this record. |
| [Focused Vitest log](browser/vitest.log.gz) | Two files, 58 passing tests. Run `npm --prefix src/Meridian.Ui/dashboard run test:vitest -- src/screens/operations-continuity-screen.view-model.test.ts src/screens/operations-continuity-screen.test.tsx --maxWorkers=2` from the repository root. |
| [Repaired v9 screen](browser/screenshots/S4-repaired-v9.png) / [published v10 screen](browser/screenshots/S5-published-v10.png) | Two retained screenshots visibly labelled response simulation and pending human verdict. Other captured screenshots remain unretained in this compact packet and are marked accordingly. |

The [artifact manifest](artifact-manifest.json) binds the fixed supporting files by byte count
and SHA-256. It excludes this evolving narrative and `continuation.json`; their final identity
will be established by the review commit.

The rendered projection exposes workflow version and contributor record IDs; it has no separate
plan/evidence version fields. Names such as `response-simulation-close-plan-v9` and
`response-simulation-report-evidence-v9` are fixture references, not real retained version
verification. The server regression lane must establish actual plan/evidence version checks.
For live acceptance, capture workflow ID/version, plan ID/version, report revision and evidence
versions before and after repair; a refresh or ready headline alone is insufficient proof.

## Retained operator decisions and live handoff

The historical record contains `actualOperatorDecisions: []`. Both row decisions, all 12
criterion decisions and all 44 case/client decisions are **pending**. Identity and decision
time are null. Their exact decision objects are retained in `continuation.json`, with the
historical source recorded explicitly. Pending means undecided; it is not an accept, reject
or defer verdict.

The only recorded environment statement remains: “No existing non-production accounting
population or Windows operator session is available.” No new provisioned population,
installed Windows session or designated operator verdict has been supplied for this
continuation. Populate the live session record only from actual returned identities and
observed results.

For the live S7/S9 sequence, retain each client's refusal response and rendered refusal;
append repairs through the evidence owner; reload the workflow, plan, evidence and shared
projection; compare the resulting scope/identities/versions; then have the authorized human
perform publication through the existing maker/checker lane. Reload both clients and retain
the resulting publication/evidence IDs. A delayed pre-repair response must not restore the
old detail or transfer publication availability to another selection. Record D1 fields beside
the matching response during S4/S4b/S5/S6. Collect each operator's accept/reject/defer decision,
identity, UTC timestamp, rationale and criterion references without replacing prior records.

Live execution and all operator decisions remain pending until that session is completed.
