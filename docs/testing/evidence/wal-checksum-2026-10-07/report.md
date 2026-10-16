# WAL checksum performance evidence

All eight portable benchmark stages pass with the existing profile and budgets in [provider run 37678970064](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37678970064). Acceptance is scoped to source checkpoint `bba13ca9c156a42b5ca7f0f9c196d9199a70ef75` and its actual hosted tested merge `f06b91711cadea591e09076f1e4e8ddad0643a35`.

Hosted Meridian CI quality and integration gates passed at that checkpoint. The completed local canonical command still exited 1 at a generated-document commitment check after all .NET and browser tests passed. Main `83d6440e50c6bdb7fbc334c8aaec791c151c573f` is integrated in the resolved merge result with all twenty accepted source files byte-verified unchanged. Fresh final stable-tree repository checks remain pending. Current outcomes are reported on [PR #3123](https://github.com/rodoHasArrived/Meridian-main/pull/3123); the packet preserves the original successes and failures separately.

## Accepted provider measurements at bba13ca9

The clean hosted checkout used Ubuntu 24.04.5, AMD EPYC 9V45, four logical CPUs, .NET SDK 10.0.100 and runtime 10.0.0. Both the benchmark command and existing budget validator exited zero without timeout.

| Portable stage | Mean ns/op | Existing limit ns/op | Allocation B/op | Existing limit B/op | Verdict |
|---|---:|---:|---:|---:|---|
| `DedupKey_CacheHit` | 42.385 | 200 | 0 | 0 | Pass |
| `DedupKey_CacheMiss` | 326.999 | 800 | 128 | 256 | Pass |
| `WalChecksum_Small` | 154.690 | 400 | 0 | 0 | Pass |
| `WalChecksum_Medium_1KB` | 170.501 | 600 | 0 | 0 | Pass |
| `WalChecksum_Large_4KB` | 357.714 | 1,200 | 0 | 1,024 | Pass |
| `NewlineScan_Portable` | 0.703 | 50 | 0 | 0 | Pass |
| `AlpacaParse_Trade_SourceGenerated` | 367.078 | 900 | 384 | 512 | Pass |
| `AlpacaParse_Quote_SourceGenerated` | 468.829 | 1,200 | 472 | 640 | Pass |

The [original manifest](runs/hosted-provider-pass-37678970064/run.json), [validator evidence](runs/hosted-provider-pass-37678970064/budget-evidence.json) and [full BenchmarkDotNet report](runs/hosted-provider-pass-37678970064/bdn/results/Meridian.Benchmarks.PipelineBudgetBenchmarks-report-full.json) retain eight complete stage rows and **433 raw measurement records**, including warmup, overhead and excluded outlier records. All sixteen original text files and fifteen original artifact hashes are preserved. The [artifact verification receipt](receipts/provider-artifact-verification.json) records the independent checks.

The [provider source-binding receipt](receipts/provider-source-binding.json) retains twenty production, test, benchmark and toolchain file hashes. The [actual signed merge metadata](receipts/provider-tested-merge-metadata.json) records merge parents `1f94c380d3756063ebe4e4d73951ee0e3dfd58d0` and `bba13ca9c156a42b5ca7f0f9c196d9199a70ef75`, with a complete Git tree identical to the source checkpoint. This supersedes the historical seventeen-file binding for current acceptance while preserving that earlier receipt.

The [current-main provider integration receipt](receipts/current-main-provider-integration.json) compares all twenty accepted files with actual working bytes and stage-zero blobs in the resolved merge index. It records main `83d6440e50c6bdb7fbc334c8aaec791c151c573f`, evidence parent `a9daba63f849af05f338b76c2d2b90f13ad6fb06`, and unchanged main-side blobs relative to the earlier main base. The accepted `bba13ca9`/`f06b9171` source bytes are preserved; final stable-tree CI is still pending.

The accepted [profile](runs/hosted-provider-pass-37678970064/profile.json) and [exported budgets](runs/hosted-provider-pass-37678970064/bdn/perf-budgets.json) are byte-identical to the clean local baseline. Their SHA-256 digests remain `f89ebf264b25340f30c6c4a85d28007d10ffd53f0acb90684989880dc14f0e26` and `73c03f00210faac0b1d7bd5838d491cb8daae989ddc4304b30d9283997ebedce`; every latency/allocation limit, fixture, filter and validator failure rule is unchanged.

This EPYC 9V45 host differs from the earlier EPYC 7763 hosts. The results establish budget acceptance on the recorded machines; cross-host timing differences do not isolate a causal provider speedup or a hardware-independent percentage improvement.

## Historical accepted measurements

The clean hosted checkout tested merge `f7c1f6d77ec44b2a4924c892626678221fef3317` for production head `a65c3f3db71868724d5a3813e70575f75a903279`. The runner used Ubuntu 24.04.5, AMD EPYC 7763, four logical CPUs, .NET SDK 10.0.100, and runtime 10.0.0. Both the benchmark command and the existing budget validator exited zero.

| Portable stage | Mean ns/op | Existing limit ns/op | Allocation B/op | Existing limit B/op | Verdict |
|---|---:|---:|---:|---:|---|
| `DedupKey_CacheHit` | 60.250 | 200 | 0 | 0 | Pass |
| `DedupKey_CacheMiss` | 716.495 | 800 | 128 | 256 | Pass |
| `WalChecksum_Small` | 199.240 | 400 | 0 | 0 | Pass |
| `WalChecksum_Medium_1KB` | 253.192 | 600 | 0 | 0 | Pass |
| `WalChecksum_Large_4KB` | 574.056 | 1,200 | 0 | 1,024 | Pass |
| `NewlineScan_Portable` | 2.469 | 50 | 0 | 0 | Pass |
| `AlpacaParse_Trade_SourceGenerated` | 726.600 | 900 | 384 | 512 | Pass |
| `AlpacaParse_Quote_SourceGenerated` | 950.710 | 1,200 | 472 | 640 | Pass |

The [original run manifest](runs/hosted-final-pass/run.json), [validator evidence](runs/hosted-final-pass/budget-evidence.json), and [full BenchmarkDotNet report](runs/hosted-final-pass/bdn/results/Meridian.Benchmarks.PipelineBudgetBenchmarks-report-full.json) retain eight complete stage rows and **444 raw measurement records**, including warmup, overhead, and excluded outlier records. All sixteen original text files and the fifteen original manifest hashes are preserved.

At the historical integration checkpoint, after integrating main `1f94c380d`, a [source-binding receipt](receipts/current-main-integration.json) verifies that seventeen checksum, deduplication, test, benchmark and toolchain files remain byte-identical to both the accepted branch head and the fetched actual hosted tested merge.

The accepted [profile](runs/hosted-final-pass/profile.json) and [exported budgets](runs/hosted-final-pass/bdn/perf-budgets.json) are byte-identical to the [clean local baseline](runs/local-baseline/run.json). Their SHA-256 digests are respectively `f89ebf264b25340f30c6c4a85d28007d10ffd53f0acb90684989880dc14f0e26` and `73c03f00210faac0b1d7bd5838d491cb8daae989ddc4304b30d9283997ebedce`.

Profile `prd112-linux-x64-net10-v1` retains Release, the exact eight-stage benchmark filter, one launch, three warmups, eight measured iterations, and a 250 ms iteration target. It also retains `DOTNET_ROLL_FORWARD=Disable`, `DOTNET_ROLL_FORWARD_TO_PRERELEASE=0`, `DOTNET_TieredCompilation=1`, `DOTNET_TieredPGO=1`, and `DOTNET_gcServer=0`. Restore/build/execution time caps, fixture contents, allocation and latency thresholds, completeness checks, and validator failure behavior are unchanged. The existing SIMD-only newline exclusion remains in effect; no portable stage is waived.

## Integrity and implementation

New segments identify the checksum format explicitly as `MDCWAL02|2|<creation timestamp>`. They retain a lowercase 64-character BLAKE3-256 digest over a canonical, lossless PackBits encoding of length-framed record bytes. Existing `MDCWAL01|1` segments keep their original SHA-256 verification over UTF-8 `sequence|timestamp:O|recordType|payload`.

The v2 frame contains little-endian signed 64-bit sequence and `DateTime.ToBinary()` values, followed by signed 32-bit UTF-8 record-type and payload lengths, then the exact UTF-8 field bytes. The existing replacement fallback is preserved. Length framing distinguishes the type/payload boundary independently of delimiter characters.

Canonical PackBits selects the earliest run of at least three identical bytes. Literals and runs split at 128 bytes; a one-byte run remainder becomes a separate literal, and a two-byte remainder uses a run packet. Token 128 is never emitted. Scalar and accelerated scanners produce the same bytes. Lossless encoding preserves the complete framed input before cryptographic hashing; every call encodes and hashes its input without a record or digest cache.

Reads choose the algorithm from the supported magic/version pair, without guessing or fallback. Recovery and commit scans support mixed v1/v2 segments. Repair preserves original supported headers and valid digest strings. Unsupported or malformed recognized formats halt recovery under every corruption mode and cannot be truncated. The distinct v2 magic also makes older binaries' magic-only deletion check reject v2 segments. Retained v2 segments must be drained before downgrading to a v1-only binary.

The separate deduplication optimization changes hexadecimal formatting to the runtime span encoder while preserving the SHA-256 identity, first-16-byte truncation, lowercase key format, and single string allocation. The historical accepted head did not apply the reusable SHA-provider candidate retained in diagnostic snapshots. After a later hosted run exposed another dedup miss violation, the production span hashing path now reuses a private provider per thread through `Sha256Digest.ComputeBytes`, resets state after every success, and discards/disposes a failed provider. Every constructor thread primes its own provider before the global formatter warmup. SHA-256 inputs, truncation and persisted keys are unchanged.

## Profiling and measurement limits

The [initial component profiler](components/before/components-before.log) isolated encoding and SHA-256 work. [Native OpenSSL measurements](components/native/README.md) and [managed/native hash candidates](components/candidates/README.md) retained every repetition and source. A [general-purpose LZ4 candidate](components/compression/README.md) was rejected after its 32-bit and 64-bit encoders produced different compressed representations for the same input. Such representation differences cannot define a portable canonical checksum input.

The [PackBits production-core profile](components/packbits/README.md) preserves the exact timed source snapshot, build and correctness logs, environment receipt, and all 36 measured rows. Its source SHA-256 before and after timing is `d184f707b85e6b6830ae4d54c691fb297ccaa6fef6068ee3e1f99a19b798d3bb`. Each case warmed 50,000 calls and retained three 50,000-call repetitions; fixed fixtures and 256 preallocated varied inputs were measured separately. Every row allocated zero managed bytes. An independent scalar oracle checked 144 adversarial byte fixtures and 1,536 framed-record digests before timing.

| Component fixture | Payload bytes | Fixed-input median ns/op | Varied-input median ns/op |
|---|---:|---:|---:|
| Repeated ASCII | 64 | 202.294 | 208.562 |
| Repeated ASCII | 900 | 296.405 | 344.950 |
| Repeated ASCII | 4,096 | 887.490 | 862.385 |
| Representative market JSON | 64 | 319.920 | 359.223 |
| Representative market JSON | 900 | 1,446.520 | 1,337.862 |
| Representative market JSON | 4,096 | 2,602.362 | 2,919.011 |

The portable WAL budgets cover the existing **64/900/4,096-byte repeated-ASCII fixtures**, which compress strongly. They include UTF-8 encoding, framing, canonical PackBits, and hashing; final hexadecimal string materialization and disk I/O remain outside the measurement boundary. Representative JSON retains more hashed bytes and shows higher component latency. Passing these fixture budgets does not establish the same latency for arbitrary JSON, incompressible payloads, WAL append/recovery, or an end-to-end pipeline. The component profile uses `DOTNET_TieredCompilation=0`, so its medians are explanatory evidence rather than acceptance results or directly comparable portable means.

The [dedup formatter profile](component-profiles/dedup/README.md) retains 42 measurements: six fixed/varied implementation cases with seven repetitions each, all allocating 128 bytes per key. Its 576 independent identity-oracle events passed across cultures and Unicode/pooled venue buffers. Broad timing ranges did not establish an 800 ns acceptance margin; the completed hosted portable run supplies that acceptance evidence.

## Retained failures and provenance

Repeated main and PR #3109 failures remain visible alongside the passing result. Their completed benchmark commands produced eight measured stages; the unchanged validator rejected latency violations while allocation budgets passed.

| Retained run | CPU | WAL small / medium / large ns/op | Original result |
|---|---|---:|---|
| [Prior main 37643199718](runs/hosted-main-prior/run.json) | Xeon Platinum 8370C | 774.554 / 1,460.253 / 4,069.947 | WAL stages fail; dedup miss also fails |
| [Main 37644053856](runs/hosted-main-current/run.json) | Xeon Platinum 8573C | 729.698 / 1,296.201 / 3,586.006 | WAL stages fail |
| [PR #3109 37652037382](runs/hosted-pr-3109/run.json) | EPYC 9V74 | 673.788 / 1,329.547 / 3,820.875 | WAL stages fail |
| [Clean local baseline](runs/local-baseline/run.json) | EPYC 9V74 | 854.762 / 1,603.454 / 5,053.999 | WAL stages and dedup miss fail |
| [First hosted v2 37659411104](runs/hosted-first-optimized/run.json) | EPYC 7763 | 196.866 / 250.886 / 649.619 | WAL passes; dedup miss fails at 835.531 ns |
| [Historical accepted run 37666873544](runs/hosted-final-pass/run.json) | EPYC 7763 | 199.240 / 253.192 / 574.056 | All eight portable stages pass |
| [Latest pre-provider head 37673101515](runs/hosted-final-head-failure-37673101515/run.json) | EPYC 7763 | 202.600 / 246.756 / 581.680 | WAL passes; dedup miss fails at 823.729 ns |
| [Accepted provider checkpoint 37678970064](runs/hosted-provider-pass-37678970064/run.json) | EPYC 9V45 | 154.690 / 170.501 / 357.714 | All eight portable stages pass |

The [first local v2 run](runs/local-first-optimized-interrupted/run.json) completed all eight BenchmarkDotNet measurements, but its execution session was interrupted before the harness recorded benchmark exit or invoked validation. Its original `running` manifest remains unchanged. The separate [interruption receipt](receipts/optimized-interrupted-receipt.json) and validator output record complete measurements and a dedup miss failure at 935.735 ns. This run is not represented as a completed or passing canonical run.

Different CPU models, quotas, and contention limit causal comparisons across these hosts. The retained results establish repeated failures and a successful unchanged-profile run; they do not establish a hardware-independent percentage improvement.

The accepted provider [GitHub artifact 11508497650](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37678970064/artifacts/11508497650) was 70,984 ZIP bytes with SHA-256 `9867614926cce2358902bb6a5a87434eae013c13e5ca1e0e0f110c325977e8c6`. Its sixteen retained text files total 497,394 bytes. The historical [GitHub artifact 11502599845](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37666873544/artifacts/11502599845) remains 70,610 ZIP bytes with SHA-256 `c20314268e904ae4091b65b4f5379edb3226b0b0ce7bc8761f31c246b7b53086`, with sixteen text files totaling 498,412 bytes. The packet [manifest](manifest.json) records archive provenance, original source locations and copied-file hashes for every run and component snapshot. Original manifests and failed verdicts are preserved byte for byte; compiled binaries, packages, native libraries and caches are excluded.

## Validation status

The reusable provider change passed **88** focused hashing, exact key compatibility, dedup ordering/replay and allocation tests with zero failures or skips. Tests compare changing inputs against independent framework SHA-256 across padding/block boundaries and four dedicated concurrent threads, check short and oversized destinations, and verify zero warmed managed allocation. Provider portable acceptance is complete at the retained `bba13ca9` checkpoint; later integrated source revisions require their own validation.

The [exact command receipt](integrity/commands.json) and lossless log/TRX copies independently confirm **132** focused WAL/allocation tests, **82** checksum tests with hardware intrinsics disabled, and **3** timestamp round-trip tests under `TZ=America/New_York`, all passing with zero failures or skips. Coverage includes legacy and pinned v2 digests, official BLAKE3 vectors, canonical encoding oracle/round-trip checks, buffer/run boundaries, UTF-8 fallback, cultures and timestamp kinds, concurrent calls, field tampering, mixed-version recovery/rotation/commits/repair, and unsupported-header preservation.

The standalone `python3 verify-evidence.py` checks packet hashes, unchanged original run manifests, raw measurement inventories, source snapshots, and lossless gzip originals. For the accepted run it also requires a clean successful checkout, benchmark and validator exit zero, exactly eight passing stage results within their existing limits, retained raw samples for every stage, and byte-identical baseline profile/budgets.

The initial local canonical CI attempt on `a65c3f3db71868724d5a3813e70575f75a903279` retained 19,338 passing tests, four failing `ToolProcessRunnerTests`, and five registered skips across nineteen shards, then exited 1. Diagnosis found that the container's PID 1 retained killed descendants as zombies, affecting process-exit assertions.

The retained [artifact-only Linux subreaper](ci/linux-subreaper.py) adopts and reaps orphaned child processes while executing the unchanged command. Its [focused proof](ci/tool-process-subreaper-receipt.json) passed seven process-runner tests with zero failures/skips in thirteen seconds; eight adopted SIGKILL descendants were reaped, with no new zombies or residual adopted processes. This supplies the process reaping normally provided by init without changing production code or weakening tests.

The subsequent canonical command completed every .NET shard (19,639 passes, zero failures, five existing registered skips) and 3,651 browser tests, but exited 1 because central-package discovery treated three archived upstream `.csproj` snapshots as Meridian projects. The [complete failure archive](ci/canonical-evidence-packaging-failure/archive-manifest.json) retains all command outcomes. The snapshots are now lossless `.csproj.gz` evidence, retaining exact decompressed hashes; project discovery, package pins and tests are unchanged.

The later clean `bba13ca9` canonical command ran from **2026-10-07 20:01:55 to 20:40:25 UTC** and exited **1**. All **19 .NET shards** passed with **19,646 passing tests, zero failures and five existing registered skips**. All **42 browser batches** passed with **3,651 passing tests and zero failures or skips**. Adding the accepted run and receipts during execution changed generated repository-structure and documentation-health output, so `Verify whole-repo generated documentation is committed` failed. The later full workflow/script suite was not reached. The [complete documentation-drift archive](ci/provider-documentation-drift/archive-manifest.json) retains the exact receipt, launch, logs, summaries and logged diffs; its [source-freeze check](ci/provider-documentation-drift/source-freeze-receipt.json) confirms the twenty bound source files and relevant runtime/test/toolchain paths still match `bba13ca9`.

Hosted [Meridian CI 37678971005](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37678971005) quality and integration gates passed at `bba13ca9`, along with CodeQL, Windows Desktop Build, Documentation Automation, Maintenance and Roadmap Source Docs. The separate PostgreSQL Schema Control check failed because the added provider shifted contract source line numbers and therefore generated provenance; the [refresh receipt](receipts/schema-manifest-refresh.json) records that manifest correction. The resolved integration of main `83d6440e` preserves all twenty accepted source files. A fresh complete local/hosted validation of the final stable tree remains pending.

The [CI environment archive](ci/README.md) preserves the original failures and focused remediation evidence. Full canonical local and hosted runs, including their actual outcomes and source revisions, are recorded on [PR #3123](https://github.com/rodoHasArrived/Meridian-main/pull/3123). Required GitHub Actions checks on the final PR head remain the merge authority. The accepted portable result is independently complete for its recorded source checkpoint.
