# CI environment evidence

Hosted [Meridian CI 37678971005](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37678971005)
quality and integration gates passed at source checkpoint `bba13ca9`, and its
[portable provider run](../runs/hosted-provider-pass-37678970064/run.json) passed
all eight stages with unchanged limits. These results are scoped to that
checkpoint. Newer-main integration and final repository checks remain pending.

The [completed local provider command](provider-documentation-drift/archive-manifest.json)
started at 2026-10-07 20:01:55 UTC and completed at 20:40:25 UTC with **exit 1**.
All nineteen .NET shards passed: **19,646 passed, zero failed, five existing
registered skips**. All forty-two browser batches passed: **3,651 passed, zero
failed or skipped**. Adding the accepted portable evidence during execution
changed generated documentation, and the final generated-document commitment
check failed. The logged diff names repository-structure.md and both
doc-health-dashboard files. The full workflow/script suite was not reached.

Its original command receipt, launch, wrapper log, command log, .NET/browser
summaries and quality steps are preserved in the archive. The
[source-freeze receipt](provider-documentation-drift/source-freeze-receipt.json)
checks the twenty accepted source-file hashes and confirms relevant runtime,
test, benchmark, script, workflow and toolchain paths still match `bba13ca9`.
The failed command is retained as failed; regenerating and committing the
documentation requires a fresh complete run.

The initial canonical `bash scripts/ci.sh` run exited 1 with 19,338 passing tests,
four process-containment failures and five existing registered skips. Its complete
log, command receipt, summaries and affected-shard evidence are preserved here.
The container's PID 1 (`tail`) did not reap killed orphaned descendants, leaving
zombies that process-exit assertions correctly detected.

The artifact-only [Linux subreaper](linux-subreaper.py) supplies normal init reaping
without changing repository code or tests. Its [focused command/process receipt](tool-process-subreaper-receipt.json)
and lossless log/TRX copies record seven passing tests, zero failures/skips,
eight adopted killed descendants reaped, and no new zombies or residual adoptees.

Full canonical runs execute the unchanged `bash scripts/ci.sh` under this wrapper.
Current full local and hosted gate outcomes are recorded on [PR #3123](https://github.com/rodoHasArrived/Meridian-main/pull/3123).
The earlier failure archives and independent process-reaper proof remain
historical evidence alongside current checkpoint acceptance.

Files ending in `.gz` decompress to the exact original bytes. The packet manifest
retains original sizes and SHA-256 digests as well as compressed-file digests.

The [subsequent full command](canonical-evidence-packaging-failure/archive-manifest.json)
exited 1 after all nineteen .NET shards and 3,651 browser tests passed. Its sole
failure was central-package discovery treating retained upstream `.csproj`
snapshots as Meridian projects. Those snapshots are now lossless gzip evidence;
the original bytes remain verifiable, and package tests and pins are unchanged.
The later provider/packaging command is the separately retained documentation
drift failure above; final integrated source still needs fresh validation.
