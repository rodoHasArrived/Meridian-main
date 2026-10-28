# AI Throughput Maintenance Pilot

**Status:** observed; wider delivery impact unproven  
**Owner:** core-team  
**Reviewed:** 2026-10-28

Six isolated agent maintenance trials passed independent exact-change validation. Task packets
plus generated guidance did not consistently improve time to the first useful edit. This is
feasibility and usability evidence, not evidence of a general development-speed improvement.

## Observations

| Metric | Baseline median | Treatment median | Matched pairs |
| --- | ---: | ---: | ---: |
| First useful edit | 17.726 s | 21.403 s | 3 |
| Repeated unchanged-content reads | 0 | 0 | 3 |
| Correction cycles | 0 | 0 | 3 |
| Validation wait | 0.066 s | 0.063 s | 3 |
| Time to passing PR | Unavailable | Unavailable | 0 |

The median within-pair first-edit difference is +3.433 seconds (treatment slower). Treatment was
faster in pair 2 and slower in pairs 1 and 3. Millisecond validation differences are recorder/process
overhead at this task size and should not be interpreted as meaningful improvement. Zero observed
repeated reads and corrections leave no measured reduction opportunity in these trials.

## Method And Limits

Each pair inserted one specified sentence immediately under `Useful Commands` in the canonical
Claude shared context and synchronized both host mirrors, preserving all other text. See
[manifest.json](manifest.json) for exact objectives, trial order, source base revision, and tool
hashes at the time of the pilot. Each fixture used real repository guidance and routing metadata.
The runner's [independent verifier](verify-pilot.py) checked exact edits and host-specific text
preservation against originals. All six validations passed; no correction was recorded.

Baseline used manual discovery and edits. Treatment included packet generation, packet reading,
canonical editing, and guidance generation. The same agent handled both cohorts within a pair:
pairs 1 and 3 ran baseline first, pair 2 ran treatment first. Different agents handled each pair.
Tasks ran concurrently on the same host. Prompt constraints, learning/order effects, tiny tasks,
partial fixture repositories, differing read strategies, model latency, and explicit instrumentation
overhead limit comparison. This is a convenience sample, not a randomized experiment. Tool-internal
reads are excluded; deliberate agent-visible fixture reads were logged and completeness attested
by each runner. Task-start timestamps exclude fixture preparation and assignment/setup before the
runner starts. Timings measure local task execution, not time from user request.

No fixture task became a PR, so PR timing is unavailable. The implementation PR is a different task
and must not be substituted for those missing values. The reporter checks supplied check evidence
offline; GitHub remains the authority for real PR validation.

## Pilot Findings And Changes

Initial packets sometimes selected `Platform Snapshot` or `Source Documentation Mesh` instead of
the explicitly requested `Useful Commands` section. Agents in pairs 1 and 3 needed additional
source reads. The retained [initial packets](packets/) document that behavior. After these trials,
heading selection was changed to prefer a named heading in the objective, with regression tests.
Host-guidance tasks now also exclude unrelated product-source suggestions and include the tooling
index. Those changes were tested separately; these six observations were not retroactively
replaced with timings from the revised tool.

Independent review also tightened reporting: incomplete traces and failed-only validation trials
cannot enter matched comparisons, validation outcomes remain visible, and passing-PR events must
follow task completion and match its delivery SHA when supplied. The raw observations remain
unchanged; [report.json](report.json) was regenerated using the stricter reporter.

Keep the workflow optional. Repeat on real comparable implementation tasks through verified
passing PRs before considering default adoption or claiming savings. Separate packet-only and
generator-only trials if attributing effects to individual tools matters.

## Reproduce The Report

The [raw event logs](events/) retain actual UTC observations and file-content hashes.
[report.md](report.md) lists individual trials; [report.json](report.json) includes paired medians,
validation outcomes, comparison eligibility, and unavailable evidence.

From the repository root:

```python
from pathlib import Path
import subprocess
import sys

base = Path("docs/testing/evidence/ai-throughput-pilot-2026-10-28")
command = [sys.executable, "build/scripts/ai/throughput-metrics.py", "report"]
for log in sorted((base / "events").glob("*.jsonl")):
    command.extend(["--log", str(log)])
command.extend(["--output", str(base / "report.json"),
                "--markdown-output", str(base / "report.md")])
subprocess.run(command, check=True)
```

To rerun the task validator, use an isolated fixture containing the original three context files
in `artifacts/originals.json` (mapping relative paths to original UTF-8 text), then run
`python docs/testing/evidence/ai-throughput-pilot-2026-10-28/verify-pilot.py <fixture-root> "<sentence>"`.
Fresh trials require fresh logs and observations; regenerating reports does not rerun the pilot.
See the [workflow and metric definitions](../../../ai/tooling/throughput-pilot.md).
