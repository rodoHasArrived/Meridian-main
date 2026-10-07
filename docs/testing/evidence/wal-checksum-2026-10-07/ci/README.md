# CI environment evidence

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
This historical packet proves portable benchmark acceptance and the environment
remediation; it does not substitute a focused test for the full repository gate.

Files ending in `.gz` decompress to the exact original bytes. The packet manifest
retains original sizes and SHA-256 digests as well as compressed-file digests.

The [subsequent full command](canonical-evidence-packaging-failure/archive-manifest.json)
exited 1 after all nineteen .NET shards and 3,651 browser tests passed. Its sole
failure was central-package discovery treating retained upstream `.csproj`
snapshots as Meridian projects. Those snapshots are now lossless gzip evidence;
the original bytes remain verifiable, and package tests and pins are unchanged.
The full command must be rerun on the provider and packaging change.
