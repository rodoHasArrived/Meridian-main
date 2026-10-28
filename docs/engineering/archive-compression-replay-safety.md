# Archive compression and replay safety

## Representation contract

`FileMaintenanceService.CompressAsync` and repair recompression publish a complete,
verified, create-only `.jsonl.gz` before removing the source `.jsonl`. Required
backups, including existing source checksum evidence, must all complete before
any source mutation. Decompressed staging bytes and the current source must match
the retained source SHA-256. Directory synchronization precedes source deletion.

Two physical representations can survive interruption between publication and
cleanup. This is a recoverable publication protocol, **not an atomic two-name
filesystem transaction**. `JsonlArchiveFiles` makes the source and its exact gzip
counterpart one logical directory input. It compares their complete logical
payload digests before returning a read handle when both are present. Different
or corrupt representations refuse replay; readers do not choose unverified
precedence, delete evidence, or suppress events by event ID.

Only terminal `.jsonl`, `.jsonl.gz`, `.jsonl.gzip`, `.jsonl.zst`, `.jsonl.lz4`, and
`.jsonl.br` names are directory candidates. Backups, checksum sidecars, rejected
line files and unpublished staging files are excluded. Same-named files in
different directories remain independent. The plain-to-gzip pairing applies to
`.gz`; this is not a general deduplication policy for independently produced
archives or other codecs.

`JsonlReplayer`, `JsonlMarketDataStore`, and both streaming and memory-mapped paths
of `MemoryMappedJsonlReader` share this discovery/open contract. Discovery is
materialized before events are yielded. A captured plain path resolves to its
gzip successor if cleanup has already removed the source. Readers retain the
resolved handle and use its actual filename and length rather than reopening the
old path. Existing codec detection, query filters and corruption reporting stay
in their respective readers.

## Interruption and recovery

| Interruption | Retained evidence | Replay and subsequent maintenance |
| --- | --- | --- |
| Before publication | Source, backups, possibly staging | Ignore staging and backups; read source once. Retry compression. |
| After publication or directory synchronization | Source and complete gzip, backups | Compare payload digests; expose one logical input. Retry verifies and synchronizes the retained destination before cleanup. |
| During source or sidecar cleanup | Verified gzip, possibly source/sidecar, backups | Read once. Any failed cleanup remains an operation error; retry after releasing blocking handles. |
| During replay | Retained source or gzip read handle | Continue that representation only. Cancellation leaves storage unchanged; a fresh replay starts from the beginning. |

A pre-existing destination is never overwritten. Recovery cleanup requires
matching decompressed bytes and an unchanged source; a destination checksum
sidecar collision still refuses the operation. Required-backup failure also
blocks recovery cleanup. Windows can refuse deletion of a memory-mapped source;
that must remain an explicit failure with both verified copies retained, not a
false compression success. Cleanup can be retried after readers close.

An orphan source checksum sidecar after source deletion is excluded from replay
but is not automatically deleted by a directory retry with no remaining source.
Recovery copies and orphan staging files remain operator evidence; this change
is not a garbage-collection policy.

## Rollout and review boundary

Upgrade all archive replay readers before enabling compression. Old wildcard
readers can still consume backups, staging, or overlapping representations.
Until upgraded, keep compression disabled for trees used by those readers.
Preserve conflicting evidence for operator investigation rather than deleting a
copy based on filename or modification time.

This contract covers lossless maintenance of archived inputs. It is not a global
snapshot across unrelated concurrent producers, a new source-writer fencing
protocol, or a durable consumer cursor guaranteeing exactly-once downstream
side effects across process restarts. Existing archive immutability and source
ownership assumptions remain required.

## Validation

`ArchiveCompressionCrashTests` injects faults at verified staging, publication,
directory synchronization, source deletion and sidecar deletion, then constructs
fresh maintenance and replay services. It also covers artifact exclusion,
conflicting/corrupt copies, checksum refusal, required backups, cleanup during an
active replay, stale discovery paths, cancellation and independent directories.
These are deterministic production-code fault-injection tests, not host
power-loss emulation.

Run the focused tests and the full repository gate:

```sh
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj -c Release \
  -p:EnableWindowsTargeting=true \
  --filter 'FullyQualifiedName~ArchiveCompressionCrashTests|FullyQualifiedName~FileMaintenanceServiceTests|FullyQualifiedName~JsonlReplayerTests|FullyQualifiedName~JsonlMarketDataStoreTests|FullyQualifiedName~MemoryMappedJsonlReaderTests|FullyQualifiedName~ScheduledArchiveMaintenanceServiceTests'
bash scripts/ci.sh
```

Run the focused slice on Linux and Windows. Hosted checks and independent
Storage/Data/Backtesting review remain necessary; a protocol model or static
inspection alone is not acceptance evidence.
