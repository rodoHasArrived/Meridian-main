# What To Work On Next — Meridian (2026-09-29)

**Status:** prioritization input; not a governance decision and not a roadmap-status document
**Owner:** core-team
**Reviewed:** 2026-09-29
**Baseline:** `main` at `95c8a321`
**Supersedes:** [2026-09-27](../../../archive/docs/plans/next-work-determination-2026-09-27.md), and the
2026-09-28 determination proposed in still-open [#3012](https://github.com/rodoHasArrived/Meridian-main/pull/3012)
**Method:** re-measured the whole open pull-request queue with `git merge-tree --write-tree origin/main`
against the current `main` head; read the roadmap registry (`docs/roadmap/data/*.yml`), the readiness
tracker (`docs/product/implementation-todo-list.md`), and live GitHub Actions state; verified each
carried-forward claim against current source rather than against its own record.

> This document ranks work. It does not move a roadmap row, accept an item, or certify a release.
> The roadmap registry remains authoritative for status; the tracker remains authoritative for the
> P0 release gate. Every conflict count and CI result below is anchored to `95c8a321` and must be
> rechecked against the live head before acting.

## Headline

**The two engineering items the 2026-09-28 determination ranked highest are no longer open work —
they are open pull requests, and each one conflicts with `main` in exactly two files, both of which
are generated output that a checked-in script exists to resolve.** The program's bottleneck today is
not deciding what to build. It is landing what is already built.

- **#3028 `codex/tenant-isolation-cutover`** delivers the `W9-GOV-008` remainder: a real
  `TenantScopeEnforcement: fail-closed|deployment-boundary` configuration key, strict mode as the
  default on omission, a pre-serve readiness gate over configured PostgreSQL stores, reviewed
  backfill with quarantine and immutable receipts, and migration 038. Conflicts: **2**, both
  `docs/status/doc-health-dashboard.{json,md}`.
- **#3026 `codex/postgres-statement-workflow`** delivers the end-to-end statement → journal-sourced
  ledger → deterministic match → casework round trip on PostgreSQL that the last two determinations
  called the highest-value engineering item. `quality-gate` passed at its head `5519d562` across all
  four lanes. Conflicts: **2**, the same two generated files.
- Both are resolved by `python build/scripts/resolve-generated-merge-conflicts.py --apply` after a
  base merge — both paths are already in that script's `GENERATED_FILES` set
  (`build/scripts/resolve-generated-merge-conflicts.py:22-23`) — followed by regeneration,
  validation, and a fresh hosted gate on the merged head.

**`Production Certification` is green on the current `main` head.**
[Run #111 / 36511568497](https://github.com/rodoHasArrived/Meridian-main/actions/runs/36511568497),
`event: push`, `head_sha: 95c8a321`, all four jobs: dependency evidence, encrypted backup and clean
restore, deterministic PostgreSQL integration with the "Reject failed or skipped deterministic
tests" step passing, and same-commit documentation evidence. This certifies `95c8a321` only.

**The readiness tracker is now stale against that head.** It is anchored at `13aa7576` and reviewed
2026-09-23; `main` has advanced **81 commits** since the 2026-09-28 baseline alone, touching 61
`src/` and 50 `tests/` paths. Open PR [#3018](https://github.com/rodoHasArrived/Meridian-main/pull/3018)
already exists to reconcile it.

## New finding — the roadmap registry is not valid YAML, and its schemas are enforced by nothing

`docs/roadmap/data/roadmap-items.yml` **fails to parse under any standard YAML reader**:

```bash
python3 -c "import yaml;yaml.safe_load(open('docs/roadmap/data/roadmap-items.yml'))"
# yaml.scanner.ScannerError: mapping values are not allowed here
#   in "docs/roadmap/data/roadmap-items.yml", line 791, column 886
```

Line 791 is the `current_summary` of `W8-UX-CONSOL-001`, an unquoted plain scalar 1,982 characters
long containing `Source-verified 2026-08-26: ` — and `": "` terminates a plain scalar. The other
five registry files parse cleanly; this one does not.

Nothing failed, because `build/scripts/docs/common.py:212-217` catches every exception from
`yaml.safe_load` and silently falls back to a hand-rolled `_parse_yaml_subset`. Every registry
consumer — `validate-roadmap-registry.py`, `render-roadmap-docs.py`, `render-roadmap-diagrams.py`,
`render-source-docs.py`, `scan-source-todos.py` — reads the registry through that fallback today.
`python3 build/scripts/docs/validate-roadmap-registry.py` exits 0 with no output, which is what a
silent fallback looks like from the outside.

I verified the fallback currently reproduces that field byte-for-byte (1,982 chars parsed, 2,003-char
raw line minus the 21-char key prefix), so **no data is wrong today**. The exposure is that the file
authoritative for program status is readable only by one bespoke parser in this repository, and the
validator cannot tell the difference between "valid" and "unparseable, recovered by fallback."

Compounding it: the nine JSON Schemas in `docs/roadmap/schemas/` are referenced by **nothing** under
`build/`, `scripts/`, or `.github/` — `grep -rn "roadmap/schemas\|jsonschema\|schema.json"` over
those trees returns no matches. `validate-roadmap-registry.py` checks the `schema.id` string, not the
document against the schema.

This sits squarely inside `PRD-017` ("Restore one trustworthy product/operations evidence chain").
The fix is small: quote the offending scalar, make `load_data` fail loudly for the registry rather
than falling back, and wire the existing schemas into the validator.

## Corrections to carried-forward claims

The 2026-09-28 determination was measured at `0b956b06`. Three of its claims are no longer true,
all because the #3021–#3027 CI consolidation program landed in between:

| Carried-forward claim | State at `95c8a321` |
| --- | --- |
| "`Publish Smoke` is `workflow_dispatch`-only, so `PRD-013` needs its own dispatch at the tagged SHA" | **No longer true.** `publish-smoke.yml:4` now declares `workflow_call`, and `desktop-installer-packaging.yml:101-106` calls it with exactly `project: web-workstation`, `runtime: win-x64`. That workflow triggers on `push: tags: v*` and also calls `production-certification.yml:98`. The tag run mints the `PRD-013` evidence; no separate dispatch is needed |
| "Fix the prerelease → MSIX version mapping — **blocks the tag**" | **Reclassified, not fixed.** `build/scripts/ci/release-preflight.py:15-27` still maps any prerelease to revision `0`, but `require_increasing` now **rejects** the collision with a named error: "RC and stable tags with the same major.minor.patch both map to revision 0 and collide; choose a higher package version." The identity can no longer be silently burned. What remains is a **tag-scheme choice**, not engineering work |
| Tenancy anchor `Composition/Features/TenantScopeServiceRegistration.cs:16-18` | **Path moved** to `src/Meridian.Application/Composition/TenantScopeServiceRegistration.cs:17-18`. The behaviour is unchanged on `main` — still `FromEnvironmentValue(..., DeploymentBoundary)`, environment variable only, no configuration key — but **#3028 changes exactly this**, so it is in-flight work, not open work |

## Fresh queue measurement

28 open PRs. Conflicts measured today against `95c8a321`; the generated/hand-written split is what
decides the cost.

| PR | Branch | Conflicts | Where | Disposition |
| --- | --- | --- | --- | --- |
| #3028 | `tenant-isolation-cutover` | 2 | both generated | **P0 delivery; resolve with the script** |
| #3026 | `postgres-statement-workflow` | 2 | both generated | **P0 delivery; resolve with the script** |
| #2931 | `p0-scoped-credential-ownership` | **0** | — | was 7 yesterday; **merges clean now** |
| #3029 | `design-doc-v1-1` | 0 | — | clean |
| #2944 | `sharp` 0.35.3 → 0.35.4 | 0 | — | clean dependency |
| #2932 | dashboard tooling group | 0 | — | clean dependency |
| #2878 | `lucide-react` 0 → 1 (**major**) | 0 | — | clean, but hold: major |
| #3011, #3018, #3019, #3020, #3024, #3016 | 2026-09-28 codex/automation set | 2–4 | generated | mechanical |
| #2981 | logging group | 1 | `tests/.../FutureProjectionServiceTests.cs` | one real test file |
| #2903 | `postcss-selector-parser` | 1 | `docs/source/generated/source-hash-manifest.json` | reviewed-baseline procedure, not auto-accept |
| #2307 | dashboard build-tooling (**74 days**) | 2 | `package.json`, `package-lock.json` | was 0 yesterday — **decayed**; regenerate the lock |
| #2920, #2928 | cash-ladder, CSV evidence | 4 | 3 generated + 1 `src/**/README.md` | near-mechanical |
| #3004 | `reconciliation-lineage-2636` | 16 | was 8 | decaying fast |
| #2953 | `manual-journal-audit-recovery` | 52 | mostly generated | mechanical but large |
| #2826 | `first-trusted-close` | 4 | 4 hand-written, incl. `LedgerJournalInternalTransactionSource.cs` | genuine; overlaps #3026 |
| #2896, #2897, #2789 | replay, corp-action snapshots, backtesting | 85 / 92 / 114 | mixed | #2789 stays `[DRAFT — RE-CUT REQUIRED]` |
| #2587 | `react-router` (**major**) | 445 | was 607 | re-cut, do not merge |

Three dependency PRs the last determination flagged have since merged (#2965, #2967, #2968), as did
#2998 and #2999. #2983 and #3010 are closed. **#2307 moving from 0 to 2 conflicts in one day is the
decay argument observed again**, and it is the oldest thing in the queue.

## Ranking

| Priority | Work | Why now |
| --- | --- | --- |
| **P0** | **Land #3028.** Merge `main`, run `resolve-generated-merge-conflicts.py --apply`, regenerate, revalidate, get a fresh hosted gate on the merged head | The `W9-GOV-008` remainder — the fail-closed tenancy default with a real configuration key — is **built**. It conflicts in two generated files. Its own body still shows `quality-gate` pending on the refreshed commit; that is the remaining work, not the design |
| **P0** | **Land #3026.** Same procedure, then dispatch `production-certification.yml` for its branch | The statement → ledger → casework PostgreSQL round trip, ranked highest-value engineering for two consecutive determinations. `quality-gate` already passed at `5519d562`; hosted database proof is the open item |
| **P0** | Provide **both** `MDC_SIGNING_CERT_PFX_BASE64` and `MDC_SIGNING_CERT_PASSWORD` in the protected `desktop-release-signing` environment | Unchanged and still not engineering work. `desktop-installer-packaging.yml:57-61` throws without both. Hard prerequisite for a signed tag run; nothing else waits on it |
| **P0** | Decide the ~1 GB consumer installer question | Unchanged (`implementation-todo-list.md:107`, 1,043,350,783 bytes). It ships inside the RC; decide before the freeze |
| **P0** | Choose the release tag scheme so RC and stable identities strictly increase | **Reclassified from engineering to decision.** The preflight now fails closed on the collision, so this can no longer corrupt a release — but it will refuse the tag. Pick the scheme before freezing |
| **P0** | Drain the clean dependency PRs **before the freeze**: #2944, #2932. Resolve #2981 (one real test file) and #2903 (source-hash manifest, via the reviewed-baseline procedure) or record an explicit pre-freeze deferral | The RC freezes the dependency set; bumping afterwards invalidates the same-commit evidence the tag exists to mint |
| **P0** | **Then** freeze a commit and cut the RC tag | The tag run now calls `Publish Smoke` (`web-workstation`/`win-x64`) and `Production Certification` itself. Every candidate-changing row above must land first — the tracker requires all P0 rows on **one** release commit (`implementation-todo-list.md:125-127`) |
| **P0** | **After** the tag run, schedule the operator replay/reconciliation review of the uploaded `production-recovery-drill-*` artifact | `PRD-015` is not closed by the run alone; the artifact does not exist until the run uploads it |
| **P1** | Fix the registry parse break and enforce the schemas: quote the `W8-UX-CONSOL-001` summary, make `load_data` fail loudly for `docs/roadmap/data/*`, wire `docs/roadmap/schemas/*.json` into `validate-roadmap-registry.py` | New finding above. Under an hour. The file that is authoritative for program status currently parses only in this repository, and the validator cannot distinguish valid from recovered-by-fallback |
| **P1** | Take or decline `W9-CORPACT-011` | **7 days** in `ready_for_acceptance`. Zero engineering; clears the last W9 acceptance lane |
| **P1** | Merge #2931 while it is clean, and reconcile the readiness tracker via #3018 | #2931 went 7 → 0 conflicts; that window closes. The tracker is 81 commits behind its own baseline |
| **P2** | Continue `W10-LOT-002` — the only `critical` row | Verified open in source today: `grep -rn "Successor" src/Meridian.Contracts/Accounting/Lots/` returns **nothing**, so append-only predecessor/successor corporate-action mutations are unimplemented. Amortization, advance refunding, and shadow-operation acceptance remain, the last **defined** at `security-lot-convergence-blueprint.md:287,306,334` — unimplemented, not unscoped. Do not drop it: it is the gate between a canonical-lot cutover and silent basis corruption |
| **P2** | Schedule `W10-SEAM-001` live operator certification, then `W10-MARK-001` | Both are one operator session from closing; `SEAM` unblocks the desktop lane |
| **P3** | Drain the 2026-09-28 codex/automation set (#3011, #3016, #3018, #3019, #3020, #3024) — generated conflicts only | Cheap, and every day they sit they get more expensive: #2307 proved that in 24 hours |
| **P3** | Hold #2878 and #2587 (majors) until after the RC; re-cut #2587 and #2789 | Majors do not belong in a pre-freeze drain |

## What not to work on

No new product surface (`program-state.yml` `scope_gate`). Not the ten other planned W10 rows.
No re-cutting the large stranded branches except #2789 and #2587. No re-proving the certification
lane — it is green on the current head. No re-litigating accepted W9 rows.

**And: do not write another determination tomorrow.** This is the fourth in ten days, and the third
in three. The 2026-09-28 document opened by observing that nothing on its predecessor's list had
moved; this one observes that its own top two items became pull requests that nobody has merged.
The constraint is review and merge throughput, not ranking. A determination is worth writing when
the queue has drained enough that the ranking would change — not on a cadence.

## Note on the pending 2026-09-28 document

[#3012](https://github.com/rodoHasArrived/Meridian-main/pull/3012) proposes
`next-work-determination-2026-09-28.md` and is still open. Its baseline `0b956b06` is 81 commits
behind `main`, its top two recommendations are now the in-flight PRs above, and three of its claims
are corrected in this document. **Recommend closing #3012 without merging** rather than landing two
determinations that both claim to supersede 2026-09-27. If it is merged first instead, archive
`next-work-determination-2026-09-28.md` alongside 2026-09-27 and keep this document as the active
input. That is a maintainer call, not one this document makes.
