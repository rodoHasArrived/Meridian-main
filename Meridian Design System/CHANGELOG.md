# Changelog

Semver from here forward: **MAJOR** = breaking component API or token removal ·
**MINOR** = new components/tokens/templates, behavior changes with compatible APIs ·
**PATCH** = fixes with no API surface change.

## [Unreleased] — increment A · semantic safety

### Fixed — public API review completion (#3520)
- **`ActionQueue` / `ActionEnvelopeProvider`** — a queue retry binds to the selected instruction, retaining its key, payload and next attempt. Action-only consumers keep their existing behavior; duplicate retries share one dispatch.
- **`DatePicker` / `DateRangePicker`** — calendar navigation stays within day cells while Tab reaches month, preset and footer controls. Month changes preserve control focus and an enabled day at date bounds; Escape closes and restores trigger focus.
- **`DataGrid`** — valid decimal strings, including committed edits, participate in proofs, totals and group subtotals. Missing, invalid and nonfinite evidence still withholds a proof or total.
- **`EditableCell`** — a pending inline edit is canceled when the cell becomes disabled; a later blur cannot commit it. The existing table regression now also runs in `test:patches`.
- Added source-based Chromium regressions for the three #3520 repairs and included them in `test:patches`; checked bound-control focus on the CI-pinned Chromium browser.

### Fixed — signal wash token and stale identity guidance
- **`tokens/colors.css`** — `--brand-signal-a10` was a terracotta `rgba()` literal, so it followed neither a brand nor dark mode; it now aliases `--accent-a10` like the rest of the `--brand-signal-*` names.
- **`BRAND_GUIDELINES.md`** — no longer says `data-brand="steel"` is selectable (4.3.0 has no such preset), and its colour, type and usage guidance state the 4.3.0 values instead of the v2.0 copper palette.
- **`VISUAL_FOUNDATIONS.md`** · **`guidelines/VISUAL_FOUNDATIONS.md`** — one text in both copies, matching the 4.3.0 tokens. The root copy still described the v2.0 copper identity; the guidelines copy had the palette but named the wrong pending colour, primary-button states, chart ink and type ramp.

### Fixed — consuming-workstation component review
- **`ScenarioSelector`** — native button radios support Space/Enter; arrows and Home/End move focus with selection. A missing saved selection leaves a reachable first option without changing the caller's value; read-only choices remain disabled.
- **`OptimisticProvider`** — observer exceptions no longer reinterpret successful writes or compensating writes as transport failures, interrupt dispatch, or stop subsequent batch notifications. Observer errors remain visible in the console. Source-level Chromium regressions cover both repairs.

One rule, applied to every surface that decides something: **a surface may assert only what
something established.** Absence is a named state — never a zero, never a pass, never a clock
standing in for an acknowledgement. Record: `docs/SEMANTIC_SAFETY_4_4.md`. Gate:
`tests/semantic-safety.html` (46 negative-path assertions across 8 suites, every suite carrying a positive control).

### Fixed — absence read as agreement
- **`TieOutPanel`** — a missing source was coerced to `0`, so the gross difference vanished and the panel read **Tied out**. A source with no value, or one carrying `missing` / `pending` / `stale` / `na` / `exception`, now makes the tie-out **indeterminate**: the residual renders as —, each unusable input is named, acceptance and resolution are unavailable, and a caller-declared `status="tied"` cannot outrank the gap. New status id `indeterminate`; `TieOutSource.value` is now optional in the contract.
- **`AttestationPanel`** — an empty panel derived **Ready for attestation** (no gates to fail, no evidence to fall short of, nothing bound). Readiness is now asserted from what *is* declared: absent gates, evidence set, snapshot, version or signatory roles are reported as under-declaration, and a caller-declared `state="ready"` does not survive them. New **`snapshotFindings`** prop (the output of `DecisionSnapshot.verify`) — a `void` finding expires the pending signature with the change stated; a `review` finding asks for a re-read. The "cannot attest" line is no longer suppressed in terminal states.
- **`JournalEntryForm`** — Post was gated on arithmetic alone. New **`blockers`** prop (string or `{label, detail}`) names the conditions outside the arithmetic — a changed reviewed snapshot, a closed period, a withdrawn approval — and disables Post while any remain. Balance is a precondition for posting, not permission to post.
- **`components/accounting/assurance.card.html`** — the demo advanced an account by inventing a preparer, then a reviewer, then a controller. It now signs as one signed-in operator in one authority, and refuses — in words — when the next stage needs a different authority or a second person. No synthetic signer escalation anywhere in the operational examples.

### Added
- **`tests/semantic-safety.html`** — the negative-path suite, landed *before* any template migration: tie-out indeterminacy · attestation eligibility and snapshot voiding · snapshot verify/freeze · assurance history retention across a re-preparation · posting blockers · `readAnswer` over seven malformed transport answers · queue rungs read off the answer rather than a clock.
- **`docs/SEMANTIC_SAFETY_4_4.md`** + **Documentation › Semantic safety** card — increment A's before/after ledger with its exit checks, and the proposed scope of increments B (one coherent accounting loop) and C (compositional and suite adoption).

### Unchanged, deliberately
Nothing was migrated across the 28 templates: increment A is contract and test work only. Every declaration panel still states that it is *declared here, enforced by the server* — these are legibility and eligibility rules, not authorisation.

## [4.3.0] — 2026-09-08 (rc) · 2026-09-10 (stable)

The agency layer. QA evidence: `docs/QA_REPORT_4.3.0.md`. Narrative: `docs/RELEASE_NOTES_4.3.0.md`. Promoted to **stable** on 2026-09-10 after the unit suite (49/49) and the Chromium keyboard / ARIA / responsive matrix (`tests/A11Y_RUN_4.3.0.md`) were re-run on this tree. The assistive-technology and Firefox / WebKit half stays a declared defer.

### Fixed — found by the 4.3 browser run (2026-09-10)
- **`OptimisticCell` · `OptimisticRowBar`** — Escape or Cancel from the revert reason dialog returned focus to `<body>`; both now restore focus to the Revert control that opened it.
- **`WorkspaceLayout`** — the `role="separator"` divider's own box was 7 px across; it is now a 25 px keyboard/drag target (content-box padding + negative margin, `z-index:1`) with the same 7 px of layout and the same look. Region collapse / pop-out icons 20 → 24 px.
- **`Toggle`** — `.mds-tog-wrap` (the whole click target) gains `min-height:24px`; the 36 × 20 track had set the label to 20 px.
- **`tests/a11y-run.html`** — reports `document.hasFocus()` as its first row, starts on click when the embedded preview lacks system focus, re-acquires focus before every keyboard row, exposes `window.__a11yRun.keyboard(from, to)` for driving rows in bursts, waits for a streamed template's control geometry to settle before measuring targets, and types into React inputs through the native value setter. Without the first of these, every `focus()`-dependent row is a false FAIL in an unfocused frame.

### Changed — release identity
- `release.json` → **4.3.0 · rc**, declared 2026-09-08. readme and `docs/METRICS.md` regenerated: **334 exports across 199 modules · 130 cards · 28 templates · 680 tokens**.
- **15 exports classified** that the family gate would have failed on: `OptimisticCell` · `OptimisticProvider` · `OptimisticRowBar` · `OptimisticHooks` · `SelectionCompare` · `SelectionCompareProvider` · `SelectionCompareHooks` · `CompareToggle` · `CompareTray` · `CompareSheet` → **`data`**; `AnnotationLayer` · `AnnotationProvider` · `AnnotationAnchor` · `AnnotationPanel` · `AnnotationHooks` → **`evidence`**; and `ActionQueue` · `ActionQueueChip` · `QUEUE_RUNGS` (4.2, never classified) → **`decision`** beside `ActionEnvelope`. All fifteen enter at **release-candidate** maturity (`scripts/generate_families.py` › `STABILITY`), alongside the 4.3 Assurance and LocalDate exports — 28 release-candidate exports in total.
- **Conformance gate re-run on the 4.3 tree** — two findings fixed: a C-02 blocking hit on a `LocalDate.jsx` docstring that spelled out the anti-pattern (reworded), and two missing prompt files (`Assurance.prompt.md`, `LocalDate.prompt.md`). 0 blocking.

### Added — the agency layer: revert with a reason, mark-up, comparison by selection (UX depth tracks B-2 · D-3 · E-1 · E-2)
An operator could commit a value but not take it back with a reason, could sign a figure but not annotate it, and could compare entities only where a template author had pre-declared the plates. Three components close that, folded into the 4.2 release candidate's surface and adopted on six production screens.

- **New `OptimisticCell` · `OptimisticProvider` · `OptimisticRowBar`** (`components/data/`, B-2) — `EditableCell`'s write counterpart. A committed edit reads **Provisional** (existing condition, no new vocabulary) until `commit` resolves; a throw **rejects it with the reason attached** and restores the prior value; a confirmed edit stays revertible — one cell (*Revert…*) or a row / selection batch (`OptimisticRowBar`) — under a stated reason recorded as its own write, never an erasure. Card: **Data › Optimistic editing & revert**.
- **New `AnnotationLayer`** (`components/operations/`, E-1) — `AnnotationProvider` per object in view, `AnnotationAnchor` on a grid cell, series point, ledger line or object header (¶ marker, count, popover), `AnnotationPanel` (All · Stale · Pinned). Every note carries the object version it was written against; the operator picks **follow** (stale on a later version, *Still holds at vN* to re-affirm) or **pin** at write time. Stale is text + marker, never colour alone. The ¶ control stops propagation, so annotating a row does not select it. Card: **Operations › Annotation layer**.
- **New `SelectionCompare`** (`components/data/`, E-2) — `CompareToggle` on a row / series / line, `CompareTray` (inline or corner), `CompareSheet` → `PlateGrid` with one shared domain and tick set; capped at `max` (default 6) because past ~12 the plate contract says table + one focused plate. Card: **Data › Comparison by selection**.
- **New archetype `templates/interaction-depth`** (D-3) — the normative reference for the interaction cluster on a 50,000-row windowed grid: right-click verbs, bulk edit, saved views, column chooser, keyboard-only path, plus the three components above with fixture policies that reject for stated reasons.
- **Adopted on production screens** — `security-master-registry` (inspector fields write optimistically; the *Save changes* staging step is gone; record header is an annotation anchor with the audit length as object version; annotations panel on the Notes tab) · `ingestion-operations` (queue **priority** is an `OptimisticCell`; the scheduler refuses a third *high* under `SCHED-2`) · `reconciliation` (every statement and ledger memo is a line anchor; version advances with matches) · `trading-desk` (position symbols are cell anchors; version = fill count, so a follow-note goes stale on the next fill; panel row focuses the symbol) · `accounting-workstation` › Account Sheet (account header anchor, version = live postings) · `strategy-runs` › Run Ledger (▢ on any completed run with a series → normalised equity paths on one scale).
- `api/template-eligibility.json` classifies `interaction-depth` (eligible: sel · con · fig · cap); totals recomputed — 28 templates, 156 eligible cells, 22 conforming (14.1%).

### Added — `LocalDate` on the namespace, and the template date/number sweep (C-01 · C-02)
`core/localDate.js` owned the calendar-date rule since 4.1 but only components could import it, so screens kept writing `toISOString().slice(0, 10)`. **`LocalDate`** now carries the module on the namespace (`toISO · fromISO · dayOfInstant · utcDayOfInstant · addDays · …`) beside `Format`; `LocalDateText` renders one as `<time>`. All **36 template findings** swept: every `toLocaleString("en-US")` and the one `new Intl.NumberFormat` in a template now go through `Format`, the three calendar-date slices through `LocalDate` (the ingestion coverage grid *states* that its sessions are UTC-bucketed instead of smuggling it through `toISOString`). `_strategy-executor/screen-kit.js` gained `K.int · K.usd0 · K.usd0s` and its `K.isoD` reads `LocalDate`.

### Added — Assurance: certification as carried state (`docs/ACCOUNTING_INTERFACE_4_3.md` › O-4)
New `components/accounting/Assurance.jsx` at release-candidate maturity. Stages climb **unprepared → prepared → reviewed → certified** one at a time (`signAssurance` throws on a skip); every signature is bound to the balance it vouched for, and `assuranceState` reports a moved balance as **void, not stale**. `AssuranceChip` (derived or explicit stage, strikes through when void), `CertificationMatrix` (accounts × stages, the close's real progress bar), `assuranceCoverage` (what the lock gate reads). `TrialBalance` gained a `trailing` column renderer. In `accounting-workstation`: an Assurance column on the TB (row → Account Sheet), an assurance strip + the one advancing verb on the Account Sheet, a *Material accounts certified* gate and the matrix on Period Close — **locking is blocked with the uncovered accounts named**. Fixture: 1310 · Equities was certified on 15 June against 819,882.00 and JE-1080 then moved it, so the gate arrives blocked and says why.

### Added — Account Sheet (`docs/ACCOUNTING_INTERFACE_4_2.md` › O-2)
The account as a destination, not a filter. `templates/accounting-workstation/views/AccountSheetView.jsx`, reached as `?view=account&account=1100`, `G A`, a palette lookup (type a code or name → *Sheet* / *Ledger* verbs), or *Account sheet* on the entry inspector and the chart of accounts. One surface: the closing balance stated on the account's normal side, above **RollforwardTrace** proving opening + debits − credits = closing (opening derived and *said so* where the fixture states none), **TieOutPanel** (bank statement ↔ ledger for cash, proof ↔ chart for the rest), open items by settlement clock, the postings with running balance (row → entry inspector), and **EnvelopeCoherence** across the postings' envelopes. Deep links now win over the `view` tweak default when the URL names a view.

## [4.2.0] — 2026-09-01 · release candidate

Feature-complete and gated. QA evidence: `docs/QA_REPORT_4.2.0.md`. The Chromium keyboard / ARIA / responsive matrix was executed for the first time (`tests/a11y-run.html` → `tests/A11Y_RUN_4.2.0.md`); `rc` rather than `stable` because the NVDA / VoiceOver journeys remain unrun (`release.json` › `why`).

### Fixed — found by the browser run
- **`LayoutRegion`** — collapsing a region while focus was inside it dropped focus to `<body>` (the one defect `tests/A11Y_STATIC_AUDIT_4.1.md` could not close from source). The region now remembers whether it held focus before a toggle and moves it to the labelled rail on collapse, back to the collapse control or body on expand.
- **Target size floor (24 px, `guidelines/ACCESSIBILITY.md`)** — `SegmentedControl` buttons at both sizes, `DensityToggle`, `ObjectTrail`'s pin star, `WorkspaceLayoutBar`'s select / Save / Reset, and `TagInput`'s remove control now meet it. Glyphs and row heights are unchanged; the hit boxes grew.

Three release-scale items from `docs/reviews/Large Improvements.html`, in the order that made them
safe. Full notes: `docs/RELEASE_NOTES_4.2.0.md`.

### Added — the operator layer on the seven archetypes (UX depth tracks A · B-1 · D)
The decision layer was finished; the layer *around* it — how an operator arrives, finds, submits and
returns — was thin (`docs/UX_DEPTH_4_1.md`). This pass ships it on the archetypes, so every new kind
of screen copies it in.

- **New `ActionQueue` + `ActionQueueChip`** (`components/operations/`, track B-1) — the tray of
  consequential actions in flight. Rows climb **submitted → acknowledged → confirmed | rejected |
  timed out**, derived from `ActionEnvelope`'s four fates (no new vocabulary): `inflight` under the
  ack window is submitted, past it acknowledged; `unlanded` is timed out. Each row carries its
  idempotency key, elapsed clock, receipt, rejection reason verbatim, and — for timed out — the one
  primary verb *Retry · same key*. Confirmed rows leave after `settleMs`; rejected rows leave once
  acknowledged; timed-out rows never leave on their own. `docked="corner"` (default, hides when
  empty) or `"inline"`. Card: **Operations › Action queue**.
- **`CommandPalette` deepened** (track D-2) — `resolve(query)` for async **object lookup** (type a
  symbol, a case id, a report name → rows grouped by type), **verbs on a row** (`verbs:
  [{label, run}]`, Tab / → cycles, Enter runs the highlighted one), `meta` mono suffix, **recency
  ranking** (session-scoped, `scope` = screen id; a *Recent* group on an empty query). Static
  `commands` work unchanged.
- **`templates/_scaffold/operator-layer.js`** — `MeridianOperatorLayer.build({sections, actions,
  objects, verbs})` turns a screen's nav sections, actions and objects into the palette's `commands`
  + `groupOrder` + `resolve` and the `HotkeysProvider` `bindings` in one call, so a NavRail `shortcut`
  becomes a real `G x` chord and a palette row at once. Plus `trail-bridge.jsx` / `action-bridge.jsx`,
  which hand the `ObjectTrail` and `ActionEnvelopeProvider` context APIs to a DC logic class.
- **All 7 archetypes** now mount `CommandPalette` (with object lookup) + `HotkeysProvider` (`?`
  sheet) — was 1 and 4 of 27. `dashboard-workstation`, `analytical-workbench`, `basket-builder`
  carry a **session-scoped `ObjectTrail`** (`storage=sessionStorage` by decision — nothing about
  traversal outlives the tab); `analytical-workbench` adds a **session-scoped `WorkspaceLayout`**
  (evidence ⇄ inspector, two presets). The three archetypes that write — `basket-builder` (commit),
  `settings-admin` (save · purge), `report-pack` (deliver) — route through `ActionEnvelopeProvider`
  → **`ActionQueue`**, with fixture transports that reject for a stated policy reason (sector limit ·
  zero recipients · draft watermark) and, on the basket, a `Shift+S` fixture toggle that silences the
  adapter so the timed-out rung and same-key retry can be seen.

### Added — `components/decision/`, the decision chain as composition patterns
The eighth domain. Evidence and consequence existed as a dozen separate components a screen had to
assemble correctly; a decision bound to nothing was still a valid commit.

- **New `EvidenceRail`** — the persistent *why is this value what it is* plane. **Slot**-based, not a
  schema: `EVIDENCE_SECTIONS` fixes the reading order (summary → sources → calculation → lineage →
  audit), the rail owns the coverage denominator and the evidence's own continuity state, the screen
  supplies the bodies. A section the screen cannot prove is **absent, not a blank tab**. Real
  tablist: arrow keys, Home/End, roving tabindex.
- **New `ConsequenceRail`** — *what changes if this proceeds*. Reversibility is stated **first**
  because it decides how much of the rest matters; impacts are grouped by who feels them
  (`CONSEQUENCE_KINDS`: financial · accounting · controls · reports · approvals); effects are words
  (`Re-runs`, `Superseded`), never a bare colour; blocking guards are listed **beside** the
  consequence rather than only disabling the button. Declares no vocabulary of its own —
  `ReversibilityClass` is imported from `DecisionFlow`.
- **New `DecisionSnapshot`** — exactly what was reviewed, frozen. `capture()` deep-freezes; there is
  no setter. Evidence and calculations are stamped by id + version + hash, not embedded, so the
  snapshot stays small. `verify()` returns **findings, not a boolean**: a changed object version or
  a changed evidence set is `void` (an approval whose subject moved is void, not stale), a re-run
  calculation or a different workspace context is `review`. `describe()` is the audit sentence —
  actor, authority, object, version, context, evidence count, calculation version, guard count, UTC
  instant. `ActionGuard` answers *may I*, `ActionEnvelope` answers *did it land*, this answers *what
  did I review* — and it is what an attestation binds to.
- The trio ships at **`release-candidate`** maturity (`api/families.json` › `stability`): public and
  supported, shape may still move inside a minor while templates adopt it.
- Card: **Decision › Evidence → consequence → proof**, with an upstream-change button that voids a
  captured snapshot in front of you.

### Fixed — the gate was reading 27 of 35 screens, so C-16's entry check was dead code
`tests/conformance.html` built its file map from `_ds_manifest.json`, which names **one** entry per
template. Eight screens are secondary `.dc.html` entries inside a classified slug —
`StrategyBuilder`, `StrategyOnboarding`, `BacktestBuilder`, `BacktestCompare`, `CoveredCallLab`,
`RunLedger`, `CloseTieOut`, `ReconciliationException` — and no static rule had ever read any of
them. C-16 checks `entries.includes(file)`, so its *classified but does not declare this entry* half
could not fire either: the harness only ever handed it the 27 paths the manifest happened to name.

- **Template paths now come from `api/template-eligibility.json`**, the record that declares all 35
  entries, unioned with the manifest's list. The record is fetched first so the path list can be
  expanded from it.
- **Immediate result: 17 findings that were invisible before** — 16 × C-01 (bare
  `Intl.NumberFormat` / `toLocaleString` instead of `core/format.js`) and 1 × C-13 (a submit path
  with no `ActionEnvelope`). All template-scoped, so all **review**, not blocking: the static gate
  went 65 → 82 review findings with 0 blocking, and the eight screens joined the adoption backlog
  they should have been in all along.
- **New `counts` assertion** — *all N declared template entries were scanned*. It fails both when a
  declared entry is not fetched **and** when the expansion produces no more paths than the manifest,
  so this regression cannot return silently.
- Disk truth stays with `scripts/check_template_eligibility.py`: a file on disk but absent from the
  record is its finding, since a browser cannot list a directory. The two gates are now explicitly
  complementary, and `guidelines/CONFORMANCE.md` says so normatively.

### Fixed — the spelled-count assertion failed the gate on a true sentence
The `counts` assertion that compares the spelled rule count across the three law documents matched
*any* spelled number followed by `rules`. `docs/RELEASE_NOTES_4.2.0.md` says **"Four rules were wrong
on the first run and were fixed rather than tolerated"** — a true statement about four of the sixteen
— and the assertion read it as a claim that the table holds four rules, failing the gate.

- **The claim shape is now pinned** rather than the bare word: `<n> static rules`, or
  ``<n> rules (`C-01…C-16`)``. Sentences about a subset are untouched. The doc was correct, so the
  assertion changed and the sentence stayed — a gate whose false positives get explained away stops
  being read, which is the same principle that fixed C-02, C-05, C-06, C-07 and C-12 on their first run.
- **A narrowed matcher can pass by matching nothing**, so a companion assertion requires at least one
  checkable claim to have been found. `guidelines/CONFORMANCE.md` stated its count as the heading
  *The static gate — sixteen rules*, which the pinned shape does not match, so the heading now carries
  the range: all three law documents are covered, three claims, no drift.
- Stale figures corrected in the release notes: template findings `18 · 2 · 5` → `34 · 2 · 6`, and
  *adoption across 27 screens* → *across the 35 template entries*.

### Fixed — the METRICS.md census assertion could never match, and read as coverage
`tests/conformance.html` compared the generated census line in `docs/METRICS.md` against the manifest
with a regex that hard-coded the separator as `", "`. `scripts/release_identity.py` emits ` · `, and
has since `themes` was added to the census. The assertion therefore reported *snapshot line not found*
on every run — a **false negative behind a green-looking scoreboard**, which is worse than no assertion
because it reads as coverage. Every number in the line was in fact correct.

- **The gate no longer encodes the line's shape.** The census format belongs to the generator, so the
  assertion now locates the line by its stable opening (`**<n> exports across <n> modules**`) and reads
  each labelled count independently. A separator change, a reordering, or a new field cannot break it.
- **`themes` is now checked too** — six fields, up from five. It was in the generated line and in the
  manifest but in no assertion, which is how the mismatch survived unnoticed.
- **Proved by negative test through the real page**, not a replication: with the line saying 124 cards
  against a manifest of 125, the gate fails with *cards: metrics 124 vs manifest 125*, then passes
  again once restored.
- The root cause of the miss was mine: I verified the earlier fix by re-implementing the assertions in
  a headless script instead of running the harness, and my paraphrase used the separator the document
  actually has. A replication that is written by reading the document under test cannot detect a gate
  that disagrees with that document. Verification now runs the page.

### Changed — BREAKING: one object identity, not two
- **`FinancialObjectType` and `FinancialObjectRef` are removed** from `operations/DecisionFlow`.
  They were a second identity union that omitted `strategy`, so Meridian's seventeenth object
  existed for the `ObjectHeader` that displays it and not for the decision flow that approves it.
- **New canonical `ObjectRef`** in `core/ObjectModel` — `{ type: MeridianObjectType, id, version,
  label? }`. Every subsystem that points at an object refers to it through this type. Widening the
  vocabulary means adding to `MeridianObjectType`. Migration: `docs/UPGRADING.md` › 4.2.
- **New rule C-15** fails the build on a second declaration of a canonical domain union.

### Added — one release identity, and counts that cannot be typed
The tree called itself *production-ready 3.5.0* in `readme.md`, *a 4.2.0 tree* in `docs/METRICS.md`,
and carried unreleased 4.1/4.2 sections here. Three statements, no source of truth.

- **New `release.json`** — version, channel (`stable` · `rc` · `beta` · `development`), last stable,
  QA report and **what that report defers**. The evidence supports exactly one reading, now
  declared: **4.2.0-dev, development channel, last stable 3.5.0**. Not a release candidate — the
  browser and screen-reader matrix is still deferred, and a candidate that cannot show that
  evidence is a claim rather than a state.
- **New `scripts/release_identity.py`** — renders the marked blocks in `readme.md` and
  `docs/METRICS.md` from `release.json` + `_ds_manifest.json` + `api/families.json`. `--check` is a
  release gate. No version and no count is typed by hand anywhere in the package.
- **New `api/families.json` + `scripts/generate_families.py`** — the public family record, with a
  `stability` register. The nine families summed to **284** under a sentence claiming they covered
  **303**; the 20 exports in no family (the whole `ActionEnvelope` group, `Disclosure`, the `Format`
  carriers, `DOW`) are now assigned, `SelectCheckbox` — a barrel name that was never on the
  namespace — is gone, and the gate fails on an unclassified export, a phantom name, or a symbol
  claimed by two families. Families now sum **exactly** to the manifest.

### Fixed — invented density tokens, and the rule that recommended them
- **`--control-h` and `--row-dense` are not tokens.** The new `decision/` components referenced them
  for tab and row heights, so those two surfaces were permanently pinned to their px fallbacks —
  `DensityToggle` and the `data-theme-density` scopes would have moved every other row in a
  workstation and not these. Corrected to `var(--theme-control-height)` and
  `var(--table-row-height)`, the names that exist.
- **Root cause: C-05's own `fix` string named them.** Of the four tokens the rule advised
  (`--row-dense`, `--row-standard`, `--row-review`, `--control-h`), only `--row-review` is real, and
  the rule's scan only looks for pinned px — so it stayed silent either way and the advice survived
  two releases. The `fix` now names the real tokens.
- **New card assertion `tokens-resolve`** — a `var(--name, fallback)` whose name is defined nowhere
  in the closure is silently permanent, and no static scan can see it. A loaded card has the whole
  token closure, so the card gate now cross-references every `var()` in every injected component
  stylesheet against every declared custom property and names the component that invented one.

### Fixed — the release validator's own findings
- **`WorkspaceLayout`'s `!important` removed.** `.mds-lreg--collapsed{flex:0 0 auto!important}`
  existed to stop a consumer `style` from flex-growing a collapsed region; the region already sets
  `flex` inline, so the fix is **order** — when collapsed, the component's flex and extent are
  applied after the consumer's style.
- **`SplitPane`'s three retained and explained.** A drag cursor must beat every descendant's own
  `cursor` (the pointer is captured by `<body>`, and a child's rule is more specific), and print must
  unroll the inline height/width the drag wrote. Both now carry a `/* important: <reason> */` pragma.
- **`no-important` no longer fails on prose.** The gate matched the raw string, so `TrialBalance`'s
  comment explaining why it does *not* need `!important` was a failure. It now blanks comments
  (block comments across lines included) before matching, and reads the pragma. **New rule C-14**
  mirrors it in the browser gate.
- **Three stale template scaffolds synced** — `adapter-operations`, `ops-wall`, `ownership-graph`
  shipped a 1,122-byte `ds-base.js` against the canonical 3,625-byte loader, so they had no
  environment-badge fallback and no `DataStateSwitch` degradation path.
- **`readme.md` inventory and `guidelines/CARD_INDEX.md` regenerated** — the card index said 114
  cards with per-group counts up to four short, and the readme's census block had been hand-edited
  away from what `generate_inventory.py` emits.
- **`tests/conformance.html`'s parity check no longer hardcodes seven domains** — it read
  `across 7 domains` literally, so the eighth domain would have silently failed the census assertion
  it exists to protect.

### Added — a conformance gate that actually runs (L-02)
`_adherence.oxlintrc.json` had been generated for releases and nothing in the repository ran it.

- **New `tests/conformance.html`** — static rules over every component, token file and template
  entry; assertions over all 124 specimen cards; one scoreboard. `window.__conformance` for
  machines, PASS/FAIL in `document.title`, `?cards=all` to include the card gate.
- **New `tests/conformance-rules.js`** — sixteen rules (`C-01…C-16`), each carrying the shipped
  defect it comes from plus two fixtures: the violation it must catch and the conformant form it
  must stay quiet on. A rule that cannot catch its own fixture is reported as failing.
- **New `docs/conformance.card.html`** — Documentation-group scoreboard; counts read from the
  manifest, verdict from the last actual run, "NEVER RUN" until there is one.
- **New `guidelines/CONFORMANCE.md`** — the rule table, what the gate does not claim, how to add a rule.
- Documentation counts regenerated and now gate-checked: **310 exports across 193 modules · 124
  cards · 27 templates · 665 tokens**. Typed counts that stop matching `_ds_manifest.json` fail an
  assertion instead of drifting for two releases (284/118 vs. 292/120).

### Fixed — the defect classes the first run found
- **6 literal DOM ids** → `React.useId()`: `TieOutPanel`, `CommandPalette`, `Drawer`,
  `ResolutionDrawer` (×2), `VersionConflictNotice`. Two instances on one screen shared an aria
  relationship before this.
- **15 `<button>`s with no `type`** → explicit everywhere, and **`Button` now defaults to
  `type="button"`** (`Pagination`, `Toast`, `NumberInput`, `ContextMenu`, `FileUpload`,
  `ColumnChooser`, `EmptyState`, `BulkActionBar`, `NavRail`). *Behaviour change:* a `Button` that
  relied on the implicit form submit needs `type="submit"`.
- **`transition: all`** in `JournalEntryForm` and `BulkActionBar` → named properties on the house curve.

### Added — `core/format.js`, the numeric authority (L-01)
Ten modules hardcoded `en-US` while `money.js` and `Timestamp` took a `locale`.

- **New `core/format.js`** — money, accounting negatives, quantity, price-to-tick, percent, basis
  points, compact, signed delta, house glyphs; **one memoized `Intl` constructor for the system**.
  Decimals come from the instrument (`decimalsForTick`, `currencyDecimals`), not the call site.
- **New `core/FormatProvider.jsx`** — `FormatProvider` (locale · currency · negative style ·
  numerals · precision, also setting the process defaults so module-level formatters follow),
  `useFormat()` (safe with no provider), `Format` on the namespace for templates, `FormatHooks`,
  `NEGATIVE_STYLES`. Nest a provider for a region that must differ.
- **Swept:** `BasisComparisonMatrix` `RollforwardMatrix` `PostingImpactPreview` `ScheduleLadder`
  `VarianceCell` `TieOutPanel` `CommitmentBar` `MaturityLadder` `CashFlowLadder`
  `MaterialityAssessment`, plus the 14 bare `toLocaleString("en-US")` callers the gate found and
  `trading-desk`'s render-edge formatters.
- **`money.js`** keeps the arithmetic and formats through the authority; it re-exports no formatter
  (one shared bundle scope means one owner per name). The **`Money` carrier composes both halves —
  no consumer API change**.
- **New `guidelines/NUMERIC_AUTHORITY.md`** (`N-1…N-8`). Gate: C-01.
- *Deliberate output change:* negatives that Intl rendered with a hyphen now use U+2212, and an
  unknown ISO code takes the house narrow-space prefix. Otherwise byte-identical under `en-US`.

### Added — an action contract: the fate of an instruction (L-03)
`ActionGuard` answered *may I*; nothing answered *did it*.

- **New `operations/ActionEnvelope.jsx`** — `ActionEnvelope`, `ActionEnvelopeProvider`,
  `ActionStateChip`, `InstructionLedger`, `ActionHooks`, `ACTION_STATES`. A mutating control
  declares endpoint, idempotency key, permission, posture and failure surface *before* the attempt
  (undeclared renders as a defect) and shows one of four fates: **in flight · landed · unlanded ·
  rejected**. No "failed" state exists. A double-clicked submit is one instruction; a retry after
  unlanded reuses the key and increments the attempt.
- **New `tests/mock-transport.js`** + **`tests/action-contract.html`** — latency, replay, a 4xx with
  a reason, a 500 whose write landed and whose answer did not, silence; 24 assertions, including the
  retry that resolves to the original receipt with exactly one order at the venue.
- **`templates/trading-desk`** adopts it: only a **landed** instruction reaches the blotter, and a
  new `transport` tweak (*answers · refuses · loses the answer · silent*) exercises all four fates.
- **New `guidelines/ACTION_SEMANTICS.md`** (`A-1…A-9`). Adoption gate: C-13.

### Added — template eligibility, and the first honest adoption figure
`guidelines/TEMPLATE_CONTRACT.md` shipped the eligibility *rules* in 3.2 and nothing was ever
classified against them, so every adoption number in the repository was either a raw reference count
wearing a percent sign or, latterly, the word *unmeasured*. Step 1 of the contract's own migration
sequence is now done.

- **New `api/template-eligibility.json`** — all **27 templates × 10 primitives = 270 cells**, each
  cell either eligible or exempt **with a declared reason code**. **152 eligible, 118 exempt.**
  Deliberately **hand-authored**: whether a screen owes `Datum` is a design judgment about that
  screen, and generating it would only launder the judgment. Every template also carries its
  archetype, its `.dc.html` entries, and its scanned reference counts.
- **Three templates are eligible for nothing** — `asset-sheet`, `blank-workstation`, `session-start`.
  That is the eligibility model working. A sign-in gate with a context bar would be a worse screen.
- **First measured adoption: 22 of 152 eligible cells — 14.5%.** `Capsule` is 0/5 by design (its use
  condition requires working move/resize affordances, still unwired since 3.2). Four templates are
  partially migrated — `alm-review` 6/7, `strategy-formation` 6/7, `close-management` 5/8,
  `reconciliation` 5/8; the other 20 eligible templates conform on nothing yet. No target and no
  trend are published alongside it: the number is a diagnosis of where local state still lives.
- **New `scripts/check_template_eligibility.py`** — seven gates: coverage, completeness (every
  primitive classified exactly once, eligible XOR exempt), reason codes (declared *and* used),
  entries-match-disk, reference scan, **stray** (a screen using a primitive it claims exemption
  from — the failure that would otherwise make an exemption self-serving), and adoption arithmetic.
  `--write` regenerates the `docs/METRICS.md` adoption block, which is now generated output.
- **New `eligibility` gate in `scripts/check_design_system_governance.py`** — a new template folder
  fails the release gate until it is classified. An unclassified screen silently shrinks the
  denominator, which is exactly how 284/118 outlived two releases.
- `guidelines/TEMPLATE_CONTRACT.md` moves from *staged* to **normative, classified**.
- **New rule C-16** (`blocked`) — every `templates/<slug>/*.dc.html` entry is classified: on the
  record, declared as an entry, all 10 primitives resolved eligible-XOR-exempt, and every exemption
  citing a declared reason code. The release gate already refused an unclassified folder, but the
  card-assertion layer said nothing, so the condition was invisible in the Design System tab —
  where the percentage is read. C-16 is the first **cross-file** rule: `runStatic` now hands each
  rule the whole file map, because the defect is not in the template, it is the absence of a record
  *about* it. `selfTest` grew per-half fixture paths for the same reason — a rule whose subject is
  *which path is on record* cannot prove itself from one path.
- **New `eligibility` card assertion** — a card that publishes `window.__eligibility` has its
  totals and its **printed** percentage recomputed from its own rows. A hand-typed figure in a card
  is precisely the drift that let 284/118 outlive two releases; the gate assertions caught it in
  `readme.md` and `docs/METRICS.md` and nowhere else.
- **New card `docs/eligibility.card.html`** (Documentation) — the 27 × 10 record, rendered: adoption
  per primitive, the exemption-reason legend, and the per-screen matrix with the `unmigrated` cells
  as the visible worklist. Every number on it is computed at load from
  `api/template-eligibility.json`; none is typed.

### Deferred
`ingestion-operations` queue adoption, C-11's 15 `aria-posinset` findings, and template numeric/date
adoption (20 findings, recorded per file and line by the gate) — all 4.3. Primitive migration itself
is 4.3+ and now has a per-cell worklist: `api/template-eligibility.json` → `unmigrated`.

## [Unreleased — 4.1.0] — in progress

Ships after 4.0. Two tracks from `docs/UX_DEPTH_4_1.md`, both strictly additive: no changed
defaults, no retired exports.

### Fixed — dates were a day early west of UTC (2026-08-26)
The single worst class of defect in the system. `DatePicker` and `DateRangePicker` converted the
picked day with `toISOString()`, which turns local midnight into the **previous** UTC day for every
user west of Greenwich. A settlement date clicked as the 26th was emitted as `2026-08-25`. Display
had the mirror bug: `new Date("2026-08-26")` parses as UTC midnight and `toLocaleDateString`
rendered it back a day. Two more components bucketed instants by UTC day.

- **New `core/localDate.js`** — the shared calendar-date vocabulary: `toISO`/`fromISO` (local, never
  UTC), `parseLoose`, `addDays`/`addMonths` (month-end clamping), `weeksFor`, `rangePresets`,
  `dayOfInstant`. Both pickers are built on it. **Never route a calendar date through
  `toISOString()`**; that function is for instants only (`Timestamp`, `FreshnessIndicator` use it
  correctly).
- **`DatePicker`** — dates now round-trip exactly. Also: the field is **typeable** (ISO,
  `26 Aug 2026`, `8/26/2026`; a bad entry snaps back), and the calendar is a real ARIA grid with the
  standard keyboard model (arrows, PageUp/PageDown month, Shift+PageUp/PageDown year, Home/End,
  Enter, Escape returning focus to the field) — it previously had **no keyboard support at all** and
  no ARIA. Added `min`, `max`, `disabledDate` (weekends, holidays, closed periods), `disabled`,
  `required`, `error`, `clearable`, `showToday`, `weekStartsOn`, `name`, viewport-edge flipping, and
  a visible month that resyncs when `value` changes externally. Nav and day buttons gained
  `type="button"` — inside a `<form>` they used to submit it.
- **`DateRangePicker`** — same date fix and same keyboard/ARIA rebuild, plus **preset ranges**
  (Today, Last 7d/30d, MTD, QTD, YTD, or your own via `presets`) beside the calendar, a live range
  preview shading from the anchor as the pointer moves, `min`/`max`/`disabledDate`, `months`,
  `error`, `clearable`, and a fixed header (the old two-month header put the nav buttons in
  different places per column and padded with empty spans).
- **`FilterBuilder`** — `dayOf()` bucketed by UTC day, so an "on this date" filter silently dropped
  local-evening rows. Now local.
- **`EventTimeline`** — `dayKey()` filed a 7pm New York event under tomorrow's date heading. Now local.
- **`Validators.date`** *(2026-08-27)* — the last of the family. Each `format` branch parsed on a
  different basis: `iso` went through bare `new Date(value)` (local for `2026-6-15`, UTC for
  `2026-06-15`), `us`/`eu` were rebuilt into ISO strings and parsed UTC, and `minDate`/`maxDate` were
  parsed UTC regardless — so the same day passed on one path and failed `minDate` on another for
  anyone east of UTC. All three formats now parse through `localDate.fromISO` to local midnight, and
  bounds parse the same way (`Date` objects accepted). Bound messages quote the **parsed** bound as
  ISO, so a `Date` argument no longer leaks a timezone-stamped JS date dump into field copy. Also **strict**: only the named shape is
  accepted, so a datetime or a foreign format is a clear error instead of a silent
  reinterpretation — an unknown `format` now says so rather than reporting every date invalid.

### Changed — `MultiSelect` is now the multi-pick control it was always documented as (2026-08-26)
**Breaking, and deliberately so.** `MultiSelect` was a bulk-selection toolbar that duplicated
`data/BulkActionBar` — badly:
- its **"Select all" was wired to `onClearSelection()`**, so the primary affordance cleared the
  selection (the source carried a comment admitting the component couldn't select all);
- it passed `indeterminate` as a JSX attribute, which React does not forward to the DOM, so the
  partial state never rendered;
- it was the only component in `core/` styled entirely with inline styles — no tokens, no density,
  hardcoded radii, and `transition: all` against the system's own rule.

Meanwhile `Select.prompt.md` had long told consumers "for multi-pick use `MultiSelect`", and the
system had no multi-pick field at all. `MultiSelect` is now that field: chips in the field, a
checkbox listbox, type to filter, grouped options, `maxSelected`, and bulk actions scoped to the
**currently filtered** set. It reuses `Checkbox` (which handles `indeterminate` correctly, via ref).

**Migration.** Nothing in the system imported the old component, so the blast radius is external
only. If you used the toolbar, switch to `BulkActionBar` + `BulkSelectCheckbox` from `data/` — the
correct implementation of that pattern, and now the only one. The `SelectCheckbox` export is gone;
`BulkSelectCheckbox` replaces it.

### Added — `pickers` card (2026-08-26)
`DatePicker` and `DateRangePicker` had no card at all — nothing in the Design System tab exercised
them, which is part of why the date bug survived. The new Core card demos both against a settlement
calendar (weekends + holidays struck through, `min` chained to the trade date) alongside grouped and
capped `MultiSelect`.

### Changed — `Combobox`, `Stepper`, `SplitPane` brought up to standard (2026-08-26)
Additive APIs; three behavior changes, called out below.

**`Combobox`**
- **Per-instance ids.** Every instance hardcoded `id="mds-cb-listbox"`, so two comboboxes on one
  screen produced duplicate ids and `aria-controls` pointed at whichever rendered first. Ids now
  derive from `useId` (or an explicit `id`).
- **`aria-activedescendant`** now announces the highlighted option, and the menu scrolls to keep it
  visible — arrowing past the 240px fold used to move an invisible highlight.
- The label is a real `<label htmlFor>`, so clicking it focuses the field.
- **Form parity with `Input`** — `error` (message + red border + `aria-invalid`/`aria-describedby`)
  and `required`. `FormField` can now treat the two alike.
- Added `clearable`, `filter`, `onQueryChange`, `name`, option `meta` / `group` / `disabled`, a
  polite result count for screen readers, Home/End/PageUp/PageDown, a working caret toggle button,
  and close-on-focus-out (Tab used to leave the menu hanging open).
- Opening the menu lands the highlight on the **current selection** instead of row 0.
- **Behavior change:** the menu no longer opens on bare focus — it opens on click, ↓, or typing, so
  tabbing through a form doesn't pop menus. Pass `openOnFocus` for the old behavior.
- Memoized option normalization and filtering (both re-ran on every keystroke and every parent
  render).

**`Stepper`**
- **Behavior change:** with no `onStepChange`, steps now render as static text rather than buttons
  that focus and do nothing.
- Added per-step `error` (red mark, `!` for the number), `disabled` and `locked` (forward-locked
  wizards — both unreachable and non-focusable), `description`, and `id` for a stable key.
- `orientation="vertical"` for a left-rail stepper.
- **Density adherence** — steps were pinned at `padding:11px 16px`; they now track
  `--theme-row-height`, overridable with `--stepper-height`.
- Removed a dead `content:'✓'` rule (no-op on a non-pseudo element) and the `transition:all .12s`
  that conflicted with the system's no-transition rule; replaced the CSS-rewrite-on-mount hack with
  the standard one-shot `inject()`.
- **Fixed in `forms-depth` card:** it passed `activeIndex`, which the component never read, so the
  wizard demo always showed step 1 as current.

**`SplitPane`**
- **Fixed: localStorage was written on every `pointermove`** — a synchronous write per frame of
  every drag. Now written once when the drag settles.
- **Fixed: text selected while dragging** and the cursor flickered across panes. The drag now takes
  pointer capture and locks `user-select` + the resize cursor for the window's duration.
- **Fixed: sizes were never re-clamped to the container** — a persisted 900px rail stayed 900px in a
  700px window. A `ResizeObserver` re-clamps on layout change.
- Added `minSecondary` so a generous `max` can't crush the other pane, `"%"` strings for
  `initial`/`min`/`max`/`minSecondary`, `collapsible` (+ controlled `collapsed`/`onCollapsedChange`),
  `disabled`, `onResize`/`onResizeEnd`, and `dividerLabel`.
- `aria-valuetext` (px, or "Collapsed") — a bare px `aria-valuenow` told a screen reader nothing.
- Print rule: the split unrolls to stacked full-width blocks and the divider disappears.

### Changed — `TreeView` brought up to the system's own standards (2026-08-26)
Strictly additive to the API; one behavior change, called out below.
- **Selection and expansion are now independent.** Clicking a branch row used to *always* toggle it,
  so a parent account could not be inspected without collapsing everything under it. The row now
  selects; the caret (or a double-click, or `Space`) toggles. `Enter` selects, `Space` toggles —
  the ARIA tree pattern. **This is the one behavior change**; screens that relied on click-to-toggle
  on the row body should point users at the caret or pass `onToggle`.
- **Controlled expansion** — `expanded` + `onExpandedChange` join `defaultExpanded`, so a tree can
  be driven from URL/store state like every other stateful surface in the system. `onToggle(node,
  open)` fires on every expand/collapse and is the lazy-load hook.
- **Announceable on a flat DOM.** Rows carried `aria-level` but not `aria-setsize`/`aria-posinset`,
  which a flat `role="tree"` requires — a screen reader could not say "3 of 7". Both are now
  emitted per row, alongside `aria-disabled` and `aria-busy`.
- **Keyboard model completed** — printable **typeahead** (700ms run, wraps) and **`*`** to expand
  every sibling at the focused level. `Home`/`End` and all motion now skip disabled nodes.
- **Density adherence** — the row was pinned at 30px, ignoring the density scale. It now reads
  `--theme-row-height` (terminal 26 · compact 28 · standard 36 · spacious 44), overridable per
  instance with `--tree-row-height`.
- **Added to `TreeNode`:** `hasChildren` (declare a branch before its children exist), `loading`
  (busy mark + `aria-busy`), `childCount` (count on a collapsed branch), `disabled`, and `text`
  (typeahead source when `label` is not a string). New props: `guides` (hairline depth rules, on by
  default), `emptyLabel`, and pass-through `aria-label` / `HTMLAttributes`.
- **Fixed** a stale-focus effect that re-ran on every render (the flattened row list is memoized
  now) and a print rule so trees unroll flat without the selection wash.

### Fixed — tick precision, end to end (2026-08-23)
- **A limit price seeded from the ladder rendered `201.1`, not `201.10`.** `OrderTicket` read the
  new `priceDecimals` prop in source while the shipped `_ds_bundle.js` still carried the old
  2-decimal default — source correct, artifact stale. Rebuilt; the prop is now live for
  `OrderTicket`, `DepthLadder`, `OptionChainTable` and `TimeAndSalesTape`.
- **`trading-desk` no longer hardcodes 2 decimals** in the spread readout, the ladder-click toast,
  or the positions `Avg px` / `Last` cells — all four now derive from the instrument's tick via
  `decimals(inst.tick)`. Every symbol in the fixture watchlist ticks at 0.01 or 0.25, so this was
  latent rather than visible; adding a 4-decimal FX symbol would have exposed it. Verified across
  the 0.01 (AAPL) and 0.25 (ESU6) paths: ladder, spread label and seeded ticket agree.

### Added — deployment (2026-08-23)
- **`docs/DEPLOYMENT_CHECKLIST_4_1.md`** — the gate list for putting the three deepened screens in
  front of real desks. Mechanical gates (bundle freshness, `scripts/validate_release.py`, scaffold
  parity), then the part that matters: every action 4.1 added, with the endpoint / idempotency key /
  permission / optimistic-or-confirmed / failure-surface question that has to be answered before it
  touches real capital, a real pipeline, or a real on-call rotation. Names what this repo
  deliberately does not verify.

### Changed — flow verification pass (2026-08-19)
- **`trading-desk` ladder gained a Join / Take intent.** End-to-end testing found the ladder could
  only ever produce passive orders: bid→buy and ask→sell rests at the level clicked, so the fill
  path was unreachable from the ladder and every seeded order came back "Resting on book". `Join`
  (default, unchanged behavior) rests at the clicked level; `Take` crosses from it — click the bid
  to hit it, the ask to lift it — and the toast names which of the two you are doing. No API change.
- **`alerting-workstation` selection now holds the alert, not its row.** Resolving an alert dropped
  it out of the "Needs action" filter and the inspector fell through to `rows[0]` — leaving the same
  buttons in the same place aimed at a *different* alert, so the operator's next click or `r` hotkey
  landed on it. The inspector now keeps the alert it acted on, states that it has left the queue, and
  offers "Show in queue" to follow it into its new state filter.
- **Disabled controls explain themselves** in the trading-desk order drawer and the ingestion job
  inspector, per `guidelines/STATES_AND_ANTIPATTERNS.md` ("disable *and* explain — a silently dead
  control reads as broken"). A filled, rejected or cancelled order and a complete, cancelled or
  just-queued job each now say what their state permits and which action still applies.

### Changed — screen depth pass (2026-08-18)
- **`OrderTicket` gained `prefill`, `guards`, `onDraftChange`** (all optional, no changed defaults).
  A prefill lands once per distinct `token`, so a depth-ladder click or an amend can seed the draft
  without overwriting typing; `guards` renders an `ActionGuardList` of pre-trade checks and any
  `blocking` guard disables submit; the composed order now carries `notional` and the draft clears
  on submit.
- **`trading-desk` is a working order lifecycle**, not a static blotter: ladder clicks seed the
  ticket, submitted orders rest or fill (partial then complete) against the book, fills move real
  positions with avg-cost, realized and unrealized P&L, working orders cancel or replace from an
  per-order audit drawer (`EventTimeline` + executions), and the tape adds market prints alongside
  blotter and fills. New tweaks: `autoFill`, `orderCapUsd`.
- **`ingestion-operations` runs its queue**: progress streams (toggleable), jobs retry from
  checkpoint / pause / raise priority / cancel from a job inspector drawer, a coverage gap-scan
  queues a real backfill that fills the cell on completion, providers scope the queue and the log,
  and a `DialogForm` composes new jobs.
- **`alerting-workstation` commits its triage**: acknowledge / silence / resolve / reopen move the
  alert and write to its history, bulk selection triages many at once, and the inspector is tabbed
  — detail + runbook, history with operator notes, and the pending rule revision behind an
  approve/reject gate that rewrites the threshold.

### Changed — housekeeping (2026-08-17)
- **API family barrels regenerated.** The 37 exports shipped after the 4.0 family snapshot are now
  assigned: `Figure`/`FigureSet` → `analytics`; `DataGrid`, `Registry` → `data`; `ObjectTrail`,
  `HandoverBrief` → `coordination`; `ValueEnvelope`, `AdapterDiagnostics` → `evidence`;
  `ScheduleLadder` → `accounting`; `ScreenComposition`, `WorkspaceLayout` → `shell`. All 284
  exports are again reachable from exactly one family; counts refreshed in `api/README.md` and
  `docs/METRICS.md`.
- **Doc counts re-derived from the manifest** (27 templates = 7 archetypes + 20 production screens;
  284 exports · 185 modules · 118 cards · 665 tokens) across `readme.md`, `templates/README.md`,
  `docs/ARCHITECTURE.md`, `docs/METRICS.md`; `guidelines/PATTERNS.md` and the readme surface table
  now reference the post-4.0 Strategy template names.
- **Dark-mode token audit** (`docs/QA_DARK_AUDIT.md`): components and templates carry no hardcoded
  light-surface colors; two card galleries had `background:"white"` section chrome, now
  `var(--bg-well)`.
- **IBM Plex self-hosted.** TTF binaries for both families (Sans and Mono, 400/400i/500/600/700
  each, OFL licenses alongside) now ship in `assets/fonts/`; `tokens/fonts.css` serves them with
  `local()` short-circuit. The Google Fonts dependency is removed entirely.

### Added — session continuity (track A)
- **`guidelines/SESSION_CONTINUITY.md`** — the contract a screen owes a **returning** operator:
  arrangement, traversal, delta. States the prohibition that governs all three — a restored layout
  carries region size, collapse and visibility, and never a filter, selection, period or as-of,
  because a layout that carries data context makes a stale figure look intentional.
- **`WorkspaceLayout`** (shell) → `WorkspaceLayout` + `WorkspaceLayoutProvider` +
  `WorkspaceLayoutBar` + `WorkspaceLayoutGrid` + `LayoutRegion` + `LayoutHooks`. Named, persisted
  screen-level arrangements (`mds.workspace-layout.<screenId>`, one key per screen) with an explicit
  `Default`, visible dirty state, deliberate save/reset, collapse to a labelled focusable rail, and
  `role="separator"` dividers that resize by arrow key. `SplitPane` stays pane-local and
  `SavedViews` table-local; this is the screen-level layout identity that did not exist.
  `slots` declares regions positionally for markup-first callers (design-component templates).
- **`ObjectTrail`** (core) → `ObjectTrail` + `ObjectTrailProvider` + `ObjectTrailList` +
  `TrailHooks`. Per-operator traversal across the seventeen object types. An entry is a reference
  (`{type,id,version,label}`), never a snapshot, and returning to a moved version is a navigation
  **with a notice** — never a silent load of newer data under an older heading. `Breadcrumb` states
  where an object sits; the trail states where the person has been.
- **`HandoverBrief`** (operations) → `HandoverBrief` + `HandoverBriefChip`. What moved on the
  operator's objects since an explicit last-seen mark, in fixed order — breached → assigned →
  changed → resolved — each row with its own clock and actor. Acknowledgement moves the mark and
  nothing else. Empty is a statement, never a blank panel.
- **Adoption** — `templates/session-start` gains the arrival surface (brief + trail beside the
  sign-in card, chip in the topbar); `templates/adapter-operations` runs on a persisted layout with
  a trail strip.
- Cards: *Workspace layout* (Shell), *Object trail* (Core), *Handover brief* (Operations).

### Added — outbound adapters (track F-1)
- **`guidelines/ADAPTER_SEMANTICS.md`** — five postures (`linked · degraded · retrying ·
  suspended · unconfigured`) for the integrations that carry instructions *away* from the
  workstation, and why they cannot share the inbound vocabulary: a stale read makes a figure old, a
  failed adapter makes an instruction **unlanded**.
- **`AdapterDiagnostics`** (operations) → `AdapterDiagnostics` + `AdapterPostureChip` +
  `ADAPTER_POSTURES` + `AdapterHooks`. Posture, retry schedule (attempt · backoff · next attempt),
  verbatim last error with code and clock, queue depth with its oldest age, signing-key rotation
  clock, and `Version`-aware disabling of mutating controls. A `retrying` row with no next attempt
  renders as the contradiction it is — retrying with no schedule is a suspension in fact.
- **`templates/adapter-operations`** — the reference screen (27th template): diagnostics beside the
  log that produced them and the interventions that answered them. Closes `CONTRACT_AUDIT.md`
  §A-5 / §D-2, carried since July.
- Card: *Adapter diagnostics* (Operations).

### Added — reading depth (track G)
- **`guidelines/PROGRESSIVE_DISCLOSURE.md`** — the depth contract, and the law that governs it:
  **depth is not concealment.** Two reading depths and only two (`summary`, `full`); the list of
  things that may never withdraw (identity, the figure a screen is about, any condition worse than
  normal, the count of what is hidden); the residue a collapsed region owes; and the drill ladder —
  row → drawer → screen, each tier earning the next.
- **`Disclosure`** (core) → `DisclosureProvider` + `DetailDial` + `DisclosureRegion` +
  `DisclosureResidue` + `DetailOnly` + `DisclosureHooks`. One dial per screen, per-region
  overrides, and operator collapse that outranks the dial — persisted per operator
  (`mds.disclosure.<screenId>`) **and** mirrored into `?detail=` so a shared link carries the depth
  its sender was reading at. Arrangement only, never filters or as-of
  (`SESSION_CONTINUITY.md`). A `DisclosureRegion` header is a standing report: collapsed or open it
  states count, worst severity and freshness, so nothing hides behind a chevron. Outside a provider
  every consumer degrades to **full** detail — the failure mode is more, never less.
- **Depth tiers on the three fact surfaces.** `DataGrid` (`detail`, `onDetailChange`, per-column
  `tier`, plus `onDrill` for the tier-2 drawer affordance), `KeyValueGrid` and `EntitySummary`
  (`detail`, `onReveal`, per-item `tier`). Each states how many columns/fields it withheld and
  offers the way back in the same place. Untiered content never withdraws, so existing callers are
  unchanged.
- **Adoption** — `templates/reconciliation` (dial in the chrome; runs, imports and evidence as
  regions that report themselves; break drawer over the plane), `templates/security-master-registry`
  (reference sections collapse at summary depth, identity and classification never),
  `templates/accounting-workstation` (tiered account and close facets),
  `templates/trading-desk` (five ladder levels and only orders still in play at summary depth, with
  the withheld count stated).
- Card: *Progressive disclosure* (Core).
- **Entry inspection and reversal** (`templates/accounting-workstation`) — the tier-2 rung the books
  were missing. A GL row opens the whole journal entry: every line, the Σ debit = Σ credit proof,
  the `EnvelopePanel` for the figure and `EnvelopeCoherence` across its lines, and the one
  correction a posted period allows — **reversal, never edit**. The reversal posts equal and
  opposite lines under a new ref with an `overridden · desk` envelope; both entries stand in the
  ledger, and the original line states *reversed by JE-nnnn* where it is read. Why the control is
  disabled (locked period · viewer role · wrong entity scope · already reversed · voided) is stated
  beside it, not discovered by clicking. Command + hotkey: *Inspect last posted entry* (`I`).

### Fixed
- **`Drawer`'s scrim had no elevation.** `.mds-drawer-overlay` sat at `z-index: auto` while every
  other overlay in the system carries its rung (`Dialog` → `--z-modal`), so any sticky table header
  (`--z-raised`) painted *through* an open drawer — reproduced in the accounting workstation, where
  the ledger's header cut a band across the entry inspector. The scrim now carries `--z-drawer`.
- **`WorkspaceLayoutGrid` gained `slots`** — positional region declarations, one per child. Without
  it a design-component template could not adopt `WorkspaceLayout` at all: an `<x-import>` passes a
  mount, so the grid saw mounts instead of `LayoutRegion` elements and measured nothing. The general
  rule now sits in `guidelines/EXTENDING.md`: **a children-shaped API must also have a
  markup-shaped path**, or it cannot be adopted by any template.
- **Nine accessibility defects in the 4.1 surfaces**, from the static audit
  (`tests/A11Y_STATIC_AUDIT_4.1.md`): `AdapterDiagnostics` rows announced six unlabelled numbers
  (the column strip is `aria-hidden`) and now carry a composed name; `aria-expanded` without
  `aria-controls` on adapter rows and layout regions; `ObjectTrail`'s recents promised a menu with
  `aria-haspopup`; dividers reported a bare `aria-valuenow`; layout *Save*/*Reset* were unnamed;
  **a `hidden` region had no keyboard route back** (the bar's static note is now a *Show N hidden*
  button, restoring the law's rule 5 for the hidden path); dirty state gained `role="status"`; dead
  `pendingName` state removed.

### Still carried
F-2 operations-continuity approvals · F-3 trading depth (`OptionChainTable`,
`TimeAndSalesTape` have no screen) · tracks B, C, D of `docs/UX_DEPTH_4_1.md`; track E's
**annotation** half (`AnnotationLayer`, `MarkupPin`) — track G shipped the reading-depth half.
**F-4 partially:** the static audit shipped; the assistive-technology run is still
*prepared, not executed* — `tests/BROWSER_TEST_PLAN.md` §4.1 states what it must settle, and no
conformance claim was upgraded.

## [Unreleased — 4.0.0] — in progress

One way to state a value, one way to show a table. Plan: `docs/reviews/Release 4.0 Plan.html` ·
census: `docs/adoption-census-4.0.json`.

### Changed (phase 5 — curation)
- **Template set 32 → 26** — five merges, ten folders retired, every retired surface reachable
  as a `view` tweak / URL param on its survivor (the curation gate):
  reconciliation-workstation + reconciliation-exception + close-tie-out → **`reconciliation`**
  (queue → matcher → exception chain); close-tie-out content → **`close-management`**
  (`view=tie-out` drill-in); strategy-onboarding + strategy-builder → **`strategy-formation`**;
  backtest-builder + backtest-compare + covered-call-lab → **`strategy-runs`**
  (`view=backtest|compare|lab`); amx-governance → **`strategy-governance`**;
  journaling-workstation → **`accounting-workstation`** (journal posting is the GL compose
  mode — new `view` + `compose` tweaks and `?view=gl&compose=1` deep link).
  `_strategy-executor/screen-kit.js` `ROUTES` is the suite's single routing table.
- **Picker regrouped by archetype** — the six archetype groups are the only groups: `Operate`
  (ingestion-operations, ops-wall) folded into **Monitor**; `Research` (charting-workstation) and
  `Analyze` (alm-review) into **Investigate**.
- **`templates/PRODUCTION_SCREENS.md` rewritten** as the surface → template + variant map, so
  nothing that merged became unfindable. Live docs and cards updated (GETTING_STARTED, COOKBOOK,
  cookbook + system-map cards, templates/README).

### Changed (phase 5 — value semantics)
- **Value-semantics pass** — every template stating a tie-out or P&L figure now renders it as a
  `ValueEnvelope`: reconciliation's break drawer (statement / ledger / difference trio) joins
  dashboard-workstation, ops-wall, close-management tie-out and ownership-graph. Computed
  analytics (backtest metrics, chart series) and print statements (report-pack) stay plain —
  envelopes are for stated figures, not derivations on the page that derives them
  (`guidelines/VALUE_ENVELOPE.md` boundary).

### Changed (phase 5 — census re-measured)
- **`docs/adoption-census-4.0.json` regenerated** from `_ds_manifest.json` against all 26 template
  folders: **268 exports** (was 280 — `core/icons.js` and `operations/conditions.js` deleted, the
  four generic row-table renderers folded into `DataGrid`), **167 adopted by ≥1 template**, 101 by
  none, of which 79 are card-only. `cut[]` is now empty by design: the two whole-module deletions
  shipped and, per plan E-03, the remaining 22 bar-hitting symbols are exempted vocabularies and
  sub-parts of kept modules (`exemptFromCutBar`).
- **`readme.md` inventory block regenerated** — 181 modules · 268 exports across 7 domains.
- Two deviations recorded in the census: **`WorksheetGrid` kept** (E-01 proposed deleting it; it is
  a cell-addressable worksheet, not a row-record table — `strategy-formation` uses it), and
  **`Figure` now has zero template adoption** — `alm-review` moved to `AnalyticalFigure` /
  `AnalyticalFigureGrid` and `report-pack` draws its own print figures, leaving the survivor chart
  primitive card-only.
- **`Figure`'s claim corrected, not its code.** Its docstring said it retired `CashFlowLadder`,
  `MaturityLadder`, `ScheduleLadder`, `IncomeSchedule` and `DurationContribution`; it did not —
  `alm-review` renders three of them, because each carries a bucket definition, a two-sided
  comparison mode, or per-bucket envelopes that `Figure` has no props for. `Figure` is now documented
  as the primitive for a *new* ladder/plate/distribution/trace, paired with `AnalyticalFigure` for
  the evidence contract. Consolidating the domain ladders onto it is **deferred to 4.1** as plan item
  **D-01**, with a stated acceptance test — it is a component-API change and does not belong in this
  release's last phase. No consumer action in 4.0.
- **`docs/reviews/Release 4.0 Plan.html` restated against shipped numbers** — the headline tally, the
  E-01 adoption table (at-plan vs. shipped) and the E-02 evidence block now read 268 / 167 / 101 / 22;
  E-01 and E-03 are marked resolved, and D-01 is written up as the one deferral.

### Added (phase 5)
- **`templates/ops-wall`** — cookbook recipe 3 assembled: the shared-monitor operations wall on
  the 4.0 stack (`ScreenComposition` monitor regions, desk P&L as a `ValueEnvelope`), terminal
  density + dark + LIVE by default, `severityFloor` tweak filtering alert board and log tail.
- **Migration tooling** — `docs/UPGRADING.md` § → 4.0 (prose) + `scripts/codemod_4_0.py`
  (mechanical renames, template-path remaps, human-needed flags; dry-run by default).

### Removed (phase 4)
- **`DenseDataTable`** — the workhorse is gone; `DataGrid` is the one row-record table. The
  `Blotter` preset now wraps `DataGrid` (public API unchanged); all five card demos and every
  living doc/guideline/prompt updated; `api/data.js` barrel pruned.

### Amended
- **`WorksheetGrid` stays.** The plan slated it for deletion behind "DataGrid `editable`", but it
  is a cell-addressable spreadsheet (formula bar, active cell, grid semantics), not a row-record
  table — PATTERNS itself forbids conflating the two. 4.0's claim is: one row-record table
  (`DataGrid`), one worksheet (`WorksheetGrid`).

### Removed (phase 1 — zero-usage deletions)
- **`ExpandableDataTable` · `GroupedDataTable` · `FilteredDataTable`** — zero template usage;
  subsumed by `DataGrid`'s `expand` / `groupBy` props and `Toolbar`. `aggregateRows` went with
  `GroupedDataTable` (DataGrid's per-column `total` covers it).
- **`core/icons.js`** — the seven `Glyph*` exports; `Checkbox` inlines its two marks.
- **`CONDITIONS` de-exposed** — `operations/conditions.js` is bundle-internal as its header always
  claimed; the vocabulary's public home is `DataCondition.CONDITIONS`.

### Added
- **`DataGrid` sorting + row activation** — the two `DenseDataTable` capabilities the grid
  lacked: `sortable` (internal) or `sortKey`/`sortDir`/`onSort` (screen-owned, indicators only),
  and `onRowClick` + `activeKey` for the committed row, independent of checkbox `select`.

### Added
- **`DataGrid` absorbs the rest of `DenseDataTable`'s contract (phase 3)** — windowed rendering
  past 500 rows (spacers + visible slice; skipped when grouping/expanding), `label`/`caption`
  region a11y, keyboard row activation, select-all header checkbox with indeterminate,
  column management (`onColumnResize`/`onColumnReorder`/`onColumnPin` — grip, drag,
  double-click-pin, Alt+←/→ · Ctrl+←/→ · P), row height tracking `--theme-row-height` when no
  explicit `density`, and `rowKey="index"` for positional keys on static seed tables.

### Changed
- **All 10 remaining `DenseDataTable` screens migrated to `DataGrid` (phase 3)** — alerting,
  analytical-workbench, backtest-builder, backtest-compare, basket-builder (checkbox multi-select
  → key-based `selected`/`onSelectedChange`), blank-workstation, covered-call-lab (×3),
  ingestion-operations, investment-event-ledger, security-master-registry (column management,
  id-keyed selection). `templates/` now has zero `DenseDataTable` references.
- **`dashboard-workstation` rebuilt as the 4.0 reference (phase 2)** — monitor regions declared
  via `ScreenComposition` (context · metrics · primary · secondary · positions · status), the
  blotter on `DataGrid` with a stated proof (Σ unrealized P&L), every stated P&L a `ValueEnvelope`
  (condition · authority · valuation clock, marks on hover).
- Docs stop recommending the deleted renderers (PATTERNS decision tree, GETTING_STARTED,
  COOKBOOK recipe 04, STATES_AND_ANTIPATTERNS, PERFORMANCE, CARD_INDEX, WORKSTATION_BLUEPRINT).

### Consolidation tier — phases 1–4 (shipped in 4.0)

**Conceptual consolidation.** Eight trust vocabularies, eighteen tables, five identical
ladders and thirty-one forked screens collapsed into four contracts. Strictly additive:
every component they subsume remains as a single-facet renderer.

- **`operations/ValueEnvelope`** — one contract for what is known about a figure
  (condition · authority · evidence · clocks · materiality), three fixed disclosure tiers,
  and five coherence rules that run on the data (`envelopeCheck`, `EnvelopeCoherence`).
  Time roles now attach to the value, not the screen. Law: `guidelines/VALUE_ENVELOPE.md`.
- **`data/DataGrid`** — the single tabular primitive. Capabilities are props (`select`,
  `groupBy`, `expand`, `edit`, `totals`, `density`); domain tables are `GRID_SCHEMAS`
  entries (ledger, trialBalance, taxLots, blotter, aging); `proof` makes a table state its
  own arithmetic — "Σ debit = Σ credit ✓" or "Out by 12.40". Law: `guidelines/TABULAR_CONTRACT.md`.
- **`charts/Figure`** — parametric geometries (ladder, plate, distribution, trace) with
  series colour assigned by role in `FIGURE_ROLES` and nowhere else.
- **`core/Registry`** — one vocabulary carrier with alias resolution, so `compact`/`spacious`
  land on the same rows as `dense`/`review`.
- **`shell/ScreenComposition`** + `defineScreen` — a screen is a declaration (archetype +
  regions), validated against its archetype, with drift reported on screen. Regions fill from
  `slots` or from markup children tagged `data-region="<id>"`. Reference migration:
  `templates/close-tie-out/`.

New cards: Value envelope (Operations) · DataGrid (Data) · Screen composition (Shell).

**`templates/family-office` → `templates/ownership-graph`.** The family-office workspace is
redesigned as a general-purpose ownership & control investigation: any entity structure
(corporate, fund, trust), an economic vs voting lens, look-through ownership traced to
ultimate owners as value envelopes (derived authority, filings as evidence), and an
ownership register whose custom proof states whether every entity's equity sums to 100%.
The balance-sheet metrics, commitments and account panels leave — they belong to a
portfolio surface, not an ownership question.

**Decision semantics.** Component coverage stopped being the constraint; adoption of one coherent
operating model started being it. This work makes context → object → evidence → consequence → decision
a set of shared contracts rather than a description of three newer workstations. Strictly additive:
no existing component API changes behaviour, and `--accent` stays as the white-label palette entry.

#### Added
- `core/ValueAuthority` — six authority stages (source · calculated · override · approved · posted ·
  reported) with per-candidate version, actor, effective date and condition. `AuthorityStack` for the
  lineage rail, `AuthorityChip` inline, `AuthorityPanel` for the full candidate table, `resolveAuthority`
  for the resolver. The word is *authoritative*, never *true* — authority is contextual.
- `core/MaterialityAssessment` — policy-backed classification (immaterial · operational · financial ·
  reporting) with quantitative benchmark and a qualitative factor checklist. `MaterialityBadge` inline.
  Materiality is never derived from a tolerance test.
- `operations/ActionGuard` — one eligibility contract: `satisfied` / `warning` / `blocking`, each with
  reason, policy reference, evidence refs and a named remediation carrying its required authority.
  `ActionGuardList` and `ActionAvailability`, which states an unavailable action's reason, resolution
  and authority *before* the click instead of shipping an unexplained disabled button.
- `operations/DecisionFlow` — `DecisionFlowProvider` owns step order, the requirement graph, guard
  results, URL and reload restoration, transition history, and invalidation when the object version
  moves underneath a completed review. `DecisionFlowRail`, `DecisionFlowStage`,
  `DecisionConsequenceBar` (which requires a declared reversibility class), `useDecisionFlow`.
- `api/` — ten public family barrels (`primitives`, `coordination`, `data`, `analytics`, `evidence`,
  `decision`, `accounting`, `trading`, `shell`, `legacy`) as lazy named views of the one runtime
  bundle: `MeridianAPI.decision.DecisionFlowProvider`. Every export belongs to exactly one family;
  `legacy` keeps the flat namespace so nothing in the 3.x line is forced to migrate. 4.0 turns the
  families into real subpath exports without changing the names consumers write today.
- `guidelines/DECISION_SEMANTICS.md` — **normative.** The spine, the four contracts, the reversibility
  classes, permission visibility, bulk population proof, decision provenance, and EvidenceRail /
  ConsequenceRail as *composition patterns* — explicitly not new generic cards.
- `guidelines/TEMPLATE_CONTRACT.md` — **normative.** Per-primitive require-when / do-not-require-when /
  acceptance-test rules, archetype expectations, the data-state versus continuity precedence table,
  the mobile sibling-state rule, and the migration sequence. Compliance is *eligible templates
  conforming*, never *templates referencing*.
- `guidelines/decision-spine.card.html`, `components/core/value-authority.card.html`,
  `components/operations/decision-flow.card.html` — specimens for the above.
- `docs/METRICS.md` — generated from the manifest and the family barrels. Hand-maintained counts are
  how 230 and 240 came to disagree inside one release; this file replaces the prose numbers.
- Tokens: `--link` / `--link-hover` (+ `--interaction-link*` aliases) for text links and text buttons,
  `--brand-signal-ghost` / `--brand-signal-a10` so a warm wash can no longer be mistaken for a
  selected-row surface, and `--theme-row-review` / `--row-review` — the review rhythm as a real
  density-scoped token (terminal 34 · compact 38 · standard 44 · spacious 52) rather than a 44 px
  literal, so guards, conflict, posting and attestation rows scale with every other row in the system.

#### Changed
- **Terracotta interaction uses reclassified** across component sources, cards, guideline cards and
  template inline styles: focus → `--interaction-focus`; committed selection, checked controls, active
  tabs, sorted columns and current steps → `--interaction-selected` / `--interaction-selected-surface`;
  links and text buttons → `--link`; informational status → `--semantic-info`; scenario series →
  `--scenario-signal`; genuine identity uses → `--brand-signal` and its washes. Focus and selection
  are now separately expressed everywhere, which they were not.
- Hardcoded terracotta focus-ring fallbacks (`var(--focus-ring,2px solid #C56343)`) now fall back blue.

#### Notes
- Not yet done: template migration (guidance only this pass, by request), the runnable validator, and
  the 4.0 items — `--accent` removal, structured-only `TimeContext`, `VarianceCell.tolerance`
  deprecation removal, physical folder moves, ESM/subpath distribution, DTCG token source.

## 3.5.0 — 2026-08-12

**Strategy formation — one identity, one artifact.** Completes the six-stage formation redesign
as a first-class capability: `strategy` becomes Meridian's seventeenth object, and formation,
construction, backtesting, runs, comparison, and governance now run on ONE versioned Strategy
identity and ONE immutable run artifact. Basket Builder and Covered-Call Lab adopt the same
contracts as secondary consumers. Ships as an additive minor: no existing prop or callback is
removed; role-semantic migration notes in `docs/UPGRADING.md`.

### Added
- **`core/StrategyModel`** — pure identity/lifecycle/formation carrier: `STRATEGY_LIFECYCLE`
  (draft → validating → blocked/ready-for-review → in-review → approved/changes-requested →
  active/suspended → superseded/retired), the six guarded `STRATEGY_FORMATION_STEPS` (Mandate ·
  Thesis · Universe · Portfolio rules · Risk & governance · Review), stage validation recomputed
  from CURRENT field values, canonical mjcf/1 JSON + first-party SHA-256, hash-bound review
  acknowledgment, checkpoint snapshots, guarded transitions. Family: `coordination`.
- **`operations/StrategyRunModel`** — pure carrier for the immutable **StrategyRunArtifact v1**:
  canonical input hashes, `verifyProof`, `freshness(artifact, draft)` derived centrally (never
  stored), one-time metric derivation from raw series + trades (total/benchmark/excess return,
  ACT/365.2425 CAGR, rolling-peak drawdown, declared-method Sharpe, win rate, holding period,
  turnover, fees — missing evidence yields **null + a validation issue, never a fixture value**),
  finalization deep-freeze, legacy `Run` adapters (`reported` never merges into derived).
  Environment, run lifecycle, validation disposition, proof integrity, and current-draft
  freshness are independent axes. Family: `evidence`.
- **`guidelines/ENTITY_SCHEMAS.md`** — Strategy, StrategyFormationSnapshot, and
  StrategyRunArtifact v1 wire shapes with the five-axis table.
- **ObjectModel** — `strategy` appended as the seventeenth object (identity contract, lifecycle,
  time roles). `OBJECT_TYPES` grows by one; nothing else moves.
- **`templates/_strategy-executor/`** — suite-local controller (`window.MeridianStrategyExecutor`,
  store key `meridian.strategyExecutor.store.v1`): fixture-first persistence with session-only
  fallback on corrupt/unavailable/unsupported storage (visible notice), identity precedence URL →
  store → canonical fixture, `strategy`/`run`/`runA`/`runB` carried across relative navigation
  (artifact JSON never enters a URL), deterministic run engine seeded by the proof hash, shared
  worksheet validation, version- and proof-bound attestations with derived standing/superseded.
  Plus `screen-kit.js` (one nav rail, boot, sampling, field metadata).
- **Tooling restored** (self-contained, offline, stdlib-only): `scripts/generate_inventory.py`,
  `check_design_system_governance.py`, `sync_catalog.py`, `sync_template_scaffold.py`,
  `compile_bundle_extensions.py` + `bundle_extensions.json`, `validate_release.py`, with
  `THIRD_PARTY_NOTICES.md`. Bundle, manifest, adherence registry, and inventory are regenerated
  idempotently — never hand-edited.
- **Tests** — `tests/unit.html` (offline browser suite: schemas, lifecycle, hashing, invalidation,
  revert-to-current, metrics, time coherence, adapters, persistence failures, migrations, URL
  identity, export/manifest parity, the full acceptance journey) and `tests/BROWSER_TEST_PLAN.md`
  (keyboard/ARIA + responsive matrix — prepared, deferred, not claimed).
- API families: `coordination` +3 (`StrategyModel`, `STRATEGY_LIFECYCLE`,
  `STRATEGY_FORMATION_STEPS`), `evidence` +2 (`StrategyRunModel`, `STRATEGY_RUN_AXES`).

### Changed — the seven Strategy Executor screens (one shared draft + artifact)
- **`strategy-onboarding` → Compose · Strategy Formation.** The four-step wizard is replaced by
  the six guarded stages on `DecisionFlowProvider` (persisted per Strategy id/version,
  `formationStage` in the URL), composed with WorkspaceContextBar, ObjectHeader, TimeContext,
  ActionGuardList, ContinuityFrame, and version-conflict handling. Every gate recomputes from
  current values; edits invalidate dependent stages, proof freshness, and review acknowledgment
  even after they were visited. Review checkpoints the Strategy (version bump + frozen snapshot).
- **`strategy-builder`** — the worksheet grid now edits the shared draft's rule statements
  (live-validated; Builder, Formation, and Field & Formula see every edit); Run backtest is
  formation-gated and hands off to Runs.
- **`field-formula`** — composes the same shared statements: cell picker, live validation,
  suggestions, apply-to-worksheet.
- **`backtest-builder` → Compose · Backtest** — snapshots the draft into a new immutable
  artifact; metric tiles/curves/blotter derive from the artifact's raw series + trades; dataset
  edits stale the proof live; export and stage-candidate are gated until revert or rerun
  (revert-to-run-inputs provided).
- **`strategy-runs`** — projects immutable artifacts with derived freshness, proof verification,
  run trace, five-gate rail, rerun/compare/export actions; legacy runs render reported figures as
  provisional.
- **`backtest-compare`** — reads compatible completed artifacts (A/B in the URL): KPI deltas with
  declared methods, overlaid equity, per-run drawdown, configuration diff, proof-bound provenance,
  gated promote-to-review handoff.
- **`amx-governance` → Admin · Governance & Catalog** — real lifecycle transitions
  (start review / request changes / approve) recording version- and proof-bound attestations;
  standing/superseded is derived, so a later material edit supersedes an approval on screen.
- **Adapters.** Basket Builder gains **Use in strategy** (hands basket lines into the shared
  draft's Portfolio rules and opens Formation at that stage). Covered-Call Lab moves to the shared
  StrategyRunArtifact: edited scenarios recompute deterministically, shares/contracts reconcile
  (800 sh → 8 contracts · overwrite ratio), the headline return is labeled **Period return** with
  annualized CAGR alongside (it was mislabeled CAGR), and navigation/actions are repaired.

### Accessibility (additive; migration notes in UPGRADING)
- **Stepper** — progress semantics corrected: an ordered list, not tabs; `aria-current="step"`;
  "Step n of m, state" announced; `label` prop.
- **SegmentedControl** — real radio group with roving focus: `radiogroup`/`radio`,
  `aria-checked`, Arrow/Home/End move and select.
- **Slider** — visible label associated (`aria-labelledby`); `valueFmt` ships as
  `aria-valuetext`.
- **Tabs** — `aria-controls` only for panels that exist; no dangling references.
- **Select** — fully named select-only combobox: `role="combobox"` named by label AND value,
  `aria-controls`, `ariaLabel` fallback.
- **WorksheetGrid** — complete grid semantics with row/column indices, `aria-activedescendant`
  navigation, named cells (ref · value · formula · error), `aria-invalid`, focus restoration
  after keyboard edits; `label` prop.
- **DenseDataTable** — `label`/`caption` name the region and table; Enter/Space row activation;
  `aria-rowcount`/`aria-rowindex` keep virtualized positions accurate; row-named selection;
  keyboard column controls (Alt+←/→ resize · Ctrl+←/→ reorder · P pin).
- **ChartFrame** — `ariaLabel`, `ariaDescription`, `accessibleData` (hidden data table);
  forwarded through EquityCurve, DrawdownChart, Histogram, BarChart, CandleChart, WaterfallChart,
  ScatterChart, DepthChart, YieldCurve. Unlabeled figures stay presentational.
- **StatusBanner / DataStateSwitch** — configurable `announce` (polite/assertive/off); ready
  content is never wrapped in a live region.
- **MetricCard** — accessible name carries value, trend + delta, context, tone, and the new
  `sparklineLabel`; `context`/`hero`/`sparkline` props documented.

## 3.4.0 — 2026-08-12

**Temporal semantics.** Meridian works in twelve kinds of time and, until now, each surface named
them itself — `date`, `asOf`, `timestamp`, `when`. A figure stated in the wrong clock is not
imprecise, it is wrong. This release makes the clock part of the contract. Strictly additive.

### Added
- `core/TimeContext` — the temporal contract. Six exports:
  - `TIME_ROLES` — the ledger: twelve roles, each with its clock (`event` in the world ·
    `book` in the books · `observation` in this figure), precision, owning system, and definition.
  - `TimeContextProvider` — declares the roles once per screen; controlled or uncontrolled.
  - `TimeContextPanel` — the declaration rendered, grouped by clock, with the roles the context
    actually carries and the coherence findings underneath.
  - `TimeRole` — one role inline, for headers, cells, and inspector lines.
  - `TimeHooks` — `useTimeContext`, `checkTimeCoherence`, `formatTimeRole`, `TIME_ROLES`.
  - `TimeContext` — module carrier (`.Provider` / `.Panel` / `.Role` / `.check` / `.use`).
- **Coherence rules.** `checkTimeCoherence` is pure and returns `error` / `warning` / `info`
  findings: settlement before trade, a figure calculated before the evidence it cites, a posting
  outside its reporting period, evidence arriving after the close deadline, a position settling past
  that deadline, a passed deadline on an open period, an as-of preceding its own trade, a valuation
  date silently differing from as-of, an effective date after posting. Run it in gates as well as in
  panels — an attestation must not be signable over an impossible date set.
- `guidelines/TIME_SEMANTICS.md` — **normative.** The three clocks, the role ledger, which roles
  each surface owes, the rule table, and the anti-patterns a single `date` prop creates.
- Card **Core › Temporal semantics** — one declaration, coherent and impossible records side by side.

### Changed
- `templates/reconciliation-exception/` declares its time roles through `TimeContextProvider` and
  renders them in the evidence rail. `AccountingPeriodSelector` still owns *choosing* the period and
  `WorkspaceContextBar` still shows period and as-of in the chrome — all three now read the same roles.
- Formatting is fixed by the contract: dates `2026-07-31`, instants `2026-07-31 23:59:00Z`,
  periods verbatim. No implied timezone anywhere.

## 3.3.1 — 2026-08-11

**Integrity and adoption.** The 3.3.0 chains proved the Release 2–4 components render together;
this patch makes one of them behave like a production workstation and brings the package's own
numbers back into agreement with the manifest. No component API changed.

### Changed
- `templates/reconciliation-exception/` **recomposed**. Was a vertically stacked instructional
  page: nine expanded steps, explanatory paragraphs above each component, selection and workflow
  held in local template state, no context declaration, no degraded-data behaviour, no standard
  controls. Now a three-region workstation —
  - exception queue and **workflow rail** (completed steps collapse to a one-line proof summary;
    blocked steps name the exact unmet guard),
  - **one active decision step** as the dominant work plane,
  - a persistent **evidence and consequence rail** (source coverage, linked evidence, calculation
    trace, audit history) that stays with the selection,
  - a sticky **consequence bar** carrying version, period, residual, unresolved gates and the one
    permitted primary action, disabled with its guard named.
- Adopts the operating language: `DataStateSwitch` → `WorkspaceContextProvider` /
  `WorkspaceContextBar` → `LinkedSelectionProvider` (scope `exception`, URL-synced) →
  `ContinuityFrame` around the work plane. Local `selectedId`-style coordination is gone.
- Workflow position and completed steps are **URL-backed** (`?step=&done=`), so the task resumes.
- Standard template controls restored: `density`, `theme`, `environment`, `dataState`.
- Instructional prose replaced with operational state statements ("$1,050.00 of the $1,381.68
  difference is explained… the remaining $331.68 blocks close control C-104").
- Interaction colour corrected: selection rails and selected rows use `--interaction-selected` /
  `--interaction-selected-surface`, focus uses `--interaction-focus`. No terracotta in
  interaction state on this screen.
- Decision density raised — 13px body, 11px minimum ordinary label, sentence-case section labels,
  36–44px action and queue rows.
- Real narrow-layout siblings: at ≤1180px the three regions become one plane with a
  Queue / Step / Evidence switch and a sticky bottom action bar.

### Fixed
- Documentation drift. `readme.md` (28 → **31** templates, 21 → **24** production screens,
  225 → **230** exports, 172 → **173** modules, 635 → **644** tokens, five exports missing from the
  inventory: `MATURITY_BUCKETS`, `SelectionHooks`, `ChangeSetHooks`, `Status`,
  `WorkspaceHooks`), `docs/ARCHITECTURE.md` (L5 template count),
  `docs/PERFORMANCE.md` (640 KB / 135 KB → **1,141 KB / ~247 KB**).
- `docs/RELEASE_NOTES_3.3.1.md` and `docs/QA_REPORT_3.3.1.md` added; the package's release
  documentation now names the release it ships.

## 3.3.0 — 2026-08-11

**The chains, demonstrated.** Releases 2–4 shipped seventeen components that were specified to
compose; nothing in the system showed them composing. This release adds three template chains that
walk the roadmap's own sequences with working state, so the contract between components is
executable rather than prose. Strictly additive — no component API changed.

### Added
- `templates/reconciliation-exception/` — the Release 2–3 decision chain on one exception, nine
  gated steps: `VarianceCell` → `TieOutPanel` → `EvidencePreview` → `CalculationTrace` →
  `ResolutionDrawer` → `ChangeSetReview` → `PostingImpactPreview` → `VersionConflictNotice` →
  `AttestationPanel`. Steps unlock only when the prior step is genuinely completed; the staged
  change set validates live, the posting stays blocked until warnings are acknowledged, and the
  attestation binds to the version that survives conflict resolution.
- `templates/close-management/` — close governance on one selection contract. `CloseCalendar`'s
  three views, `ControlDependencyGraph` (graph and outline), `SourceCoverageSummary` with
  click-to-filter population, `ResolutionDrawer` on the root blocker, and `AttestationPanel`
  gated on it. Resolving price approval C-118 propagates through the graph, the calendar, the
  critical path, the coverage denominator, and the attestation gates.
- `templates/alm-review/` — the Release 4 visualization chain: `CashFlowLadder`,
  `MaturityLadder`, `DurationContribution` and `BasisComparisonMatrix` over one horizon
  selection, with `CalculationTrace` and `EvidencePreview` reachable from any selected value.
  Base and +200bp scenario toggle across all four views; unscheduled cash flow, unknown maturity,
  and a position with no available duration stay visible rather than being dropped.

### Notes
- The three chains are production screens, not archetypes — they consume the system through the
  compiled bundle exactly as a downstream project does.
- Adopting a chain does not require adopting all of it: each step is an ordinary component mount,
  and the sequencing lives in the template's own logic class.

## 3.2.0 — 2026-08-11

**Hardening the operating language.** The six primitives 3.1.0 introduced are brought to the point
where a production screen can adopt them without working around them. Strictly additive — no prop,
default, or export changed or removed; a 3.1.0 consumer upgrades untouched. Screen adoption is 3.3.

### Added
- `LinkedSelectionProvider`: `scope` names a provider so one screen can host several independent
  linked groups, reachable by `useLinkedSelection("name")`. `multiple`, `max`, and `items` add
  additive selection with `toggle` / `add` / `remove` / `selectAll` / `selectRange`;
  `LinkedSelectionTarget` reads ⌘/Ctrl and Shift. `history` / `canGoBack` / `back()` walk committed
  selections. New hook surface: `selections`, `selectedIds`, `count`, `scope`, `multiple`, `items`.
- `Datum`: `svg` renders inside a plot's own `<svg>` and shares its coordinate space; `to` makes a
  range band for spans. New `DatumGroup` export measures its container and assigns label collision
  lanes so crowded labels stack instead of overprinting.
- `ContinuityFrame`: `since` derives and ticks the age on an adaptive cadence; `dim` (default
  `"auto"`) fades and marks retained content on stale/offline/error; `onRetry` / `retryLabel` /
  `retrying` render a recovery control; `retainedLabel` tunes the corner mark.
- `WorkspaceContextBar`: `collapse` (`"auto"` default / `"never"` / `"always"`) measures the bar and
  folds low-priority facets into a `More +N` disclosure — scenario → currency → book → as-of →
  period; entity, data condition, and a locked period never collapse. `minVisible`, `overflowLabel`.
- `AnalyticalFigure`: `variant` (`"standard"` / `"small-multiple"` / `"print"`). New
  `AnalyticalFigureGrid` export tiles small multiples under a declared `sharedScale`, `source`, and
  `note`. A print layer now applies to every figure.
- Four specimen cards for the hardened states, and `guidelines/OPERATING_LANGUAGE.md` — normative
  vocabulary, rule/test/anti-pattern per primitive, restraint clause, adoption matrix by archetype,
  and the seven-point screen checklist.

### Changed
- Retained content in `ContinuityFrame` is dimmed and corner-marked on stale, offline, and error —
  the states 3.1.0 already locked read-only. The lock is now legible; which states lock is unchanged.
- `AnalyticalFigure` carries a variant class on its root (`mds-figure--standard` by default).

### Known gaps
- `Capsule`'s `movable` and `resizable` still render affordances that do nothing. Marked do-not-use
  in `OPERATING_LANGUAGE.md` pending wiring.
- No template consumes the six primitives yet — release 3.3.

## 3.1.0 — 2026-08-11

**Coordinated analytical operating system.** Adds 25 modules and 34 public exports for shared
workspace context, linked selection, last-known-good continuity, analytical figures, accounting
proof, close/posting consequence, staged changes, concurrency, attestation, and evidence coverage.

### Added
- `Datum`, `Capsule` family, `ContinuityFrame` family, `LinkedSelectionProvider` family, and
  `WorkspaceContextBar` family.
- `AnalyticalFigure`, `AnalyticalField`, `CashFlowLadder`, `MaturityLadder`,
  `DurationContribution`, and `ControlDependencyGraph`.
- `VarianceCell`, `TieOutPanel`, `CalculationTrace`, `BasisComparisonMatrix`,
  `RollforwardMatrix`, `AccountingPeriodSelector`, `CloseCalendar`, and `PostingImpactPreview`.
- `ChangeSetReview` family, `VersionConflictNotice`, `ResolutionDrawer`, `AttestationPanel`,
  `EvidencePreview`, and `SourceCoverageSummary`.
- Ten coordinated specimen cards, the idempotent extension compiler, catalog synchronization,
  a restored contrast checker, and a single-command release validator.

### Changed
- Primary `Button` actions use the graphite/paper structural axis; terracotta remains the brand
  and scenario signal. Link actions use interaction blue.
- Retuned muted text, accent-label ink, pressed accent, white-label hover states, and faint chrome
  text so the automated light/dark and white-label contrast matrix is clean.
- README inventory and card catalog are generated from manifest and `@dsCard` source metadata.

### Fixed
- Removed remaining `!important` declarations, stopped fixture seed exports from reaching the
  public namespace, restored missing contrast automation, and brought gallery coverage current.

## 3.0.1 — 2026-08-10

### Changed
- **Accounting tables now carry header semantics.** `scope="col"` on every `<thead>` cell in
  `TrialBalance`, `TaxLotTable`, `FxRevaluationTable`, `StatementTable`, `AllocationEditor`,
  `JournalEntryForm`, `AgingTable`, and `ReconciliationPanel` — screen readers announce the
  column with each figure instead of reading a bare number. `LedgerTable`, `IncomeSchedule`, and
  `BasisBridge` already had it. Accessible names added to the two label-less header cells
  (`StatementTable`'s line-item column, `ReconciliationPanel`'s match checkbox). No API change,
  no visual change.
- **`templates/asset-sheet` extended.** The three placeholder wells are now real content:
  an overview page (lede, at-a-glance grid, when-to-use / what-it-replaces), a components page
  showing live `StatementTable` · `TrialBalance` · `RollforwardTrace`, and a specifications page
  with a prop table, consumed-token list, and a three-signature sign-off block. Paging gained
  `←`/`→` keyboard nav, footer prev/next buttons, a rail progress meter, and `aria-current` on
  the active rail item. Page count is data-driven — a blank `pageNTitle` drops the page, and a
  spare fourth page is there to be named.

### Removed
- Empty `templates/asset-sheet/` scaffold — the sheet is a product screen and lives in
  `templates/asset-sheet/` (3.0.0 split); the stub reached no picker and held no content.

## 3.0.0 — 2026-08-09

**The split: design system vs. product frontend.** 28 templates was a product's screen library
wearing a design-system badge — every retone, contract change, and scaffold sync cost ×28.
`templates/` now holds **7 archetype templates**, one reference composition per picker group
(Start `blank-workstation` + `session-start` · Monitor `dashboard-workstation` · Investigate
`analytical-workbench` · Compose `basket-builder` · Report `report-pack` · Admin
`settings-admin`). The other **21 production screens moved verbatim into their own tier** — staged upstream for
extraction into a separate project. In this project they are back in `templates/`, since that is
the tree the template picker indexes.

### Breaking
- **21 templates left the picker**: accounting-workstation, alerting-workstation, amx-governance,
  asset-sheet, backtest-builder, backtest-compare, charting-workstation, covered-call-lab,
  family-office, field-formula, ingestion-operations, investment-event-ledger,
  journaling-workstation, reconciliation-workstation, report-library, report-scheduler,
  security-master-registry, strategy-builder, strategy-onboarding, strategy-runs, trading-desk.
  Copies keep working — folder depth is unchanged, so `ds-base.js` and every intra-suite link
  resolve as before. Migration: `docs/UPGRADING.md › → 3.0`.
- **`session-start` regrouped Admin → Start** — it is the entry gate, and Start otherwise held
  only the bare shell.

### Changed
- **Governance scope follows the split**: the full template contract (viewport/main/h1/badge/
  tweaks/DataStateSwitch/$preview/naming) now binds `templates/` only. `templates/`
  keeps the local-link, raw-hex/radius/!important, and scaffold-drift gates — all 21 screens met
  the full contract at split time.
- **`sync_template_scaffold.py` syncs both trees** while the screens are staged here.
- **`alerting-workstation`'s dashboard nav route** repointed across the boundary
  (`../../templates/dashboard-workstation/`) — the one cross-tier link; noted for extraction.
- Repointed references in `readme.md`, `CONTRIBUTING.md`, `docs/ARCHITECTURE.md`,
  `docs/UPGRADING.md`, `guidelines/PATTERNS.md`, `guidelines/VISUALIZATION_GRAMMAR.md`,
  `github.md`, and `index.html` (fallback stat 28 → 7).

### Added
- **`templates/PRODUCTION_SCREENS.md`** — the 21-screen index with groups and one-liners, the
  cross-link map, scaffold/governance status, and the extraction plan.

## 2.1.0 — 2026-08-09

**T-01 closed: the editor can reach every template.** The markup-in-template conversion that
1.25.0 started on `dashboard-workstation` now covers all 28 entries — headings, panel titles,
table column labels, form sections, dialogs, and empty-state prose live in each `.dc.html` as
directly editable markup, with binary UI states on `data-*` attributes styled by tightly-scoped
helmet rules. `docs/reviews/Template Improvements.html › T-01` is marked resolved.

### Changed
- **Six templates converted from `screen.jsx` monoliths**: `report-library` (catalog with
  row-expand, readiness rail, parameter dialog), `report-scheduler` (packs table, delivery
  history, detail card), `settings-admin` (validation stack, recipients, typed purge confirm),
  `security-master-registry` (saved views, filter builder, bulk actions, five-tab entity
  inspector with staged edits), `trading-desk` (watchlist, depth ladder, ticket, tape), and
  `reconciliation-workstation` (break queue + split-pane matcher with cross-highlight and the
  break drawer — both panes, one file).
- **`security-master-registry/registry-table.jsx`** is the one deliberate React helper left: the
  column-managed `DenseDataTable` (resize/reorder/pin/persist) around `TableHooks`, mounted as a
  named `x-import`. Everything around it is markup.

### Removed
- **17 dead `screen.jsx`/`matcher.jsx` files** — the six live monoliths above plus ten leftovers
  in folders converted earlier (accounting, basket-builder, charting, covered-call-lab,
  family-office, field-formula, ingestion-operations, journaling, strategy-builder,
  strategy-runs). Template folders now ship only what the entry actually loads.

## 2.0.0 — 2026-08-07

**Housekeeping major.** Executes the removal 1.24.0 announced, and brings every advertised count
back to the census (`_ds_manifest.json`).

### Removed
- **`Modal`, `ModalHeader`, `ModalBody`, `ModalFooter`, `ModalForm`** — the deprecated aliases over
  `Dialog`, slated for removal in 1.25.0 but still shipping. `Dialog` is the one overlay; width is
  `size`. Migration: `docs/UPGRADING.md › → 2.0`. Inventory is now **140 modules → 189 exports**.

### Changed
- **The docs tell one story again.** Stale counts corrected everywhere they had drifted:
  `readme.md` (template index 29 → 28; 193 → 189 exports; core 51, data 21 modules), `index.html`
  (V2.0 stamp; fallback stats to 189 / 28 / 622 / 48 / 88), `docs/GETTING_STARTED.md`
  (160/26/389 → 189/28/622, per-domain counts, a missing Trading section, seven-screen strategy
  suite, bundle path), `github.md` (26 → 28 templates). Every remaining `Modal` mention in
  guidelines, docs, tests, and prompts now reads `Dialog`; the unit test migrated with it.
- **Review artifacts archived.** `Adversarial Review.html` and `Template Improvements.html` moved
  from the project root to `docs/reviews/` — historical records of the 1.24/1.25 remediation, not
  live law. The one item still open from them (markup-in-template conversion beyond
  `dashboard-workstation`, T-01) is tracked in that document. *(Closed in 2.1.0.)*

## 1.25.0 — 2026-08-06

**Templates become templates.** The set rendered 29 credible screens but under-delivered as
copy-and-edit starting points: content unreachable to the editor, five forked scaffolds, a contract
enforced on a minority of folders, and overlapping surfaces nobody pruned.

### Added
- **`DataStateSwitch`** (`data/`) — the whole data-state ladder as one wrapper: children render only
  in `ready`; `loading`, `empty`, and `error` paint a full-surface slot with a shared header strip.
  **Every template** wires it to its `dataState` tweak, so a copy ships all four states.
- **Standard tweak set on all 28 templates** — `density`, `theme`, `environment`, `dataState`, plus
  `$preview` at each screen's true working size (previously 2 of 29 declared one). The first three
  drive the system's own token scopes (`<html data-theme>`, `<body data-theme-density>`,
  `<html data-environment>`), so every template now demonstrates the theming it is built on.
- **`scripts/sync_template_scaffold.py`** — writes the canonical `ds-base.js` (from
  `templates/_scaffold/`) and the current `support.js` runtime into every folder; `--check` fails on
  drift. Scaffolding is generated, not authored.
- **Governance gates**: `template-entry-name`, `template-preview-size`, `template-tweaks`,
  `template-data-state`, `template-env-badge`, `template-scaffold-drift`. The README contract is now
  asserted instead of aspirational.

### Changed
- **`dashboard-workstation` is the reference for editable templates.** Its content — headings, KPI
  row, panels, table, banner — moved out of `screen.jsx` into the `.dc.html` as markup a consumer can
  click and edit; only the SVG equity curve stayed React (`chart.jsx`), and the frame (topbar, nav
  rail, status bar) is composed by the logic class. The remaining templates still mount a `screen.jsx`
  and are queued for the same conversion.
- **One scaffold.** Five `ds-base.js` variants and five `support.js` versions (53,959 → 69,134 bytes)
  collapsed to one each. Templates that loaded a partial token list now get dark mode, contrast
  modes, and print like the rest.
- **`ds-base.js` guarantees the environment badge.** Screens built on `WorkstationTopbar` render
  their own (now marked `[data-environment-badge]`); everything else gets a fallback chip driven by
  `<html data-environment>` — the contract's Live/Paper/Fixture visibility went from 7 of 29 to all.
- **Template names carry a group** — `Start · Monitor · Investigate · Compose · Report · Admin` —
  and descriptions were tightened under the picker's truncation point.
- **`analytical-workbench` and `investment-event-ledger`** gained the `<h1>` the contract required.

### Removed
- **`split-pane-reconciliation`** — merged into `reconciliation-workstation`, which now carries both
  panes of the same job under a `view` tweak (`queue` · `matcher`). Also removes the one entry
  filename that broke the PascalCase rule.
- Three orphan `templates/*/index.html` files (charting, dashboard, security-master-registry).

## 1.24.0 — 2026-08-06

**Adversarial-review remediation.** One shell, one overlay, one bar, one composition doc, one row
height — and a governance gate for each hole that let the duplicates in. Removes ~4.2 MB of
unreferenced weight (roughly 40% of non-source bytes).

### Changed — the system now obeys itself
- **`WorkstationShell` is adopted, not just advertised.** `blank-workstation` (the reference
  shell), `dashboard-workstation`, `alerting-workstation`, and `journaling-workstation` now compose
  their frame from it instead of hand-rolling `display:flex` chrome. The shell renders the `<main>`
  landmark and gained `archetype`, `label`/`labelledBy`, and `asideLabel`; the governance checker
  accepts it as the landmark so screens are never told to hand-roll a second one.
- **Row height has one answer: the density token.** The `--theme-row-height` CSS fallback in
  `DenseDataTable`, `FilteredDataTable`, `CaseQueue`, and `useThemeRowHeight` moved from the retired
  40px to the standard scope's **36px**; the retired `cozy` scope name is gone from every comment,
  doc, and specimen (terminal 26 · compact/dense 28 · default/standard 36 · spacious/review 44).
- **One overlay.** `Dialog` gained `size` (`sm` 420 · `md` 520 · `lg` 720) and `DialogForm` (the
  ported `ModalForm`). `Modal`, `ModalHeader/Body/Footer`, and `ModalForm` are now thin deprecated
  aliases — **removed in 1.25.0**. `journaling-workstation` migrated, and its hand-rolled
  `.journ-modal-*` CSS went with it.
- **One bar.** `Gauge` (radial dial) and `LinearGauge` are **removed** — zero template usage, and a
  120px SVG arc is the least information-dense way to show one scalar. `ProgressBar` absorbs the
  need: new `max` and `target` (a dotted datum at the reference ratio).
- **No `!important` in component source.** `TrialBalance` (cascade order), `LedgerTable`
  (`AmountCell mode="muted"` instead of a CSS override), and `CorrelationHeatmap` (diagonal colour
  computed inline, where the cell background already lives) were fixed at the cause.

### Added
- **`GroupedDataTable` promoted** — shipped for two releases as an untyped ghost export; now has a
  `.d.ts`, a `.prompt.md`, and a card. `aggregateRows` is documented alongside it.
- **`TimeAndSalesTape` promoted** — same treatment: types, prompt, and a Trading card.
- **Two governance gates**: `component-typing` fails any `components/` module that exports a
  capitalized symbol without a sibling `.d.ts` (the hole that hid three ghost exports while the
  gate reported clean), and `important-declaration` fails `!important` in component/template source.

### Removed
- **`DockGrid`, `ApprovalRequest` + `ApprovalsInbox`** — 48 KB of speculative, untyped, undemoed
  code with no home in any of the 29 templates.
- **`ui_kits/`** (4 kits) — documented as "not the recommended starting point", never synced, and
  `ui_kits/dashboard/components.jsx` was compiling into the bundle and assigning a dark-themed
  `Button` / `DenseDataTable` / `WorkstationShell` onto `window`, colliding with the real ones.
  The four templates cover the same surfaces under the enforced entry contract.
- **`guidelines/WORKSTATION_BLUEPRINT.md`** — the fourth composition guide, teaching the hand-rolled
  shell and a stale size table. Replaced by **`docs/WORKSTATION_BLUEPRINT.md` v2.0** (archetypes,
  surface hierarchy, data-state model, quality gate) with a card in the Documentation group; folder
  anatomy and the seed-data load-order rule now live only in `templates/README.md`, and geometry is
  a pointer table — no fourth copy of the pixel values, no scaffold-time marketing claim.
- **`docs/changelog/`** (5 files) — its own index warned readers off three of four documents; the
  fourth described "108 components / 6 domains" against today's census. `CHANGELOG.md` is the history.
- **`Meridian Design System.html`** (616 KB bundled export) and **`exports/`** (a forked second
  landing page) — build artifacts at the project root. `index.html` is the landing page.
- **`uploads/preview/`** (3.5 MB, 20 files) — absorbed v2.1 staging material including a parallel,
  unversioned token layer, in the folder the governance checker quarantines.
- **`assets/brand/meridian-symbol{,-light,-monochrome}.svg`** — byte-alike aliases of the mark trio.
- **`Button.stories.html` / `Input.stories.html`** — a 2-of-189 documentation pattern that read as
  "only these two are documented"; `docs/COMPONENT_API_REFERENCE.card.html` is the one that scaled.
- **`tests/qa-hifi-pass.html` / `tests/verify-new-components.html`** — dated review-session artifacts.

### Renamed
- Cards named after the release they shipped in now say what they contain:
  `new-components` → `combobox-tree-accordion`, `new-primitives` → `content-primitives`,
  `data-extras` → `sparkline-empty-columns`.

## 1.23.0 — 2026-08-06

**Foundations v2.1 token retone.** Every token value now comes from the Foundations & Primitives
sheet. Token **names and component APIs are unchanged** — this is a value-level release, but a
visible one: surfaces, ink, focus/selection color, type ramp, spacing base, and density heights all
move.

### Changed — color
- **Material hierarchy replaces the stone/white ladder.** Paper (canvas) `#E7E3DC → #F2F0EB` · Plate (panel) `#FBFAF8 → #F8F7F3` · Recessed/rail (`--bg-medium`) `#F6F4EF → #EBE8E1` · **Well** (`--card-surface-raised`) `#F5F2EB → #FCFBF8` — data surfaces now read *brightest*, insets recede toward the canvas. New `--surface-overlay-base #FFFFFF`.
- **Focus and selection left the brand.** `--border-focus` is now the interaction blue `#2D6FB3` (was terracotta), with a new family: `--focus`, `--selected #496C8F`, `--selected-surface #E7EDF2`, `--interaction-hover #DDE5EC`. `--bg-hover #F1EEE7 → #EDEFF0` and `--bg-active #F4E3DA → #E7EDF2` follow. Terracotta is now strictly signal: primary action, active-nav indicator, crosshair, scenario series.
- **Signal + semantics retuned**: accent `#C56A4A → #C56343` (pressed `#A64E33`, hover `#D2795A`); green `#4A7C5E → #2E7655`; red `#A6433F → #B54B42`; orange `#9A7113 → #986717`; information `#5D6F7F → #2F648A`. New semantics for the condition model: `--pending #7A6B52`, `--neutral #6D757D`, `--missing #A34A42`, plus flat condition washes `--green/red/orange/info-surface`.
- **Ink and rules cooled**: text `#22252A/#4E5258/#69717A/#9BA0A6` → `#20252B/#535C65/#6B737B/#A4ABB2` (muted is held one step darker than the spec's `#7A828A` to keep AA on plate); rules `#DAD8D3/#C6C2B8/#B8B3A8/#E4E3DE` → `#D2CDC3/#BEB8AD/#AAA49A/#E2DED5`. New `--ink-inverse`, `--brand-field #5D7286` (the Field motif), `--brand-axis #29323A` (the Datum ink).
- **Chrome** moved from neutral graphite `#2E2E2E` to the axis graphite `#29323A` with cool interior details; **dark mode** moved from warm carbon to cool slate — canvas `#141312 → #11171D`, plate `#201E1C → #171E25`, rail `#1D252D`, rules `#2C3640`, ink `#F1EFEA/#BEC4CA/#8B949D`, signal `#DC8E6E → #D88466`, focus `#5C9FE5`, selection `#2C4054`.
- ~1,700 legacy hex fallbacks across components, cards, templates, docs, and the landing pages were swept to the new values, so `var(--token, #fallback)` pairs stay truthful.

### Changed — type, space, motion, density
- **Type ramp v2.1**: display/editorial `56/64 → 48/52`, page title `24/32 → 22/28`, section `18/26 → 16/22`; new `--type-workspace 28/34`, `--type-data-medium 18/24`, `--type-body-dense 13/18`, `--type-body-review 15/22`, `--type-label-lg 11/16`, `--type-micro 10/14`, `--lh-label`, `--lh-metric`, and `--weight-data 450`.
- **Spacing is a 4px base**: `--space-xs 3→4`, `--space-sm 6→8`, `--space-3xl 64→48`, new `--space-4xl 64`.
- **Radius scale completed**: new `--radius-analytical 2`, `--radius-overlay 6`, `--radius-dialog 8`, `--radius-pill 999`.
- **Motion**: new `--motion-instant 80ms`, `--motion-reflow 240ms`, `--motion-deliberate 280ms`, `--ease-exit`; `--ease-standard` is now `cubic-bezier(.2,0,0,1)`.
- **Density modes map to v2.1**: `compact` = dense 28px rows (was 32), default = standard 36 (was 40), `spacious` = review 44 (was 48); `terminal` stays 26. Each scope now also sets `--theme-control-height`, `--theme-min-target`, `--theme-body-size`, `--theme-label-size`.
- **New anatomy tokens**: `--table-row-height`, `--table-header-height`, `--selection-rail`, `--capsule-header-height/-footer-height/-h-sm|md|lg|xl`, `--drawer-width-min/-max`, `--drawer-action-height`, `--stroke-hairline/-emphasis`, the `--opacity-*` triad, and the `--grid-*` column/gutter/margin set.
- The visualization layer now resolves through the base tokens: `--series-actual → --brand-axis`, `--series-prior/benchmark → --chart-compare-1/2`, selection tokens → `--selected*`, pending condition → `--pending`, so a white-label or dark flip carries the whole ledger.

### Docs
- `guidelines/TOKEN_REFERENCE.md` gained the material-hierarchy table, the interaction-family rule, the density-mode table, and the anatomy/opacity/grid sets; `VISUAL_FOUNDATIONS.md`, `BRAND_GUIDELINES.md`, `readme.md`, and the landing pages were rewritten to the new palette names (Paper · Plate · Well · Recessed · Overlay · Signal).

## 1.22.0 — 2026-08-06

**Visualization & analytical grammar (v2.1).** The system gains an evidence layer: color assigned by
series role, ten named data conditions, comparison geometry as a token contract, and an event-first
investment-accounting vocabulary. Additive — existing component APIs and template markup are unchanged.

### Added
- **`tokens/visualization.css`** (112 tokens, imported after `colors-dark.css`) — the **series-role ledger** (`--series-actual/prior/benchmark/scenario/target/estimated` + semantic roles), stroke/dash/marker scales, model-interval band colors, plot furniture, the visualization type scale, the comparison-plate geometry contract (`--plate-*`), evidence-inspector widths (`--inspector-*`), responsive breakpoints, **ten data-condition triads** (`--cond-<condition>-fg/-bd/-bg`), connection-state colors, and the v2.1 material-hierarchy aliases (`--surface-paper/plate/well/recessed/rail/overlay`, `--signal`).
- **`charts/TraceChart`** — the annotated line primitive: roles instead of colors, endpoint direct labels with deterministic collision resolution, range/bracket/reference annotations, model bands, committed selection with a datum rail, ←/→/Home/End keyboard selection, and a visually-hidden exact-value table (`Missing`, never blank).
- **`charts/ComparisonPlate` + `PlateGrid`** — shared-scale small multiples; the grid derives one domain and tick set for the whole set, so a plate can never scale itself to its own data. Selection rails and tints a plate; it is never resized.
- **`charts/IntervalBar`** — labelled quantile rows for model ranges, with the required "this is model uncertainty, not data freshness" note.
- **`charts/LineageGraph`** — layered, orthogonally-routed provenance/dependency graph (longest-path layering, stable node order, labels outside nodes, HTML node buttons over an SVG edge layer) plus the accessible node-and-relationship outline.
- **`operations/ClaimHeader`** — the reading-path opener: context path, claim, caveat, qualifying state chips, and the controls that change the view.
- **`operations/DataCondition` + `DataConditionValue` + `DataConditionLegend`** — the ten-condition vocabulary (`confirmed · provisional · estimated · stale · overridden · missing · na · pending · exception · reconciled`), its notation (`4.35% Est.`, `4.35% · Stale 18m`, `Missing`, `—`), and the data-state contract block. Loose status strings normalize (`Matched` → reconciled, `BreaksDetected` → exception).
- **`operations/EvidenceInspector` + `EvidenceSection` + `ProvenanceTrace`** — the synchronized rail: selected value → evidence → contract → model range → lineage → next action.
- **`operations/ConnectionState`** — live · delayed · partial · offline · error, stated beside the evidence, with the last-known-good rule.
- **`accounting/LifecycleRibbon` + `LifecycleLanes`**, **`IncomeSchedule`**, **`RollforwardTrace`** (tie-out proved on screen, "Out by …" when it does not), **`BasisBridge`** (statutory / GAAP / tax treatments with changed cells marked structurally).
- **`Money.effectiveYieldSchedule()`** — constant-yield coupon/accretion/income/book-value schedule, so the schedule, the rollforward, and consumer figures agree to the cent.
- **`guidelines/VISUALIZATION_GRAMMAR.md`** (+ Documentation card) — the analytical law: job model, reading path, series-role ledger, direct labeling, scale integrity and financial notation, plates, tables, uncertainty vs. data condition, live/degraded behavior, interaction states, persistence scopes, graph layout, responsive and accessibility contracts, renderer budgets, motion verbs, annotation, and the 22-question QA gate with its prohibited deviations.
- **`templates/analytical-workbench/`** — the reference implementation of the grammar (claim, trace chart, evidence inspector, plates, exact-value table, lineage graph).
- **`templates/investment-event-ledger/`** — event-first investment accounting: one captured trade resolved into lifecycle, position, generated journal, income schedule, book-value rollforward, basis bridge, and the four override resolutions.
- New cards: **Analytical workbench**, **Plates & lineage** (Charts), **Data conditions** (Operations), **Investment event** (Accounting), **Visualization Grammar** (Documentation).

### Changed
- **Chart series aliases resolve through the role ledger** — `--chart-primary` is now `--series-actual` (graphite ink in light, paper in dark), `--chart-compare-1` → `--series-prior`, `--chart-compare-2` → `--series-benchmark`, `--chart-scenario` → `--series-scenario`. Existing charts inherit the ledger with no code change; primary traces read as ink rather than navy, matching the v2.1 identity sheet.
- **`guidelines/STATES_AND_ANTIPATTERNS.md`** — the five *interface* states are now paired with the ten *data conditions* and the connection contract; five new anti-patterns (detached legend, independently scaled plates, terracotta as loss, hover-only values, force-directed lineage).
- `readme.md` inventory and template index updated (140 modules → 193 exported symbols; 29 templates).

## 1.21.0 — 2026-08-05

Organization & governance release. No API, token, or rendered-style changes.

### Added
- **`docs/ARCHITECTURE.md`** — the compile model, the L0–L5 layer/dependency rules (imports point down, never up or sideways), component trio anatomy, generated-file inventory, and the quality-gate table.
- **`CONTRIBUTING.md`** — 60-second orientation plus checklists: add a component (trio + specimen + gates), add/change a token, add a template, release protocol, don'ts.
- **Folder READMEs** — `components/`, `templates/`, `tokens/`, `tests/`, `scripts/`, `guidelines/`, `docs/`, `ui_kits/` each index their contents and conventions.
- **`scripts/check_design_system_governance.py` restored** — `tests/test_design_system_governance.py` imported a file that no longer existed. The checker now enforces: local link integrity (html + md), no `uploads/` references, no raw hex in component/template source (`var(--token, #hex)` fallbacks sanctioned; cards/stories/generated runtimes exempt), radii ≤ 8px, the template entry contract (viewport · `<main>` · `<h1>`, following `<x-import>` into siblings), no legacy workspace titles, and `.prompt.md` coverage. `scripts/governance_baseline.json` holds per-gate grandfathered exemptions — shrink it, never grow it.
- **Zero-baseline token adherence** — the 22 legacy raw-hex component files were rewritten to `var(--token, #hex)` fallbacks (focus rings now use the shorthand `var(--focus-ring, 2px solid #C56A4A)` pattern, chart canvas inks resolve tokens with hex fallbacks); the checker sanctions hex inside `var()` fallbacks and code comments. The baseline ships **empty** — every file is held to the rules.

### Changed
- **One home per doc.** `PATTERNS.md` and `BRAND_GUIDELINES.md` moved into `guidelines/`; stale root duplicates of `VISUAL_FOUNDATIONS` / `CONTENT_FUNDAMENTALS` / `ICONOGRAPHY` deleted (`guidelines/` copies were already canonical — the root `VISUAL_FOUNDATIONS` still carried pre-retheme hexes); `INSPIRATION_BRIEF.md` archived to `docs/changelog/archive/`. All cross-references updated. Root now owns only `readme.md` · `CHANGELOG.md` · `CONTRIBUTING.md` · `SKILL.md`.
- **`readme.md` restructured** — repository map + "where do I…" index up front; inventory counts corrected to the manifest census (128 modules → 166 exported symbols; 27 templates — `split-pane-reconciliation` was missing from the index; `Money` carrier was missing from the accounting list).
- **Counts self-update.** `index.html` and `docs/system-map.card.html` now read component/template/token/card counts live from `_ds_manifest.json` instead of hardcoding them (both had drifted).

### Fixed
- Template a11y contract: real `<h1>` landmarks in `asset-sheet` (sheet title), `session-start` (Sign in), `charting-workstation` (visually-hidden); `role="main"` in `report-pack`.
- `assets/brand/README.md` linked the brand guidelines at a path that never resolved.

## 1.20.0 — 2026-08-05

Modularity & extensibility pass. Additive — no breaking changes despite 2.0 clearance.

### Added
- **`WorkstationShell`** (shell/) — slot-based workstation frame: `topbar` / `nav` / `statusBar` / `aside` slots around a scrolling main area. Every slot optional with graceful collapse; main and aside are crash-isolated (a failing panel renders an evidence strip, never a blank shell). Templates and consumers should compose this instead of hand-rolled flex chrome. New Shell card.
- **`guidelines/EXTENDING.md`** (+ Documentation card) — the extension contract: stable-vs-internal API surface, and one-page recipes for adding a component (d.ts + jsx + prompt.md trio, scoped-CSS/tokens-only rules), a token (tier placement + dark value), a brand preset (`data-brand` block), a density scope, and a template (compose via `WorkstationShell`). Plus the pre-ship robustness gates.

### Fixed
- `NavRail.d.ts` doc comment still said "teal-blue".

## 1.19.1 — 2026-08-05

### Fixed
- **Documentation coherence pass** — purged pre-retheme vocabulary from all shipped docs: "teal-blue"/"cyan" accent language → terracotta across `readme`, `VISUAL_FOUNDATIONS` (both copies), `ICONOGRAPHY`, `SKILL.md`, component `.d.ts`/`.prompt.md` doc comments (Button, Input, Tabs, NavRail, DenseDataTable, AccountTree, TaxLotTable); Segoe/Cascadia/JetBrains font references → IBM Plex Sans/Mono; `assets/brand/README.md` color+type spec rewritten to current tokens.
- `GETTING_STARTED.md` counts corrected (160 components · 26 templates · 389 tokens) and motion spec aligned to 120–300ms standard cubic.
- `VISUAL_FOUNDATIONS` chart-series spec aligned to the navy data-ink model (`--chart-primary` navy; terracotta = scenario/crosshair only). No API, token, or rendered-style changes.

## 1.19.0 — 2026-08-05

From the May 2025 brand asset sheets (P&L Calendar · Yield Curve · Ownership Graph).

### Changed
- **Chart data ink is now navy.** New base `--navy #2E4B6E` (+ `--navy-dim/-a10/-a20`; dark `#85A3C4`); `--chart-primary` repoints accent → navy. Terracotta no longer draws observed data — it is reserved for **scenario/shock overlays** (`--chart-scenario`, always dashed), the crosshair, and brand.
- New series-recession ladder: `--chart-compare-1` (“1M ago” steel) · `--chart-compare-2` (“3M ago” gray), plus `--chart-realized` / `--chart-unrealized` split tokens.

### Added
- **YieldCurve** `overlays` prop — N prior-period curves recede down the ladder; `scenario: true` renders dashed terracotta. `compare` still works as a one-overlay shorthand.
- **PnLCalendar** `bars` (per-day intraday micro-bars around a dashed midline) and `heat` (set `false` for the sheet style — the signed value alone carries color, no wash).
- Cards restaged to the sheet presentation: *Yield curve* (Today / 1M / 3M / +25bp bull-steepen scenario), *P&L Calendar* (micro-bars, heat off), *Chart encodings* (ladder + scenario + realized/unrealized rows).
- **Template:** `asset-sheet` — the branded presentation frame from the sheets themselves: dot-field masthead with terracotta sheet title, description block, MEASURE / UNDERSTAND / CONVERGE motto band, a content well for the showcased component, and the corporate footer (M roundel · version/date). Masthead, footer, and as-of strings are tweakable props.

## 2026-08-05 — Programmed Institutionalism v1.0 retheme

- New identity applied from the brand sheet (assets/brand/identity-sheet.png): warm paper/stone surfaces (Stone #E7E3DC canvas, Architect White #FBFAF8 panels), graphite #2E2E2E chrome, terracotta #C56A4A accent, independent desaturated semantics (forest #4A7C5E · brick #A6433F · ochre #9A7113 · slate blue #5D6F7F).
- Type: IBM Plex Sans (interface) + IBM Plex Mono (all data, tabular) replace Segoe/Cascadia. Body 14px, section 18px, display 56px (report covers only).
- Radii 2/4/6 (chips/controls/sheets); motion 120–300ms standard cubic; shadows on detached overlays only.
- Brand marks regenerated from the identity construction: dot-field Meridian mark (vertical axis · repeated dots · radial arcs · one terracotta dot), wordmark, stacked lockup, hero, tiles.
- Dark mode retuned to warm graphite-carbon. Token NAMES, component APIs, and templates unchanged.


## 1.18.0 — 2026-07-05

### Added
- **Charts:** `WaterfallChart` — contribution bridge (start/delta/total bars, dashed level
  connectors, computed Net) for P&L attribution and NAV walks. `Treemap` — squarified
  exposure/allocation map (alpha washes by day-delta or group, measured layout, progressive
  labels). `BarChart` — the missing categorical-bars primitive (vertical SVG or ranked
  horizontal rows, `signed` tinting). Card: *Attribution & exposure*.
- **Data:** `SavedViews` — named, persisted view snapshots (query + sort stack + filters)
  with locked presets, save-as, an "· edited" divergence indicator, update and delete;
  persists per `storageKey`. `useTableState` gains `getViewState()` / `applyViewState(state)`
  / `setFilters` (additive). Card: *Saved views*.
- **Templates:** `session-start` — the missing lifecycle surface: credentials → MFA verify →
  environment gate (Live requires typed confirm) → role, with readiness footer and status
  bar. `backtest-compare` — two runs side by side: KPI delta table, overlaid equity curves,
  paired drawdown/returns panes, configuration `DiffView` + provenance rail; joins the
  Strategy Executor suite. `report-pack` — printable LP monthly pack (cover, performance
  summary with live `EquityCurve`, income statement, positions) whose figures tie out.
- **Print layer:** `tokens/print.css` gains the opt-in `.mds-sheet` page model — Letter page
  cards on screen, real `@page` pagination in print — plus `.mds-no-print`,
  `.mds-avoid-break`, `.mds-sheet__footer`. Existing defensive workstation print rules
  unchanged. Documented in `guidelines/PATTERNS.md › Print & report packs`.

## 1.17.0 — 2026-07-05

### Changed
- **`accounting-workstation` rebuilt — modular and fleshed out.** The single 470-line
  `screen.jsx` is now a DC-template shell + logic-class store over per-view modules
  (`views/*.jsx`) and a `data.js` seed module with internally consistent figures (the
  trial balance proves, the recon variance equals the one open break). NavRail now
  *drives* the views (the old duplicate tab strip is gone): General Ledger · Chart of
  Accounts · Trial Balance · Reconciliation · **FX Revaluation** (new) · **Period
  Close** (new, `GateRail` + `ValidationIssueList` + `ReadinessPanel`) · Statements ·
  Tax Lots. Hand-rolled tabs/search/segmented/drawer replaced with `Toolbar`, `Select`,
  `SegmentedControl`, `Drawer`, `EmptyState`, `TrialBalance`. Flows: the open recon
  break and the FX reval both open the journal drawer *prefilled* and settle the books
  on post; period lock gates on the close rail and flips the workstation read-only
  (`ReadOnlyBanner`, disabled posting, `Reopen` to reverse). Adds `CommandPalette`
  (Ctrl-K), `HotkeysProvider` (`G L`…`G X`, `N`), `ToastProvider` confirmations, CSV
  export, and a voided-pair ledger example. Tweaks: environment · role (viewer =
  read-only) · density.

## 1.16.0 — 2026-07-05

### Added
- **Charts:** `YieldCurve` — term-structure chart (yield vs tenor) for Basket Builder and
  fixed-income surfaces; sqrt-of-years x-scale, prior-date/benchmark overlay, tenor-pair
  spread readout in bp with inversion flag. Card: *Yield curve*.
- **Trading:** `OptionChainTable` — straddle-layout option chain (calls | strike | puts,
  mirrored columns), accent-wash ITM shading, spot marker row, configurable
  greeks/IV/OI columns, keyboard strike selection. Card: *Option chain*.
- **Cards:** *Token playground* (Theming) — flip brand / theme / density live on one set
  of components; the white-label pitch, demonstrated.
- `docs/PERFORMANCE.md` — measured consumer payload (135 KB gzip bundle · 13 KB CSS
  closure · React peer), CSS-injection behavior (verified duplicate-free), and the
  monolith trade-off.

### Changed
- **Keyboard pass on the mouse-heavy data surfaces** (contracts in `ACCESSIBILITY.md` §2
  and `guidelines/PATTERNS.md › Keyboard navigation`): `DepthLadder` price column is one tab stop
  (↑/↓ levels, Home/End, Enter prefills); `CoverageMatrix` cells navigate as a roving
  grid with the readout following focus; `WorksheetGrid` adds Home/End, Ctrl+corner, and
  PageUp/PageDown jumps. No visual change; non-interactive usages unaffected.
- **Dark-mode contrast sweep — measured** (full table now in `ACCESSIBILITY.md` §1):
  dark `--accent-dim` `#3C6688` → `#609BC9` (pressed-button ink was 3.11:1; accent-dim-as-
  text ~2.5:1 on panels — dark pressed now goes lighter, per dark-UI convention); dark
  `--text-muted` `#8893A0` → `#8F9AA7` (was 4.28:1 on hover rows). `check_contrast.py`
  gained pressed/hover/accent-text pairs plus composited alpha-wash pairs (`WASH_PAIRS`).

## 1.15.0 — 2026-07-05

### Added
- **Templates** (§D of `docs/CONTRACT_AUDIT.md` — the three largest uncovered backend
  domains): `reconciliation-workstation` (imports · statement runs · SLA-tracked
  break-case triage via `CaseQueue`/`SlaChip` · account sign-off queue with next-best-
  action), `family-office` (balance-sheet metrics · `OwnershipGraph` with entity
  inspector · evidence-backed accounts with `ProvenanceChip` · `CommitmentBar` stack ·
  capital activity · readiness blockers), `covered-call-lab` (backtest request params ·
  strategy-vs-underlying `EquityCurve` · 21-metric block · trades · chain preview with
  filter pass/reject · run library).

## 1.14.0 — 2026-07-05

Driven by the `Meridian.Ui.Shared` contract audit (`docs/CONTRACT_AUDIT.md`).

### Added
- **Operations:** `CaseQueue` (case-triage list mirroring `ReconciliationCaseSummaryDto` /
  `OperationsBreakCaseDto` — priority rail, status, SLA, assignee), `SlaChip` (the case
  clock: OnTrack / Warning / Breached with due-in / over-by durations; deterministic via
  `now`), `ProvenanceChip` (the recurring evidence tuple — source system · as-of ·
  worst-of completeness/reconciliation dot).
- **Charts:** `OwnershipGraph` — layered entity/control structure for
  `FamilyOwnershipGraphDto`; measured SVG edges with ownership-% labels; accepts DTO
  field names directly.
- **Accounting:** `CommitmentBar` — called / unfunded / distributed / NAV funding bar
  with derived DPI · TVPI for `CapitalCommitmentDto`.
- **Cards:** *Case triage & SLA* (Operations), *Ownership graph* (Charts), *Capital
  commitments* (Accounting), *Contract audit · 2026-07* (Documentation).
- `docs/CONTRACT_AUDIT.md` — template↔endpoint coverage, data-shape drift, next steps.

### Fixed
- `status.js` normalizer covers the contract enums that previously fell to "info":
  `NotReady`, `Skipped`, `Matched`, `BreaksDetected`, `Resolved`, `AwaitingApproval`,
  `OnTrack`, `AtRisk`, `Breached`, `Paused`, `SignedOff`, `Reopened`.

## 1.13.0 — 2026-07-05

### Changed
- **Design System tab reorganized.** The 32-card "Components" dump is split by domain — Core ·
  Data · Charts · Accounting · Trading · Operations · Shell — mirroring `components/`. Singleton
  groups folded away (Layout → Core, Dark Mode → Theming, Docs → Documentation); all theming /
  white-label / dark-mode cards now live under one **Theming** group.
- Card names normalized: middot separators throughout; recency names ("new components/primitives")
  replaced with what the cards actually show.

### Added
- **Depth & Motion** foundations group (3 cards): *Surface ladder* (flat elevation, light + dark,
  the border ladder), *Stacking order* (the `--z-*` ladder and overlay shadow policy), *Motion*
  (timing tokens, what animates, what never does).
- **System map** card (Documentation): foundations → 149 exports → 19 templates at a glance.

### Fixed
- readme component counts (117 → 149 exported symbols; `accounting/` 7 → 11 in Architecture).

## 1.12.0 — 2026-07-05

### Added
- **Accounting components:** `TrialBalance` (sectioned Assets/Liabilities/Equity/Revenue/Expenses
  proof with subtotals and a Σdebit = Σcredit footer), `AgingTable` (AR/AP aging buckets with
  escalating amber → red washes, bucket-share footer), `FxRevaluationTable` (booked vs. current
  rates, per-row and net unrealized G/L), `AllocationEditor` (interactive weight editing over
  cent-exact `allocateAmount` splits).
- **Money helpers:** `daysBetween`, `agingBucketIndex`, `buildTrialBalance` (postings → trial-
  balance rows), `amortizeStraightLine` (remainder-exact schedules), `fxRevalue` — all exposed on
  the `Money` carrier and used by the new components, so consumer math ties out to the pixel.
- **Cards:** "Trial balance & aging" and "FX reval & allocation" under Components.

### Fixed
- `Money.d.ts` parse error (no interface declaration) — the carrier is now typed via
  `MoneyHelpers`, restoring it to the compiled manifest.

## 1.11.0 — 2026-07-02

### Added
- **Components:** `InstrumentChip` (symbol + venue + asset-class identity chip), `DepthLadder`
  (classic DOM price ladder — washed depth, spread bps, price-click prefill), `CoverageMatrix`
  (symbol × session data-availability heat grid), `TimeframeSwitcher` + `AsOfControl` (chart-
  toolbar resolution picker + Live/as-of session clock), `FilterBuilder` (structured
  field/operator/value query rows that compile a predicate), `LogTail` (mono evidence stream
  with level filters + follow-tail), `EventTimeline` (vertical audit trail).
- **Templates:** `trading-desk` (watchlist, depth ladder, order ticket behind the live confirm
  gate, blotter/fills tape), `ingestion-operations` (provider freshness, backfill queue with
  inline progress, coverage heat, run log), `report-scheduler` (report packs, recipients,
  delivery-history timeline).
- **Density:** new **`terminal`** scope (`data-theme-density="terminal"`) — 26px rows, tightest
  spacing, for multi-monitor ops walls. `DensityToggle` gains it as the densest option.
- **Docs & cards:** `docs/COOKBOOK.md` (five assembly recipes) + card, Token Browser card
  (searchable, copy-to-clipboard, live swatches), White-Label Kit card (live `data-brand`
  switcher), Keyboard Map card (shortcut matrix + live keydown tester). `.prompt.md` coverage is
  now **100%** — every component ships per-component usage guidance.
- **Tooling:** `scripts/visual_diff.py` (Playwright screenshot-diff over every `@dsCard`, Pillow
  diff, baseline/compare modes) and governance rule `prompt-coverage` (every `X.jsx` + `X.d.ts`
  requires an `X.prompt.md`).

## 1.10.0 — 2026-07-02

### Added
- **Components:** `Timestamp`, `Delta`, `TagInput`, `SplitPane`, `Toolbar` (+Group/Spacer/Divider),
  `DiffView`, `PnLCalendar`, `OrderTicket`, `Blotter`, `FillsFeed`, `ErrorBoundary`,
  `NotificationCenter`, `HotkeysProvider` (with the "?" shortcut sheet), `UserMenu`, `RoleBadge`,
  `ReadOnlyBanner`.
- **Templates:** `alerting-workstation` (SplitPane triage, loading/empty/error ladder, DiffView
  audit rail), `settings-admin` (forms, validation stack, danger zone with typed confirm).
- **Stylesheets:** `tokens/print.css` (reports print as documents), `tokens/contrast-modes.css`
  (Windows High Contrast / forced-colors).
- **Tooling:** `scripts/check_contrast.py` + `tests/test_contrast.py` (WCAG token regression
  gate), `tests/unit-tests.html` (behavioral harness), `tests/gallery.html` (all cards on one
  page — visual-regression baseline), governance rules `white-on-fill` and `gallery-coverage`.

### Changed
- **Tables now read `--theme-row-height`** — `DenseDataTable`/`FilteredDataTable` row height and
  virtualization math track the density token instead of a hardcoded 40/32px. See UPGRADING.md.
- `OrderTicket` composes `Input`/`Select` instead of hand-rolled fields.
- `WorkstationTopbar` environment chip renders `Badge` instead of a duplicate implementation.

## 1.9.0 — 2026-07-01

### Fixed
- **Contrast:** 11 components hardcoded white text on solid fills (failed AA in dark mode);
  all now route through `--text-on-accent` / new alias `--text-on-fill`.
- **Focus:** `Dialog`/`Drawer`/`Modal` now share `core/useOverlayFocus.js` — the documented Tab
  trap is actually implemented (it previously wasn't, anywhere); focus restore on close; Modal
  gained focus management; CommandPalette traps Tab.
- `Popover` positions in `useLayoutEffect` (no (0,0) flash); stale 6px radius fallbacks → 2px;
  overlay entrance motion unified on `--motion-base`/`--ease-standard`.

### Changed
- Shell chrome (`WorkstationTopbar`, `StatusBar`) tokenized — 9 new `--topbar-*`/`--chrome-*`
  tokens replace raw hex.
- Superseded process reports moved to `docs/changelog/archive/`.
