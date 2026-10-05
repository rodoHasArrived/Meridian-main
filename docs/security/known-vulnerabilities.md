# Known Vulnerabilities

This document is the central registry for dependency vulnerabilities that have been assessed, risk-accepted, or remediated in Meridian.

## Registry Policy

- **Scope:** NuGet, npm, Docker image, GitHub Action, and other third-party dependency findings.
- **Single source of truth:** Security workflow exceptions must point back to this file rather than duplicating rationale inline.
- **Required fields for accepted risk:** package, advisory/CVE, source, justification, mitigation, review cadence, and named owner/approver.
- **Review cadence:** Accepted vulnerabilities must be reviewed at least quarterly and removed promptly when an upstream fix becomes available.
- **Workflow integration:** the `dependency-evidence` job in `.github/workflows/production-certification.yml` is the enforcing gate. npm findings route through `build/scripts/ci/validate-npm-audit.py` against `build/config/security/npm-audit-accepted-advisories.json`, which fails closed on both unaccepted and stale entries. `Directory.Build.props` may suppress a NuGet restore-audit finding only by exact advisory URL after the same accepted-risk review; note that `NuGetAuditSuppress` does not affect `dotnet list package --vulnerable`, so a suppressed NuGet advisory still reds the gate.

## Pending decision: KV-2026-003 — braces stack exhaustion

**Status: proposed, not approved or active.** Investigation date: 2026-10-05.
The requested decision-maker and proposed accountable owner is **@rodoHasArrived**, the
repository's named [CODEOWNER](../../.github/CODEOWNERS). Ownership is not approval.
`build/config/security/npm-audit-accepted-advisories.json` remains empty; the dependency gate
must remain red for this advisory until a supported fix passes or a human explicitly approves
and records a bounded acceptance. Merging this investigation alone does not accept the risk.

### Finding and evidence

- **Package:** `braces` 3.0.3; **advisory:**
  [GHSA-vfj7-8cjw-p6xm](https://github.com/advisories/GHSA-vfj7-8cjw-p6xm) /
  CVE-2026-93687; **severity:** high, CVSS 3.1 7.5; **affected:** `<=3.0.3`.
  Recursive AST traversal can exhaust the Node.js stack on deeply nested brace patterns,
  including patterns below the package's character limit. An uncaught `RangeError` can
  terminate the build/watch process.
- **Initial hosted baseline checked:** `main` commit
  `e3bf60bae577c132d8444a827ca4dc3181cc48a1`, Production Certification
  [run #147, attempt 1](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37351972283),
  [dependency job 111904784527](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37351972283/job/111904784527).
  NuGet reports no vulnerable packages across the solution. npm reports five high affected
  package nodes from this one GHSA, plus a separate low advisory. The final assertion correctly
  fails on the unaccepted npm finding; step conclusions masked by `continue-on-error` are not
  the job verdict.
- **Retained baseline:**
  [production-dependency-evidence-147-1](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37351972283/artifacts/11362468562),
  artifact ID `11362468562`, ZIP SHA-256
  `478075a50b84e225b37581089a9e2f28aad6d9ad95b29b811b3297e179a02d11`,
  expires `2027-01-03T17:54:46Z`. The downloaded ZIP digest was verified. Its three scan files
  are retained under [evidence/2026-10-05-dependency-certification](evidence/2026-10-05-dependency-certification/manifest.json),
  alongside fresh npm graph, audit, upstream metadata and bounded reachability evidence.
- **Latest completed hosted recheck:** [run #151, attempt 1](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37360660094)
  on `main` commit `433ff014b51244daeab8deca5fba812f9bb5fe6a` confirms the same result:
  all 56 NuGet projects clean, five high npm nodes from this unaccepted GHSA, and the final
  dependency assertion failed. Artifact `production-dependency-evidence-151-1` (ID `11367836066`)
  was downloaded and its ZIP SHA-256 verified as
  `80d7d434e508dbf5d4df3edfa5dfc6c354ed7706a9c343072fd742f4cd3ce4d1`.
  Its raw reports are retained with the proposal PR's commit-specific evidence bundle.
- **Fresh recheck:** Node 24.19.0 / npm 11.9.0, clean `npm ci`, then `npm audit --json`
  reproduces the same finding. The unchanged validator with the empty registry returns 1.
  `npm audit --omit=dev --json` reports zero findings; this scoped result supplements the
  full scan and does not replace the all-dependencies gate.

The resolved development dependency paths are:

```text
tailwindcss 3.4.15 -> chokidar 3.6.0 -> braces 3.0.3
tailwindcss 3.4.15 -> micromatch 4.0.8 -> braces 3.0.3
tailwindcss 3.4.15 -> fast-glob 3.3.3 -> micromatch 4.0.8 -> braces 3.0.3
```

### Supported remediation options, rechecked 2026-10-05

| Option | Current evidence and disposition |
| --- | --- |
| Compatible `braces` update | npm still publishes 3.0.3 as latest; the reviewed advisory lists no fixed release. An override to 3.0.3 does not remediate this finding. Recheck upstream weekly and retire any acceptance promptly when a tested patch exists. |
| Tailwind 3.x update | Latest `v3-lts` 3.4.19 still requires chokidar 3, fast-glob 3 and micromatch 4; these retain `braces`. Updating within 3.x cannot clear this GHSA. |
| Tailwind 4 migration | npm identifies 4.3.3 as a semver-major fix. Use the [upstream migration guide](https://tailwindcss.com/docs/upgrade-guide) and `@tailwindcss/vite` or `@tailwindcss/postcss` 4.3.3, migrate the CSS entry/configuration and verify supported browsers. This is the supported removal path, but it is not implemented or certified by this proposal. |
| Unrelated low advisory | `postcss-selector-parser` 6.1.2 has GHSA-w9m9-85wc-3x92; 6.1.3 is a compatible patch. It is below the existing high gate threshold and outside this proposed acceptance. A routine lockfile update can address it separately. |

The migration must demonstrate a fresh full audit and dependency graph with this GHSA absent,
dashboard tests/build/strict typecheck, and visual/browser regression checks for the seven
operator navigation surfaces. Tailwind 4 changes configuration, utility semantics and browser
requirements; an unreviewed `npm audit fix --force`, a major transitive override, or an
unreleased fork is not a validated remediation. Re-run `bash scripts/ci.sh` and hosted
Production Certification on the resulting commit, retaining both ecosystems' evidence.

### Exposure, mitigation and residual risk

Verified at the baseline commit: the affected packages are marked development-only in the
lockfile. `tailwind.config.ts` supplies `./index.html` and `./src/**/*.{ts,tsx}` as content globs.
`postcss.config.cjs` runs Tailwind during the Vite build; fast-glob calls micromatch brace
expansion. These current globs do not exercise pathological nesting. Bounded isolated probes
of the installed packages nevertheless produced `RangeError` for deep nesting, including a
fast-glob call. Exact failure depth varies with runtime; these are library-level results, not
proof of a remotely exploitable workstation route or proof of impossibility.

The assessed consequence is loss of build/watch availability if attacker-controlled nested
patterns reach these tools. A compromised contributor, dependency, plugin or build configuration
remains relevant. A clean production-only audit is not a complete artifact reachability proof,
and the [upstream maintainer's dispute](https://github.com/micromatch/braces/issues/70#issuecomment-5995348316)
of the advisory does not withdraw the active finding.

**Conditions the human owner must verify before any acceptance is activated:**

1. Keep content/glob configuration repository-controlled and reviewed. Do not pass operator,
   provider, uploaded-file or other untrusted patterns to Tailwind, chokidar or micromatch.
   Reassess immediately if build inputs, plugins or glob sources change.
2. Run builds/watchers only in isolated development or CI processes with finite resource/time
   budgets, without production credentials; distribute prebuilt assets. The dependency evidence
   job already uses `ubuntu-latest`, read-only repository permissions, nonpersistent checkout
   credentials and a 35-minute timeout. These facts do not establish controls for every local,
   signing or release build; verify those separately. Stop/restart a failed watcher rather than
   exposing it as an operator service.
3. Continue the full NuGet/npm scans, high/critical threshold, final assertion, required checks,
   and 90-day hosted evidence retention. No suppression, severity downgrade or gate bypass is
   part of this proposal. New findings, severity escalation or changed exposure require review.

### Bounded proposal and named human decision

- **Requested decision:** @rodoHasArrived should choose and record either the Tailwind 4
  remediation work, rejection/continued blocking, or explicit temporary acceptance after
  verifying the conditions above. Requested decision deadline: **2026-10-07 17:00
  America/Phoenix** (`2026-10-08T00:00:00Z`). No response means no acceptance.
- **Proposed scope:** only `braces` / GHSA-vfj7-8cjw-p6xm at a maximum severity of `high`, in
  the reviewed dashboard development graph. Rationale: limited build-time exposure and no
  compatible upstream patch while a supported major migration is evaluated. Residual build
  availability risk remains; this is a human risk decision, not a claim of safety.
- **Fixed expiry:** proposed machine `review_by: 2026-10-19` (UTC). The existing validator
  accepts through that UTC date and fails from **2026-10-20T00:00:00Z**, which is
  **2026-10-19 17:00 America/Phoenix**. This is an upper bound, not 14 days from delayed
  approval. If the deadline or exposure changes, recheck the evidence before deciding.
- **Review cadence:** proposed owner @rodoHasArrived, weekly on October 12 and October 19,
  and immediately on an upstream patch, advisory change, incident, new runtime path or
  dependency/configuration change. Remove the acceptance as soon as a verified fix lands;
  no automatic renewal. The validator also rejects a stale acceptance that no longer matches
  a reported advisory.
- **Activation is a separate human-approved change:** record the attributable decision URL,
  actual approval date, mitigation verification, owner and expiry in this registry; then mirror
  exactly that decision in the machine registry with `id`, `ghsa`, `package`, `max_severity`,
  `reason`, `owner`, `accepted_on` and `review_by`. Do not fill an approval date or approver
  on the human's behalf. Re-run the unchanged gate, its tests and hosted certification on that
  resulting commit. A green dependency result would not itself approve a production release.

## Accepted Vulnerabilities

None at this commit. Retired acceptances are kept below for traceability.

### KV-2026-001 retired — DotNetZip 1.16.0 - Path Traversal (GHSA-xhg6-9j5j-w4vf)

- **Retired:** 2026-08-19, having reached its 2026-08-17 review date.
- **Basis:** DotNetZip is no longer referenced anywhere in the tracked build. `git grep DotNetZip`
  outside `docs/` and `archive/` returns nothing, and no `Directory.Packages.props` entry,
  project file, or props file mentions it. Independently, the `dependency-evidence` job of
  [Production Certification run 32266755834](https://github.com/rodoHasArrived/Meridian-main/actions/runs/32266755834)
  ran `dotnet list package --vulnerable --include-transitive` across the whole solution and
  reported no vulnerable packages. That command does not consult `NuGetAuditSuppress`, so a
  DotNetZip 1.16.0 still present in the graph would have been reported regardless of the
  suppression.
- **Action taken:** the `MeridianNuGetAuditSuppression` entry for this advisory was removed from
  `Directory.Build.props`, satisfying its own recorded RatchetPlan ("Remove once upgraded
  transitive dependency chain no longer triggers the advisory"). The
  `System.IO.Compression.UseStrictValidation` runtime-integrity default is retained on its own
  merits and is unrelated to this acceptance.

There are no accepted vulnerabilities at this commit;
`build/config/security/npm-audit-accepted-advisories.json` is likewise empty.

---

## Fixed Vulnerabilities (2026-08-19)

The following npm advisories were cleared in `src/Meridian.Ui/dashboard` by upgrading rather than
by accepting risk. `npm audit --package-lock-only` reports 0 vulnerabilities at this commit.

### react-router / react-router-dom 7.18.1 -> 7.18.2 (GHSA-qwww-vcr4-c8h2)

- **Severity:** High
- **Advisory:** https://github.com/advisories/GHSA-qwww-vcr4-c8h2
- **Fix:** Upgraded `react-router-dom` to 7.18.2. This closed the acceptance recorded as
  KV-2026-002 on 2026-07-28, whose stated rationale ("no patched 7.x exists") stopped being true
  when upstream narrowed the advisory range to `>=7.12.0 <7.18.2` and shipped 7.18.2. The
  acceptance entry was removed from
  `build/config/security/npm-audit-accepted-advisories.json` in the same change, because
  `build/scripts/ci/validate-npm-audit.py` fails closed on an acceptance that no longer matches a
  reported advisory.

### nanoid 3.3.16 -> 3.3.18 (GHSA-2v37-7h3g-55p8)

- **Severity:** High
- **Advisory:** https://github.com/advisories/GHSA-2v37-7h3g-55p8
- **Fix:** Added a `nanoid: ^3.3.18` override. `nanoid` is a dev-only transitive of `postcss`,
  whose declared range `^3.3.16` already admits the patched version.

---

## Fixed Vulnerabilities (2026-05-17)

The following vulnerability was fixed by pinning a transitive dependency in `Directory.Packages.props`:

### Snappier 1.3.0 → 1.3.1
- **CVE:** CVE-2026-44302 (GHSA-pggp-6c3x-2xmx)
- **Severity:** High
- **Fix:** Upgraded transitive pin to 1.3.1 (fixed in 1.3.1+)
- **Source:** Transitive dependency from Parquet.Net 5.5.0
- **Resolution:** `Directory.Packages.props` uses central transitive pinning so Parquet.Net can keep requesting Snappier 1.3.0 while restore resolves Snappier 1.3.1.
- **Validation:** `dotnet package list --project src\Meridian.Storage\Meridian.Storage.csproj --vulnerable --include-transitive --no-restore --verbosity normal` reports no vulnerable packages.

---

## Fixed Vulnerabilities (2026-03-27)

The following vulnerabilities were fixed by pinning transitive dependencies in `Directory.Packages.props`:

### System.Text.RegularExpressions 4.3.0 → 4.3.1
- **CVE:** CVE-2019-0820 (GHSA-cmhx-cq75-c4mj)
- **Severity:** High
- **Fix:** Upgraded transitive pin to 4.3.1 (fixed in 4.3.1+)

---

## Fixed Vulnerabilities (2026-02-10)

The following vulnerabilities were fixed by pinning transitive dependencies in `Directory.Packages.props`:

### System.Drawing.Common 4.7.0 → 8.0.11
- **CVE:** CVE-2021-24112 (GHSA-rxg9-xrhp-64gj)
- **Severity:** Critical
- **Fix:** Upgraded to 8.0.11 (fixed in 4.7.2+)

### System.Net.Security 4.3.0 → 4.3.2
- **CVE:** Multiple (GHSA-6xh7-4v2w-36q6, GHSA-qhqf-ghgh-x2m4, etc.)
- **Severity:** High/Moderate
- **Fix:** Upgraded to 4.3.2 (fixed in 4.3.1+)

### System.ServiceModel.Primitives 4.4.0 → 4.10.3
- **CVE:** CVE-2018-0786 (GHSA-jc8g-xhw5-6x46)
- **Severity:** High
- **Fix:** Upgraded to 4.10.3 (fixed in 4.4.1+)

### System.Private.ServiceModel 4.4.0 → 4.10.3
- **CVE:** CVE-2018-0786 (GHSA-jc8g-xhw5-6x46)
- **Severity:** High
- **Fix:** Upgraded to 4.10.3 (fixed in 4.4.1+)

### System.Formats.Asn1 6.0.0 → 8.0.1
- **CVE:** CVE-2024-38095 (GHSA-447r-wph3-92pm)
- **Severity:** High
- **Fix:** Upgraded to 8.0.1 (fixed in 6.0.1+)

### System.Security.Cryptography.Pkcs 6.0.1 → 8.0.1
- **CVE:** CVE-2023-29331 (GHSA-555c-2p6r-68mm)
- **Severity:** High
- **Fix:** Upgraded to 8.0.1 (fixed in 6.0.3+)

### System.Net.Http.WinHttpHandler 4.4.0 → 8.0.0
- **CVE:** CVE-2017-0247 (GHSA-6xh7-4v2w-36q6)
- **Severity:** High
- **Fix:** Upgraded to 8.0.0

---

## Vulnerability Scanning

Automated vulnerability scanning runs:
- **Weekly, on tag, and on demand:** the `dependency-evidence` job in
  `.github/workflows/production-certification.yml` runs `dotnet list package --vulnerable
  --include-transitive` and the npm audit gate.
- **On every PR and push:** CodeQL analysis via `.github/workflows/codeql.yml`.

There is no `.github/workflows/security.yml`; the lane it used to describe now lives in the two
workflows above.
