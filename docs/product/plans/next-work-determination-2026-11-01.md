# What To Work On Next — Meridian (2026-11-01)

**Snapshot:** Dated snapshot (latest at time of writing) - the roadmap registry (`docs/roadmap/README.md`) is authoritative for current status.

**Status:** prioritization input; not a governance decision and not a roadmap-status document
**Owner:** core-team
**Reviewed:** 2026-11-01
**Baseline:** `origin/main` at `7ca57b629b1cf40d2429438ecdf204cab3f57d82` (2026-11-01 11:58 UTC)
**Supersedes:** the ranking carried by the
[2026-10-27 determination](next-work-determination-2026-10-27.md), whose bar is **not** met —
`v0.1.0-rc.2` is not tagged and the signing secrets have not been refused.
**Method:** read the `Meridian CI`, `CI` and `Production Certification` `push` run history on
`main` by `head_sha`; read all nine job conclusions of `Meridian CI` run 6127 on the current head
and pulled the failing job's log; reproduced the failure locally with `npm ci` plus
`node scripts/generate-diagrams.mjs --tracker-only`, then re-verified with
`python3 build/scripts/run-maintained-generation.py --docs-only`; read
`production-certification.yml`'s trigger block and `git log` for it; listed every `Release
Readiness` and `Generated Repair Publisher` run; re-measured all 14 open pull requests with
`git merge-tree --write-tree --name-only origin/main refs/remotes/pr/<n>`; parsed
`roadmap-items.yml` with `yaml.safe_load`; re-counted the `tests/scripts` temporary-directory call
sites; ran the certification lane's own npm-audit gate at its `--fail-level high`; read
`docs/ai/solo-developer-workflow.md` and `tools/roadmap/enforce_phase_scope.py`.

> This document ranks work. It does not move a roadmap row, accept an item, or certify a release.
> The roadmap registry remains authoritative for status; `docs/product/implementation-todo-list.md`
> remains authoritative for the P0 release gate. Recheck CI results against the live head before
> acting.

## The one-line answer

The delivery-system backlog that the last six determinations ranked **is substantially gone**. What
remains is one red lane on `main` (fixed in this branch), one human decision that has now been
named by seven consecutive passes, and a product lane whose five W10 rows all need the *same*
thing: evidence, not more source. **The next work is the product lane.**

## `main` is red, on the same defect class that made `rc.1` uncertifiable

This is the decisive finding, and it reverses the predecessor's headline.

The 2026-10-27 determination opened by reporting `4fb27518` as "the first `main` head in this
series green across **both** gating lanes." That head is five days behind. On the current head,
`Meridian CI` run 6127 is **red**:

| Job | Conclusion |
| --- | --- |
| `verify-docs` | **failure** |
| `verify-dotnet` | success |
| `verify-browser` | success |
| `verify-workflows` | success |
| `service-backed-integrations` (PostgreSQL evidence) | success |
| `require verified integration evidence` | success |
| `integration-gate` | success |
| `quality-gate` | **failure** (aggregator, consequent) |

Eight of nine jobs are green. The failure is one step — "Verify whole-repo generated documentation
is committed" — and its log is unambiguous:

```text
modified: docs/status/wpf-screen-development-tracker.json
modified: docs/status/wpf-screen-development-tracker.md
Converged: True; failed steps: 0
...
Generated files are stale.
```

The generation itself **converged with zero failed steps**. Two generated files were simply not
committed. #3351 changed `src/Meridian.Wpf/Features/**/*.cs`, a declared input of the tracker
generator, so the tracker's source fingerprint moved from `91d7d8857faa` to `403b53986050`; the
follow-up "Refresh generated docs for new files" commit refreshed other generated outputs but not
this one.

**Why this is worth the top of the document rather than a footnote.** This is the *same class of
defect* that made the `v0.1.0-rc.1` tag permanently uncertifiable. The readiness tracker records
`rc.1`'s fatal flaw as "`c51c5d8d`'s own `Meridian CI` push run is red on `verify-docs`
(source-hash drift the #3239/#3243 merge left behind)". A merge touched a generated input, the
regeneration was partial, the docs lane went red, and an immutable tag was cut on that tree anyway.
Eleven days later the identical mechanism has reddened `main` again. The lesson the program paid a
tag for has not been mechanized.

**Fixed in this branch**, because it is two lines and the predecessor's own precedent is to land a
release-unblocking fix rather than recommend it a second time:

1. **Reproduced** at the baseline: `npm ci`, then
   `node scripts/generate-diagrams.mjs --tracker-only` → both tracker files modified.
2. **Confirmed scope** with the lane's own entry point,
   `python3 build/scripts/run-maintained-generation.py --docs-only` → `Converged: True`, and these
   two files the **only** modifications. (The advisory UML step fails locally on a missing optional
   renderer; it is advisory, produced no diff, and passes in CI.)
3. **The diff is one fingerprint line per file.** No screen, task, or Gantt content moved:
   98 registered screens and 6 open tasks before and after.
4. **Phase gate checked:** both paths are named explicitly in `GENERATED_ARTIFACT_PATTERNS` in
   `tools/roadmap/enforce_phase_scope.py`, whose comment describes this exact trap, so the commit
   needs no phase declaration.

## The `rc.2` precondition as written is no longer measurable

This is the structural finding, and it invalidates the wording of the last three bars.

Commit `ea51dd620` — "ci/docs: make local full gate tier-aware; **stop certifying every main
push**; correct required-check docs" — **removed the push-to-`main` trigger from
`Production Certification`**. Its triggers are now `workflow_call`, `workflow_dispatch`, and a
weekly `cron: "17 3 * * 0"`.

The consequence is visible in the run history. `Production Certification`'s last `main` push run is
**353 on `a359654b` (2026-10-31)**; the four `main` pushes since then have **no run at all**. So:

- Every bar from 2026-10-24 onward is phrased as "green across both `Meridian CI` **and**
  `Production Certification`." On a push head, that condition can no longer become true — not
  because the lane fails, but because it does not run.
- The required contexts per `.github/workflows/README.md` are `quality-gate`, `Secret Scan`,
  `Analyze csharp`, and `Analyze javascript-typescript`, with `integration-gate` an optional
  companion. `Production Certification` is **not** among them.

The change itself is defensible and deliberate — it is part of the adopted
[solo-developer workflow](../../ai/solo-developer-workflow.md) and its commit records the consumer
inventory it checked. But it means **the release precondition must be restated as an action, not an
observation**: dispatch `Production Certification` on the chosen tag candidate and read *that* run.

For what it is worth today, the lane's own dependency gate would pass. Run locally at this
baseline, `validate-npm-audit.py --fail-level high` reports `PASS`. The dashboard carries **11
moderate and 0 high** advisories — up from 2 moderate on 2026-10-25, all rooted in `minimatch
<11.0.1` through the dev-only `eslint`/`typescript-eslint` tree. Below the lane's threshold, so it
gates nothing; worth naming only because with the weekly cadence a *new* high advisory now has up
to seven days of `main` pushes to hide in.

## P0: still the same two secrets, for the seventh consecutive pass

`PRD-014` is unchanged. `MDC_SIGNING_CERT_PFX_BASE64` and `MDC_SIGNING_CERT_PASSWORD` are still not
provisioned in the protected `desktop-release-signing` environment. The repository still holds
exactly two tags, `v0.1.0-rc.1` and `eval-v0.1.0-eval.1`. `Release Readiness` still has only **two
runs ever**, both failures, the latest `40294900031` (attempt 4, 2026-10-29) — a receipt of the
secrets' absence, now six days old.

No engineering moves this item. It has been the sole human blocker in seven consecutive
determinations, and this pass has nothing to add to the argument.

## What actually closed since 2026-10-27 — three of the predecessor's blocks

This is the part worth reading, because it changes the shape of the backlog rather than its ranking.

**The conflict tax is gone.** Five consecutive determinations tracked it upward: 367 → 428 → 449
conflicts, 34 of 40 PRs conflicting, 833 conflicted file entries. Re-measured today across all 14
open PRs:

| Measure | 2026-10-27 | 2026-11-01 |
| --- | --- | --- |
| Open PRs | 46 | **14** |
| PRs with conflicts | 34 of 45 measured | **1 of 14** |
| Total conflict messages | 449 | **2** |

The single conflicting PR is **#3352**, and both conflicts are on
`docs/status/doc-health-dashboard.{json,md}` — resolver-covered, and the path that conflicted on 27
of 45 PRs a week ago.

**The repair automation works.** `Generated Repair Publisher` was at 625 runs with "all twelve
recent `workflow_run` invocations failed." #3232 merged (`16bf6a307`), and runs **974 through 978 on
the current head are all `success`**. The P1 "land #3232, then promote the regenerate-on-merge item"
is closed on its first clause, and the second clause is now much weaker — the mechanism it was
meant to replace is working.

**The scheduled-document queue was cleaned up.** The five superseded determinations (#3207, #3223,
#3249, #3259, #3264) and the nine-PR security-master stack (#3111–#3263) are all closed. Those were
the queue's two largest hand-written conflict sources and 14 of its 46 PRs. The standing P2
recommendation from four passes is **done**.

**`vitest` moved past the advisory** on its own: `5.0.5`, well beyond the `4.1.12` the predecessor
asked for. That P2 is closed.

What this means for this series: of the six delivery-system items the last determination ranked at
P1/P2, **four are closed and one is reduced**. The backlog these documents were written to track has
been worked off. That is the strongest available argument for the cadence recommendation they kept
making, and it is why this document is shorter than its predecessors.

## The product lane is now the work, and every W10 row needs the same thing

`roadmap-items.yml` parses to **56 rows: 21 `planned`, 17 `done`, 9 `in_progress`, 9 `accepted`, 0
`ready_for_acceptance`** — up from 48 rows, with `W10-BOND-001` registered and `W10-RECON-001` moved
from `planned` into `in_progress`.

The nine `in_progress` rows are the two W8 UI-lane rows (`on_track`) and seven W10 depth rows. Six
are `green`; **`W10-RECON-001` is the only `yellow` row in the registry.**

The user-requested queue is unchanged and now genuinely unblocked: **`W10-LOT-002` →
`W10-MARK-001` → `W10-SEAM-001`, before `W10-RECON-001`** in that queue only.

**The useful observation is that none of these rows is short of source.** Read against
`docs/testing/evidence/next-work-20261029/README.md`, which states each row's delivered boundary and
its outstanding requirement, the pattern is uniform:

| Row | What is outstanding |
| --- | --- |
| `W10-LOT-002` | Final automated proof, broader consumer cutover, unsupported exercise/valuation and short-tax cases, compatibility retirement, independent review |
| `W10-MARK-001` | Provisioned retained IDs, population-wide preview, affected-position drilldown, repair/override proof, attributed live verdicts |
| `W10-SEAM-001` | Same host/client candidate with complete declared scope, missing/stale/mismatched contributor refusal, repair recovery, live verdicts in both clients |
| `W10-RECON-001` | Current automated lineage proofs plus a retained run sequence demonstrating recurrence, valid clearing and business-calendar escalation |

Every entry is an evidence artifact, not a feature. **The next concrete unit of product work is a
retained evidence packet for `W10-LOT-002`, run on a single candidate build, not another increment
of source.** The queue order says `LOT` first; the evidence record already names the test selection
(`CanonicalOpenLotConsumerTests`, `FactorPaydown`, and the rest) and requires PostgreSQL results to
be distinguished from ordinary ones.

### One registry correction to make

`W10-RECON-001` carries `health: yellow` and `last_reviewed: 2026-09-26`, and its `current_summary`
describes its implementation as "in PR 3004." **PR #3004 merged on 2026-10-28** — after the last
determination. So the only yellow row in the registry is yellow on a review that predates the merge
of its own implementation by a month, and describes landed work as pending. Re-review that row
against the merged head and set its health from current evidence; whichever way it resolves, the
current value is not reporting the tree.

## Carried forward unchanged: the `verify-workflows` races

The one engineering item on this list that neither closed nor improved — it got measurably worse:

| Measure | 2026-10-27 | 2026-11-01 |
| --- | --- | --- |
| `tempfile.TemporaryDirectory(` call sites in `tests/scripts` | 321 across 98 files | **380 across 119 files** |
| Using `ignore_cleanup_errors` | 0 | **0** |

Plus the unchanged cancellation/allocation reconcile window in `tools/schema_control/disposable.py`.
`verify-workflows` is green on this head, which is exactly why it matters: `quality-gate` is a
required context, and this is a way for it to go red with no code defect behind it. The `pwsh`
preflight recommendation carries forward intact, including the predecessor's own correction — CI
must keep failing; only the diagnostic should change, and it must not become a skip.

## The gap worth closing, since the same defect has now cost two heads

`rc.1` was lost to uncommitted generated-doc drift after a merge; `main` is red today for the same
reason. The drift gate correctly refuses, and `Generated Repair Publisher` — now working — produces
the patch. What is missing is the step that applies it before the next merge rather than after a red
run. #3232's `.gitattributes` / regenerate-on-merge follow-up is the item that addresses this, and
its justification has changed: it is no longer "the publisher is broken," it is "a working publisher
still needs someone to act on its output."

### The same merge left a second stale artifact, and no required check can see it

Found while clearing the first one, and it is the stronger half of the argument above.

`7ca57b62` also left `docs/source/generated/source-hash-manifest.json` stale for **`SRC-APP` and
`SRC-WPF`** — which are precisely and only the two source modules it changed
(`src/Meridian.Application/{Commands/ConfigCommands.cs,Services/ConfigurationService.cs}` and four
files under `src/Meridian.Wpf/`). Verified in a clean worktree at the base with nothing else applied:
`build_manifest` disagrees with the committed manifest for those two modules and no other.

So one merge left **two** derived artifacts stale. The difference is in how each one surfaces:

| Stale artifact | Where it surfaces |
| --- | --- |
| `wpf-screen-development-tracker.{json,md}` | `verify-docs` → the required `quality-gate`. Loud, and red on `main` |
| `source-hash-manifest.json` (`SRC-APP`, `SRC-WPF`) | Only the non-required `Prepare generated repair patch` check, and only on a **PR branched off the bad head** |

`validate-doc-hashes.py` is invoked by `make/docs.mk` and `make/ai.mk` and by the reviewed-hash
merge tool. **No GitHub workflow runs it.** So this drift is invisible to every required check and
to every `push` run on `main`; it becomes visible only when someone opens a PR, whose
`generated-repair` check then fails for a defect the PR did not introduce. Any PR off this base that
does not itself touch those two modules inherits that failure — which is what happened to this
determination's own branch, and to #3354 before it refreshed its own module hash.

That is worth more than the two-line fix it took to clear: a reviewed-hash gate that only a
downstream PR can trip will keep charging the queue for defects it did not cause, and the regenerate
-on-merge item above is what stops both symptoms at the source.

## Summary

| Priority | Work | Why now |
| --- | --- | --- |
| **P0 — done in this branch** | **Regenerate `docs/status/wpf-screen-development-tracker.{json,md}`** | `Meridian CI` run 6127 is red on `verify-docs`, failing `quality-gate` (a required context) on the current `main` head. Generation converged with zero failed steps; only these two files were uncommitted. Reproduced and re-verified locally; the diff is one fingerprint line per file. Same defect class that made `v0.1.0-rc.1` uncertifiable |
| **P0** | **Provision `MDC_SIGNING_CERT_PFX_BASE64` and `MDC_SIGNING_CERT_PASSWORD`, or formally refuse them** | `PRD-014`. Seventh consecutive determination to name it. Two tags in the repository, neither certifiable; `Release Readiness` has two runs ever, both failed. No engineering moves this |
| **P0** | **Restate the `rc.2` precondition as an action: dispatch `Production Certification` on the tag candidate and read that run** | `ea51dd620` dropped the push-to-`main` trigger. The last four `main` pushes have no certification run at all, so "green across both lanes on a push head" can no longer become true. The lane is not required; it is weekly and on demand |
| **P0** | **`PRD-002`'s remaining clauses** — browser/WPF/setup/default-runtime credential ownership; legacy ownership/verification migration, rollback and rotation proof | Still the tracker's only code-rather-than-signature P0. The C1–C8 and #3328/#3329 series advanced it; the clauses themselves are not recorded closed |
| **P1** | **Produce the `W10-LOT-002` evidence packet on one candidate build** | The user-requested queue's head, no longer gated behind anything. `next-work-20261029` already names the test selection and requires PostgreSQL results kept separate. All five W10 rows are short of *evidence*, not source — this is the first one |
| **P1** | **Re-review `W10-RECON-001` and set its health from the merged head** | The registry's only `yellow` row, `last_reviewed: 2026-09-26`, describing PR #3004 as its pending implementation. #3004 merged 2026-10-28 |
| **P1** | **Fix the two `verify-workflows` races** — `ignore_cleanup_errors=True`; the `disposable.py` reconcile window | The only item here that got worse: 380 call sites across 119 files, up from 321/98, still zero using the flag. `quality-gate` is required, and this reddens it with no code defect |
| **P1** | **Promote the `.gitattributes` / regenerate-on-merge follow-up** | Re-justified, not carried: `Generated Repair Publisher` now succeeds (runs 974–978 green after #3232). The remaining gap is that nothing applies its patch before the next merge — which is what cost `rc.1` and cost this head, twice over: `7ca57b62` left both the WPF tracker and the `SRC-APP`/`SRC-WPF` source hashes stale |
| **P1** | **Run the reviewed source-hash validator in CI** | `validate-doc-hashes.py` lives only in `make/docs.mk`, `make/ai.mk` and the merge tool; **no workflow runs it**. Manifest drift on `main` is therefore invisible to every required check and surfaces only as a failed non-required `generated-repair` check on an unrelated downstream PR — it hit this branch and #3354 |
| **P2** | **Add a `pwsh` preflight to the script-test lane that fails once with an accurate message** — not a `skipUnless` guard | Carried forward intact, including the predecessor's correction. Dropped to P2 because the lane has been green since the outage healed |
| **P2** | **Rebase the eight D-series PRs stacked on #3328, and resolve #3352** | #3328 merged as `b735371a`, so #3339–#3346 are stacked on landed history. All eight currently measure conflict-free; #3352's two conflicts are both resolver-covered `doc-health-dashboard` paths |
| **P2** | **Bump the dev-only `eslint`/`typescript-eslint` tree past `minimatch <11.0.1`** | 11 moderate advisories, up from 2; 0 high. Below the gate's threshold, so it gates nothing — but the weekly certification cadence means a future *high* advisory has up to seven days of `main` pushes to hide in |
| **Closed since 2026-10-27** | Conflict tax (449 → 2); #3232 landed and the publisher is green; the determination and security-master stacks closed (46 → 14 PRs); `vitest` at `5.0.5` | Four of the predecessor's six delivery-system items are closed and one is reduced. Recorded here so the next pass does not re-rank them |

## The bar for the next determination

**The next determination is due when a `v0.1.0-rc.2` tag run has completed on a candidate that
carries a dispatched `Production Certification` receipt, or when the signing secrets are formally
refused.**

This is the predecessor's bar with the measurement corrected for `ea51dd620`, and it should be the
last one phrased around the release. The delivery-system backlog this series existed to track is
worked off: of six items ranked a week ago, four are closed. The remaining release question is one
person's decision about two secrets, and a seventh ranking document did not move it.

**What should replace this series is a product-evidence record, not another ranking.** The five W10
rows are each short of a named, retainable evidence packet, and
`docs/testing/evidence/next-work-20261029/README.md` already specifies what each one must contain.
That is a thing to produce, not to prioritize.
