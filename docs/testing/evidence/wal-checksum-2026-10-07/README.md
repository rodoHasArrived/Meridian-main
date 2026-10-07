# WAL checksum evidence packet

All eight portable stages pass in [hosted run 37666873544](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37666873544) with the existing profile and budgets. Canonical repository CI remains pending after an initial container process-reaping failure; the unchanged full command is rerunning through an artifact-only Linux subreaper wrapper.

The [performance report](report.md) explains the accepted measurements, versioned integrity design, retained failures, representative JSON limits, and validation status. The accepted run tested clean merge `f7c1f6d77ec44b2a4924c892626678221fef3317` for production head `a65c3f3db71868724d5a3813e70575f75a903279`.

- [Accepted run and original manifest](runs/hosted-final-pass/run.json): all sixteen text files, fifteen original hashes, eight passing stages, and 444 raw measurement records.
- [Accepted validator evidence](runs/hosted-final-pass/budget-evidence.json): unchanged mean-latency and allocation limits; zero violations.
- [Clean baseline](runs/local-baseline/run.json): byte-identical profile and exported budget contract.
- [Integrity command receipts](integrity/commands.json): 132 normal, 82 scalar, and 3 time-zone tests, with lossless log/TRX copies.
- [Packet manifest](manifest.json): source/archive provenance, original file hashes, component snapshots, and the complete retained-file inventory.

Run `python3 verify-evidence.py` from this directory or invoke the script by its full path. It requires only Python's standard library. Verification covers every copied file, unchanged original run manifests, interrupted-run receipts, component source hashes, and decompressed integrity evidence. It also requires a clean accepted run, benchmark and validator exit zero, all eight passing stages with raw samples, and profile/budget bytes equal to the baseline.

Earlier failures and the interrupted local optimization remain preserved as recorded. Component diagnostics retain their exact timed source and raw measurements; scratch SHA-provider reuse was not applied to production. Compiled binaries, native libraries, packages, build outputs, and caches are excluded. The manifest excludes only itself and its checksum file, avoiding a circular hash definition.
