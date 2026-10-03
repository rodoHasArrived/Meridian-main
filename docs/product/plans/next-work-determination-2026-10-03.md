# What To Work On Next — Meridian (2026-10-03)

**Status:** prioritization input; not a governance decision and not a roadmap-status document
**Owner:** core-team
**Reviewed:** 2026-10-03
**Baseline:** `main` at `94c53780d5772ca8ac8bc34b80bd4532bbcc381d`
**Supersedes:** the Tier 0 of [2026-10-02](next-work-determination-2026-10-02.md). The rest of that
document is carried forward by reference, re-verified, and **not restated** here
**Method:** read live GitHub Actions job *and step* state for `Production Certification` runs #125
through #132; reproduced the failing gate locally with `npm audit --json`; queried the npm registry
for the affected package's published versions; measured the dependency set across the green/red
boundary with `git diff --stat`. Every claim is anchored to a run id, a commit, a `file:line`, or a
reproducible command.

> This document ranks work. It does not move a roadmap row, accept an item, risk-accept a
> vulnerability, or certify a release.

## Headline

**`Production Certification` is red on `main` and has been for the last two runs. The dependency
set did not change. A newly surfaced advisory against `braces` has no upstream fix, and it blocks
the release candidate that the 2026-10-02 determination ranked as the only remaining P0 path.**

The previous determination's central claim — "there is no longer an engineering-shaped blocker
between `main` and a release candidate" and "`main` is producing [a certified commit] on every
push" — **was true when written and is false now.** There is no green head to freeze.

| Run | Head | Conclusion | When |
| --- | --- | --- | --- |
| [#130](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37073985831) | `2dbd8723` | **success** | 2026-10-02 22:43 |
| [#131](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37084625401) | `b50b6111` | **failure** | 2026-10-03 01:03 |
| [#132](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37112317558) | `94c53780` | **failure** | 2026-10-03 09:13 |

Three of four jobs are green at `94c53780` — documentation evidence, the backup/restore drill, and
the deterministic PostgreSQL integration lane all pass. **Only `NuGet and npm dependency evidence`
fails**, and only its npm half: the NuGet gate's outcome is `success`.

## The failure, precisely

From the job log of run #132, job `111172463567`, step 9:

```
npm-audit gate: UNACCEPTED GHSA-vfj7-8cjw-p6xm (braces, high): no acceptance entry
npm-audit gate: FAIL — see unaccepted or stale entries above.
```

Reproduced locally in `src/Meridian.Ui/dashboard` (`npm audit --json`, exit 1): **6 advisories, 1
low and 5 high. All five highs are one root cause.**

- **GHSA-vfj7-8cjw-p6xm** — "braces vulnerable to stack-exhaustion denial of service through deeply
  nested patterns", **high**, vulnerable range **`<=3.0.3`**.
- The lockfile pins `braces` **3.0.3** (`package-lock.json:1875-1877`).
- **`3.0.3` is the latest version `braces` has ever published** (`registry.npmjs.org/braces`:
  `dist-tags.latest = 3.0.3`). **There is no fixed version to bump to.**
- It fans out to `chokidar`, `micromatch`, `fast-glob`, and `tailwindcss`, which is why one advisory
  reports as five.
- npm's only suggested remediation is **`tailwindcss` 4.3.3 with `isSemVerMajor: true`** — a
  major bump from the pinned `3.4.15`.

**The gate is behaving correctly.** `validate-npm-audit.py` is fail-closed by design, and
`build/config/security/npm-audit-accepted-advisories.json` carries `"accepted": []` — no entry, so
no suppression. Nothing is broken in the gate or in the repository.

### This is not a repository regression, and that is the important part

**The dependency set is byte-identical across the green/red boundary.**

```bash
git diff --stat 2dbd8723 b50b6111 -- src/Meridian.Ui/dashboard/package-lock.json \
                                     src/Meridian.Ui/dashboard/package.json
# (no output)
```

`braces` was `3.0.3` at the green run #130 and is `3.0.3` now. `package-lock.json` has not been
touched by any of the 26 commits since the 2026-10-02 baseline — its last change was `6934d934`.
The advisory surfaced in npm's feed between runs #130 and #131; the repository did not move.

**So the certification lane contains one gate whose verdict depends on the date it runs rather than
on the commit it runs against.** `npm audit` queries the live advisory registry. For a lane whose
entire purpose is minting *same-commit* release evidence, that is a structural gap, and it has now
materialized:

- An RC tagged at `02196672` yesterday would have been green at tag time and **red on re-run
  today**, with identical content.
- Conversely, a green dependency-evidence artifact proves "no unaccepted high advisory *as known on
  the run date*", which is a weaker claim than the artifact's placement in a release-certification
  lane implies.
- This recurs by construction. It is not specific to `braces`, and draining the dependency queue
  does not prevent the next instance.

This is a **new** finding. No prior determination identified it, and it reframes the pre-freeze
dependency drain: the drain reduces exposure but cannot close a time-dependent gate.

## Tier 0 — Unblock the dependency gate. Nothing else can reach a green head.

Every item in the 2026-10-02 Tier 0 is downstream of this. The signing credential, the installer-size
decision, and the tag itself are all still real, still ranked, and **all unreachable** until a
commit can go green.

There are exactly two paths, and they are not close in cost:

1. **Risk-accept GHSA-vfj7-8cjw-p6xm in the register** — the recommended path. This is precisely the
   case the gate was built for; its own docstring names "an advisory whose vulnerable code path is
   unreachable in the shipped artifact and which has no compatible upstream fix". The reachability
   case is strong and verifiable: **all five affected packages are `dev: true` in the lockfile**
   (`braces` 3.0.3, `micromatch` 4.0.8, `chokidar` 3.6.0, `fast-glob` 3.3.3, `tailwindcss` 3.4.15),
   `tailwindcss` is a `devDependencies` entry, and `braces` is reached only through build-time glob
   and file-watch tooling. It is not in the shipped `wwwroot/workstation/` bundle.
2. **Major-bump `tailwindcss` 3.4.15 → 4.3.3** — not pre-freeze work. It is a breaking change to the
   browser workstation's entire styling layer, and the 2026-10-02 determination already ranked
   majors (#2878, #2587) as post-freeze. Taking a styling major to clear a dev-only DoS advisory
   inverts that ordering for no security gain in the shipped artifact.

**Path 1 requires a named human approver and this document does not supply one.** The registry
policy in `docs/security/known-vulnerabilities.md` requires "package, advisory/CVE, source,
justification, mitigation, review cadence, and named owner/approver" for every accepted risk, and
`validate-npm-audit.py` enforces `owner`, `accepted_on`, and `review_by` as required fields
(`REQUIRED_ACCEPTANCE_FIELDS`). A risk acceptance signed by an automated session would be a forged
approval in a security register, so the entry is deliberately **not** authored here. `KV-2026-001`
is the format precedent, including its retirement record.

What the approver needs to decide is narrow: the `review_by` horizon (the registry's cadence is
quarterly), and whether dev-only reachability is accepted as the mitigation basis. The evidence
above is the whole technical case.

**Do not suppress this by lowering `--fail-level`, by emptying the audit step, or by setting
`continue-on-error` on the assert step.** The gate's value is that it caught a real advisory within
hours. The acceptance register is the designed escape hatch; use it.

## Tier 1 — Carried forward from 2026-10-02, re-verified today

Each re-checked against current source at `94c53780`. **All still open, all unchanged, none
restated** — read the predecessor for the rationale.

| Item | Verified today |
| --- | --- |
| **The registry YAML parse break** (P1) | Still failing, still line 797, now **4 days old**. `python3 -c "import yaml;yaml.safe_load(...)"` → `ScannerError` at `797:886` |
| **Take or decline `W9-CORPACT-011`** (P1) | Still `ready_for_acceptance`, `last_reviewed: 2026-09-22` — now **11 days**. Zero engineering |
| **Re-review `W9-GOV-008` / `W9-INGEST-009`** (P1) | Unchanged: `2026-09-28` and `2026-09-25`, both still predating the merges that moved source past their records |
| **`W8-WPF-PARITY-001` row hygiene** (P1) | `last_reviewed: 2026-07-06` — now **89 days**, on an active co-equal lane and named productization target |
| **`W10-LOT-002` successor mutations** (P2) | Still absent: `grep -rn "Successor" src/Meridian.Contracts/Accounting/Lots/` returns nothing |
| **`W10-PROV-001` status vs. #3041** (P2) | Row still `planned` (`last_reviewed: 2026-07-31`) with an open implementation PR |
| **Close #3031, #3012, #3030** (P1/P2) | All three still open. #3031's substance (`brace-expansion` 5.0.12) is confirmed on `main` at `package-lock.json:1876`, so it remains obsolete — **and note it is a different package from the `braces` advisory above; it does not address this failure** |

One correction to the predecessor's framing, not its facts: it listed the seven clean dependency PRs
as pre-freeze work because "the RC freezes the dependency set". That reasoning holds, but Tier 0
shows the freeze does not freeze the *gate*. Draining them is still right; it is no longer
sufficient.

## What not to work on

- **Bumping `braces`.** There is nowhere to bump to. 3.0.3 is the latest published version.
- **The `tailwindcss` 4.x major**, as a way to clear this advisory. See Tier 0, path 2.
- **Cutting the RC before the gate is green.** The tag's one job is minting same-commit evidence;
  tagging a red commit mints a red artifact.
- **Re-running the certification lane expecting a different result.** Two consecutive runs, and the
  cause is a live-registry advisory, not a flake. Run #132 is reproducible locally.
- **New product surface.** The `program-state.yml` `scope_gate` is unchanged, and eight rows remain
  in progress or awaiting acceptance across three waves.
- **Another determination tomorrow.** This one exists because the top-ranked item became blocked by
  a cause no prior determination had identified. The 2026-10-02 bar still applies: the next one is
  due when the gate clears, the queue drains, or the RC lands.

## Summary

| Priority | Work | Why now |
| --- | --- | --- |
| **P0** | **Risk-accept GHSA-vfj7-8cjw-p6xm** in `npm-audit-accepted-advisories.json` with a mirroring `docs/security/known-vulnerabilities.md` record — **needs a named human approver** | The only blocker between `main` and a green head. No upstream fix exists; all five affected packages are dev-only. Every other P0 is downstream |
| **P0** | Decide whether the dependency gate belongs in a *same-commit* certification lane as currently built, or needs a pinned advisory snapshot | A green artifact from this lane is date-scoped, not commit-scoped. It just flipped a green head red with no repo change, and will again |
| **P0** | Then resume the 2026-10-02 Tier 0 in its recorded order: signing credential → installer-size decision → drain clean PRs → freeze and tag `v0.1.0-rc.1` | Unchanged and still correct. Blocked, not wrong. No `v*` tag has ever existed |
| **P1** | Fix the registry parse break (**4 days**) | Under an hour; the status-authoritative file parses only in this repository |
| **P1** | Take or decline `W9-CORPACT-011` (**11 days**); re-review `W9-GOV-008`, `W9-INGEST-009`; refresh `W8-WPF-PARITY-001` (**89 days**) | Zero engineering, and four rows currently misdescribe `main` |
| **P1** | Close #3031 (obsolete), #3012 and #3030 (superseded) | They misdescribe the program; #3031 is also easily mistaken for a fix for Tier 0 and is not one |
| **P2** | `W10-LOT-002` successor mutations; reconcile `W10-PROV-001` against #3041; read #2826 against merged #3026 | Unchanged from 2026-10-02 |
| **P3** | The `.gitattributes` merge driver; #2307's lockfile; the three broken tracker links | Unchanged from 2026-10-02 |

## Limits of this document

- **It does not risk-accept anything.** Tier 0's recommended path requires a named approver by the
  registry's own policy, and that approval is not this document's to give.
- **It does not move a roadmap row.** The registry stays authoritative for status; the tracker stays
  authoritative for the P0 release gate.
- **The advisory's publication timestamp is inferred, not retrieved.** The GitHub advisory API
  returned no payload for this id at the time of writing. The window between runs #130 and #131 is
  established from the gate flip against a byte-identical dependency set, which bounds it without
  depending on the feed's own metadata.
- **Recheck before acting.** Both the conflict counts carried forward from 2026-10-02 and the
  advisory set behind Tier 0 can change without a commit.
