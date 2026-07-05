<!-- phase:PR7 -->
# Blueprint — Quote-stream fan-out (Phase 4 Step 2 + companion-pane stream sharing)

**Status:** Proposed
**Owner:** Workstation Shell and UX
**Reviewed:** 2026-07-05

**Summary:** Design for review — no implementation in this PR.
**Extends:** `docs/product/web-ui-improvements-implementation-plan-2026-07.md` (Phase 4 "Step 2" and Phase 6d follow-up).
**Scope owner surfaces:** `src/Meridian.Ui.Shared` (server SSE), `src/Meridian.Domain/Collectors` (notifier seam), `src/Meridian.Ui/dashboard` (client stream + companion bridge).

This blueprint is a code-ready design. It names the exact seams, interfaces, and files to add/modify, and a phased checklist. It deliberately does **not** change the storage hot path.

---

## 1. Problem

Two independent "one stream per need" gaps make quote streaming cost more than it should:

1. **Server (Phase 4 Step 2):** the SSE endpoint `GET /api/workstation/stream`
   (`src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.Stream.cs`) is a **poll-bridge** — each
   connection runs its own `while (!ct.IsCancellationRequested)` loop that rebuilds a snapshot from
   `QuoteCollector.Snapshot()` every `StreamPollIntervalMs = 2000`. N browser connections ⇒ N
   independent 2s poll loops over the same in-memory state. There is **no** consumer-facing
   subscribe/topic surface on `EventPipeline` or `QuoteCollector`, **no** connection registry, and
   **no** per-session stream cap (only a 50-symbol-per-connection guard).

2. **Client (Phase 6d):** each browser window opens its **own** `EventSource`
   (`src/Meridian.Ui/dashboard/src/lib/quotes-stream.ts`, refcounted per symbol-set *within a
   window*). A pop-out companion pane (`/panes/*`) showing the same symbols opens a **duplicate**
   server connection — the refcount map is per-document and does not cross windows.

The two halves compose: server fan-out turns N poll loops into one event-woken push per topic;
client fan-out turns N windows into one owner `EventSource` re-broadcast to follower panes over the
existing same-origin `BroadcastChannel`.

## 2. Goals / non-goals

**Goals**
- Replace the per-connection 2s poll with an **event-driven** push woken by quote updates, coalesced
  per topic (latest wins), never blocking or backpressuring the storage hot path.
- Introduce a **topic** model (`quotes:<symbol-set>`, and an "all tracked" topic) as the unit of
  subscription and fan-out.
- Add a **connection registry** with a **per-session concurrent-stream cap**, closing streams on
  session expiry.
- Let companion panes **follow** the opener's stream over the companion `BroadcastChannel` instead of
  opening their own `EventSource`, with a safe fallback when the opener is gone.
- Preserve the existing degradation contract: any failure ⇒ `healthy=false` ⇒ consumers resume their
  polling fallback. Fallback stays first-class.

**Non-goals**
- No changes to `EventPipeline`'s storage path, channel policy, or durability guardrails. The
  storage sink flow (`Channel<TracedMarketEvent>` → consumer → `IStorageSink.AppendAsync`) is
  untouched.
- No WebSocket transport (SSE stays the wire format; provider WebSockets are upstream and unrelated).
- No new mobile surface, no cross-*origin* messaging.
- Workspace/report/inbox topics beyond quotes are sketched but out of scope for the first PR (see
  §9 Phasing) — the topic seam is designed to accept them.

## 3. Current state (grounded)

| Concern | Where | Behavior today |
| --- | --- | --- |
| SSE quote endpoint | `WorkstationEndpoints.Stream.cs` → `MapStreamEndpoints` (route `/api/workstation/stream`, const `UiApiRoutes.WorkstationStream`) | Per-connection `while` loop, `StreamPollIntervalMs=2000`, `StreamHeartbeatIntervalMs=15000`, `StreamMaxSymbols=50`; builds via `LiveDataEndpoints.TryBuildQuotesSnapshotResponse`; diffs with `QuoteRowsEqual`; cancellation via request `ct`; **no registry**. 503 when `GetService<QuoteCollector>()` is null. |
| Quote state | `QuoteCollector` (`Meridian.Domain.Collectors`, `IQuoteStateStore`) | `Snapshot()` (poll-only) + `TryGet`; `OnQuote(MarketQuoteUpdate)` → `Upsert` → `_publisher.TryPublish(MarketEvent.BboQuote(...))`. **No consumer event/observable.** Singleton (`CollectorFeatureRegistration.cs`). |
| Pipeline | `EventPipeline` (`Meridian.Application.Pipeline`) | `IMarketEventPublisher`; single bounded `Channel<TracedMarketEvent>` (via `EventPipelinePolicy.*.CreateChannel<T>()`); producer → **single storage sink**. No public reader/subscribe/topic. `DualPathEventPipeline` wraps it (`TryPublishQuote`/`TryPublishTrade`), also no consumer surface. |
| Second SSE | `StatusEndpoints.cs` `GET /api/events/stream` | Same poll→diff→`text/event-stream` shape (status, not quotes). |
| Client stream | `lib/quotes-stream.ts` (`subscribeQuotesStream`), `hooks/use-quotes-stream.ts` | One `EventSource` per normalized symbol-set **per window**, refcounted, 1000ms `CLOSE_LINGER_MS`; `quotes` event → `QuotesSnapshotResponse`; `error` ⇒ unhealthy ⇒ poll fallback. |
| Companion bridge | `lib/companion-pane/chrome-bridge.ts` | `BroadcastChannel` (`meridian.workstation.companion.v1`); `CompanionBridgeMessage = appearance \| scope \| session-expired`; validated `normalizeCompanionBridgeMessage`; `opener-broadcast.ts` lazy sender. |
| Per-session caps | — | **None.** Only per-connection 50-symbol guard + client-side per-window refcount. |

## 4. Design overview

```
 UPSTREAM PROVIDER ADAPTERS
        │ OnQuote(MarketQuoteUpdate)
        ▼
 ┌──────────────────────┐   TryPublish(MarketEvent)   ┌───────────────┐
 │   QuoteCollector      │ ─────────────────────────▶ │ EventPipeline │ ─▶ storage sink  (UNCHANGED)
 │  (IQuoteStateStore)   │                             └───────────────┘
 │                       │  NEW: after Upsert, raise
 │                       │  IQuoteUpdateNotifier.Notify(symbol)   ← cheap, non-blocking, no storage
 └──────────┬───────────┘
            │ symbol changed (coalesced signal)
            ▼
 ┌───────────────────────────────┐   snapshot per topic (coalesced)   SSE connections
 │  QuoteStreamBroadcaster        │ ─────────────────────────────────▶  (registry + per-session cap)
 │  (UI.Shared singleton)         │                                     writes `event: quotes`
 │  topic → subscriber channels   │
 └───────────────────────────────┘
                    ▲                         BroadcastChannel (same origin)
   main window EventSource ──── quotes snapshot ────▶ companion panes (followers, no EventSource)
```

Two seams, each independently shippable and testable:

- **Server:** `IQuoteUpdateNotifier` (Domain) + `QuoteStreamBroadcaster` (UI.Shared) + a
  `StreamConnectionRegistry` with per-session caps. The SSE endpoint subscribes to a topic channel
  instead of polling.
- **Client:** a `quotes` message on the companion bridge + owner/follower roles in the stream client,
  so panes consume the opener's feed.

## 5. Server design

### 5.1 Notifier seam (Domain) — decouple "quote changed" from storage

Add a minimal, allocation-free signal that `QuoteCollector` raises **after** a successful `Upsert`,
independent of the storage publish. This is the "opt-in broadcast seam" the plan calls for, kept off
the hot path (it does not touch the `Channel<TracedMarketEvent>`).

`src/Meridian.Domain/Collectors/IQuoteUpdateNotifier.cs` (new):

```csharp
namespace Meridian.Domain.Collectors;

/// Fired after a quote's in-memory state changes. Implementations MUST be non-blocking and
/// exception-safe — this runs on the ingestion path and must never stall it.
public interface IQuoteUpdateNotifier
{
    void NotifyQuoteChanged(string symbol);
}
```

`QuoteCollector` gains an **optional** notifier (default no-op, so existing construction and tests are
unaffected):

```csharp
// QuoteCollector.cs — constructor gains an optional param (backward compatible)
public QuoteCollector(IMarketEventPublisher publisher, IQuoteUpdateNotifier? updates = null)

// inside Upsert(...), after sequence assignment and _publisher.TryPublish(...):
// The try/catch lives at the CALL SITE (not only inside the impl) so that even a
// buggy/throwing notifier implementation can never surface on the ingestion path.
try { _updates?.NotifyQuoteChanged(symbol); }
catch { /* swallow — ingestion must never fail on a UI-streaming concern */ }
```

Rationale: the notifier lives in Domain next to the collector, but its only production implementation
lives in the UI layer (the broadcaster). Domain stays free of streaming concerns; the default is a
shared `NullQuoteUpdateNotifier`. The call-site guard is what the §8 "throwing notifier" test pins —
belt-and-suspenders with the impl's own internal guard.

### 5.2 `QuoteStreamBroadcaster` (UI.Shared singleton) — topic fan-out

`src/Meridian.Ui.Shared/Streaming/QuoteStreamBroadcaster.cs` (new). Implements
`IQuoteUpdateNotifier`. Responsibilities:

- Maintain `topic → Set<Subscriber>` where a **topic** is the normalized, sorted symbol-set key
  (reusing the exact normalization in `NormalizeStreamSymbols`), plus the sentinel `*` topic for
  "all tracked symbols".
- On `NotifyQuoteChanged(symbol)`: mark every topic containing `symbol` (and `*`) **dirty**; wake a
  single shared coalescing loop. Do **not** build snapshots on the ingestion thread.
- A single background coalescer (one `Channel<Unit>`-woken loop, or a short timer floor of
  ~250ms — configurable `QuoteStreamOptions.CoalesceIntervalMs`) rebuilds each dirty topic's snapshot
  via the existing `LiveDataEndpoints.TryBuildQuotesSnapshotResponse`, diffs with `QuoteRowsEqual`,
  and pushes the snapshot into each subscriber's bounded channel.
- Each **subscriber** owns a `Channel<QuotesSnapshotResponse>` created with
  `BoundedChannelOptions(capacity: 1, FullMode = DropOldest)` — latest-snapshot-wins, never blocks the
  broadcaster. (Precedent: `CoveredCallBacktestService` uses bounded channels in this layer.)

Public surface:

```csharp
public interface IQuoteStreamBroadcaster : IQuoteUpdateNotifier
{
    // Returns null when the per-session cap is exceeded (endpoint maps null → 429).
    QuoteStreamSubscription? TrySubscribe(StreamTopic topic, string sessionId, CancellationToken ct);
}

public sealed class QuoteStreamSubscription : IAsyncDisposable
{
    public ChannelReader<QuotesSnapshotResponse> Reader { get; }
    // Disposal removes the subscriber from its topic and the registry.
}
```

`StreamTopic` is a small value type: `StreamTopic.Quotes(IReadOnlyList<string> symbols)` (normalized)
or `StreamTopic.AllQuotes`. Designed to grow (`StreamTopic.Workspace(key)`, `StreamTopic.Inbox`) —
the broadcaster keys everything by `topic.Key` string.

### 5.3 Connection registry + per-session cap

`src/Meridian.Ui.Shared/Streaming/StreamConnectionRegistry.cs` (new, singleton):

- `bool TryReserve(string sessionId)` / `void Release(string sessionId)` — **thread-safely**
  increments/decrements a per-session counter (the registry is a singleton hit concurrently by many
  request threads: use `ConcurrentDictionary<string, int>` with atomic `AddOrUpdate`, or a per-session
  lock), guarded by `QuoteStreamOptions.MaxConcurrentStreamsPerSession` (default 4). Reserve-and-check
  must be a single atomic step so two racing connections cannot both slip past the cap.
- `void CloseSession(string sessionId)` — signals all reservations for a session to cancel (wired to
  session-expiry / logout so streams close promptly).
- Keyed by the workstation session identity already available via `RequireWorkstationTenantScope`
  (the tenant/session id used by the group). Exact accessor to confirm in implementation; the
  registry takes the resolved id as a string.

The broadcaster consults the registry inside `TrySubscribe` (reserve on success, release on
disposal). Cap breach ⇒ `null` ⇒ endpoint returns **429 Too Many Requests** with a `Retry-After`.

### 5.4 SSE endpoint rewrite (subscribe, don't poll)

`WorkstationEndpoints.Stream.cs` — replace the poll loop body with a subscription drain, keeping the
existing content-type/heartbeat/cancellation scaffolding and the 503/400 guards:

```
1. 503 if QuoteStreamBroadcaster unavailable (mirrors current QuoteCollector null-check).
2. topic = StreamTopic from NormalizeStreamSymbols(symbols)  (unchanged 50-symbol/400 guard).
3. sub = broadcaster.TrySubscribe(topic, sessionId, ct);  if null → 429 + Retry-After.
4. await foreach (snapshot in sub.Reader.ReadAllAsync(ct)) → write `event: quotes\ndata: …\n\n` + flush.
   - heartbeat: `HttpResponse.Body`/`BodyWriter` is **not** safe for concurrent writes, so the
     heartbeat must NOT be a second writer racing the snapshot loop. Emit it from the **single write
     path** — either merge a heartbeat sentinel into the subscriber channel (preferred: one writer,
     no lock), or guard every write with a `SemaphoreSlim`. A naive background timer writing straight
     to `Response.Body` would throw / corrupt the stream.
   - initial frame: broadcaster seeds the subscriber's channel with the current snapshot on subscribe
     so a new client gets data immediately (parity with today's first-tick behavior).
5. finally: await sub.DisposeAsync()  (releases the registry reservation).
```

Wire-format and route are **unchanged** (`event: quotes`, same `QuotesSnapshotResponse` JSON), so
`lib/quotes-stream.ts` needs no change for the server half. `StreamPollIntervalMs` is retired for
quotes; the coalescer's floor interval replaces it as the max push cadence.

### 5.5 DI wiring

- Register `QuoteStreamBroadcaster` as a singleton implementing `IQuoteStreamBroadcaster` +
  `IQuoteUpdateNotifier`, plus `StreamConnectionRegistry` and `QuoteStreamOptions` (bound from config,
  `EnableWindowsTargeting`-safe). UI.Shared composition.
- Pass the broadcaster into `QuoteCollector` construction in
  `CollectorFeatureRegistration.cs` — resolve `IQuoteUpdateNotifier` (defaulting to
  `NullQuoteUpdateNotifier` when the UI host is absent, e.g. headless/tests), so Domain has no hard
  dependency on UI.Shared.

## 6. Client design (companion-pane stream sharing)

### 6.1 Bridge message

Extend `CompanionBridgeMessage` in `lib/companion-pane/chrome-bridge.ts`:

```ts
export type CompanionBridgeMessage =
  | { type: "appearance"; appearance: Appearance }
  | { type: "scope"; scope: AppShellOperatingScopeInput }
  | { type: "session-expired" }
  | { type: "quotes"; symbolsKey: string; snapshot: QuotesSnapshotResponse }   // NEW
  | { type: "quotes-request"; symbolsKey: string };                            // NEW (follower → owner)
```

`normalizeCompanionBridgeMessage` validates `symbolsKey: string` and a structurally-checked
`snapshot` (`timestamp: string`, `count: number`, `quotes: array`) — cross-window data stays
untrusted.

### 6.2 Owner / follower roles in the stream client

`lib/quotes-stream.ts` gains a role concept (default = today's behavior):

- **Owner** (the main workstation window): unchanged — opens the shared `EventSource`. Additionally,
  on every `onSnapshot`, re-broadcasts `{ type: "quotes", symbolsKey, snapshot }` over the companion
  channel (via `opener-broadcast.ts`). On receiving `quotes-request` for a `symbolsKey` it already
  owns, it re-sends the last snapshot immediately (fast pane warm-up).
- **Follower** (a `/panes/*` window): `subscribeQuotesStream` does **not** open an `EventSource`.
  Instead it registers channel interest keyed by `symbolsKey`, emits a one-shot `quotes-request`, and
  delivers `onSnapshot`/`onHealthChange` from inbound `quotes` messages. Health = "have I received a
  snapshot for this key within `FOLLOWER_STALE_MS`".

Role is decided by route: `isCompanionPaneRoute(location.pathname)` ⇒ follower. `use-quotes-stream.ts`
is unchanged — the role switch lives entirely inside the stream client.

### 6.3 Degradation (opener gone)

The owner broadcasts an explicit disconnect on `pagehide` (this is exactly the reliable unload event
6d already uses for `session-expired`; prefer it over `beforeunload`/`unload`, which are unreliable
and bfcache-hostile) — either a dedicated `stream-owner-gone` message or the existing
`session-expired`. So closing the main tab flips followers **immediately**, not after a timeout.
Followers also flip on the `FOLLOWER_STALE_MS` watchdog as a backstop for the case where the opener
dies without firing `pagehide` (crash, kill). On either signal the follower sets `healthy=false`, so
consumers resume their polling fallback (the same first-class fallback used everywhere). **No
owner-election in v1**: a stranded pane polls, exactly like a degraded main window. Promotion
(follower opens its own `EventSource`) is noted as a future refinement, not built.

This means the *worst case* is unchanged from today (each stranded window polls); the *common case*
(pane open beside a live main window) drops from 2 server connections to 1.

## 7. Contracts summary

- **Wire (unchanged):** `GET /api/workstation/stream?symbols=CSV` → `text/event-stream`,
  `event: quotes` frames of `QuotesSnapshotResponse`, `: heartbeat` comments. New status code **429**
  (+`Retry-After`) when the per-session cap is exceeded.
- **New server types:** `IQuoteUpdateNotifier`, `NullQuoteUpdateNotifier`, `IQuoteStreamBroadcaster`
  / `QuoteStreamBroadcaster`, `QuoteStreamSubscription`, `StreamTopic`, `StreamConnectionRegistry`,
  `QuoteStreamOptions` (`CoalesceIntervalMs`, `MaxConcurrentStreamsPerSession`,
  `SubscriberChannelCapacity`).
- **New client contract:** two `CompanionBridgeMessage` variants (`quotes`, `quotes-request`) +
  owner/follower behavior in `quotes-stream.ts`. `chrome-bridge.ts` normalizer extended. No API-route
  or DTO change on the client.

## 8. Test plan

**Server (xUnit, `tests/Meridian.Tests/Ui/`)**
- `QuoteStreamBroadcasterTests`: subscribe → `NotifyQuoteChanged` → subscriber receives a coalesced
  snapshot; two subscribers on the same topic both receive one build (fan-out, not per-subscriber
  rebuild); rapid notifies coalesce to the latest (capacity-1 drop-oldest); unchanged rows produce no
  push (`QuoteRowsEqual`); dispose removes the subscriber.
- `StreamConnectionRegistryTests`: cap enforced per session; release frees a slot; `CloseSession`
  cancels outstanding subscriptions.
- `WorkstationStreamEndpointTests` (extend existing): still emits `text/event-stream` + `quotes` +
  heartbeat via TestServer; **429 + Retry-After** past the cap; 503 when broadcaster absent; 400 past
  50 symbols; initial frame delivered on connect.
- Hot-path guard: a test asserting `QuoteCollector.Upsert` with a **throwing** notifier still
  publishes to the pipeline and does not surface the exception (notifier is best-effort).

**Client (vitest, `src/Meridian.Ui/dashboard/src/`)**
- `chrome-bridge.test.ts` (extend): normalize accepts well-formed `quotes`/`quotes-request`, rejects
  malformed snapshots.
- `quotes-stream.test.ts` (extend): owner re-broadcasts each snapshot; follower opens **no**
  `EventSource`, emits `quotes-request`, delivers snapshots from inbound messages, and flips unhealthy
  after `session-expired` / stale timeout → fallback resumes. Use a fake channel + stubbed
  `EventSource` (patterns already in this file/`chrome-bridge.test.ts`).

**Validation commands**
```bash
dotnet test tests/Meridian.Tests -c Release /p:EnableWindowsTargeting=true \
  --filter "FullyQualifiedName~QuoteStreamBroadcaster|FullyQualifiedName~StreamConnectionRegistry|FullyQualifiedName~WorkstationStream"
npm --prefix src/Meridian.Ui/dashboard run test
npm --prefix src/Meridian.Ui/dashboard run build
```

## 9. Phasing (each a separate PR, hot-path review gated)

1. **PR A — Notifier seam + broadcaster (server), behind a flag.** `IQuoteUpdateNotifier`,
   `QuoteStreamBroadcaster`, `StreamTopic`, options, DI, `QuoteCollector` optional param. Endpoint
   still polls; broadcaster runs in shadow with unit tests. No behavior change. **Requires hot-path
   review** of the notifier call site.
2. **PR B — Endpoint switch + registry/caps.** Rewrite the SSE loop to subscribe; add
   `StreamConnectionRegistry`, 429, session-expiry close. Flip the flag. Endpoint tests updated.
3. **PR C — Client companion fan-out.** Bridge `quotes` messages, owner/follower roles, follower
   degradation. Adopt on the watchlist + live-quotes panes.
4. **PR D (optional, future) — Additional topics** (`workspace:<key>`, `inbox`, `report-run:<id>`)
   reusing `StreamTopic`; wire the `use-workstation-data` workspace poller suspension deferred in
   Phase 4 step 1.

## 10. Risks & mitigations

| Risk | Mitigation |
| --- | --- |
| Notifier stalls the ingestion path | Notifier is non-blocking + exception-swallowing; it only sets a dirty flag and wakes a loop — no snapshot build, no storage, no `await` on the ingestion thread. Hot-path review gate on PR A. Explicit throwing-notifier test. |
| Broadcaster backpressure | Per-subscriber bounded channel capacity 1, `DropOldest` — latest-wins, broadcaster never blocks; a slow client drops intermediate snapshots, never the newest. Coalescer floor interval caps rebuild frequency. |
| Snapshot rebuild cost per topic | One rebuild per dirty topic per coalesce tick, shared across all subscribers of that topic (vs today's per-connection rebuild). Net reduction whenever a topic has >1 subscriber; equal otherwise. |
| Per-session cap wrongly rejects legitimate multi-window use | Default cap 4 (main + a few panes); configurable; 429 + `Retry-After` is explicit and the client falls back to polling, never hard-fails. |
| Cross-window message spoofing | `BroadcastChannel` is same-origin; `normalizeCompanionBridgeMessage` structurally validates every field before use. |
| Stranded pane shows stale data silently | Follower flips `healthy=false` on `session-expired` or stale timeout ⇒ `FreshnessChip` leaves `live` and polling resumes ⇒ visible + self-healing. |
| Durability guardrail drift | Storage sink path is untouched; all new channels use `EventPipelinePolicy`/bounded options per the guardrails; no WAL/atomic-write path is modified. |

## 11. Open questions for the reviewer

1. **Session identity for caps** — confirm the exact accessor exposed by
   `RequireWorkstationTenantScope` to key `StreamConnectionRegistry` (tenant id vs session id vs
   both). Affects whether the cap is per-user-session or per-tenant.
2. **Coalesce floor** — is 250ms an acceptable max push cadence for quotes, or should it be
   configurable per topic (e.g. tighter for a focused single symbol)?
3. **Owner-election** — is poll-fallback-only for stranded panes acceptable for v1, or should a
   follower promote to owner immediately on `session-expired`?
4. **Cap default** — 4 concurrent streams/session: right ceiling given expected pop-out usage?
