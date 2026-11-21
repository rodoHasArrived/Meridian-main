# Meridian 4.1 — production deployment checklist

Date: 2026-08-23 · Applies to: 4.1.0 (additive minor) · Audience: the team putting these screens
in front of real desks.

4.1 turned three screens from panels that *display* state into workstations that *change* it —
`trading-desk`, `ingestion-operations`, `alerting-workstation`. Inside this design system every one
of those changes is a fixture mutation: it always succeeds, instantly, with no authorization and no
audit. That is correct for a design system and dangerous in production. This checklist is mostly
about the seam between the two.

Nothing here is a browser or backend test we ran. It is the list a consuming team must clear.

---

## 1 · Generated artifacts (blocking, mechanical)

`_ds_bundle.js`, `_ds_manifest.json`, `_adherence.oxlintrc.json` are compiler output — never
hand-edited, and a stale bundle is a silent-wrong-render class of bug, not a build warning. 4.1
already produced one: `OrderTicket` read the new `priceDecimals` prop in source while the shipped
bundle still had the 2-decimal default, so a limit seeded from the ladder rendered `201.1`.
Source was right; only the artifact was behind.

- [ ] `python3 scripts/validate_release.py` → PASS (orchestrates every gate in §2).
- [ ] Bundle rebuilt from the deploy commit, not carried from a branch. Confirm one 4.1-only prop
      is present in the artifact (`grep priceDecimals _ds_bundle.js` → `OrderTicket`, `DepthLadder`,
      `OptionChainTable`, `TimeAndSalesTape`).
- [ ] `scripts/generate_inventory.py --check` and `scripts/sync_catalog.py --check` clean, so the
      readme census and `guidelines/CARD_INDEX.md` describe what actually shipped.
- [ ] `scripts/sync_template_scaffold.py --check` clean — every `templates/*/ds-base.js` byte-identical
      to `templates/_scaffold/`. A consumer that hand-edits one `base` path and drifts loses the
      bundle silently.

## 2 · Static and unit gates

- [ ] `scripts/check_design_system_governance.py` — component trio pairing, raw hex, `!important`,
      manifest/source parity, template contracts, broken routes, hard-coded ARIA references.
- [ ] `tests/unit.html` opened offline → all pass. Schemas, lifecycle transitions, canonical hashing,
      invalidation, metric derivation, adapters, persistence, URL identity.
- [ ] `tests/A11Y_STATIC_AUDIT_4.1.md` reviewed; every item marked deferred there is either done or
      accepted in writing by the owning team.

## 3 · Action surface — the real work

Each row is an action 4.1 added. In this repo it mutates local state. In production every one needs
all five columns answered before the screen ships. An unanswered column is a production incident
waiting for its first busy morning.

**Answer per action: endpoint · idempotency key · permission · optimistic or confirmed · failure surface.**

### `trading-desk`

| Action | Notes for wiring |
| --- | --- |
| Ladder click → seed ticket | Client-only, no wiring. Safe. But confirm `Join`/`Take` maps to your venue's semantics: Join rests at the clicked level, Take crosses from it. Getting these backwards inverts intent silently. |
| Submit order | **Never optimistic.** Must be confirmed by the venue ack. Needs a client order ID as idempotency key — a double-submit on a flaky connection must not double the position. |
| Cancel working order | Confirmed. Must tolerate the race where a fill lands between render and cancel: the UI has to accept "too late to cancel" as a normal outcome, not an error toast. |
| Replace (cancel/replace) | Confirmed, and atomic at the venue if it supports it. A replace that half-lands is the worst state on this screen. |
| Fill → position / P&L | Must come from the venue's fill stream, not computed client-side. The fixture's avg-cost and realized/unrealized math is illustrative; your books are authoritative. |

- [ ] `environment="live"` is driven by real deployment config, never a default or a URL param a
      user can flip. The real-capital confirm gate is the last thing between an operator and a
      mistake — it must not be defeatable from the address bar.
- [ ] `orderCapUsd` reflects the desk's actual limit, and cap breach is enforced server-side too.
      A client-only cap is a suggestion.
- [ ] Tick precision comes from the instrument master (`decimals(tick)`), not a per-screen constant.
      Verified in 4.1 across the 0.01 and 0.25 tick paths; an FX 4-decimal symbol should be
      re-checked when one is added.

### `ingestion-operations`

| Action | Notes for wiring |
| --- | --- |
| Retry from checkpoint | Idempotency is the whole feature. Retrying must resume, not re-ingest — a double-ingest corrupts the coverage map the screen displays. |
| Pause / resume | Confirmed by the scheduler. A paused-in-UI-but-running-in-fact job is a data-integrity problem. |
| Raise priority | Permission-gated: this reorders work for everyone, not just the operator. |
| Cancel job | Confirmed, with the partial-output question answered: is a cancelled job's partial data kept, quarantined, or rolled back? The screen must say which. |
| Gap scan → queue backfill | Rate-limit and de-duplicate. A scan across a wide range can queue enough work to starve the live pipeline. |
| Compose new job (`DialogForm`) | Validate server-side. The dialog's validation is a courtesy, not a contract. |

- [ ] Progress streaming has a defined behavior when the stream drops — stale progress that keeps
      looking live is worse than a visible "connection lost".

### `alerting-workstation`

| Action | Notes for wiring |
| --- | --- |
| Acknowledge / silence / resolve / reopen | Each writes to alert history with the acting operator and a server timestamp. Client clock is not evidence. |
| Silence | Needs an explicit expiry. A silence with no end is how an alert gets permanently lost. |
| Bulk triage | Partial failure is the normal case at scale: N succeeded, M failed, and the UI must show which. An all-or-nothing toast over a bulk action hides real state. |
| Rule revision approve / reject | Permission-gated and audited — this rewrites a threshold, changing what fires for everyone afterwards. Two-person review if your governance requires it. |
| Operator notes | Durable, attributed, and immutable once saved. |

- [ ] The 4.1 selection fix holds against your real data: acting on an alert keeps the inspector on
      *that* alert even after it leaves the active filter, and "Show in queue" follows it. Re-verify
      with a live stream where new alerts arrive mid-triage — that is the case that broke before.

## 4 · Acceptance journeys to re-run after wiring

Fixture passes prove the UI composes. Re-run each end-to-end against staging with a real adapter:

- [ ] Desk: select instrument → read book → Take from the ask → order fills → position and P&L move
      → cancel a second working order → replace a third → audit drawer shows the full event chain.
- [ ] Ingestion: job fails → inspector → retry from checkpoint → completes without double-ingest →
      gap scan → backfill queues, runs, and fills the coverage cell.
- [ ] Alerting: alert fires → acknowledge → silence with expiry → resolve → history shows all four
      transitions with operator and timestamp → reopen → rule revision approved → new threshold fires.
- [ ] Each journey run once as a role *without* permission, confirming actions are hidden or
      disabled-and-explained (`guidelines/STATES_AND_ANTIPATTERNS.md`), never silently dead.

## 5 · Deliberately not claimed here

Consistent with `docs/QA_REPORT_3.5.md`, this design system ships fixture-first with no backend,
no live execution, and no network dependency. Therefore **not** verified in this repo:

- Real screen-reader validation (NVDA/JAWS) and the responsive browser matrix —
  `tests/BROWSER_TEST_PLAN.md` is prepared and unrun; needs a browser install.
- Latency and throughput under production message rates. The streaming toggles here run on
  intervals chosen to look right in a demo, not to match a market-data feed.
- Any authorization, audit, or persistence behavior. All three are stubs.
- Multi-operator conflict: two desks acting on the same order, job, or alert at once. The screens
  assume a single actor.

## 6 · Rollout

- [ ] Ship to a paper/fixture environment first. `environment` is a first-class prop on this
      screen family precisely so the same build can run harmless.
- [ ] One desk, one shift, with the previous tool still available.
- [ ] Confirm the rollback: reverting the bundle must not leave a template referencing a prop the
      older artifact lacks. That failure mode is a silent default, not a crash — the `priceDecimals`
      bug in reverse.
