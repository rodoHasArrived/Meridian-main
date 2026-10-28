# Task Context And Throughput Pilot

**Status:** pilot  
**Owner:** core-team  
**Reviewed:** 2026-10-28

This optional workflow reduces repeated discovery and manual guidance copying. It extends the
existing context-budget and prompt-routing tools; it does not replace applicable instructions,
source inspection, the task-to-proof matrix, or required GitHub checks.

## Assemble A Task Packet

Run from the repository root with Python 3.11+ and the existing docs dependencies
(`python -m pip install -r build/scripts/docs/requirements.txt`):

```bash
python build/scripts/ai/task-context.py \
  --task "Update the shared guidance generator" \
  --target-file build/scripts/ai/generate_guidance.py \
  --output artifacts/ai/task-context.json \
  --markdown-output artifacts/ai/task-context.md
python build/scripts/ai/task-context.py --check artifacts/ai/task-context.json
```

The JSON packet contains the objective, explicit target files, route, registered module owners,
candidate validation commands, instruction references, excerpts, missing/omitted sources, content
hashes, and Git revision when available. Markdown is the reading surface; JSON is the freshness
record. Commands are suggestions, never executed by packet generation. Script IDs in route
metadata are distinct from runnable validation commands. Shared-contract targets flag both browser
and WPF consumers for impact review.

Targets must already exist. References must stay inside `--root`; external symlinks, path escapes,
and outputs colliding with consulted inputs are rejected. Optional missing metadata is reported.
Default limits are 12 excerpts and 24,000 excerpt characters, with at most 3,000 characters per
excerpt; these are excerpt budgets, not total packet-size limits. Targets precede optional context.
Read instruction files in full and inspect omitted or truncated source when needed.

Check freshness before reusing a packet across sessions or edits. The check recomputes selection,
hashes, applicable instructions, metadata, and revision; any difference fails and requires a fresh
packet. This deliberately invalidates a packet after its target changes. Do not treat a packet as
proof that its recommended validation has run.

## Generate Shared Guidance

The `.claude/skills/_shared/project-context.md` marked shared section remains canonical:

```bash
python build/scripts/ai/generate-guidance.py --check --summary
# After editing the canonical shared section:
python build/scripts/ai/generate-guidance.py --write --summary
```

The generator updates only the `shared-context` block in the Codex and Agents copies. It preserves
host-specific prefixes and suffixes byte-for-byte and leaves current files untouched. All three
files must exist with one valid marker pair; malformed inputs fail before replacements begin.
Symlinks are rejected. Replacements are atomic per file, not a transaction across multiple files;
rerun the check after an interrupted write. Default mode is read-only.

The existing AI inventory check still enforces shared-block equality and now points to this repair
command. It uses the generator's strict UTF-8, marker, and file-safety checks, so missing or
malformed canonical files and mirrors fail the inventory gate too. Other policy mirrors and
host-specific guidance remain separately maintained. No CI
workflow or protected governance file is changed by this pilot.

## Describe The Observable Outcome

Use a small JSON brief to make the assignment reviewable before editing. The required fields are
`desiredBehavior`, `acceptanceExample`, `ownedPaths`, `exclusions`, and `validationCommands`.
Behavior and the acceptance example are nonempty text; the other fields are lists of nonempty
strings. Exclusions may be empty. Owned paths are repository-relative; exclusions describe what
must stay out of scope. Validation commands are retained as explicit instructions, not executed
by packet generation. See the [maintained guidance-task example](examples/guidance-task.json).

```bash
python build/scripts/ai/task-context.py \
  --brief-file docs/ai/tooling/examples/guidance-task.json \
  --output artifacts/ai/guidance-task.json \
  --markdown-output artifacts/ai/guidance-task.md
```

The packet uses the desired behavior as its objective and carries every brief field. Existing
owned files provide context anchors. If the task owns only new paths, supply an existing
`--target-file` as an anchor. Owned paths describe assignment boundaries, not an edit permission
enforcement mechanism. Browser/WPF briefs must identify `sharedContract`: the shared contract or
service seam to use, or an explicit `none: <reason>` for a presentation-only change. This makes
the shared-state decision visible before either surface starts implementing.

The brief is a hashed input: changing acceptance, ownership, exclusions, or validation makes its
packet stale. `--brief-file` and `--task` are alternatives. Plain `--task` remains supported for
orientation and tasks that do not need a structured brief.

## Recommend Focused Validation

```bash
# Inspect staged, unstaged, deleted, and untracked nonignored paths:
python build/python/cli/buildctl.py recommend-validation
# Include committed branch changes since the merge base, plus local changes:
python build/python/cli/buildctl.py recommend-validation --base origin/main
# Preview a chosen scope without executing validation:
python build/python/cli/buildctl.py recommend-validation \
  --changed-file src/Meridian.Ui.Services/Meridian.Ui.Services.csproj --json
```

Recommendations contain command argument arrays, affected paths, selection reasons, input hashes,
consumer impact, unknown-coverage warnings, and retained final-check requirements. They use
path-based proof selection and the reverse .NET project-reference graph. A shared contract or
workstation-service change includes both browser and WPF consumers. Known Python, development
launcher, and browser test paths select focused checks; documentation and workflow changes also
recommend their maintained lanes. Renames include both old and new paths so a move across
subsystems retains both scopes. An invalid or unavailable `--base` fails with an error; fetch
the reference before retrying. Uncertain mappings stay visible for manual review rather than
being reported as covered. Recommendations do not execute arbitrary brief or registry commands.

Context packets include the same validation plan and bind its consulted inputs to freshness
checking. Review the plan, run the applicable commands during iteration, and retain their actual
results separately. Project references are a conservative approximation: runtime wiring, reflection,
MSBuild imports/expressions, and external dependencies can require more checks than the graph
identifies. Unknown files and unresolved dependencies require manual proof selection. Full
`bash scripts/ci.sh` and GitHub's required checks remain completion requirements.

Choose tools proportionally: when the shared-guidance edit is already located, edit the canonical
block and use the generator directly. Build a packet for discovery, cross-module work, or resuming
context. Markdown now presents the brief and target excerpts without repeating identical text;
equivalent paths, hashes, warnings, and instruction references remain visible. Full optional
excerpts remain in JSON. Recommendation generation skips .NET graph discovery for non-.NET scopes
and prunes previously excluded build/dependency directories before walking the graph.

## Record Comparable Tasks

Use three matched pairs initially, with the same acceptance criteria, task family, model, and
validation environment within each pair. Alternate which cohort runs first. The baseline uses the
existing manual discovery/copying workflow; treatment uses task packets and generated guidance.
Start the clock before discovery or packet generation. Keep one JSONL log per concurrent trial.

```bash
python build/scripts/ai/throughput-metrics.py record \
  --log artifacts/ai/trial.jsonl --trial task-1-treatment \
  --event task_started --pair task-1 --cohort treatment
python build/scripts/ai/throughput-metrics.py record \
  --log artifacts/ai/trial.jsonl --trial task-1-treatment \
  --event file_read --path build/scripts/ai/generate_guidance.py
```

Use `--root` for isolated repositories. Record every deliberate agent-visible file read, then
perform the read; tool-internal reads are excluded. Record `useful_edit --path <file>` immediately
after the first substantive source edit contributing to the accepted result. Artifact generation
does not count. Record `correction` when repairing an incorrect implementation, not for an initial
planned edit. Record validation events around the actual command:

```bash
python build/scripts/ai/throughput-metrics.py record \
  --log artifacts/ai/trial.jsonl --trial task-1-treatment \
  --event validation_started --validation-id focused
# Run the task's real validation command, retaining its output and exit code.
python build/scripts/ai/throughput-metrics.py record \
  --log artifacts/ai/trial.jsonl --trial task-1-treatment \
  --event validation_finished --validation-id focused --result pass
python build/scripts/ai/throughput-metrics.py record \
  --log artifacts/ai/trial.jsonl --trial task-1-treatment \
  --event task_finished --instrumentation-complete
```

Use the observed result, including failures. Omit `--instrumentation-complete` if reads,
corrections, or validation events were missed. The recorder is explicit instrumentation, not an
automatic trace collector; completeness is a runner attestation. It cannot detect unlogged work.

| Metric | Definition |
| --- | --- |
| Time to first useful edit | Task-start observation to first substantive source-edit observation |
| Repeated file reads | Subsequent deliberate reads of the same path and SHA-256 content |
| Correction cycles | Explicit repairs of incorrect edits, counted by the runner |
| Validation wait | Union of observed validation intervals, including failures; overlapping intervals count once |
| Time to a passing PR | Task start to recorded successful required checks on one specific PR head revision |
| Validated completion | Task start through recorded completion after successful final validation, including discovery, corrections, failed attempts, and validation wait |

All timestamps come from the recorder's UTC clock. Validation intervals include event-recording
overhead, and tool-call/model latency contributes to first-edit timing. Changed-content reads are
distinct from repeated unchanged reads. Missing or incomplete count evidence is `null`, not zero;
no observed validation interval also produces `null`. Preserve failures and retries in the log.

For PR timing, retain the PR URL, full head SHA, the required-check policy applicable to that run,
and successful check evidence. Record `passing_pr --url <url> --commit-sha <sha> --checks-json <file>`;
the JSON is a nonempty list of `{name, conclusion, commitSha, evidenceUrl}` objects. Each conclusion
must be `success` and each SHA must match the recorded head. The reporter validates the supplied
shape and consistency offline; the runner must verify authenticity, completeness of required checks,
and the current PR head through GitHub. Local tests never establish a passing PR. This event can
be appended after `task_finished`; its timestamp is observation time, not an inferred historical
completion time.

```bash
python build/scripts/ai/throughput-metrics.py report \
  --log artifacts/ai/task-1-baseline.jsonl \
  --log artifacts/ai/task-1-treatment.jsonl \
  --output artifacts/ai/throughput.json \
  --markdown-output artifacts/ai/throughput.md
```

Repeat `--log` for other pairs. Reports retain per-trial observations, evidence gaps, and medians
computed only from pairs with both values available for each metric. Negative paired differences
mean lower treatment values. Inspect task correctness and failed validation before interpreting
timing; a faster wrong result is not a delivery improvement. Keep raw logs with the report.

Matched comparisons require complete instrumentation, no unfinished validation, and a final
successful validation result. Earlier failures remain visible and their wait time is retained.
Incomplete first-edit timings appear only as observations. Passing-PR events must follow task
completion; supply `--commit-sha` on `task_finished` to bind later check evidence to that revision.

## Environment Setup And Local Validation Measurements

Use the [profile-aware environment command](../../engineering/web-development.md#install-and-choose-a-mode)
to check tool versions and fingerprint dependency/fixture inputs, or explicitly restore the two
npm lockfiles and selected backend dependencies. Fixture-only setup does not require .NET or a
database. The devcontainer shares the npm setup command and installs pinned Python script dependencies.

For local validation feedback, supply a unique `--task-id` on related `buildctl test` attempts
and summarize their receipts with `buildctl validation-metrics`. See the
[measurement definitions](../../development/build-observability.md#local-feedback-latency-and-observed-retries)
for measured feedback/queue/execution time, missing legacy data, and task grouping.

Receipt metrics complement the explicit task-event recorder above. They do not measure human
correction cycles, implementation time, or a passing PR, and are not added retroactively to the
retained pilot evidence. A failed validation attempt alone does not establish a code defect.

## Validation And Initial Evidence

```bash
python -m unittest tests.scripts.test_task_context tests.scripts.test_generate_guidance tests.scripts.test_throughput_metrics
python -m unittest tests.scripts.test_task_brief tests.scripts.test_validation_plan
python build/scripts/ai/generate-guidance.py --check --summary
python build/scripts/docs/check-ai-inventory.py --summary
```

The initial pilot uses six isolated maintenance trials: three matched sentence additions to the
canonical shared context and both mirrors, with an independent exact-change/host-preservation
validator. Fixture roots contain current context copies and routing metadata, not the whole
repository. The same agent handles both cohorts within a pair, so learning, task simplicity,
instrumentation overhead, and concurrent runner load limit interpretation. Treatment combines two
tools; this pilot cannot attribute effects to either tool individually. PR timing is unavailable
for these temporary trials. See the [retained pilot evidence](../../testing/evidence/ai-throughput-pilot-2026-10-28/README.md).

Use these observations to decide the next experiment, not to announce a general speedup. Before
making this workflow a default, repeat on real comparable implementation tasks through passing PRs
with independently checked event coverage and required-check evidence.

The [refinement evidence](../../testing/evidence/ai-throughput-pilot-2026-10-28-refinement/README.md)
retains the failed maintenance retest and a separately preregistered validation-planning
confirmation. The latter met its operational threshold: six of six pairs faster, 71.436% median
paired reduction, all twelve final plans accepted. This supports using the recommender for
dependency-based planning; it does not establish faster source edits or passing PRs.
