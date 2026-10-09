# W10-SEAM-001 refresh and blocker-detail continuation

**Status:** active; shared tests and WPF cross-compilation passed; full CI and clean browser reproduction failed on inherited Tailwind/PostCSS integration; live acceptance pending

**Owner:** Workstation Shell and UX / Accounting and Ledger / designated operator

**Reviewed:** 2026-10-06

This continuation records the B1 and D1 repair work under the
[W10 operator acceptance procedure](../../w10-mark-seam-operator-acceptance.md).
Application candidate: `2f3bef7feb23f5fa9be1a9ad27dd3d08b9a5a732`; source base: `9c96c3f72dd60111b62b8368d29e553ec58fd7a0`; branch: `codex/w10-seam-refresh`. [Candidate binding](candidate-binding.json) verifies source hashes and `src`/`tests` Git tree IDs only. The earlier browser build and screenshots used a pre-clean-install dependency environment whose installed closure was not recorded; they do not establish reproducibility with the current lockfile. Original capture artifacts remain unchanged. [continuation.json](continuation.json) and the [dependency blocker supplement](browser/build-blocker.json) record the failed current reproduction and pending acceptance gates.

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
| B1 | Refresh workflows reloaded the shared projection/list while selected workflow detail remained at v4 after the same workflow advanced to v5. Repaired readiness left publication disabled. | Refresh the selected workflow detail and current shared plan/evidence references; the server independently validates plan/evidence versions. Refuse mismatches and delayed old responses. Retain refusal, repair, publication and reload evidence. | 550 shared tests passed. Pre-clean-install browser support passed 58 tests and 16 simulations; current-lock rerun passed 37 view-model tests but blocked 21 screen cases at CSS import. Source-hash binding does not establish dependency reproducibility. Live acceptance remains pending. |
| D1 | WPF shared blockers rendered contributor/message only. Legacy blocker rows did not establish visibility of shared blocker fields. | Render shared blocker type, count, severity, owner and causing record IDs in WPF. Verify each field against the shared response, including an absent owner/record list without implying complete evidence. | Structured shared blockers and eight new regression cases compiled in the full Windows target (0 errors, 8 warnings); eight general static checks passed. Zero Windows runtime tests or rendered desktop cases executed. |

The current shared close decision remains the authority in both clients. Asset-class coverage
is diagnostic; selected workflow detail and client presentation cannot authorize publication
when scope, workflow, plan or evidence support is stale or inconsistent.

## Validation record

| Evidence lane | Result | Acceptance limit |
| --- | --- | --- |
| Browser pre-clean-install regression, lint and build | Historical support: 58/58 tests, targeted lint, TypeScript and Vite build passed before `npm ci`. | Installed dependency closure was not recorded; these results do not certify the current lockfile. |
| Browser current-lock focused tests and build | Failed: 37 view-model tests passed; the screen file failed CSS import and its 21 cases did not run. Clean external-output build also failed; [test log](browser/clean-install-tests.log.gz), [build log](browser/clean-build.log.gz). | Inherited Tailwind 4/PostCSS integration requires a separate migration; no downgrade or risk acceptance applied. |
| Rendered browser response simulation | Historical pre-clean-install support: 16/16 observations at two desktop sizes; frozen [cases](browser/cases.json) and [provenance](browser/provenance.json). | Source hashes match; installed dependency closure is unknown. Governed responses and plan/evidence references are simulated. |
| Shared publication refusal/repair regression | Attempt 3 passed: 550 tests, zero failures/skips; [summary](server/summary.json) and [TRX](server/seam-support.trx.gz). Preserve attempt 1's concurrent asset rebuild failure and attempt 2's WPF-lock timeout in the JSON history. | Shared-service/HTTP fixtures do not establish a live population or operator authorization. |
| WPF presentation/view-model regression and build | Full Windows-target application/XAML and test assembly compiled, including eight new cases (0 errors, 8 warnings); [result](wpf/compilation.json), [build log](wpf/build-retry.log.gz). Initial assertion-type errors remain recorded. | Linux cross-compilation; zero Windows runtime tests or rendered cases. |
| WPF general static structure | Passed: 8/8; [retained report](wpf-static.txt). | These general checks do not execute the new blocker presentation or render Windows. |
| Core documentation automation | Passed: 23 steps. | Documentation validation supplies no application or live acceptance verdict. |
| Repository `bash scripts/ci.sh` | Attempt 3 FAILED on inherited Tailwind 4/PostCSS CSS import; [summary](ci/quality-gate/summary.md.gz), [steps](ci/quality-gate/steps.tsv), [log](ci/attempt-3/full-ci.log.gz). Browser lint had 0 errors/105 warnings and strict TypeScript passed; the first test batch had 112 passed tests with `app.test.tsx` failing import. Final .NET counts: 18973 passed, 0 failed, 5 skipped. Attempt 1's size-ratchet failure and attempt 2's four process-runner failures remain recorded. | CI source and tests are unchanged by the container child-reaping accommodation; local evidence remains preliminary to required hosted checks. |
| Independent workflow validation | Passed after installing PowerShell 7.6.6 in the temporary tool directory: 1,549 total tests, 1,533 passed, zero failures/errors, 16 skipped; seven existing quarantined modules. [Summary](ci/workflows/summary.md.gz), [log](ci/workflows/final.log.gz). Initial missing-tool failure is retained. | Does not change the failed full quality-gate or supply live acceptance. |
| Required same-candidate GitHub Actions | Pending | A prior commit's results cannot certify the changed application candidate. |
| Live browser refusal → repair → publication → reload | Pending | Retain authenticated requests, owned repair evidence, command/approval IDs and operator decision. |
| Live WPF refusal → repair → publication → reload | Pending | Retain installed Windows/client identity, rendered fields, the same shared responses and operator decision. |

Independent B1/D1 code review reported no remaining defects in that slice. The later clean-install dependency/build failure remains open; source review does not establish a passing full gate or live acceptance.

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
verification. The passing shared-service regression lane exercises actual plan/evidence version checks in automated fixtures; it does not establish live acceptance.
For live acceptance, capture workflow ID/version, plan ID/version, report revision and evidence
versions before and after repair; a refresh or ready headline alone is insufficient proof.

## Candidate and service/build evidence

The [candidate binding](candidate-binding.json) connects the unchanged captured sources to the implementation commit. Shared [validation-run.json](server/validation-run.json) preserves exact restore/build/test argument arrays and execution metadata. WPF's exact build command, including `--queue`, is retained in `continuation.json`.

Full CI attempt 2 ended with 18,969 .NET tests passed, four failed and five skipped. All four failures were MCP `ToolProcessRunner` tests; the [failed summary](ci/attempt-2/summary.md.gz), [core log](ci/attempt-2/core-remainder.log.gz) and [minimal process-state snapshot](ci/attempt-2/process-state-snapshot.json) remain retained. Root inspection found exited zombie descendants adopted by PID1, which was not reaping them. The [child-subreaper helper](ci/run-with-child-reaper.py) supplies that missing container behavior around the unchanged `bash scripts/ci.sh`. Its checksum, exact command and targeted-check evidence are in [child-reaper context](ci/child-reaper-context.json). No source, test expectation or CI-script change was made for this accommodation.

The targeted reaper check completed with seven passed, zero failed and zero skipped tests; both completed log and TRX agree. Attempt 3 reran the unchanged gate under that wrapper with supported `MERIDIAN_CI_TEST_MAX_PARALLEL=2`: all 19 .NET suites completed with 18,973 tests passed, zero failed and five skipped. The full gate nevertheless **failed** after `npm ci`, because Tailwind 4.3.3 is still invoked through the Tailwind 3 PostCSS configuration. Browser lint and strict TypeScript passed; the first browser batch ran 112 passing tests but one suite failed on CSS import. The separately repeated focused screen tests and build failed at the same integration boundary. All attempt-3 artifacts are taken from its frozen archive, so independent later lanes cannot overwrite its outcome. Reproduce with `MERIDIAN_CI_TEST_MAX_PARALLEL=2 python docs/testing/evidence/w10-seam-refresh-20261006/ci/run-with-child-reaper.py bash scripts/ci.sh` using the configured SDK, NuGet cache and actionlint.

The [dependency inventory](browser/clean-dependencies.json) and [blocker supplement](browser/build-blocker.json) distinguish the current installed graph from the earlier uncaptured dependency environment. The inherited version upgrade at `9e5a0ed7c636355d0c99d9827d373a2f229cf365` requires the full [documented migration](../../../security/known-vulnerabilities.md); downgrading to Tailwind 3 reintroduces the documented unaccepted high braces advisory. That broader migration remains outside the B1/D1 scope while optional user direction is unanswered; no choice or risk acceptance is inferred. Source binding alone cannot certify browser dependency reproducibility. Raw Markdown CI summaries are compressed to preserve their original relative links. Full CI remains failed regardless of subsequent independent lane results; hosted checks, Windows runtime and live operator gates remain pending.

The following Windows-only runtime check is **not executed**. Run it on Windows before the retained live acceptance session; it supplies supplemental runtime coverage and does not establish a provisioned population or an operator verdict.

```text
python build/python/cli/buildctl.py test --project tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj --configuration Release --full-wpf-build --filter "FullyQualifiedName~OperationsContinuityViewModelTests|FullyQualifiedName~AccountingCloseHttpRecoveryTests"
```

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
