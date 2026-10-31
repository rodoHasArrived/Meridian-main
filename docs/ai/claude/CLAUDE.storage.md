# CLAUDE.storage.md - Storage System Guide

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-10-30

Use the implementation links below for storage work. The former file counts, invented method
signatures, binary WAL diagram, compression benchmarks, and abbreviated persistence recipes are
not API contracts. This guide describes source behavior inspected at
`6f901f0e5192a126b107e55aec6520c31eacd841`; it does not certify a deployment or a recovery run.

## Storage Architecture Overview

The [event pipeline](../../../src/Meridian.Application/Pipeline/EventPipeline.cs) supports optional
WAL integration. Do not assume every sink or configured host has WAL enabled. With the WAL/dedup
path configured, its consumer ordering is:

```text
validate → reserve → WAL append → WAL flush
         → sink append → sink flush → dedup commit/flush → WAL commit
```

A successful producer `TryPublish` or `PublishAsync` is in-memory admission, not durable delivery.
A crash before the WAL flush can lose queued events. Recovery after the durable WAL boundary is
at-least-once: a crash after sink flush but before dedup commit can cause replayed duplicates.
Do not describe this as exactly-once delivery or acknowledge a batch before sink durability.

## File Locations

| Boundary | Current source |
| --- | --- |
| Ingestion, admission, batch persistence, and replay to sinks | [`EventPipeline`](../../../src/Meridian.Application/Pipeline/EventPipeline.cs) |
| Bounded queue policy and loss/backpressure choices | [`EventPipelinePolicy`](../../../src/Meridian.Core/Pipeline/EventPipelinePolicy.cs) |
| WAL append, commit, enumeration, repair, and truncation | [`WriteAheadLog`](../../../src/Meridian.Storage/Archival/WriteAheadLog.cs) |
| Versioned checksum encoding | [`WalChecksum`](../../../src/Meridian.Storage/Archival/WalChecksum.cs) |
| Portable package creation and import | [`PortableDataPackager`](../../../src/Meridian.Storage/Packaging/PortableDataPackager.cs) |

For sinks, policies, replay, export, metadata stores, and maintenance, inspect the current files under
`src/Meridian.Storage/` and their callers. Do not use a static file count or an illustrative class
signature as evidence that a public method exists.

## File Organization

Use `StorageOptions` and `JsonlStoragePolicy` for actual naming and partition behavior. Storage
layout depends on configuration; a sample `live/`, `historical/`, or tier directory is not an
invariant. Keep provider identity and partition boundaries intact when changing discovery/replay.
Do not assume lexicographic filename order is global event-time or WAL-sequence order.

## Configuration

Trace the active host's configuration and resolved data root before changing a path. Distinguish
logical `DataRoot`, effective `StorageOptions.RootPath`, and the installed host's configuration
location. Do not store customer data beside installed binaries or introduce an unrelated fallback
root merely because a local development example uses `data/`.

Verify each configuration property against the actual configuration type and host mapping.
Enabling a sink does not implicitly enable WAL, deduplication, retention, or recovery verification.
Report the tested configuration and storage backend rather than claiming a universal default.

## Compression Profiles

Inspect `CompressionProfileManager` and the actual writer/reader before choosing a codec. A profile
name, enum member, or desired tier format does not prove that every path supports it. Remove
unmeasured throughput/ratio assumptions from planning: record codec, level, workload, environment,
and measured results when making a performance claim.

Verify a round trip before replacing or deleting an original. Publication, directory durability,
reader behavior during a transition, and restart recovery all need coverage; compression success
alone does not establish crash consistency.

## Write-Ahead Logging (WAL)

### WAL Operations

The actual WAL API includes `InitializeAsync`, `AppendAsync<T>(data, recordType, ct)`,
`CommitAsync`, `GetUncommittedRecordsAsync`, and `TruncateAsync(throughSequence, ct)`.
`AppendAsync<T>` returns a `WalRecord`, not the old guide's `Task<long>` example. Sink replay is
owned by the pipeline's `RecoverAsync`; there is no basis for copying the former standalone
`WriteAheadLog.RecoverAsync` recipe.

Initialization scans and validates records; it does not prove that they have been replayed and
flushed to the final sink. Use the real WAL implementation and caller when designing recovery,
not a shortened disposal method that commits the last seen sequence unconditionally.

### WAL File Format

The WAL is a text format with a versioned header, not a binary length-prefix/SHA256/CRC32-trailer
record layout. `WriteRecordAsync` writes each record as:

```text
sequence|round-trip timestamp|record type|checksum|payload
```

The reader splits into at most five fields, preserving pipe characters in the payload. Header
families `MDCWAL01` and `MDCWAL02` select the supported format version. The
[checksum implementation](../../../src/Meridian.Storage/Archival/WalChecksum.cs) distinguishes:

| Version | Checksum input and algorithm |
| --- | --- |
| 1 (legacy) | SHA-256 over the legacy text representation. |
| 2 (current writer) | BLAKE3-256 over a canonical, length-framed record encoded with PackBits. |

Checksums are rendered as lowercase hexadecimal strings. PackBits here defines the bytes hashed;
it does not turn the on-disk WAL record into the old guide's binary format. Preserve legacy read
compatibility and reject unsupported versions rather than silently treating them as empty files.

### Ingest Durability Ordering

Dedup reservations are in memory until sink durability is confirmed. Version-2 dedup ledger entries
represent that confirmation; legacy version-1 entries cannot suppress recovery solely because they
exist. Preserve the source's retry and corruption handling, including semantic payload failures.
A checksum-valid but undeserializable payload is not a successfully recovered business event.

Cumulative recovery acknowledgment is particularly sensitive: check
`RecoveryEnumerationIsSequenceOrdered()` before committing a horizon mid-enumeration. Clock
rollback or foreign segment names can make enumeration order differ from sequence order. Do not
commit a higher sequence while lower records remain unreplayed. Truncation must preserve active
segments and files with invalid headers rather than deleting a scan that appears empty.

### WAL Sync Modes

Use the actual `WalOptions` and writer flush behavior rather than the former illustrative sync-mode
table. Distinguish buffered append, durable flush, sink durability, and committed recovery horizon.
A weaker sync setting is not permission to advertise the stronger durability guarantee.

## Schema Versioning

Read `SchemaVersionManager`, serialization contexts, and actual persisted version fields before
changing a schema. The former generic `RegisterSchema<T>` / `MigrateAsync<T>` examples were not a
substitute for those declarations. Separate WAL format version, event/schema version, and dedup
ledger version; advancing one does not migrate the others.

## Tiered Storage

Read `TierMigrationService` and the specific writer/reader for the requested transformation. Do not
infer conversion support from a tier name or file extension. Keep the original until the supported
transformation has been verified and its publication/recovery boundary is satisfied.

## Data Retention

Use the actual retention, quota, and maintenance owners. Do not add a loop that deletes the oldest
file until a byte count fits without accounting for active writers, replay, evidence retention,
and recovery eligibility. Prefer a bounded, inspectable plan and test the failure/restart paths
before enabling destructive behavior. Configuration examples are not authorization to delete data.

## Data Replay

[`JsonlReplayer`](../../../src/Meridian.Storage/Replay/JsonlReplayer.cs) accepts a file or directory
and exposes `ReadEventsAsync(ct)`. Its optional symbol-order dictionary filters effective symbols
and supplies ranks for equal timestamps. The implementation validates source records before symbol
filtering and uses bounded external sorting because arrival order can contain late events.
Sequential filename iteration is not an equivalent implementation of deterministic event-time replay.

Verify provider/symbol/date scope, compressed inputs, ordering, duplicate behavior, malformed-input
handling, and cancellation in the consuming workflow. A retained file or parseable payload alone
is not proof of complete or independently sourced data.

## Export System

Read the actual export request/result types and format-specific writers. Keep analytical exports,
portable packages, and governed accounting/report evidence separate. A generated file is not
certification or proof that all requested rows were exported.

`PortableDataPackager.CreatePackageAsync` currently supports `PackageFormat.Zip`; other requested
formats return failure before package output is created. Enum values must not be advertised as
working TarGz or SevenZip implementations. Check `PackageResult` and validate import/round-trip
behavior, including binary payloads, rather than assuming that a completed call created a usable package.

## Event Pipeline

`EventPipelinePolicy.DurableStreaming` uses `Wait` for persistence-critical backpressure;
`Default`, `HighThroughput`, and `MessageBuffer` use `DropOldest`. Do not replace the real pipeline
with the former toy channel class, or infer losslessness from a successful `TryWrite` on a dropping
queue. Test admission failure, overload, cancellation, drain, sink flush, and dedup/WAL commit order.

## Best Practices

Retain atomic publication and directory-durability helpers rather than reducing persistence to
`WriteAllTextAsync` plus a rename. Await lifecycle-sensitive metadata persistence before reporting
success, and preserve source-generated serialization where required. Treat cancellation before and
after a durable publication point according to the existing recovery contract, not a blanket rule.

Use synthetic data for failure injection. Cover crashes on both sides of WAL flush, sink flush,
dedup commit, and WAL commit; mixed legacy/current WAL versions; corrupt headers/payloads; unordered
segment names; active-segment truncation refusal; and package binary round trips. Run the relevant
existing suites and record results; listing these scenarios is not a claim that they passed.

## Monitoring

`WriteAheadLog` declares `mdc_wal_recovery_events_total`,
`mdc_wal_recovery_corrupted_records_total`, and `mdc_wal_recovery_duration_seconds`.
The recovery-events counter is the valid-record scan tally, not a durable sink-replay count. The
implementation explicitly distinguishes it from `mdc_wal_replayed_events_total` reported by replay
consumers. Do not recreate old metric names or arbitrary thresholds without checking instrumentation.

## Related Documentation

- [Engineering and validation](../../engineering/README.md)
- [Provider implementation](../../development/provider-implementation.md)
- [Documentation ownership](../../documentation-ownership.md)

## Related Resources

The source links above define the inspected behavior. For a readiness or recovery decision, retain
the tested commit, configuration, failure scenario, result, and any unexecuted checks. Do not infer
production acceptance from this guide, a simulation, or an old passing run.
