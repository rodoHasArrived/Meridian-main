# Meridian surface → template + variant

The picker shows **27 templates** (7 archetypes + 20 production screens). Several 3.x screens merged in 4.0 — every retired surface below is still reachable as a **tweak or URL param on its survivor**, per the 4.0 curation gate. Nothing merged became unfindable; find your surface in this table.

## The map

| Meridian surface | Template | Variant / how to reach it |
| --- | --- | --- |
| Portfolio dashboard | `dashboard-workstation` | — (the 4.0 reference screen) · 4.2: palette with position lookup (open · pin · chart), `G` chords, `J/K` selection, session trail |
| Ops wall (shared monitor) | `ops-wall` | `severityFloor` tweak filters alerts + log; terminal density by default |
| Outbound adapter operations | `adapter-operations` | `scope` tweak splits LIVE / PAPER; named workspace layouts and the object trail persist per operator |
| Alert triage | `alerting-workstation` | — · ack/silence/resolve/reopen actually move the alert; severity + state filters, bulk triage bar, selection that holds through a resolve, tabbed inspector (detail · history + operator notes · rule revision with an approve/reject gate) |
| Data-pipeline operations | `ingestion-operations` | — · streaming job progress, per-provider scoping, job inspector drawer (stages, checkpoint, retry/pause/priority/cancel), gap-scan → queued backfill, log scoped to the selected job |
| Break queue *(was `reconciliation-workstation`)* | `reconciliation` | `view=queue` (default) |
| Custodian-vs-ledger matcher *(was `reconciliation-workstation`)* | `reconciliation` | `view=matcher` |
| Nine-step exception resolution *(was `reconciliation-exception`)* | `reconciliation` | `view=exception`, or "Mark exception" from the matcher's break drawer |
| Close calendar / dependencies / owners | `close-management` | `view=close` (default) |
| Tie-out proof *(was `close-tie-out`)* | `close-management` | `view=tie-out`, or "Tie-out" on a reconciliation control task |
| General ledger / books | `accounting-workstation` | `view=gl` (default) |
| Journal posting *(was `journaling-workstation`)* | `accounting-workstation` | `compose=true` tweak or `?view=gl&compose=1` — the balance-gated drawer |
| Books reconciliation *(was `journaling-workstation` tab)* | `accounting-workstation` | `view=recon` |
| Trial balance · statements · tax lots · FX reval · period close | `accounting-workstation` | `view=tb` / `stmt` / `lots` / `fx` / `close` |
| **Assurance** (4.3 · O-4) | `accounting-workstation` | `view=tb` assurance column · `view=close` certification matrix + *Material accounts certified* gate (lock blocked, uncovered accounts named) · `view=account` signs the next stage |
| **Account Sheet** — the account as a destination (4.3 · O-2) | `accounting-workstation` | `view=account&account=1100` · `G A` · palette resolves account codes and names (Sheet / Ledger verbs) · "Account sheet" from the entry inspector and the chart of accounts · closing balance over its proof, tie state, open items, postings, envelope |
| Investment event ledger | `investment-event-ledger` | — |
| Security master registry | `security-master-registry` | rows open `SecurityDetail.dc.html?security=<id>` (⌘/ctrl-click → new tab); `detailMode=inline` tweak keeps the in-place view · `G S` back to list |
| Security detail (standalone record page) | `security-master-registry/SecurityDetail` | `?security=<id>` required — unknown id redirects to the registry; edits/notes shared with the list |
| Ownership & control investigation | `ownership-graph` | — |
| Strategy formation, six stages *(was `strategy-onboarding`)* | `strategy-formation` | `view=formation` (default) |
| Strategy builder worksheet *(was `strategy-builder`)* | `strategy-formation` | `view=builder` |
| Field & formula composer | `field-formula` | — |
| Backtest compose *(was `backtest-builder`)* | `strategy-runs` | `view=backtest` |
| Run-artifact ledger | `strategy-runs` | `view=runs` (default) |
| Run compare *(was `backtest-compare`)* | `strategy-runs` | `view=compare` |
| Covered-call lab *(was `covered-call-lab`)* | `strategy-runs` | `view=lab` |
| Governance & catalog *(was `amx-governance`)* | `strategy-governance` | — |
| Asset-liability review | `alm-review` | — |
| Analytical workbench | `analytical-workbench` | — (Investigate archetype) · 4.2: palette resolves tenors / lineage nodes / plates, `H/L` tenor step, session trail + saved evidence⇄inspector layout |
| Charting terminal | `charting-workstation` | — |
| Trading desk | `trading-desk` | — · ladder click seeds the ticket (`Join` rests at the level, `Take` crosses it), guarded entry, simulated fills into positions and P&L, working-order cancel/replace, per-order audit drawer |
| Basket construction | `basket-builder` | — (Compose archetype) · 4.2: `Commit basket` through `ActionEnvelopeProvider` → `ActionQueue` (sector-limit rejection · `Shift+S` silent adapter → timed out), palette resolves baskets and constituents, session trail |
| Report catalog | `report-library` | — |
| Scheduled delivery | `report-scheduler` | — |
| Printable report pack | `report-pack` | — (Report archetype) · 4.2: `Deliver to 14 recipients` as a tracked instruction (watermark → rejected), `G 1–4` sheet jumps, palette resolves sheets and holdings |
| Branded asset sheet | `asset-sheet` | — |
| Sign-in / environment gate | `session-start` | — · 4.2: `Enter` advances, `1/2/3` pick the environment; after arrival the palette resolves handover objects |
| Settings & admin | `settings-admin` | — (Admin archetype) · 4.2: `Ctrl S` save and purge through `ActionQueue` (zero recipients → rejected), `G 1–4` section jumps, palette resolves settings |
| Bare shell | `blank-workstation` | — (Start archetype) · 4.2: palette + `?` sheet pre-wired; add actions in `operatorLayer()` |

`view` is both a tweak (Tweaks panel) and a URL param on every multi-view template; deep links like `Reconciliation.dc.html?view=exception` work as copied.

## Cross-links

- **Strategy suite** — `strategy-formation` · `field-formula` · `strategy-runs` · `strategy-governance` share ONE store (`templates/_strategy-executor/executor-store.js` + `screen-kit.js`), one nav rail, and URL-carried identity (`strategy`/`run`/`runA`/`runB`). Copy `_strategy-executor/` along with any of them. `screen-kit.js` `ROUTES` is the single routing table.
- `basket-builder` → the suite's Formation ("Use in strategy" → Portfolio rules).
- `reconciliation` → `accounting-workstation` (ledger nav, journal compose deep link).
- `close-management` → embeds the tie-out drill-in as a sibling DC (`CloseTieOut.dc.html`, in-folder).
- `alerting-workstation` → `dashboard-workstation` — repoint when extracting.

## Extraction plan

1. Copy this folder tree into a new project bound to the Meridian design system.
2. Point each screen's `ds-base.js` `base` line at the bound `_ds/<folder>` tree.
3. Repoint `alerting-workstation`'s dashboard route at whichever monitor screen that project ships.
4. Delete this section.
