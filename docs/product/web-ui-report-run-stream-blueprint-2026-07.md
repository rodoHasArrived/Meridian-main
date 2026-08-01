<!-- phase:PR7 -->
# Blueprint — Report-run status stream (stream fan-out PR D, scoped to `report-run:<id>`)

**Status:** Implemented — D1–D3 shipped (§9)
**Owner:** Workstation Shell and UX
**Reviewed:** 2026-08-01

**Summary:** Delivered design. `Meridian.Contracts/Workstation/IReportingRunNotifier.cs`,
`src/Meridian.Ui.Shared/Streaming/StreamBroadcaster.cs` + `ReportRunStreamBroadcaster.cs` +
`StreamSubscription.cs`, `UiApiRoutes.ReportingRunStream`, and
`src/Meridian.Ui/dashboard/src/lib/report-run-stream.ts` are in source. Read this as the design
record for shipped behavior, not as pending work.
**Extends:** [`web-ui-stream-fan-out-blueprint-2026-07.md`](web-ui-stream-fan-out-blueprint-2026-07.md) (§9 "PR D — Additional topics"), and supersedes its `workspace` / `inbox` topic proposal (§1 below).
**Registered in:** [`docs/engineering/blueprints/README.md`](../engineering/blueprints/README.md).
**Scope owner surfaces:** `src/Meridian.Reporting` (notify seam), `src/Meridian.Ui.Shared` (broadcaster generalization + SSE endpoint), `src/Meridian.Ui/dashboard` (stream client + reporting screen).

> **Post-delivery drift.** `StreamTopic` shipped with a tenant/fund-scoped `ReportRun` overload and
> a `TryParseScopedReportRun` helper beyond the single-argument `ReportRun(runId)` sketched in §5.2.
> The seams, wire format, status codes, and phasing are as designed.

This blueprint is a code-ready design. It names the exact seams, interfaces, and files to add/modify, plus a phased checklist. It deliberately does **not** change how reporting runs execute, and does not touch the market-data storage hot path.

---

## 1. Problem and scope decision

The stream fan-out blueprint sketched **PR D** as three new topics — `workspace:<key>`, `inbox`, and `report-run:<id>` — reusing the quote fan-out. Grounding that against current source showed the three do **not** fit the quote model equally:

- The quote fan-out is economical because a quote snapshot for a symbol set is **tenant-global**: one build is identical for every subscriber, and `QuoteCollector` already raises a `NotifyQuoteChanged` change signal.
- **workspace** payloads are assembled per request from `HttpContext`-coupled builders (`BuildTradingPayloadAsync(HttpContext)`), are **per-session/per-fund authorized**, hold no in-memory state object, and have **no change signal**. The shared-build benefit disappears (each session needs its own authorized build) and a background loop has no request/principal to build from.
- **inbox** (`BuildOperatorInboxAsync(Guid?, HttpContext)`) is likewise per-request-merged and `HttpContext`-coupled; the only mutation seam (`IOperatorInboxService.Upsert/Remove`) covers a subset and is a non-production in-memory store.
- **report-run** is the one clean fit: run state lives in a concrete server-side registry (`ReportingOrchestrationService.manifests` + `IReportingRunStore`), a run's **status is a property of the run, not the viewer** (authorization decides *whether* you may see it, not *what* you see), and there is a single write chokepoint to hang a change signal on.

**Decision (reviewer-approved): scope PR D to `report-run:<id>` only.** `workspace` and `inbox` stay on their existing client polling; the deferred "workspace poller suspension" is not pursued here because it depends on a workspace stream that does not fit the model. This blueprint covers only the report-run topic.

### Why it is worth doing

Reporting runs execute **synchronously** (`IReportingOrchestrationService.ExecuteAsync` returns the terminal manifest), so the initiating operator already gets the final status from their own `await runReportingNow(...)`. The value of a live stream is the **governed approval workflow**: `Draft → InReview → Approved → Released` transitions are separate commands, often performed by **other operators** (Reviewer, OperationsLead, ComplianceOfficer). Today those transitions surface to a watching operator only on the **30 s reporting-workspace poll** (`getReportingWorkspace` via `use-workstation-data.ts`). A `report-run:<id>` stream pushes each transition immediately to everyone authorized to watch that run — the multi-user sign-off surface Meridian treats as first-class.

## 2. Goals / non-goals

**Goals**
- Push report-run status/approval transitions to subscribers **event-driven**, woken by the single manifest-persist chokepoint, coalesced per run (latest-wins), never blocking run execution.
- Authorize **at subscribe** (where `HttpContext` exists), mirroring the existing audit endpoint's checks; build the payload on the background loop by run id alone.
- Reuse the shipped stream infrastructure — `StreamConnectionRegistry` (per-session cap), heartbeat/keep-alive scaffolding, bounded drop-oldest subscriber channels — by **generalizing** the quote-specific pieces rather than duplicating them.
- Preserve the degradation contract: any failure ⇒ consumer unhealthy ⇒ the reporting screen keeps its existing 30 s workspace refresh as the fallback.

**Non-goals**
- No change to reporting run **execution** (still synchronous), ret/attempt logic, approval rules, or `IReportingRunStore` durability (atomic file writes stay).
- No `workspace:<key>` or `inbox` stream; no workspace poller suspension.
- No new mobile surface. No WebSocket transport (SSE stays the wire format).
- No progress-bar semantics — runs are synchronous, so wake is coarse (generation, failure, each approval transition), not sub-second progress.

## 3. Current state (grounded)

| Concern | Where | Behavior today |
| --- | --- | --- |
| Run registry | `ReportingOrchestrationService` (`src/Meridian.Reporting/ReportingOrchestrationService.cs`) | `ConcurrentDictionary<string, ReportingOutputManifest> manifests` + `audits`; optional `IReportingRunStore runStore`. `GetManifest(runId)` (`:210`), `GetAudit(runId)` (`:15`). No change signal. |
| Write chokepoint | same, `PersistAsync(manifest, ct)` (`:275`) | Called on **every** manifest state change: `RunGenerated` (`:133`), `RunFailed` (`:167`), and `ApprovalTransition`/denials (`:238,:245,:252`). Wraps `runStore?.SaveAsync`. Silent — raises nothing. |
| Approval transitions | `TransitionApprovalAsync(runId, target, actor, role, notes, ct)` (`:226`) | `Draft→InReview→Approved→Released`, role-gated; writes `manifests[runId] = updated` then `PersistAsync`. |
| Status/audit read + auth | `FundStructureEndpoints.cs` `GET /reporting/runs/{runId}/audit` (`:1661`) | Auth pattern: `HasReportingReadPermission(context)` → `GetManifest(runId)` (404 if null) → `GovernedReportingTemplateCatalog.EvaluateAccess(manifest.TemplateId, BuildReportAccessQueryContext(context))` (403 if not accessible) → `ProjectReportingRunAuditTrail(manifest, GetAudit(runId))` → `ReportingRunAuditTrailDto`. Route const `UiApiRoutes.ReportingRunAuditTrail = "/api/fund-structure/reporting/runs/{runId}/audit"`. |
| Run status DTO | `Meridian.Contracts.Workstation` | `WorkstationReportingRunPayload` (full run projection); `ReportingRunAuditTrailDto` (manifest status + audit trail, returned by the audit endpoint); `ReportingStatusProjection` (`ReportingStatusProjectionService.cs:18`) — light `RunId, TemplateId, Family, Status, AttemptCount`. |
| Client run status | `screens/reporting-screen*.tsx` | `templateRunStatus` is single-request state (set around `await runReportingNow`), **not** a poll. Run rows (`vm.runStatusRows`) come from `data.reporting.recentRuns`, refreshed by the 30 s reporting-workspace poll. **No per-run-id poll exists.** |
| Quote fan-out (reuse target) | `src/Meridian.Ui.Shared/Streaming/` | `QuoteStreamBroadcaster` (hard-typed to `QuotesSnapshotResponse`, `QuoteRowsEqual`, snapshot `Func<IServiceProvider,string?,QuotesSnapshotResponse?>`); `StreamTopic` (quotes-only: `SymbolFilter`, `Quotes()`, `AllQuotes`); `StreamConnectionRegistry`; `QuoteStreamOptions`; SSE endpoint `WorkstationEndpoints.Stream.cs`. |

## 4. Design overview

```
 REPORT RUN LIFECYCLE (synchronous)
   ExecuteAsync / TransitionApprovalAsync
        │  writes manifests[runId], then …
        ▼
   PersistAsync(manifest, ct)   ← single chokepoint (src/Meridian.Reporting)
        │  NEW: after save, IReportingRunNotifier.NotifyRunChanged(runId)  (non-blocking, best-effort)
        ▼
 ┌──────────────────────────────┐  snapshot per run (coalesced)   SSE subscribers
 │ ReportRunStreamBroadcaster    │ ───────────────────────────────▶ (registry + per-session cap)
 │ = StreamBroadcaster<TPayload> │                                  writes `event: report-run`
 │   over topic report-run:<id>  │
 └──────────────────────────────┘
        ▲ builds by runId alone: GetManifest(runId)+GetAudit(runId) → ProjectReportingRunAuditTrail
        │ (no HttpContext needed — authorization already happened at subscribe)
   SSE endpoint authorizes at subscribe (HttpContext): HasReportingReadPermission + GetManifest(404) + EvaluateAccess(403)
```

Two seams, mirroring the quote design one-for-one with **run id** in place of **symbol**:

- **Notifier seam** (`IReportingRunNotifier`) fired from the persist chokepoint — the report-run analogue of `IQuoteUpdateNotifier`.
- **Broadcaster** subscribed to that signal, rebuilding each run's payload on the shared coalescing loop and fanning it out to authorized subscribers.

The one structural difference from quotes: **authorization**. Quote data is tenant-global so the endpoint does no per-topic auth; a run must be access-checked. The check runs at **subscribe** (HttpContext available); the background build is auth-free because the manifest is keyed by run id and its status is viewer-independent.

## 5. Server design

### 5.1 Notifier seam (`IReportingRunNotifier`)

New interface in `Meridian.Contracts.Workstation` (already referenced by `Meridian.Reporting` via `using Meridian.Contracts.Workstation`, so no new project dependency; the broadcaster in UI.Shared implements it — Reporting stays free of UI concerns):

```csharp
namespace Meridian.Contracts.Workstation;

/// Fired after a reporting run's manifest is persisted (generation, failure, or an
/// approval transition). Implementations MUST be non-blocking and exception-safe —
/// this runs on the run-execution path and must never stall or fail it.
public interface IReportingRunNotifier
{
    void NotifyRunChanged(string runId);
}

public sealed class NullReportingRunNotifier : IReportingRunNotifier
{
    public static readonly NullReportingRunNotifier Instance = new();
    private NullReportingRunNotifier() { }
    public void NotifyRunChanged(string runId) { }
}
```

`ReportingOrchestrationService` gains the notifier via a **new constructor overload**, keeping the
existing 4-parameter ctor as a delegating overload. Adding an *optional* parameter to the existing
ctor would be source-compatible but **not binary-compatible** — the 4-parameter signature would
disappear from metadata, so any already-compiled caller would hit `MissingMethodException` at
runtime. Constructor chaining preserves the original signature (and matches the ctor-chaining the
type already uses for its 1-parameter convenience ctor); the notifier defaults to the null-object so
existing construction and tests are unaffected:

```csharp
// Existing 4-parameter ctor retained for binary compatibility — now delegates.
public ReportingOrchestrationService(
    IReportingTemplateCatalog catalog,
    IReportingSectionRenderer renderer,
    Func<DateTimeOffset> utcNow,
    IReportingRunStore? runStore = null)
    : this(catalog, renderer, utcNow, runStore, runNotifier: null)
{
}

// New 5-parameter ctor carries the optional notifier (default null-object).
public ReportingOrchestrationService(
    IReportingTemplateCatalog catalog,
    IReportingSectionRenderer renderer,
    Func<DateTimeOffset> utcNow,
    IReportingRunStore? runStore,
    IReportingRunNotifier? runNotifier)
{
    // ... existing field assignments ...
    this.runNotifier = runNotifier ?? NullReportingRunNotifier.Instance;
}

// PersistAsync is the single chokepoint for every manifest state change:
private async Task PersistAsync(ReportingOutputManifest manifest, CancellationToken cancellationToken)
{
    if (runStore is not null)
    {
        await runStore.SaveAsync(manifest, GetAudit(manifest.RunId), cancellationToken).ConfigureAwait(false);
    }

    // Best-effort wake, AFTER the durable write. A buggy/throwing notifier must never
    // surface on the run-execution path (belt-and-suspenders with the impl's own guard).
    try
    {
        runNotifier.NotifyRunChanged(manifest.RunId);
    }
    catch
    {
        // Swallow — run execution must never fail on a UI-streaming concern.
    }
}
```

Firing inside `PersistAsync` covers generation, failure, and approval transitions in one place. (Denied transitions also call `PersistAsync(current)`; a notify there is harmless — the broadcaster coalesces an unchanged status to a no-op.)

### 5.2 Generalize `StreamTopic` (add a kind)

`StreamTopic` is quotes-only today. Add a topic **kind** + **argument** while keeping the shipped quote surface intact (`Quotes()`, `AllQuotes`, `AllQuotesKey`, `SymbolFilter`). Minimal, additive change:

```csharp
public readonly struct StreamTopic : IEquatable<StreamTopic>
{
    public const string AllQuotesKey = "quotes:*";

    private readonly string? _key;
    private readonly string? _argument;

    private StreamTopic(string key, string argument) { _key = key; _argument = argument; }

    public string Key => _key ?? AllQuotesKey;

    /// The builder argument: the comma-joined symbol filter for quote topics, or the
    /// run id for report-run topics. Empty for "all tracked symbols".
    public string Argument => _argument ?? string.Empty;

    /// Back-compat alias for quote consumers (unchanged call sites).
    public string SymbolFilter => Argument;

    public static StreamTopic AllQuotes { get; } = new(AllQuotesKey, string.Empty);

    public static StreamTopic Quotes(string? symbolFilter) { /* unchanged */ }

    /// Report-run topic: keyed by the exact run id (trimmed). One run == one topic.
    public static StreamTopic ReportRun(string runId)
    {
        var trimmed = (runId ?? string.Empty).Trim();
        return new StreamTopic($"report-run:{trimmed}", trimmed);
    }

    // Equals/GetHashCode/operators unchanged — all keyed off Key.
}
```

Run ids are opaque identifiers (no case/order canonicalization like symbols), so `ReportRun` just trims. Equality/hashing stay `Key`-based, so nothing else in the struct changes.

### 5.3 Broadcaster: generic `StreamBroadcaster<TPayload>` (decided — Q1)

The quote broadcaster's machinery — `topic → Set<Subscriber>`, the wake channel, the coalescing loop, bounded drop-oldest subscriber channels, registry reserve/release, idempotent subscription dispose — is **payload-agnostic**. Only three things are quote-typed: the payload (`QuotesSnapshotResponse`), the build delegate, and the coalesce-equality (`QuoteRowsEqual`).

**Decision (Q1): extract a generic `StreamBroadcaster<TPayload>`** in `src/Meridian.Ui.Shared/Streaming/`, parameterized by a build delegate and an equality delegate, and refactor `QuoteStreamBroadcaster` to be a thin configuration of it. The report-run broadcaster is then a second configuration. The extraction lands as a **behavior-preserving commit gated by the unchanged quote suite**, kept separate from the report-run wiring so "did I break quotes" is isolated from "does report-run work" (§9 D1c/D1d).

```csharp
public sealed class StreamBroadcaster<TPayload> : IAsyncDisposable where TPayload : class
{
    public StreamBroadcaster(
        StreamConnectionRegistry registry,
        StreamBroadcasterOptions options,
        Func<StreamTopic, TPayload?> build,          // topic → payload (closes over IServiceProvider)
        IEqualityComparer<TPayload> coalesceComparer) // skip-push when unchanged
    { /* the exact loop/subscriber/registry code from QuoteStreamBroadcaster, but over TPayload */ }

    public StreamSubscription<TPayload>? TrySubscribe(StreamTopic topic, string sessionId);
    public void Wake(StreamTopic topic);              // or Wake(all) — mirrors NotifyQuoteChanged
    internal void PublishPending();                   // test seam (unchanged shape)
}
```

- `QuoteStreamBroadcaster` becomes: `IQuoteStreamBroadcaster` façade wrapping `StreamBroadcaster<QuotesSnapshotResponse>` with `build = topic => LiveDataEndpoints.TryBuildQuotesSnapshotResponse(services, topic.SymbolFilter)` and `coalesceComparer` = a `QuoteRowsEqual`-backed comparer. `NotifyQuoteChanged` → `Wake(all)` (unchanged external behavior).
- `ReportRunStreamBroadcaster` becomes: `IReportingRunNotifier` + a `TrySubscribe` façade wrapping `StreamBroadcaster<ReportingRunAuditTrailDto>` with `build = topic => BuildReportRunPayload(services, topic.Argument)` and `coalesceComparer` = record/status equality. `NotifyRunChanged(runId)` → `Wake(StreamTopic.ReportRun(runId))`.

Two semantics differ between the two configurations, and the generic must parameterize both:

- **Wake granularity.** Quotes wake **all** topics (any symbol can change any topic); report-run wakes a **single** topic (`Wake(StreamTopic.ReportRun(runId))`). The generic exposes both `Wake(topic)` and `WakeAll()`; the quote façade keeps wake-all, report-run uses targeted wake — a strictly cheaper rebuild set.
- **Empty-topic lifecycle.** Quote topics are a *bounded* universe (canonical symbol sets) and are deliberately **retained** when empty (the shipped skip-build-when-empty optimization, which also avoids a subscribe/remove race). Report-run topics are keyed by run id — an **unbounded, ever-growing** set — so they **must be evicted on last unsubscribe**, or `TopicState` leaks one entry per run id forever. The generic takes an `EvictEmptyTopics` flag (quotes `false`, report-run `true`); eviction removes the topic under the same atomic claim used for subscriber dispose, and a later subscribe re-creates it. This is the one genuinely new piece of concurrency code and carries its own tests regardless (§8), so it is the risk to watch in D1c/D1d — not the mechanical element-type generalization.

*Risk note:* the extraction touches the shipped quote hot path. Mitigation: `QuoteStreamBroadcasterTests` + `WorkstationStreamEndpointTests` are the regression gate and must pass **unchanged**; the quote refactor is a separate commit from the report-run wiring. A standalone parallel `ReportRunStreamBroadcaster` was considered and **rejected** — it would duplicate ~250 lines of concurrency-sensitive machinery (two copies that drift), a worse long-term hazard than a test-gated extraction.

### 5.4 Report-run payload builder (auth-free, by id)

A pure `(IServiceProvider, string runId) → ReportingRunAuditTrailDto?` builder, reusing the existing projection so the stream payload matches the audit endpoint's shape the client already understands:

```csharp
// New internal static in FundStructureEndpoints (or a small ReportRunReadModel helper):
internal static ReportingRunAuditTrailDto? TryBuildReportRunAuditTrail(IServiceProvider services, string runId)
{
    var orchestration = services.GetService<IReportingOrchestrationService>();
    var manifest = orchestration?.GetManifest(runId?.Trim() ?? string.Empty);
    return manifest is null
        ? null
        : ProjectReportingRunAuditTrail(manifest, orchestration!.GetAudit(manifest.RunId));
}
```

No access context is needed here: the manifest is keyed by run id and its status is viewer-independent. **Authorization is enforced at subscribe (§5.5), not in the build.** Coalesce equality compares the projected DTO (status + audit-entry count/last-entry) so unchanged runs produce no push.

### 5.5 SSE endpoint (authorize at subscribe, then subscribe)

New SSE route, keyed by run id path segment, co-located with the other reporting-run routes:

- Route const: `UiApiRoutes.ReportingRunStream = "/api/fund-structure/reporting/runs/{runId}/stream"`.
- Handler (in `FundStructureEndpoints.cs`, reusing the audit endpoint's auth verbatim):

```
1. HasReportingReadPermission(context)                         → 403 if not.
2. orchestration = GetService<IReportingOrchestrationService>()→ 503 if null.
3. manifest = orchestration.GetManifest(runId.Trim())          → 404 if null.
4. GovernedReportingTemplateCatalog.EvaluateAccess(manifest.TemplateId, BuildReportAccessQueryContext(context))
                                                               → 403 if !IsAccessible.
5. broadcaster = GetService<ReportRunStreamBroadcaster>()       → 503 if null.
6. sub = broadcaster.TrySubscribe(StreamTopic.ReportRun(runId), ResolveStreamSessionId(context))
                                                               → 429 + Retry-After if null (per-session cap).
7. content-type text/event-stream; loop: await next payload with a heartbeat-interval
   timeout (single-writer, exactly as WorkstationEndpoints.Stream.cs after the C fixes:
   linked CTS + CancelAfter → catch OperationCanceledException → write `: heartbeat`);
   write `event: report-run\ndata: {json}\n\n` on each payload; flush.
8. finally: await sub.DisposeAsync() (releases the registry reservation).
```

The heartbeat/keep-alive/cancellation scaffolding is copied from the post-C quote endpoint (single-writer `CancelAfter` pattern, `.Produces(200/403/404/429/503)`). `ResolveStreamSessionId` and `StreamConnectionRegistry` are reused as-is.

### 5.6 DI wiring

In UI.Shared composition (`WorkstationServiceCollectionExtensions.cs`, beside the quote registrations):

```csharp
services.TryAddSingleton(sp => new ReportRunStreamBroadcaster(
    sp,
    sp.GetRequiredService<StreamConnectionRegistry>(),      // reuse the shared per-session cap
    sp.GetRequiredService<QuoteStreamOptions>()));           // reuse coalesce/cap/capacity options
services.TryAddSingleton<IReportingRunNotifier>(sp => sp.GetRequiredService<ReportRunStreamBroadcaster>());
```

`ReportingOrchestrationService` resolves `IReportingRunNotifier` where it is constructed (Reporting feature registration): pass `sp.GetService<IReportingRunNotifier>()` (defaulting to `NullReportingRunNotifier` in headless/tests), mirroring how `CollectorFeatureRegistration` resolves `IQuoteUpdateNotifier`. Reporting keeps no compile dependency on UI.Shared — the interface lives in Contracts.

*Options note:* report-run wakes are low-frequency (synchronous runs + human approval cadence), so `QuoteStreamOptions.CoalesceIntervalMs` is fine as-is; if the reviewer wants a distinct cadence, introduce a `ReportRunStreamOptions` (see §11 Q3). The per-session cap is shared with quotes by default (a run stream counts against the same ceiling); a separate cap dimension is a possible refinement.

## 6. Client design

Report-run streaming is **additive** — there is no existing per-run poll to suspend. The stream pushes live status to the reporting screen faster than the 30 s workspace refresh; on any failure the screen keeps that refresh as the fallback. Companion-pane owner/follower is **not** needed (the reporting screen is not a pop-out pane), so the client is a straightforward refcounted `EventSource` per run id.

### 6.1 `lib/report-run-stream.ts`

Mirror `quotes-stream.ts` but keyed by run id and without the companion owner/follower machinery:

```ts
export interface ReportRunStreamHandlers {
  onStatus: (payload: ReportingRunAuditTrailDto) => void;
  onHealthChange?: (healthy: boolean) => void;
}
export function buildReportRunStreamUrl(runId: string): string;      // /api/fund-structure/reporting/runs/{runId}/stream
export function subscribeReportRunStream(runId: string, handlers: ReportRunStreamHandlers): () => void;
```

One `EventSource` per run id, refcounted with the same `CLOSE_LINGER_MS` grace; `event: report-run` → parsed `ReportingRunAuditTrailDto`; `error` ⇒ unhealthy. (Adopt the post-C owner failure policy — fail-close detection + bounded reopen probes — if the shared helpers are extractable; otherwise the basic error→unhealthy path is sufficient since the workspace poll remains the fallback.)

### 6.2 `hooks/use-report-run-stream.ts`

```ts
export interface ReportRunStreamState { status: ReportingRunAuditTrailDto | null; healthy: boolean; }
export function useReportRunStream(runId: string | null): ReportRunStreamState;
```

Empty/`null` run id keeps the stream closed and `healthy=false`.

### 6.3 Reporting screen adoption

- Subscribe to `useReportRunStream(activeRunId)` where `activeRunId` is the run currently being watched (the just-executed `templateRunStatus.id`, or a run the operator has focused in the run-status list).
- On pushed status, update `templateRunStatus` and/or the matching `vm.runStatusRows` entry for that run — so approval transitions by other operators appear immediately.
- Render a `FreshnessChip` in `live` state on the watched run row while `healthy` (consistent with the quote-freshness treatment shipped in Phase 1); drop to the normal freshness/age treatment when the stream degrades.
- **No poll is suspended.** The 30 s reporting-workspace refresh continues to own the full run list and is the fallback when the stream is unhealthy.

## 7. Contracts summary

- **New wire:** `GET /api/fund-structure/reporting/runs/{runId}/stream` → `text/event-stream`, `event: report-run` frames of `ReportingRunAuditTrailDto`, `: heartbeat` comments. Status codes: 200, 403 (no read permission / not accessible), 404 (unknown run), 429 (+`Retry-After`, per-session cap), 503 (orchestration or broadcaster unavailable).
- **New server types:** `IReportingRunNotifier` + `NullReportingRunNotifier` (Contracts); `StreamBroadcaster<TPayload>` + `StreamBroadcasterOptions` + `StreamSubscription<TPayload>` (generic core); `ReportRunStreamBroadcaster` (UI.Shared); `StreamTopic.ReportRun` + `Argument`; `TryBuildReportRunAuditTrail`. `ReportingOrchestrationService` ctor gains an optional notifier param.
- **New client contract:** `report-run-stream.ts` + `use-report-run-stream.ts`; reporting-screen consumes `{status, healthy}`. No DTO change on the client — `ReportingRunAuditTrailDto` already exists.
- **Unchanged:** quote wire format/route; run execution; approval rules; `IReportingRunStore` durability.

## 8. Test plan

**Server (xUnit, `tests/Meridian.Tests/`)**
- `ReportingOrchestrationServiceTests` (extend): `NotifyRunChanged` fires on generation, failure, and each approval transition (recording notifier); a **throwing** notifier does not surface on `ExecuteAsync`/`TransitionApprovalAsync` and the run still persists (hot-path guard — the report-run analogue of the quote throwing-notifier test).
- `StreamBroadcasterTests` (generic core): targeted wake rebuilds only the named topic; two subscribers on one topic share one build; rapid wakes coalesce (capacity-1 drop-oldest); unchanged payload produces no push; dispose removes the subscriber and releases the registry slot; `DisposeAsync` idempotent. (The existing `QuoteStreamBroadcasterTests` must still pass against the refactored quote façade — the regression gate for §5.3.)
- `ReportRunStreamBroadcasterTests`: subscribe seeds the current run payload; `NotifyRunChanged(runId)` pushes the rebuilt payload; a changed status (e.g. `InReview → Approved`) pushes, an unchanged one coalesces.
- `ReportingRunStreamEndpointTests` (TestServer): 200 + `text/event-stream` + `report-run` frame + heartbeat; 403 without read permission and when `EvaluateAccess` denies; 404 for an unknown run; 429 + `Retry-After` past the per-session cap; 503 when the broadcaster/orchestration is absent. Reuse the reporting-run TestServer harness + the stream harness from `WorkstationStreamEndpointTests`.

**Client (vitest, `src/Meridian.Ui/dashboard/src/`)**
- `report-run-stream.test.ts`: builds the run-scoped URL; shares one `EventSource` per run id with refcount + linger close; dispatches parsed status; `error` ⇒ unhealthy. (Stubbed `EventSource`, patterns from `quotes-stream.test.ts`.)
- `use-report-run-stream.test.ts`: null run id stays closed/unhealthy; applies pushed status; flips unhealthy on error.
- Reporting-screen view-model test: a pushed `Approved` status updates the watched run row without a workspace refresh; unhealthy leaves the row on its polled value.

**Validation commands**
```bash
dotnet test tests/Meridian.Tests -c Release /p:EnableWindowsTargeting=true \
  --filter "FullyQualifiedName~ReportRunStream|FullyQualifiedName~StreamBroadcaster|FullyQualifiedName~ReportingOrchestrationService|FullyQualifiedName~QuoteStreamBroadcaster|FullyQualifiedName~WorkstationStream"
npm --prefix src/Meridian.Ui/dashboard run test
npm --prefix src/Meridian.Ui/dashboard run build
```

## 9. Phasing (each a separate PR, hot-path review gated)

All three phases have shipped. The checklist is retained as the delivery record.

1. **PR D1 — Notifier seam + topic generalization + generic broadcaster (shadow).** ✅ Shipped. Landed as isolated, individually CI-verifiable commits so hot-path review is scoped:
   - **D1a — Notifier seam.** `IReportingRunNotifier`/`NullReportingRunNotifier` (Contracts); `ReportingOrchestrationService` gains the binary-compatible ctor overload (§5.1) firing `NotifyRunChanged` in `PersistAsync`; DI resolves it (null-object default). Tests: fires on generation/failure/each approval transition; a throwing notifier never surfaces and the run still persists. **Requires hot-path review** of the call site.
   - **D1b — `StreamTopic` generalization.** Add `Argument` + `StreamTopic.ReportRun`, keeping the quote surface (`Quotes`/`AllQuotes`/`SymbolFilter`). Unit tests.
   - **D1c — Generic broadcaster extraction.** Extract `StreamBroadcaster<TPayload>` (with `WakeAll()`/`Wake(topic)` and `EvictEmptyTopics`) and refactor `QuoteStreamBroadcaster` onto it — behavior-preserving; `QuoteStreamBroadcasterTests`/`WorkstationStreamEndpointTests` pass **unchanged** as the gate.
   - **D1d — Report-run broadcaster (shadow).** `ReportRunStreamBroadcaster` (`EvictEmptyTopics=true`, targeted wake) + payload builder + DI, wired to `IReportingRunNotifier`, but **not yet exposed by an endpoint**. Broadcaster unit tests incl. topic eviction.
2. **PR D2 — SSE endpoint + auth + registry/caps.** ✅ Shipped. Extract the shared SSE loop/heartbeat/`ResolveStreamSessionId` helper out of `WorkstationEndpoints.Stream.cs`; add `GET /api/fund-structure/reporting/runs/{runId}/stream` with subscribe-time authorization (403/404), 429/503, heartbeat. Endpoint tests. Flip the feature on.
3. **PR D3 — Client stream + reporting-screen adoption.** ✅ Shipped. `report-run-stream.ts`, `use-report-run-stream.ts`, watched-run live status + `FreshnessChip`. Client tests. Additive — no poll suspended.

## 10. Risks & mitigations

| Risk | Mitigation |
| --- | --- |
| Generic refactor regresses the shipped quote path | Mechanical extraction (same loop, generic element); `QuoteStreamBroadcasterTests` + `WorkstationStreamEndpointTests` must pass unchanged as the gate; the quote refactor is a separate commit from report-run wiring. Parallel-class alternative considered and rejected (§5.3). |
| Notifier stalls or fails run execution | Non-blocking wake fired **after** the durable `SaveAsync`, wrapped in a call-site try/catch + a null-object default; explicit throwing-notifier test. No `await` added to the execution path beyond the existing save. |
| Authorizing on a background loop (no HttpContext) | Authorization is done at **subscribe** (HttpContext present), reusing the audit endpoint's exact checks; the loop builds by run id only, and run status is viewer-independent. A subscriber that loses access mid-stream is bounded by the connection lifetime (acceptable; runs are short-lived governance objects). |
| Low value if runs are only ever self-initiated | The value is **cross-operator approval transitions**, which are exactly the multi-user case the 30 s poll serves poorly; single-operator self-runs already get terminal status synchronously and simply see it via the stream too. |
| Per-session cap shared with quotes rejects a legitimate run stream | Default cap is generous; 429 + `Retry-After` is explicit and the screen falls back to the 30 s poll, never hard-fails. A separate cap dimension is a deferred refinement (§11 item 3). |
| Durability guardrail drift | `IReportingRunStore` atomic-write path is untouched; the notify fires only after `SaveAsync` completes; no WAL/atomic-write code is modified. |

## 11. Resolved decisions (2026-07-06)

1. **Broadcaster generalization → generic `StreamBroadcaster<TPayload>`.** Extract the generic core and refactor the shipped `QuoteStreamBroadcaster` onto it, as a behavior-preserving commit gated by the unchanged quote suite (§5.3, §9 D1c). Rationale: keep the concurrency-sensitive machinery in one audited place; the divergences (wake granularity, empty-topic eviction) are cleanly parameterized. The standalone parallel-class alternative was rejected — it duplicates ~250 lines of concurrency code that would drift.
2. **Route placement → `/api/fund-structure/reporting/runs/{runId}/stream`.** The stream is another view of the same resource as `GET /runs/{runId}/audit`; co-locating in `FundStructureEndpoints` reuses the exact auth (`HasReportingReadPermission`, `EvaluateAccess`, `BuildReportAccessQueryContext`) in-place. The SSE loop/heartbeat/`ResolveStreamSessionId` helper is extracted from `WorkstationEndpoints.Stream.cs` into a shared internal helper so both endpoints share it (§9 D2).
3. **Options + cap → reuse `QuoteStreamOptions` + the shared `StreamConnectionRegistry` cap.** The 250 ms coalesce is harmless at report-run's low frequency; the shared registry keeps the cap a stronger total-SSE-per-session guard; report-run is low-cardinality and a cap breach degrades gracefully to the 30 s poll. A separate `ReportRunStreamOptions`/cap dimension is deferred (YAGNI) until telemetry shows contention. (The `QuoteStreamOptions` name/section becomes slightly generic once it serves multiple stream types — an optional rename to `StreamOptions`, not pursued now to avoid churn.)
4. **Payload shape → full `ReportingRunAuditTrailDto`.** The audit trail *is* the value (approval transitions carry actor + notes); it reuses the pure `ProjectReportingRunAuditTrail` and an existing client type, and per-run trails are small. The light `ReportingStatusProjection` was rejected — it drops the transition detail the stream exists to surface.
