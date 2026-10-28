# Throughput Refinement Evidence

**Status:** narrow validation-planning threshold met; overall delivery impact unproven
**Owner:** core-team
**Reviewed:** 2026-10-28

The refined workflow was faster in **6/6 validation-planning pairs**, with a **71.436% median
paired reduction in validated completion time**. All twelve final plans passed an independently
reviewed exact-set verifier. This result concerns selecting affected tests, not implementing
features, running those tests, or reaching a passing PR.

## Agent Task Results

| Changed project | Manual completion | Tool-assisted completion | Reduction |
| --- | ---: | ---: | ---: |
| Contracts | 98.822 s | 12.903 s | 86.9% |
| Storage | 69.422 s | 13.250 s | 80.9% |
| UI Services | 20.532 s | 8.964 s | 56.3% |
| Execution | 36.764 s | 13.985 s | 62.0% |
| Backtesting | 22.538 s | 14.404 s | 36.1% |
| Reporting | 175.270 s | 31.879 s | 81.8% |

The [confirmation protocol](validation-planning/confirmation-protocol.json) fixed six tasks and
required at least five pairs to improve by 10%, a median paired reduction of at least 15%, all
twelve final answers accepted, complete instrumentation, and no increase in treatment corrections.
All conditions passed. The median is the median of six within-pair percentage reductions, not
the percentage difference between cohort medians.

Manual plans required three corrections after missing an F# test project; the tool-assisted
plans required none. Failed checks, investigation, and repair time remain included. Repeated
unchanged-content reads were zero in both cohorts. Source-edit and passing-PR times are unavailable
because the deliverable was a planning artifact. See [raw events](validation-planning/confirmation-events/),
[per-trial report](validation-planning/confirmation-report.json), and
[threshold assessment](validation-planning/confirmation-assessment.json).

Each cohort used a separate solver with `fork_turns=none`; no coordinator history or other-cohort
solutions were inherited. Both could use generic scripting. Treatment additionally used
`buildctl recommend-validation`. The clock began with the common runner's first action before
reading the task. It includes subsequent discovery, tool execution, answer writing, failures,
corrections, and final verification; dispatch, queue, and initial response reasoning are excluded
for both cohorts. Tasks ran in two waves with balanced launch order on the same host. Solvers
knew their tool availability, so this was not blinded or a randomized production experiment.

The Execution baseline hit a model-capacity interruption and resumed in the same thread/log.
No trial was replaced. Excluding that pair as a sensitivity check still leaves **5/5 pairs faster**,
with an 80.913% median paired reduction. This does not remove other model, order, or host-load effects.

The oracle follows captured literal project references and explicit test-project/SDK declarations;
it does not evaluate MSBuild. It also verifies immutable task and manifest hashes. All twelve
answers were [revalidated independently after completion](validation-planning/independent-revalidation.json).
All six tasks reach WPF; `wpf=false`, changing a test project itself, and explicit false precedence
are covered by unit tests rather than this corpus. See [solver assignments](validation-planning/solver-assignments.json).

## Engineering Changes And Separate Measurements

- Load the .NET graph only when the scope needs it; prune already excluded dependency/build
  directories before traversal. On the same repository, ten alternating pairs per query were
  faster in every case: AI Python 93.722 to 0.758 ms, guidance docs 94.517 to 0.657 ms, shared .NET
  95.296 to 44.531 ms. Recommended checks, consumers, and final-check requirements remained
  equivalent. These are warm API timings, excluding import and CLI startup, not task completion.
- Render duplicate excerpts once, retain every equivalent path/hash and all JSON context, and
  consolidate command presentation. The [same-input rendering comparison](packet-rendering/comparison.json)
  reduced Markdown from 24,143 to 5,659 characters (76.56%). Characters are not measured tokens
  or latency. Optional excerpt bodies remain available in JSON. Exact measured bytes are retained
  in `.md.gz` snapshots; reviewable `.md` copies use one terminal newline for repository whitespace
  checks. Snapshot hashes document this formatting-only difference.
- For an already located shared-guidance edit, use the generator directly. Reserve packets for
  discovery, cross-module work, and resumption. This avoids imposing packet cost on every task.
- Add validated completion as a metric; use a common telemetry helper with `sys.executable`,
  retain failed attempts, and allow normal repairs before terminal completion.

[Recommendation benchmark observations](recommendation-benchmark/raw-observations.jsonl),
[summary](recommendation-benchmark/summary.json), and exact before/after plans are retained.
The earlier lazy-only profile, including its inconsistent .NET result, remains under
`recommendation-benchmark/lazy-only/`. The relocatable benchmark driver accepts `--root`,
`--baseline`, and a fresh `--output-dir`; use the retained baseline snapshot. New timing runs
must use new output directories. Do not overwrite historical observations.

## Negative Results And Provenance

The [original pilot](../ai-throughput-pilot-2026-10-28/README.md) remains unchanged. The first
refinement's six maintenance pairs also **failed** their frozen threshold: only two of five
eligible pairs improved by 10%; median paired reduction was 5.777%, and the sixth baseline
failed after an incorrect recorder executable path. All twelve logs and the
[failed assessment](assessment.json) remain retained. No slower run was substituted.

The initial planning launch was aborted before the full corpus: its first baseline failed,
the runner prematurely made failure terminal, and a coordinator delay contaminated the
unlaunched treatment timer. Its logs, original runner, and
[incident notes](validation-planning/initial-attempt-notes.json) remain retained. The separate
confirmation protocol fixed the timing boundary and allowed up to three attempts, charging all
repair time. It kept the same six objectives and used fresh fixtures/solvers. The positive result
must not be presented as a successful maintenance rerun or pooled with that aborted launch.

Protocol hashes were not rewritten after observation. The reporter's explanatory wording changed
after freeze; reconstructing the frozen hash and recomputing all twelve reports establishes exact
equivalence of every timing, eligibility, and comparison field. See
[reporter provenance](validation-planning/reporter-provenance.json) and the retained wording-only patch.

Keep the conclusion narrow: use automatic validation planning where dependency discovery is
needed. This evidence does not justify claiming a project-wide development or passing-PR speedup.
