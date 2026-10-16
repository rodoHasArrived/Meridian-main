# What To Work On Next — Meridian (2026-10-02)

**Status:** prioritization input; not a governance decision and not a roadmap-status document
**Owner:** core-team
**Reviewed:** 2026-10-02
**Baseline:** `main` at `021966720e518bb76403e91b2810481a32561f0a`
**Supersedes:** [2026-09-27](next-work-determination-2026-09-27.md), and the 2026-09-28 and
2026-09-29 determinations proposed in still-open
[#3012](https://github.com/rodoHasArrived/Meridian-main/pull/3012) and
[#3030](https://github.com/rodoHasArrived/Meridian-main/pull/3030)
**Method:** verified every carried-forward claim against **current source on `main`**, not against
its own record or its pull-request body; re-measured the whole open pull-request queue with
`git merge-tree --write-tree --name-only origin/main`; read live GitHub Actions job *and step* state
for `Production Certification`; read the roadmap registry (`docs/roadmap/data/*.yml`) and the
readiness tracker (`docs/product/implementation-todo-list.md`). Every claim below is anchored to a
run, a commit, a PR number, a `file:line`, or a reproducible command.

> This document ranks work. It does not move a roadmap row, accept an item, or certify a release.
> The roadmap registry remains authoritative for status; the tracker remains authoritative for the
> P0 release gate. Recheck conflict counts and CI results against the live head before acting.

## Headline

**The two engineering items that three consecutive determinations ranked as top P0 have both
merged, and both are confirmed in source rather than only in their records. `main` is green on all
four certification lanes right now. There is no longer an engineering-shaped blocker between `main`
and a release candidate — the only hard prerequisite left is a credential somebody has to provide.**

- **#3026 merged 2026-09-30.** `tests/Meridian.Tests/Integration/StatementLedgerReconciliationPostgresTests.cs`
  exists on `main`. The gap three determinations called the highest-value engineering item — "no
  PostgreSQL-backed test anywhere touches statement ingestion or reconciliation" — is closed.
- **#3028 merged 2026-10-01.** Strict tenancy is now the **default**, in source:
  `TenantScopeEnforcementOptions.FromConfigurationValue` maps `null or "fail-closed" => FailClosed`
  (`src/Meridian.Contracts/Tenancy/TenantScopeEnforcement.cs:61`), and the registration fallback is
  `TenantScopeEnforcementOptions.FailClosed`
  (`src/Meridian.Application/Composition/TenantScopeServiceRegistration.cs:22`). Omission selects
  strict mode. The `W9-GOV-008` criterion-two flip that four determinations ranked P0 is done.
- **`Production Certification` run #125
  ([37031030309](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37031030309)) is green
  on all four jobs at the current head `02196672`** — dependency evidence, encrypted backup and
  clean restore, deterministic PostgreSQL integration, and same-commit documentation evidence. It is
  the third consecutive green run on `main` (#123, #124, #125).

A frozen, certified commit is the precondition five P0 rows are waiting on, and `main` is producing
one on every push. **The RC tag is the whole remaining P0 story.**

## What closed since 2026-09-29 — verified in source, not in records

| 2026-09-29 item | State today | Evidence |
| --- | --- | --- |
| P0: advance #3028's tenant cutover | **Merged 2026-10-01.** Strict default confirmed in source | `TenantScopeEnforcement.cs:61`; `TenantScopeServiceRegistration.cs:22` |
| P0: land #3026's PostgreSQL statement round trip | **Merged 2026-09-30.** Test exists on `main` | `tests/Meridian.Tests/Integration/StatementLedgerReconciliationPostgresTests.cs` |
| P0: drain clean dependency PRs before the freeze | **#2944 and #2932 now merge clean**, and four newer NuGet bumps do too | queue table below |
| P0: the npm dependency gate is red | **Closed on `main`.** `brace-expansion` is **5.0.12** | `src/Meridian.Ui/dashboard/package-lock.json:1876` |
| The readiness tracker is stale (81 commits behind) | **Reconciled.** `Reviewed: 2026-10-02` | `implementation-todo-list.md:5`; #3018 merged |
| P1: the registry YAML parse break | **Still open.** Unchanged, now at line 797 | reproduced below |
| P1: take or decline `W9-CORPACT-011` | **Still open. Now 10 days** at `ready_for_acceptance` | `roadmap-items.yml`, `last_reviewed: 2026-09-22` |
| P2: `W10-LOT-002` successor mutations | **Still unimplemented** | `grep -rn "Successor" src/Meridian.Contracts/Accounting/Lots/` → no match |

### #3031 is obsolete — close it

`#3031` exists to bump `brace-expansion` to 5.0.12 and clear two npm advisories. **`main` already
carries 5.0.12** (`package-lock.json:1876`), so its substantive change has landed by another route.
The PR is self-labelled "**Do not merge**" pending hosted evidence it will now never need. It also
conflicts in three generated files. Close it.

## Correction to a carried-forward claim

The 2026-09-29 determination said the RC/stable version collision means the preflight "**will refuse
the tag**". **That is not true of the first tag, and the distinction matters for sequencing.**

`require_increasing` compares the candidate against *already published* versions
(`build/scripts/ci/release-preflight.py:25-27,90`). No `v*` tag has ever existed — `git tag --list`
returns only `eval-v0.1.0-eval.1` — so `published_versions` is empty, `any(...)` over an empty list
is `False`, and the check passes.

So **cutting `v0.1.0-rc.1` is not blocked by the version scheme.** The collision bites only on the
*next* tag: `v0.1.0-rc.1` and `v0.1.0` both map to `(0,1,0,0)`
(`release-preflight.py:14-21`, prerelease suffix discarded, revision pinned to `0`), so a stable
`v0.1.0` after an `rc.1` would be refused. Pick the scheme before the **stable** tag, not before the
RC. This moves the item off the RC's critical path.

## A measurement caveat worth recording

Reading certification failures from the jobs API is misleading unless you read `outcome`, not
`conclusion`. On the #3028 merge commit
([run #118](https://github.com/rodoHasArrived/Meridian-main/actions/runs/36900203366)) the
dependency job reported *both* gate steps as `conclusion: success` while the job was red at
"Assert both dependency gates passed".

The gate is **correct, not buggy**. Both gate steps set `continue-on-error: true`
(`production-certification.yml:101`) so the evidence artifact is always complete, which makes the
API report `conclusion: success` for a step whose `outcome` was `failure`; the assert step reads
`steps.*.outcome` and is the job's real verdict (`production-certification.yml:115-124`). Anyone
triaging these runs should read the assert step's message, not the green step badges above it.

## Tier 0 — Cut the release candidate. Nothing engineering-shaped is in front of it.

Three determinations have ranked this and each time something engineering-shaped sat in front of it.
Nothing does now. The remaining steps are a credential, a product decision, and a tag.

1. **Provide `MDC_SIGNING_CERT_PFX_BASE64` and `MDC_SIGNING_CERT_PASSWORD` in the protected
   `desktop-release-signing` environment.** Both are required; the tag build throws without the
   first (`desktop-installer-packaging.yml:215`) and signs with the second
   (`desktop-installer-packaging.yml:57-58,222`). **This is the one hard prerequisite and it is not
   engineering work.** Every installed lifecycle receipt in the tracker today was minted with a
   throwaway self-signed certificate, which the tracker itself says is "**not** release evidence"
   (`implementation-todo-list.md:146`).
2. **Decide the ~1 GB consumer installer question.** `Meridian-Setup.exe` is 1,043,350,783 bytes
   because it carries both architectures plus a full PostgreSQL distribution; the tracker flags it
   as "a product decision to revisit" (`implementation-todo-list.md:146`). It ships inside the RC —
   decide before the freeze, not after.
3. **Freeze `02196672` (or a later green head) and tag `v0.1.0-rc.1`.** `Desktop Installer Release`
   triggers on `push: tags: v*` (`desktop-installer-packaging.yml:3-6`) and `Production
   Certification` does too. One tag run mints the same-commit evidence that `PRD-013`, `PRD-014`,
   `PRD-015`, `PRD-016`, and `PRD-017` are each explicitly waiting on.
4. **Activate the required release check** (`PRD-016`) — repository administrator.
5. **Sign off ADR-019, ADR-020, and the support matrix** (`PRD-000`), core team. The tenancy posture
   this must declare is now settled in source: strict-by-default, per Tier 0's merged #3028.
6. **After** the run, schedule the operator replay/reconciliation review of the uploaded
   `production-recovery-drill-*` artifact. `PRD-015` is not closed by the run alone, and the
   artifact does not exist until the run uploads it.

**State the limits.** An RC tag does not make the program production-ready. It mints same-commit
artifacts; it leaves the human approvals, the W10 depth slate, and operator acceptance of the
bounded roadmap rows untouched. It is also reversible, and an RC that fails tells you which row is
not actually ready — information the program does not currently have for any of them.

## Tier 1 — The registry parse break is three days old and still unfixed

Reproduced today, unchanged except for drifting one line:

```bash
python3 -c "import yaml;yaml.safe_load(open('docs/roadmap/data/roadmap-items.yml'))"
# yaml.scanner.ScannerError: mapping values are not allowed here
#   in "docs/roadmap/data/roadmap-items.yml", line 797, column 886
```

Line 797 is the `current_summary` of `W8-UX-CONSOL-001` — an unquoted plain scalar containing `": "`,
which terminates a plain scalar. The other five registry files parse cleanly. Nothing fails loudly
because `build/scripts/docs/common.py` catches every `yaml.safe_load` exception and falls back to a
hand-rolled subset parser, so `validate-roadmap-registry.py` exits 0 with no output — which is
exactly what a silent fallback looks like from outside.

The file that is authoritative for program status is readable only by one bespoke parser in this
repository, and the validator cannot distinguish "valid" from "unparseable, recovered by fallback".
The fix is under an hour: quote the offending scalar, make `load_data` fail loudly for
`docs/roadmap/data/*`, and wire the nine unused schemas in `docs/roadmap/schemas/` into the
validator. This sits inside `PRD-017`.

**Note for whoever takes it:** fix the quoting *first and alone*. Any other edit to
`roadmap-items.yml` lands in a file no standard reader can parse, so a mistake there is invisible to
every tool that reads it.

## Tier 2 — Three roadmap rows now understate what source does. Reconcile them, then decide.

Merging #3026 and #3028 moved source well past three records. None of this is engineering work.

- **`W9-GOV-008`** (`in_progress`, `last_reviewed: 2026-09-28`) — reviewed three days *before*
  #3028 merged. Criterion two, the fail-closed default, is now implemented on `main`. The row should
  be re-reviewed against current source. Per #3028's own body and
  [#2633](https://github.com/rodoHasArrived/Meridian-main/issues/2633), deployment-specific tenant
  attribution, exception review, and browser/WPF cutover evidence remain open — **merging #3028 did
  not complete this row**, and nothing here should be read as acceptance.
- **`W9-INGEST-009`** (`in_progress`, `last_reviewed: 2026-09-25`) — reviewed five days before
  #3026 merged. The PostgreSQL round trip its record calls missing now exists. On that evidence the
  row is a candidate to move to `ready_for_acceptance`; #3026's body is explicit that it "does not
  close #2634's operator acceptance gate", so the move is a status reconciliation, not an
  acceptance.
- **`W9-CORPACT-011`** — `ready_for_acceptance` since 2026-09-22, now **10 days**.
  `DEC-W9-ACCEPTANCE-002` says acceptance "may be reconsidered only after the approval and posting
  lane is reachable and the criterion is implemented and evidenced". It is, on PostgreSQL, and the
  round trip is in the certification lane. **The reconsideration is overdue and costs no
  engineering. Declining is a result too.**

Row hygiene while you are there: `W8-WPF-PARITY-001` carries `last_reviewed: 2026-07-06` — **88
days** — while being an active co-equal UI lane and a named productization target.

## Tier 3 — Seven PRs merge clean, and the pre-freeze drain is cheap

Measured today against `02196672` with `git merge-tree --write-tree --name-only origin/main
origin/<branch>`. 26 open PRs. The generated/hand-written split is what decides the cost.

| PR | Branch | Conflicts | Hand-written | Disposition |
| --- | --- | --- | --- | --- |
| #3036, #3035, #3034, #3033 | NuGet bumps (caching, scripting, duckdb, commandline) | **0** | — | **clean; drain pre-freeze** |
| #2944 | `sharp` 0.35.4 | **0** | — | **clean; drain pre-freeze** |
| #2932 | dashboard tooling group | **0** | — | **clean; drain pre-freeze** |
| #2878 | `lucide-react` 0 → 1 (**major**) | **0** | — | clean, but **hold: major** |
| #3011 | `automation/ai-navigation-refresh` | 2 | 0 | mechanical (`docs/ai/generated/*`) |
| #3041 | `codex/w10-amount-provenance` | 3 | 0 | mechanical; **but see Tier 4** |
| #3031 | `codex/brace-expansion-certification-fix` | 3 | 0 | **obsolete — close** |
| #2903 | `postcss-selector-parser` | 1 | 1 | `source-hash-manifest.json`; reviewed-baseline procedure |
| #2981 | logging group | 1 | 1 | one real test: `FutureProjectionServiceTests.cs` |
| #2307 | dashboard build-tooling (**78 days**) | 2 | 2 | `package.json` + `package-lock.json`; regenerate the lock |
| #2920, #2930, #2896 | cash-ladder, statement-commit, replay | 3–4 | 0 | mechanical |
| #2928 | `p0-csv-evidence` | 10 | 6 | genuine review needed |
| #3004 | `reconciliation-lineage-2636` | 17 | 3 | decaying; 3 real (`contracts.json`, an operator doc, **and `roadmap-items.yml`**) |
| #2897 | `portfolio-corporate-action-snapshots` | 11 | 1 | one file |
| #2826 | `first-trusted-close` | 7 | **7** | **genuine, and newly suspect — see below** |
| #2789 | `backtesting-quantscript-milestone-1` | 32 | 12 | stays `[DRAFT — RE-CUT REQUIRED]` |
| #2587 | `react-router` (**major**) | **413** | 351 | re-cut; do not merge |

**The RC freezes the dependency set.** Bumping a dependency after the tag invalidates the
same-commit evidence the tag exists to mint. So the seven clean PRs are pre-freeze work, or an
explicit recorded deferral — not post-freeze work.

**#2826 needs re-reading before anyone resolves it.** All seven of its conflicts are hand-written
source, and four are precisely the files #3026 just rewrote:
`LedgerJournalInternalTransactionSource.cs`, `RetainedInternalReconciliationPopulationProvider.cs`,
`InternalReconciliationPopulations.cs`, and `StatementRunWorkflowService.cs`. Whatever it was doing
to the internal reconciliation population has substantially been done on `main` by a PR that landed
with PostgreSQL evidence. Read it against merged #3026 and expect to close rather than resolve it.

**The merge driver is still unimplemented.** `.gitattributes` defines no driver for the generated
trees, and `resolve-generated-merge-conflicts.py` covers 19 enumerated paths plus the workstation
root (`build/scripts/resolve-generated-merge-conflicts.py:19-40`) — it deliberately excludes
`docs/diagrams/`, `docs/ai/generated/`, and the reviewed source-hash manifest, which is why #3011
and #2903 are not one-command cases. Vite's content-hashed renames keep regenerating this class of
conflict by construction.

## Tier 4 — W10: one real engineering remainder, and one governance mismatch

- **`W10-PROV-001` is `planned` with `last_reviewed: 2026-07-31`, but #3041 implements it.** An open
  pull request titled "Implement W10-PROV-001 scoped amount proof in browser and WPF" is building a
  row the registry says nobody has started. **#3041 also has no CI at all** — `get_status` returns
  `total_count: 0`, so not one check has run against `c8faa234`. Reconcile the row's status or hold
  the PR; and do not review it as ready while it carries zero hosted evidence. Opening a tenth W10
  lane also runs against the pattern every determination has flagged: nine lanes open at once is why
  none of them closes.
- **`W10-LOT-002`** — still the slate's only `critical` row, `last_reviewed: 2026-09-23`. Verified
  again today: `grep -rn "Successor" src/Meridian.Contracts/Accounting/Lots/` returns **nothing**,
  so append-only predecessor/successor corporate-action lot mutations remain unimplemented. That is
  the genuine engineering remainder, alongside amortization convergence and advance refunding.
  Per the 2026-09-29 correction, "shadow-operation acceptance" **is** defined
  (`security-lot-convergence-blueprint.md:287,306,334`) — unimplemented, not unscoped, so do not
  drop it. This row is the gate between a canonical-lot cutover and silent basis corruption.
- **`W10-MARK-001` and `W10-SEAM-001`** remain one operator session each from closing; both
  blockers are live operator certification, not engineering. The recorded operator-session plan
  still certifies `SEAM` first because closing it removes a dependency from the desktop lane.

## What not to work on

- **New product surface.** The `program-state.yml` `scope_gate` is explicit, and eight rows are
  in progress or awaiting acceptance across three waves.
- **Another determination.** This is the fifth in thirteen days. It is written because the ranking
  genuinely changed — the top two P0 engineering items merged and the release gate's shape changed
  with them — which is the bar the 2026-09-29 document set. **That bar still applies: the next one
  is due when the queue drains or the RC lands, not tomorrow.**
- **Re-proving the certification lane.** Green on all four jobs at `02196672`, three runs running.
  The open question is the *frozen commit*, which is Tier 0.
- **Re-litigating accepted W9 rows.** Six are accepted under recorded decisions and one is done.
- **Re-cutting the large stranded branches** beyond #2789 and #2587.
- **Untracking the built asset tree.** `PRD-013` and `PRD-018` both require one tracked canonical
  tree; fix the merge, not the tracking.
- **Majors before the freeze.** #2878 and #2587 wait.

## Summary

| Priority | Work | Why now |
| --- | --- | --- |
| **P0** | Provide `MDC_SIGNING_CERT_PFX_BASE64` **and** `MDC_SIGNING_CERT_PASSWORD` in the protected `desktop-release-signing` environment | The one hard prerequisite, and not engineering work. Nothing else in Tier 0 can start without it |
| **P0** | Decide the ~1 GB consumer installer question | It ships inside the RC; decide before the freeze |
| **P0** | Drain the seven clean PRs (#3036, #3035, #3034, #3033, #2944, #2932) or record an explicit pre-freeze deferral; resolve #2981 and #2903 | The RC freezes the dependency set; bumping after the tag invalidates the same-commit evidence |
| **P0** | Freeze a green head and cut `v0.1.0-rc.1` | One tag run mints the same-commit evidence five P0 rows wait on. No `v*` tag has ever existed. **The version scheme does not block the first tag** |
| **P0** | After the run, schedule the operator drill review; activate the required check; sign off ADR-019/ADR-020 | The three things the tag run cannot mint |
| **P1** | Fix the registry parse break and enforce the schemas | Three days old. Under an hour. The status-authoritative file parses only in this repository |
| **P1** | Take or decline `W9-CORPACT-011` (**10 days**) | Zero engineering; its stated precondition is met on PostgreSQL |
| **P1** | Re-review `W9-GOV-008` and `W9-INGEST-009` against current source; refresh `W8-WPF-PARITY-001` (**88 days**) | Three rows now understate what `main` does, because #3026 and #3028 landed after their reviews |
| **P1** | Close #3031 (obsolete) and the stale determinations #3012 and #3030 | Their substance has landed or been superseded; they currently misdescribe the program |
| **P2** | Read #2826 against merged #3026 — expect to close, not resolve | Four of its seven hand-written conflicts are files #3026 just rewrote with PostgreSQL evidence |
| **P2** | Reconcile `W10-PROV-001`'s status against #3041, and do not review #3041 as ready while it has zero CI | A `planned` row has an open implementation PR with no checks run |
| **P2** | Continue `W10-LOT-002`: successor mutations, amortization convergence, advance refunding | The only `critical` row; successor mutations verified absent from the lot contracts again today |
| **P3** | Drain the mechanical set (#3011, #3041, #2920, #2930, #2896); regenerate #2307's lock (**78 days**) | Cheap, and they decay: #2307 went 0 → 2 conflicts in a day once already |
| **P3** | Implement the `.gitattributes` merge driver / documented regenerate-on-merge for the generated trees | Recurring by construction; the existing script excludes diagrams, `docs/ai/generated/`, and the source-hash manifest |
| **P3** | Fix the three broken internal links in the P0 tracker, and consider gating link integrity in CI | Two are the tracker's own citations of the recorded operator-session plan; no job currently gates on link integrity, so they survive green runs |

## Note on the determination pile-up

Three determinations are now proposed and unmerged or superseded at once: #3012 (2026-09-28), #3030
(2026-09-29), and this one. **Recommend closing #3012 and #3030 without merging.** #3012's baseline
is ~160 commits behind; #3030's top two recommendations both merged on 09-30 and 10-01, and its
tag-scheme claim is corrected above. Landing three documents that each claim to supersede 2026-09-27
would leave the register describing a program nobody can reconstruct. That is a maintainer call, not
one this document makes.

**The tracker's own citations of that plan are already broken.** Measured on clean `main`
(`python3 build/scripts/docs/repair-links.py --summary`): 1,854 internal links checked, **3 broken,
all three in `docs/product/implementation-todo-list.md`** — the P0 release gate.

```
implementation-todo-list.md:64  -> plans/next-work-determination-2026-09-27.md#tier-5--w10-two-rows-await-an-operator-session-one-has-an-undefined-criterion
implementation-todo-list.md:91  -> plans/next-work-determination-2026-09-27.md#tier-5--w10-two-rows-await-an-operator-session-one-has-an-undefined-criterion
implementation-todo-list.md:107 -> w9-operator-acceptance-2026-08-29.md#w9-truth-001-owner-exception--2026-09-26
```

These are pre-existing and not introduced by this document. Two of them are precisely the anchors
the tracker uses to cite the recorded operator-session plan, so the P0 gate currently cites its own
sequencing authority through a link that does not resolve. Fix the anchors (or the headings they
point at) in the same pass that archives 2026-09-27. Note also that no documentation job gates on
this check — the certification lane's documentation job gates on generated-output *drift*, not on
link integrity, which is why three broken links in the P0 tracker have survived green runs.

**On archiving the predecessor.** Convention (`docs/product/plans/README.md`) says to move a
superseded determination into `archive/docs/plans/`. The 2026-09-27 document is deliberately
**left in place** here, because it is load-bearing in governance data rather than merely historical:
`docs/roadmap/data/program-state.yml:43` cites it by path as the recorded operator-session plan, and
`docs/product/implementation-todo-list.md:64,91` deep-link one of its section anchors. Moving it
would rewrite a governance-data path and break a P0-tracker anchor in the same change that adds a
prioritization input. This document **preserves** that operator-session plan rather than replacing
it (see Tier 4). Archiving 2026-09-27, with the `program-state.yml` and tracker references updated
in the same pass, is a separate governance-reviewed step.
