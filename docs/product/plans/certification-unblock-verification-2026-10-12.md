# Certification Unblock — Verification Record (2026-10-12)

**Status:** verification record; not a ranking, not a governance decision, and not a roadmap-status document
**Owner:** core-team
**Reviewed:** 2026-10-12
**Baseline:** `origin/main` at `b85095b0d373c4b9701225cbba949942c4f7d93b` (2026-10-09)
**Subject:** [#3133](https://github.com/rodoHasArrived/Meridian-main/pull/3133) against `GHSA-vfj7-8cjw-p6xm`

> This record verifies one remediation and states a merge order. It does not move a roadmap row,
> accept an advisory, or certify a release. The roadmap registry remains authoritative for status;
> `docs/product/implementation-todo-list.md` remains the P0 release gate.

## Why this is not another determination

The 2026-10-10 determination ([#3132](https://github.com/rodoHasArrived/Meridian-main/pull/3132),
unmerged) closed with an explicit instruction: *"Do not write one. ... The next useful artifact in
this directory is a **merge or a decision record**, not a fifth ranking."*

This is that record. **The ranking is unchanged** — the 2026-10-10 summary table stands, and nothing
in it was invalidated by re-checking today. What is new is that the P0 now has a built, green,
standalone remediation, and its effect on the failing gate has been measured end to end for the
first time. No prior determination could have done this: #3133 did not exist on 2026-10-10.

## The remediation exists and clears the gate

[#3133](https://github.com/rodoHasArrived/Meridian-main/pull/3133) (`claude/practical-faraday-jg7duy`,
opened 2026-10-11, **still a draft**) migrates the workstation to Tailwind 4. Verified today against
its head `4cc7cedf`:

- **The vulnerable path is gone, structurally.** `braces`, `micromatch`, `chokidar` and `fast-glob`
  are all absent from its lockfile (359 packages). On `main`, `tailwindcss` is the **sole** dependent
  of all four: `chokidar ^3.6.0`, `fast-glob ^3.3.2` and `micromatch ^4.0.8` have no other parent in
  the lockfile, and `braces` arrives only through `chokidar` and `micromatch`. `tailwindcss@4.3.3`
  declares **zero dependencies**, and a probe of the full Tailwind 4 subtree
  (`tailwindcss` + `@tailwindcss/postcss` + `@tailwindcss/vite`, 87 packages) reintroduces none of
  them. This is a removed subtree, not a version pin that can drift back.
- **`npm audit` on its lockfile reports zero vulnerabilities at every severity** — `info 0, low 0,
  moderate 0, high 0, critical 0`. The `main` baseline reports 7 (5 high, 2 moderate). The two
  moderates clear as well, so the lane does not merely drop below `--fail-level high`.
- **The gate's own decision function passes, with the acceptance register untouched.** Running the
  exact validator the failing step runs, against #3133's audit output and the current
  `"accepted": []` register:

  ```
  npm-audit gate: PASS — every gated advisory is explicitly accepted and unexpired.
  GATE EXIT=0
  ```

  `{"unaccepted": [], "accepted_in_use": [], "stale_acceptances": [], "passed": true}`
- **All 23 of its checks are green**, including `quality-gate`, `integration-gate`, `verify-dotnet`,
  `verify-browser`, `verify-docs`, `verify-workflows`, `scope-gate`, `Secret Scan`, CodeQL, the
  PostgreSQL integration job, `Validate Web Workstation Screenshots`, `WPF W4 Acceptance` and
  `Seeded Demo Smoke`.
- **The migration is bounded and behaviour-preserving by construction.** The existing TS config is
  retained via `@config "../../tailwind.config.ts"` rather than rewritten, `@import "tailwindcss"
  source(none)` scopes scanning, `plugins: []` means no plugin compatibility surface, and the three
  Tailwind 4 default changes that would otherwise show up visually (button cursor, placeholder
  colour, arbitrary-vs-named text-size ordering) are each restored explicitly in
  `src/styles/index.css`. A new `src/tailwind-build.test.ts` locks the result.

### Reproduce

```bash
git fetch origin claude/practical-faraday-jg7duy
mkdir -p /tmp/cert-probe
git show FETCH_HEAD:src/Meridian.Ui/dashboard/package.json      > /tmp/cert-probe/package.json
git show FETCH_HEAD:src/Meridian.Ui/dashboard/package-lock.json > /tmp/cert-probe/package-lock.json
( cd /tmp/cert-probe && npm audit --package-lock-only --json > audit.json )
python3 build/scripts/ci/validate-npm-audit.py \
  --audit-json /tmp/cert-probe/audit.json \
  --accepted build/config/security/npm-audit-accepted-advisories.json \
  --fail-level high --output /tmp/cert-probe/gate.json
```

## What this changes about the governance decision

`KV-2026-003` is recorded as **proposed, not accepted**, and its own text sets the exit condition:
the lane *"must remain red for this finding until a supported remediation passes or an authorized
human records a bounded decision."* **A supported remediation now passes.**

So the disposition is to **withdraw `KV-2026-003` as superseded by remediation**, not to approve it.
Two consequences:

- Its lapsed 2026-10-07 decision point and 2026-10-19 hard expiry stop being deadlines. Four
  determinations ranked that acceptance as the P0 unblock; it is no longer on the critical path.
- The register stays `"accepted": []`, which is the stronger end state. An approved acceptance would
  have to be retired later in the same change that lands Tailwind 4 — and the validator fails closed
  on stale entries from both directions. Landing the remediation first skips that cycle entirely.

Accepting an advisory remains an owned human call. This record does not take it, and the program no
longer needs it taken.

## Merge order: #3133 before #3102 — the new finding

[#3102](https://github.com/rodoHasArrived/Meridian-main/pull/3102) (`W10-LOT-002` lot posting)
carries the **identical** Tailwind 4 edit, but on stale sibling versions. Measured today:

| devDependency | `main` | #3102 | #3133 |
| --- | --- | --- | --- |
| `tailwindcss` | 3.4.19 | **4.3.3** | **4.3.3** |
| `@tailwindcss/postcss` | — | **4.3.3** | **4.3.3** |
| `autoprefixer` | 10.6.1 | removed | removed |
| `vite` | **8.3.1** | 8.0.16 | **8.3.1** |
| `postcss` | **8.5.28** | 8.5.23 | **8.5.28** |
| `@vitejs/plugin-react` | **6.1.1** | 6.0.2 | **6.1.1** |
| `eslint` | **^10.11.0** | ^10.6.0 | **^10.11.0** |
| `typescript-eslint` | **^8.70.1** | ^8.62.1 | **^8.70.1** |

`#3133`'s manifest is exactly `main`'s current versions plus Tailwind 4. `#3102`'s is Tailwind 4 plus
five silent downgrades — the risk the 2026-10-10 determination flagged.

`#3102` measures **141 conflicts** against `main` today: 134 built workstation assets, 4 generated
docs, and **3 hand-written** — `database/manifest/contracts.json`,
`src/Meridian.Ui/dashboard/package.json`, `src/Meridian.Ui/dashboard/package-lock.json`.

**If #3133 lands first, two of those three resolve to "take `main`" with no judgment call**, because
`main` will then carry both Tailwind 4 *and* the newer siblings — a strict superset of what #3102's
manifest wanted. The six-key manifest reconciliation that the 2026-10-10 determination called a
one-sitting decision disappears, leaving only `contracts.json` to read.

It also **de-welds the certification unblock from `W10-LOT-002`**, the depth slate's only `critical`
row. The lane can go green on a two-file dependency change instead of waiting on a lot-posting review
spanning 336 files. That split is what three determinations asked for; #3133 is it.

## What is actually left

1. **Mark #3133 ready, review, merge.** Expect the first green Production Certification since
   **#130** (`2dbd8723`, 2026-10-02). The other three certification jobs — documentation evidence,
   PostgreSQL integration, encrypted backup/restore — are already green at `b85095b0` (run **#205**,
   1 failed job of 4), so the dependency job is the only delta.
2. **Then #3102**, with the manifest conflicts reduced as above.
3. **Then the RC steps, unchanged and not engineering work:** signing secrets, the installer-size
   decision, freeze a green head, tag `v0.1.0-rc.1`.
4. **Everything else: the 2026-10-10 ranking, unchanged.** Land #3132 or close it — but do not
   re-derive it. Its P1/P2 rows still hold and were re-checked today: `W9-CORPACT-011` is now
   **20 days** at `ready_for_acceptance` with its PostgreSQL precondition met (declining is a result
   too); `W8-WPF-PARITY-001` is **98 days** unreviewed and `W8-UX-CONSOL-001` **85**, both named
   productization targets; `W5X-OEG-001` is **119 days**, the program's oldest; `W10-TAX-001` and
   `W10-CONSOL-001` still read `planned`/`2026-07-31` while their implementations are on `main`
   (`PostgresLedgerJournalStore.WashSale.cs`, `WashSaleDeferralRecord.cs`;
   `ConsolidationService.cs`, `ConsolidationPerimeterResolver.cs`,
   `accounting-screen.consolidation-panel.tsx`). `W10-LOT-002` remains the only `critical` row, and
   `grep -rn "Successor" src/Meridian.Contracts/Accounting/Lots/` still returns nothing on `main`.

## The process finding

Four determinations named this P0 correctly. The 2026-10-10 one named the remedy by exact version —
`tailwindcss@4.3.3`, from npm's own `fixAvailable`. The 2026-10-11 session built it, split it out
standalone as asked, and it went green in 23 checks. **Every analytical and implementation step has
now been completed correctly, and the lane is still red** — because the output is sitting in draft.

Five open pull requests from this lane: **#3133** (the fix, green), **#3132 / #3125 / #3112** (three
rankings that each identified it), and **#3102** (the critical row). The scarce resource is not
analysis, and it is no longer implementation either. It is one review-and-merge pass.

**Merge or close all five in one sitting, starting with #3133.**

## Verification caveats

- **Production Certification does not run on pull requests** (`push` and `schedule` on `main` only),
  so #3133 cannot carry its own certification verdict until it lands. What is verified here is the
  gate's decision function against #3133's lockfile — the exact comparison the failing step makes —
  plus the three sibling jobs already green at `b85095b0`.
- The gate was run at `--fail-level high`, matching the workflow's own invocation.
- The 2026-10-05 caveat on the Actions API still applies: run listings come back stale depending on
  filter, so cross-check a run's `created_at` against the head commit's date. Run **#205**
  (2026-10-11, `schedule`) is the newest at the time of writing.
- In this lane read the job **log**, not step conclusions: the two gate steps are
  `continue-on-error` and report `conclusion: success` while the job fails.
