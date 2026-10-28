# PRD-106 Replay and Parameter Re-cut Review Packet

**Status:** review required; not completion or acceptance evidence

**Owner:** Backtesting + Strategy Analytics

**Reviewed:** 2026-10-28
**Candidate branch:** `codex/pr2896-round2`

**Reviewed source commit:** `0842cf7548f6b18afee79bfb4df2f73f85787e7e`

## Scope and claim boundary

This focused re-cut contributes two safety improvements to
[`PRD-106`](../../product/implementation-todo-list.md): deterministic, bounded JSONL replay and
fail-closed Quant Lab runtime-parameter extraction. It does not claim that browser and WPF
orchestration are unified, that the other PRD-106 recovery/outbox requirements are complete, or
that PRD-106 is closed.

The `W6-BTSTUDIO-001` registry entry remains unchanged. Its completed scope is still the bounded,
host-composed browser Covered Call evidence and governed Paper-promotion loop. This re-cut neither
reopens that row nor broadens it into a general Backtesting Studio completion claim.

## Candidate behavior under review

- JSONL and supported compressed JSONL partitions are external-sorted by full UTC timestamp, then
  ordinal file position and physical line number.
- Shared roots are read/sorted once for their selected symbols. Full UTC ticks and requested symbol
  ranks survive the grouped-source and outer merges; default file/line ties remain stable.
- Sort preparation takes whole-operation admission before opening a merge batch. The final run is
  paged by UTF-8 byte offset without duplicate page files. No admission or file handle survives a
  public yield. Spool directories/files use owner-only Unix permissions.
- Sources must be closed captures. Exclusive source leases reject active writers as unavailable,
  including a transient partial tail; same-process readers serialize through bounded path gates.
  The sorted run is unaffected by later source changes. Sorting scans the capture before first
  yield to validate and establish chronology; this is not a live-tail endpoint.
- Malformed, null, or incomplete persisted event envelopes fail closed with file and line evidence. Cancellation and early
  consumer disposal release admission, close readers, and attempt spool cleanup.
- Quant Lab ties parameter metadata to the exact current editor source. Run is disabled while
  extraction is pending or unavailable, and the command path independently rejects an attempted
  launch without current-source metadata.
- Roslyn-bound discovery supports inferred types, named arguments and constant metadata; unsupported
  or incomplete globals `Param` references reject the entire extraction with HTTP 400.
- A failed parameter refresh may display the last usable descriptors and overrides as stale
  reference data, but it cannot submit them or silently fall back to inline defaults. A successful
  refresh replaces removed descriptors and retains only matching overrides.

## Independent technical review

The owner-directed PR review uses two independent technical lanes. These are agent source reviews;
no human approval, production acceptance, or release certification is claimed. Required repository
reviews, branch protections, and authoritative hosted checks remain unchanged.

| Technical review lane | Reviewer | Disposition |
| --- | --- | --- |
| Backtesting / Strategy Analytics | `review_dependencies/review_parameter_slice`, with replay-engine review by `review_dependencies` | Parameter completeness P1 addressed; full-tick ordering, shared-root grouping and stable ties retained. Source review clear; backend execution awaits the composed integration gate. |
| Accounting and Ledger | `review_dependencies/governance_evidence` | Strict envelope validation, immutable replay spools, source-position evidence and snapshot economics reviewed. Identified sidecar discovery defect corrected with regressions. Shared archive/capture composition reviewed separately below. |

The composed tree preserves original #2896 head `68f3485235b9b9acfb9821df201e3a850dbcb6ff`, main
`6bcc7a0262`, and ready #3175 source tip `25fd42ffa7` as ancestors. #3175's portfolio, reconciliation,
and notebook behavior retains its separate review scope; this packet covers their replay-reader
intersection. Canonical archive discovery excludes checksum/recovery/staging files and deduplicates
plain/gzip representations. Replay opens both representations with the closed-capture share mode;
other archive readers retain their original shared-open overload. Plain/gzip aliases share one
bounded reader gate. Conflicting representations still fail without deleting evidence.

### Review-thread coverage ledger

GitHub thread disposition is separate from this technical evidence; no thread was silently treated
as resolved merely because it became outdated.

| Finding / thread suffix | Preserved behavior and regression evidence |
| --- | --- |
| P1 replay materialization (`eoW7c`) | One final private run, bounded byte-offset pages, no duplicate page files, shared roots prepared once. `PagesOnePrivateRunWithoutDuplication_AndCleansOnEarlyDisposal`; `BuildSymbolStreamsAsync_SharedFlatRoot_EnumeratesPhysicalSourceOnce`. |
| Parse exception mapping (`eoW7j`) | Empty malformed preview with physical source/line detail. `Preview_MalformedCapture_ReturnsExplicitEmptyErrorResponse`. |
| Primed-reader disposal (`eoW7v`) | Every created enumerator is disposed on initialization failure/cancellation; disposal errors retain the original cause. `MergeAsync_PrimingFailure_DisposesEveryEnumeratorCreatedSoFar` and cancellation/disposal cases. |
| P1 incomplete parameter scan (`eoW7z`) | Semantic discovery handles inferred/named/constant calls; unsupported mixed references reject the whole scan with HTTP 400 and no partial descriptors. Compiler/endpoint completeness tests and exact-source UI regressions. |
| P1 timestamp precision (`eoW77`) | Both merges use full UTC ticks, then requested-symbol ranks and stable source ties. `MergeAsync_SubMillisecondEvents_UsesFullUtcTicksBeforeStreamOrder`; shared/mixed-root tie cases. |
| Spool permissions (`eoW8A`) | Unix owner-only directory/file modes and early cleanup asserted by the private-run regression. |
| Concurrent source mutation (`eoc6Y`) | Active writers are refused before parsing; later replay reads the private spool. Open-writer, parallel-reader and post-preparation source replacement cases. |
| Incomplete persisted envelope (`eoc6u`) | Required fields, schema/type and typed payload compatibility validated before filtering/spooling. Missing-field, invalid-envelope, empty-object and JSON-null cases retain physical positions. |
| Concurrent partial tail (`otmVc`) | Complete and partial active captures report unavailable, not malformed. `ReadEventsAsync_OpenCaptureWriter_RefusesUntilWriterCloses`; preview unavailable-response case. |

New composition regressions cover either member of an equal plain/gzip pair being written, sidecar
exclusion, exactly one replayed logical event after closure, and parallel explicit plain/gzip aliases.
The preview test host now registers the same `ReplayScanLifetime` singleton and hosted-service alias
as production; its shutdown regression proves cancellation and cleanup draining.

### Local validation and remaining execution

- Quant Lab component/view-model suites: 34 passed, zero failures.
- Chromium fixture capture at 1440×1100 passed. A 1440×900 interaction run verified that source edits
  immediately disable Run, failed metadata keeps it disabled, and recovery enables it only after a
  successful scan for the current editor text. No browser runtime errors were observed.
- Whitespace and merge-marker checks passed. Relevant module hashes and maintained documentation
  are refreshed from the composed source.
- No .NET build or test execution is claimed for this local refresh. Root integration owns the single
  canonical `bash scripts/ci.sh` run and fresh hosted validation of all combined changes. Focused
  backend coverage includes `JsonlReplayerTests`, `ArchiveCompressionCrashTests`,
  `ReplayPreviewEndpointsTests`, `MultiSymbolMergeEnumeratorTests`, `BacktestEngineIntegrationTests`,
  `RoslynScriptCompilerTests`, and `QuantLabParameterExtractionEndpointTests`.

## Required fresh hosted gates

Every check below must succeed on the exact re-cut head after the final code and documentation
change. A result from an earlier #2789 head or a superseded re-cut SHA is not reusable.

| Workflow | Required jobs/checks |
| --- | --- |
| Meridian CI | `verify-dotnet`, `verify-browser`, `verify-docs`, `verify-workflows`, `quality-gate`, and `integration-gate` |
| Windows Desktop Build | `verify-desktop (build/test WPF)` |
| Windows Desktop Build consolidated slices | dev-loop, position-blotter route, and operator-inbox route in the same `verify-desktop` run |
| CodeQL | `Analyze csharp` and `Analyze javascript-typescript` |
| Documentation Automation | `validate-docs` and `regenerate-docs` |
| Roadmap Source Docs | `scope-gate` and `schema` |

The pull request must link each hosted run and record the tested head SHA. Skipped, cancelled,
neutral, or pending checks do not count as fresh green evidence.

## Merge readiness

Source review is clear after the bounded corrections above. Merge remains dependent on the final
composed head passing the required local and hosted checks and any backend-enforced review rules.
The work remains PRD-106 hardening; the bounded `W6-BTSTUDIO-001` completion claim is unchanged.
Production operator evidence, broader PRD-106 acceptance and release certification remain open.

### Reviewed source identities

These Git blob identities let the final integration reviewer detect a later source change without
mistaking a documentation-only descendant for new code validation.

| Source | Git blob |
| --- | --- |
| `src/Meridian.Backtesting/Engine/BacktestEngine.cs` | `3828ced6f7444f716e28d02a526d3c2f52b9ab4d` |
| `src/Meridian.Backtesting/Engine/MultiSymbolMergeEnumerator.cs` | `98e9996018b963780763bc227527a62938477111` |
| `src/Meridian.Storage/Replay/JsonlReplayer.cs` | `b31119b3b379c73dc582da9e40e6ed6356f68364` |
| `src/Meridian.Storage/Replay/JsonlArchiveFiles.cs` | `3d16ad695f95620bad8e0fd77608966a97abf84d` |
| `src/Meridian.QuantScript/Compilation/IQuantScriptCompiler.cs` | `aa0b1b7674b30ee15ee1d32c61e4ac9bbe91ca34` |
| `src/Meridian.QuantScript/Compilation/RoslynScriptCompiler.cs` | `e836961679dd44d512a978dd8ed7e1ad681abc20` |
| `src/Meridian.Ui.Shared/Endpoints/QuantLabEndpoints.cs` | `d163a642c7d86d1fd84141f04323b0e066129f22` |
| `src/Meridian.Ui.Shared/Endpoints/ReplayEndpoints.cs` | `4023fb3e3a67e78d295aaf16b9495781bcdd2185` |
| `src/Meridian.Ui/dashboard/src/screens/quant-lab-screen.tsx` | `21275f7b47da3cf4bbedc1cf56c0072b4b84c67d` |
| `src/Meridian.Ui/dashboard/src/screens/quant-lab-screen.view-model.ts` | `fde5f0e414cf433c66cc2e2b0cb4de5085ff7d0f` |
