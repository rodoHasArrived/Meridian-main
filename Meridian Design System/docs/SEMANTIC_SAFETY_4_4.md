# Semantic safety and the accounting loop — increments A · B · C

**Status:** increments **A and B shipped** on this tree · **C proposed**.
**Baseline:** 4.3.0 stable · 199 modules · 334 exports · 28 templates.
**Scope note:** these are *design-system* increments. They do not replace the product's W1–W6
roadmap and they make no product-wave exit claims.

The three increments answer three different questions.

| | Question | Failure it retires |
| --- | --- | --- |
| **A · semantic safety** | Does a surface ever assert something nobody established? | Absence read as agreement: a missing input reading *tied*, an empty panel reading *ready*, a clock reading as an acknowledgement. |
| **B · one coherent accounting loop** | Can one operator close one exception end to end without changing scope or losing the instruction? | Five accounting screens deriving the same books from five private read models. |
| **C · compositional & suite adoption** | Does it stay legible and honest at laptop width, at 200% zoom, offline, and in export? | Coverage claimed from references rather than from behaviour. |

---

## A · Semantic safety — shipped

The rule, stated once: **a surface may assert only what something established.** Absence is a
state with a name, never a zero, never a pass, never a timeout dressed as agreement.

| Surface | What it used to do | What it does now | Behaviour test |
| --- | --- | --- | --- |
| `ActionEnvelopeProvider` | With no transport, answered `{ ok: true }` to itself and minted a local receipt id — every instruction "landed" against nothing. | Landing requires `ok === true` from the transport and takes the receipt id **from the adapter**. No transport, an unreadable answer, and a refusal without a reason are all **unlanded**. Demos pass a named `fixtureTransport()`. | `tests/semantic-safety.html` › *transport answers* (11 rows) + `tests/action-contract.html` |
| `ActionEnvelope` · `ActionQueue` | A row was promoted to "Accepted by the adapter" after 1.5 s of wall time. | Every rung is read off the answer: `landed` **with** an adapter receipt is *confirmed*; `landed` without one is only *acknowledged* and stays unresolved; `unlanded` is *timed out*, and its ladder never draws an acknowledgement that never happened. | *queue rungs · read off the answer, never off a clock* (6 rows) |
| `ActionEnvelopeProvider.retry` | A retry with nothing to retry fell through to `submit()`, minting a **fresh** idempotency key — the one thing the unlanded fate exists to prevent. | No-op that says so in the console; a real retry carries the same key with an incremented attempt. | *a retry with nothing to retry mints no instruction and no key* |
| `TieOutPanel` | Coerced a missing source to `0`, so the gross difference vanished and the panel read **Tied out**. | A source with no value — or one carrying `missing` / `pending` / `stale` / `na` / `exception` — makes the tie-out **indeterminate**: the residual renders as —, the gap is named, acceptance and resolution are unavailable, and a caller-declared `status="tied"` cannot outrank it. | *tie-out · a missing input is not a zero* (6 rows) |
| `AttestationPanel` | An empty panel derived **Ready for attestation** — no gates to fail, no evidence to fall short of, nothing bound. | Readiness is asserted from what *is* declared. Absent gates, evidence set, snapshot, version or signatory roles are named as under-declaration; a `snapshotFindings` entry with effect `void` expires the pending signature; a caller-declared `ready` does not survive either. | *attestation · readiness is asserted, never assumed* (8 rows) |
| `JournalEntryForm` | Gated Post on arithmetic alone. | `blockers` — a changed reviewed snapshot, a closed period, a withdrawn approval — are named on the form and disable Post. Balance is a precondition for posting, not permission to post. | *posting · arithmetic is a precondition, not permission* (3 rows) |
| `Assurance` | A re-preparation after a movement dropped every earlier signature on the floor. | Signatures **append, never replace**. `assuranceState` splits the trailing run that still binds the balance (`live`) from what a movement voided (`history`); both are retained and `Assurance.history()` reads the whole chain, oldest first. | *assurance · a re-preparation keeps the history it voided* (6 rows) |
| `assurance.card.html` | One click walked an account up the ladder by inventing a preparer, a reviewer and a controller in turn. | One signed-in operator acting in one authority. An account needing a different authority, or a second person, refuses and says which. | Card renders the refusal |

**Exit checks.**

- ✅ No missing input yields *tied* or *ready* by default — and a caller cannot declare its way past one.
- ✅ No timeout implies acknowledgement — rungs come from the answer; `ackMs` is accepted and ignored.
- ✅ No unvalidated transport response becomes *confirmed* — `ok === true` plus an adapter receipt id, or it stays unresolved.
- ✅ A re-preparation retains historical signatures — voided, struck through, never deleted.
- ✅ Changing the reviewed snapshot invalidates the pending action — void findings expire an attestation and block a post.
- ✅ Server authority remains the final enforcement boundary — every declaration panel still says, in the surface: *declared here, enforced by the server*.
- ✅ Fixture transports stay explicitly named (`fixtureTransport`, `tests/mock-transport.js`); an absent transport is a warning plus an unlanded instruction, never a silent success.
- ✅ Negative-path tests land **before** broad migration: `tests/semantic-safety.html` — 46 assertions across 8 suites (exports 1 · tie-out 6 · attestation 8 · snapshot 5 · assurance 6 · posting 3 · transport answers 11 · queue rungs 6), each suite carrying a positive control so a rule that blocks everything fails too.

**Not claimed.** These are legibility and eligibility rules in the client. They make an
instruction, its permission and its fate readable; they do not authorise anything. Nothing in A
was migrated across the 28 templates — that is deliberate, and it is where B starts.

---

## B · One coherent accounting loop — shipped

Today the reconciliation exception, journal review, general ledger, trial balance and account
sheet each derive the books from their own private read model. The same figure is assembled five
times, and the operator's scope, selection and pending instruction do not survive the walk
between them.

**Work.**

1. A **scoped books read model** over the existing contracts — `ObjectScope` (entity · book · basis),
   `TimeContext` (the twelve roles), `WorkspaceContext` (the persistent bar). One selector, one
   set of stable object identities. — `templates/_scaffold/books.js`, loaded by every template's
   `ds-base.js`.
2. Recompose all five surfaces from that selector, so a ledger row, a TB line and an account
   sheet line are the *same* object with the same identity, not five lookalikes. —
   `templates/books-loop/BooksLoop.dc.html`: tie-out, journal review, ledger, trial balance and
   account sheet, all `m.find(identity)` against one selection.
3. Wire attachments and rationale into the **reviewed journal payload**, so what was approved is
   what is submitted. — `reviewPayload()` carries rationale, evidence, scope, posting clock and
   the signed snapshot id in one object.
4. Mount pending instructions **above screen lifetime** — `sessionStorage`-backed, with durable
   record reconciliation where the platform supports it. (This is `SUITE_COHESION_4_4.md` › E-4.)
   — `templates/_scaffold/instructions.js`; `ActionQueue` now reads a store that outlives it, and
   E-4 is closed.

**Two rules the read model enforces.** *Stable identity* — every record carries
`"journal:JE-1042"`, `"glline:JE-1042#1"`, `"account:1100"`, `"break:CASE-4187"`, so selection
travels between surfaces as that string. *Actual and projected never merge* — `residual.actual` is
what the books say now, `residual.projected` is what they would say if the staged journals posted;
they are two fields on every surface, and the actual one closes on the adapter's receipt, never on
the click that requested it.

**Exit check (one journey) — executed.** Select a synthetic cash difference → inspect its evidence
→ stage a journal → approve against **one** snapshot → submit → receive an explicit receipt →
navigate to the resulting ledger, trial-balance and account records without changing scope and
without losing the instruction. Actual and projected residuals stay distinct at every step.
`tests/books-loop.html` — **61 assertions across 10 suites** (spine 3 · identity 6 · the break 5 ·
staging refusals 6 · staged-closes-projected 6 · TB proof 6 · reviewed payload 6 · instruction
fates 8 · outliving the screen 5 · scope survival 5), each refusal suite carrying a positive
control. The instruction suites re-evaluate `instructions.js` against fake storage, so the harness
never writes the operator's own tray.

**Fixed on the way.** The fixture's opening balances did not prove to zero, so the trial balance
derived from them could not prove either — an account (`3200 Retained earnings — opening`) now
carries the opening P&L for both entities. A trial balance that does not prove teaches an operator
to ignore the one number on the screen whose whole job is to be checked.

**Not claimed.** The loop is one read model over one fixture seed, not a service. `reconcile()`
takes a probe and asks the adapter about unresolved keys; nothing in this tree *is* that adapter.
Durability is declared per platform (`durable.kind`), and a store this system cannot write is a
record it does not claim.

---

## C · Compositional and suite adoption — proposed

**Work.** A continuous framing mode on `ScreenComposition` / `EvidenceRail`; evidence and
consequence legibility raised with the existing typography and density tokens (no new scale); the
exact seven operator roots applied; deployment, feed continuity, permission and execution
capability separated rather than collapsed into one "status"; report versions bound to export
references; generated-contract drift resolved; browser and screen-reader checks **executed**
rather than inferred from references.

**Exit checks.** Readable context / evidence / action at laptop width and at 200% zoom · a narrow
task flow preserves the selected record · offline inspection stays available while writes are
blocked · report references survive export · every claimed coverage figure is backed by a
behaviour test.

The two open items this inherits from 4.3: the NVDA + VoiceOver announcement journeys and the
Firefox / WebKit half of the browser matrix (`tests/BROWSER_TEST_PLAN.md`). C is where they stop
being a defer.
