# What To Work On Next — Meridian (2026-10-23)

**Status:** prioritization input; not a governance decision and not a roadmap-status document
**Owner:** core-team
**Reviewed:** 2026-10-23
**Baseline:** `origin/main` at `6ca5515b0d842c8f8b64ba1774a493785854f03d` (2026-10-23 15:45 UTC)
**Supersedes:** nothing. The [2026-10-22 determination](https://github.com/rodoHasArrived/Meridian-main/pull/3259)
(#3259, unmerged — the file is not on `main`) ranked this program correctly and **its ranking
stands**. This document does not re-rank. It records that #3259's bar was **not** met, verifies its
rows against today's head, and adds four facts it could not have had.
**Method:** read `git ls-remote --tags origin`; grouped every `push` workflow run on `main` by
`head_sha` and workflow name across the 40 most recent completed runs; read the trigger blocks of
`golden-path-validation.yml` and `demo-smoke.yml`; read the full job-dependency graph of
`desktop-installer-packaging.yml` and the `eligibility` job's step order, plus
`build/scripts/ci/check-release-signing.ps1`; listed every run of
`generated-repair-publish.yml` and `desktop-installer-packaging.yml`; re-measured all 40 open pull
requests with `git merge-tree --write-tree --name-only origin/main refs/remotes/pr/<n>`, classifying
every conflicted path against `generated()` in
`build/scripts/resolve-generated-merge-conflicts.py:71`; ran
`build/scripts/docs/validate-doc-hashes.py --summary` on a clean checkout of the head; counted
`tempfile.TemporaryDirectory(` call sites under `tests/scripts`; parsed `roadmap-items.yml` with
`yaml.safe_load`.

> This document ranks nothing new. The roadmap registry remains authoritative for status;
> `docs/product/implementation-todo-list.md` remains authoritative for the P0 release gate.
> Recheck CI results and conflict counts against the live head before acting.

## The bar was not met

#3259 set it: *"due when `v0.1.0-rc.2` is tagged and produces a certification receipt — or when a
tag runs and produces none again."*

**Neither happened.** The repository holds two tags — `eval-v0.1.0-eval.1` and `v0.1.0-rc.1` — and
no `rc.2`. `Desktop Installer Release` has **19 runs total**; the newest is still run 19 on
`v0.1.0-rc.1` (2026-10-21 16:00 UTC, `failure`). No tag has run in 48 hours.

By its own standard this determination is not due, and the honest first finding is that the
delivery path did not move. Three of #3259's nine rows did close, all of them by other work:

| #3259 row | State today |
| --- | --- |
| **P0** Merge #3251 (the release record) | **Closed.** Merged as `9a8fd24c` |
| **P1** Refresh the `SRC-UI` / `SRC-UI-DASHBOARD` reviewed hashes on `main` | **Closed.** `9a9ce64f` refreshed them; `validate-doc-hashes.py --summary` on a clean checkout of `6ca5515b` reports **0 errors, 0 warnings** |
| **P2** Resolve the determination backlog | **Partly.** #3170 and #3197 are no longer open. #3207, #3223, #3249 and #3259 still are |

Everything else is carried. The rest of this document is the four things that changed.

## New: the green receipt is not monotonic, and the `rc.2` target is already behind

This is the finding that changes a standing assumption. Four determinations have described the RC
window as *perishable* — "every merge moves the head off the receipt." That framing assumed the
newest green head is at least as good as the last one. **It is not.** Two of the eleven push
workflows are **path-filtered on `main`**:

- `golden-path-validation.yml:25-29` — `push: branches: [main]` with a `paths:` list
- `demo-smoke.yml:25-29` — the same shape

So they run only when a merge happens to touch their paths. Grouping all `push` runs by head:

| Head | Date | Green | `Golden Path` | `Demo Smoke` | Note |
| --- | --- | --- | --- | --- | --- |
| `52c1a6fe` | 10-21 20:02 | **10 of 11** | ✅ | ✅ | Publisher `skipped` by design — the only complete receipt |
| `7d072638` | 10-22 22:16 | 8 of 11 | ✅ | ✅ | `Meridian CI` and `CodeQL` **cancelled** — no gate receipt |
| `9a8fd24c` | 10-22 22:32 | 6 of 7 | — | — | Neither triggered |
| `6ca5515b` | 10-23 15:45 (**head**) | 9 of 10 | — | — | Neither triggered |

**`52c1a6fe` remains the only commit in the repository's history with a complete green receipt, and
it is now three merges and ~44 hours behind the head.** No head since has reproduced its breadth,
and by construction none will unless a merge happens to touch those path filters. `7d072638` had the
two end-to-end lanes and lost the gate to concurrency cancellation instead.

The consequence for the decision: *"tag the newest green head"* actively **loses** evidence —
specifically `Demo Smoke` and `Golden Path Validation`, which are the end-to-end lanes a
desktop-installer release line most wants. The real choice is to tag `52c1a6fe` (accepting a stale
head), or to make these lanes reachable on demand so any candidate commit can produce a full
receipt. The second is a `workflow_dispatch` trigger on two workflows.

## New: one job suppresses every piece of certification evidence a tag could mint, and the fix is ~15 lines

**This is the one P0 item that is engineering, available today, and not waiting on a person.** It is
the mechanism behind #3259's *"give `Production Certification` a tag trigger, or decouple
`production`/`smoke` from `needs: eligibility`"* row, and reading the job graph makes the fix
specific.

Six jobs in `desktop-installer-packaging.yml` fan out from a single dependency
(`:83-105,111-113`):

```yaml
ci:                needs: eligibility   # meridian-ci.yml
secrets:           needs: eligibility   # ci.yml (Secret Scan)
codeql:            needs: eligibility   # codeql.yml
production:        needs: eligibility   # production-certification.yml
smoke:             needs: eligibility   # publish-smoke.yml
release-preflight: needs: eligibility
```

And inside `eligibility` (`:43-81`), the signing check runs as an **earlier step in the same job**
than the step whose output anything actually consumes:

1. *"Validate signing prerequisites before builds"* → `check-release-signing.ps1`, which throws
   `'Signing secrets are unavailable.'` when either secret is blank
   (`check-release-signing.ps1:23-24`) and stamps `problem = 'signing-secrets-unavailable'`.
2. *"Validate exact commit and increasing package identity"* → `id: identity`, the step that
   publishes `outputs.version`, which only `smoke` reads.

Step 1 fails, so the job fails, so `outputs.version` is never published and **all six dependents are
skipped**. That is why the `rc.1` tag run *"failed closed at eligibility … nothing built or
published"* and minted no certification evidence at all — not because certification is unreachable
from a tag (`desktop-installer-packaging.yml:8-10` does trigger on `v*`, and it calls
`production-certification.yml` by `workflow_call`), but because one absent secret suppresses the
entire fan-out.

**The fix is a split, not a trigger.** Separate `eligibility` into `identity` (checkout +
`release-preflight.py`; needs no secrets and no `desktop-release-signing` environment) and
`signing-readiness` (the signing step and its 90-day readiness artifact). Then repoint
`ci`/`secrets`/`codeql`/`production`/`smoke`/`release-preflight` at `identity`, and keep
`signing-readiness` in the `needs:` list of `build-msix`, `build-consumer-setup` and everything
downstream of them (`:160,330,453,577,643,712`).

What that buys: a tag cut today — with the secrets still missing — mints `Meridian CI`, Secret Scan,
CodeQL, **`Production Certification`** and **`publish-smoke`** evidence at the tagged commit, and
fails closed only at signed packaging, which is the correct place to fail. It converts a
month-old administrator blocker from *"blocks all certification evidence"* into *"blocks signed
packaging."* Four of the six `PRD-000`/`PRD-014` evidence items are exactly those receipts.

**Honest limits.** It does not produce install/launch/update/rollback receipts — those need signed
packages, so they still need the secrets. And it does not make `rc.1` certifiable: that commit's own
`Meridian CI` is red on `verify-docs`, independently of this. It changes what the **next** tag is
worth.

## New: the queue now has real code conflicts, and #3169 is the whole of them

#3259 concluded *"nothing in the queue is blocked by a real disagreement about code."* **That is no
longer true**, because the Security Master lane merged six PRs in 48 hours
(`bc693a2a`, `6c961c09`, `52c1a6fe`, `7d072638`, `9a8fd24c`, `6ca5515b`).

Re-measured today across all 40 open PRs (39 measurable; #3263 opened at 16:09 UTC and its head ref
was not yet fetchable): **583 `CONFLICT` lines, 34 of 39 PRs conflicting** — down from 682 across 38
yesterday. Classifying every conflicted path:

| Class | Paths | In the resolver's declared scope? |
| --- | --- | --- |
| Built workstation bundle | 678 | yes (`WORKSTATION_ROOT`) |
| Generated docs in `GENERATED_FILES` | 88 | yes |
| `source-hash-manifest.json` | 16 | yes, by a dedicated merger |
| **Generated output the resolver does not claim** | **100** | **no** |
| Truly hand-written | 53 | n/a |

**30 of the 53 hand-written paths are in #3169 alone**, and 17 of those are real `src/**` source
files:

```text
src/Meridian.Contracts/SecurityMaster/{SecurityIdentifiers,SecurityIdentifierNormalizer,
  SecurityAssetTermsSchema,SecurityAssetClassCatalog,FaceValueLot}.cs
src/Meridian.Application/SecurityMaster/{SecurityMasterService,SecurityMasterMapping,
  SecurityMasterCashFlowService,ISecurityMasterQueryService}.cs
src/Meridian.Storage/SecurityMaster/PostgresSecurityMasterStore{,.TermsProjection}.cs
src/Meridian.FSharp/{Domain/SecurityIdentifiers.fs,Interop.SecurityMaster.fs}
src/Meridian.Infrastructure/{Adapters/Alpaca/AlpacaMarketDataClient,
  Resilience/WebSocketConnectionManager}.cs
src/Meridian.Contracts/Api/UiApiRoutes.cs
```

#3169 was last updated **2026-10-16** — eight days ago, against the fastest-moving area in the
repository. At 201 `CONFLICT` lines it is not mechanically resolvable and it decays every day the
lane merges. **It needs a decision (rebase onto the current lane, or close and re-cut the parts still
wanted), not a resolver run.** Outside #3169, hand-written conflict is 23 paths over 12 files —
still nothing resembling a design disagreement.

## New: the resolver's scope is a filename set, and 74 generated paths fall outside it

`generated()` is `path.startswith(WORKSTATION_ROOT) or path in GENERATED_FILES`
(`resolve-generated-merge-conflicts.py:71-72`) — one prefix rule plus 20 exact filenames. The
`example-validation.md` addition that three determinations chased is **in** now
(`:25`), which is why generated-doc conflicts fell to 88. But **100 conflicted paths across 9 PRs,
spanning 74 distinct paths, are generated output outside that set:**

| Family | Paths | Generated by |
| --- | --- | --- |
| `docs/generated/database/**` (contracts pages, `.mmd` diagrams, data-object catalog) | 29 | database contract/diagram generation |
| `docs/diagrams/ui*` (`.dot` / `.svg` pairs, 11 WPF screen maps) | 38 | UI diagram generation |
| `database/manifest/*.json` (`contracts`, `dependencies`, `schemas`, `catalog`, `migrations`) | 17 | schema-control inventory |
| `docs/status/{wpf-screen-development-tracker,program-state-summary,api-contract-coverage-dashboard}.{json,md}` | 8 | docs automation |
| `build/config/contracts/type-parity-registry.json` | 2 | type-parity generation |

**74 distinct paths is past the point where a frozen filename set is the right shape.** The fix is
prefix rules — `docs/generated/database/`, `docs/diagrams/`, `database/manifest/` — matching how
`WORKSTATION_ROOT` already works, with the existing warning in the file (*"Do not replace this list
with a wildcard over `docs/status` or `docs/generated`"*) respected by naming subtrees rather than
globbing those two roots. That raises mechanically-resolvable coverage from 82% to **93%** of
today's conflicted paths.

This is a prerequisite for #3259's P1, not a substitute: **the automation that applies the resolver
has still never once succeeded.** `Generated Repair Publisher` is at **557 runs, zero successes
ever**; the newest (run 557, today 16:10 UTC, head `6ca5515b`) is `failure`. #3232, which
re-architects it, is still an open draft.

## Process: a third of the open queue is unmerged output of two scheduled routines

Worth ranking because it is measurable and self-inflicted. Of 40 open PRs, **13 are scheduled
analysis output that nobody has merged:**

| Routine | Open PRs | Count |
| --- | --- | --- |
| Next-work determination | #3207, #3223, #3249, #3259 | 4 |
| Security Master institutional-requirements pass | #3111, #3188, #3196, #3220, #3234, #3240, #3247, #3258, #3263 | 9 |

The second routine is also the largest single source of hand-written conflict outside #3169:
**`docs/architecture/security-master-extensibility-review.md` conflicts on nine PRs** — each pass
appends to the same document, so every pass conflicts with every other. Three of them are explicitly
stacked (#3234 *"stacked on #3220"*, #3240 *"stacked on #3234"*, #3196 *"stacked on #3188"*), so the
chain cannot land head-first.

Both routines are producing correct analysis that no one consumes, and the determination lane has now
said so four times. **Recommendation: pause both until their backlogs clear.** For the Security
Master pass specifically, if it continues it should write a dated file per pass rather than appending
to one shared document, which would remove nine conflicts by construction.

## Carried, verified unchanged

- **The two `verify-workflows` races, day eight.** `tests/scripts` has **321**
  `tempfile.TemporaryDirectory(` call sites and **zero** using `ignore_cleanup_errors`;
  `tools/schema_control/disposable.py` has not been touched since `2a191564` (2026-10-15). The lane
  is green, so this stays P1 — but the `rc.2` plan depends on a head whose gate is *re-runnable*.
- **No merge driver for the bundle.** `.gitattributes:10-12` still carries `text eol=lf` for
  `src/Meridian.Ui/wwwroot/workstation/**` and **zero `merge=` directives** anywhere in the file.
  678 conflicted paths, 73% of today's tax, one directory.
- **The signing secrets.** `MDC_SIGNING_CERT_PFX_BASE64` and `MDC_SIGNING_CERT_PASSWORD` in
  `desktop-release-signing`. Administrator action, outstanding ~1 month, unverifiable from here.
- **Registry unchanged.** 48 rows: 17 `done`, 15 `planned`, 9 `accepted`, 7 `in_progress`, 0
  `ready_for_acceptance`. The seven `in_progress` rows are the two W8 UI-lane rows and the five W10
  depth rows, all `green`/`on_track`. No product-status movement in three days.

## Summary

| Priority | Work | Why now |
| --- | --- | --- |
| **P0** | **Split `eligibility` into `identity` + `signing-readiness`** in `desktop-installer-packaging.yml`, and repoint `ci`/`secrets`/`codeql`/`production`/`smoke`/`release-preflight` at `identity` | **The only P0 that is engineering and is not waiting on a person.** Six evidence-producing jobs fan out from one job whose *first step* is the signing check, so one absent secret suppresses all certification and publish-smoke evidence a tag could mint. ~15 lines; fails closed at signed packaging instead, which is the correct place |
| **P0** | **Provision `MDC_SIGNING_CERT_PFX_BASE64` / `MDC_SIGNING_CERT_PASSWORD`** | Unchanged, ~1 month outstanding, unverifiable from here. Still the only path to install/launch/update/rollback receipts |
| **P0** | **Decide the `rc.2` commit: tag `52c1a6fe`, or add `workflow_dispatch` to `golden-path-validation.yml` and `demo-smoke.yml`** | `52c1a6fe` is still the **only** complete green receipt in the repository's history and is now three merges behind. Both lanes are path-filtered on `main`, so the newest green head carries *less* evidence, not more. "Tag the newest head" loses `Demo Smoke` and `Golden Path Validation` |
| **P1** | **Decide #3169: rebase onto the current Security Master lane, or close it** | 201 conflicts including **17 real `src/**` source files**. Last updated 2026-10-16 against a lane that merged six PRs in 48 hours. Not resolver work, and it decays daily. This is the queue's first genuine code conflict in weeks |
| **P1** | **Give `generated()` prefix rules** for `docs/generated/database/`, `docs/diagrams/`, `database/manifest/` (plus the four `docs/status` dashboards and `type-parity-registry.json`) | 100 conflicted paths across 9 PRs, **74 distinct**, are generated output the resolver does not claim. Past the point where a 20-name frozen set is the right shape. Raises mechanical coverage from 82% to 93% |
| **P1** | **Land #3232, then run the resolver across the queue** | Carried from #3259 unchanged and still the highest-leverage delivery item. The Publisher is at **557 runs, zero successes ever**, newest `failure` today at 16:10 UTC |
| **P1** | **Stop the bundle churn at source** — `merge=` driver for `src/Meridian.Ui/wwwroot/workstation/`, or regenerate-on-merge | 678 conflicted paths, 73% of the tax, one directory. `.gitattributes` has `eol` rules for exactly these paths and no merge strategy |
| **P1** | **Fix the two `verify-workflows` races** | Day eight, unchanged: 321 call sites, zero `ignore_cleanup_errors`; `disposable.py` untouched since 10-15 |
| **P2** | **Pause both scheduled analysis routines until their backlogs clear**; if the Security Master pass continues, give it a dated file per pass | **13 of 40 open PRs** are unmerged scheduled output, and its shared append-target conflicts on nine of them. Four determinations have now reported that analysis is not the bottleneck |
| **P2** | **Merge #3249 and #3259; close #3207 and #3223** | #3259 is current and its ranking stands. #3207 and #3223 were written against premises that have since changed three times |

## The bar for the next determination

**#3259's bar stands unchanged and unmet: the next determination is due when `v0.1.0-rc.2` is
tagged and produces a certification receipt, or when a tag runs and produces none again.**

Nothing today argues for a sixteenth. The P0 set is one ~15-line workflow change, one administrator
action, and one commit choice; the P1 set is four specified, bounded changes. This document exists
because a schedule fired, and its most useful recommendation is the P2 row that says to stop firing
it.
