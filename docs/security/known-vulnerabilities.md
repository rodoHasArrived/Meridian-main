# Known Vulnerabilities

This document is the central registry for dependency vulnerabilities that have been assessed, risk-accepted, or remediated in Meridian.

## Registry Policy

- **Scope:** NuGet, npm, Docker image, GitHub Action, and other third-party dependency findings.
- **Single source of truth:** Security workflow exceptions must point back to this file rather than duplicating rationale inline.
- **Required fields for accepted risk:** package, advisory/CVE, source, justification, mitigation, review cadence, and named owner/approver.
- **Review cadence:** Accepted vulnerabilities must be reviewed at least quarterly and removed promptly when an upstream fix becomes available.
- **Workflow integration:** the `dependency-evidence` job in `.github/workflows/production-certification.yml` is the enforcing gate. npm findings route through `build/scripts/ci/validate-npm-audit.py` against `build/config/security/npm-audit-accepted-advisories.json`, which fails closed on both unaccepted and stale entries. `Directory.Build.props` may suppress a NuGet restore-audit finding only by exact advisory URL after the same accepted-risk review; note that `NuGetAuditSuppress` does not affect `dotnet list package --vulnerable`, so a suppressed NuGet advisory still reds the gate.

## Pending decision: braces stack exhaustion (2026-10-05)

**Status: proposed, not accepted.** This investigation authorizes no exception. The active
machine register remains `"accepted": []`; Production Certification must remain red for this
finding until a supported remediation passes or an authorized human records a bounded decision.

### Evidence and dependency exposure

- **Finding:** `braces` 3.0.3, [GHSA-vfj7-8cjw-p6xm](https://github.com/advisories/GHSA-vfj7-8cjw-p6xm)
  / CVE-2026-93687, high severity, uncontrolled recursion / stack-exhaustion denial of service
  from deeply nested patterns. The advisory covers `<=3.0.3` and lists no patched version.
  npm reports CVSS 3.1 **7.5**; the current GitHub advisory reports CVSS 4.0 **8.7**. These are
  different scoring versions, not evidence of a severity downgrade.
- **Audited baseline main:** `e3bf60bae577c132d8444a827ca4dc3181cc48a1`.
  [Production Certification #147, attempt 1](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37351972283)
  [dependency job 111904784527](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37351972283/job/111904784527)
  fails with `UNACCEPTED GHSA-vfj7-8cjw-p6xm (braces, high): no acceptance entry`.
  NuGet passed; the npm step's `continue-on-error` allows evidence collection, but its failed
  outcome correctly fails the final assertion. A successful step conclusion alone is not a pass.
- **Retained evidence:** [manifest and checksums](evidence/2026-10-05-braces/manifest.json),
  [hosted audit](evidence/2026-10-05-braces/hosted/npm-audit.json),
  [hosted gate decision](evidence/2026-10-05-braces/hosted/npm-audit-gate.json), and
  [fresh installed graph](evidence/2026-10-05-braces/local/dependency-graph.json).
  A clean local `npm ci` and full `npm audit --json` reproduce the same high-severity root cause.
  Five high package entries resolve to this one advisory. The separate low-severity
  `postcss-selector-parser` finding is not covered by this proposal.

**Main refresh:** `600cde87be9de99c615594cb1a2b09f507cd2b2f` was integrated after concurrent
lot-basis and NuGet updates. The npm manifest/lockfile, acceptance register, validator, and
certification workflow are byte-identical to the audited baseline; the
[refresh receipt](evidence/2026-10-05-braces/main-recheck.json) records that comparison.
[Production Certification #150](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37357037036)
was pending with no jobs/artifacts at the refresh observation. No passing dependency or complete
certification result is inferred for that new main commit. Earlier NuGet evidence also stays bound
to its original commit.
The newest completed dependency job at recheck was
[#148 / job 111920179861](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37356520129/job/111920179861)
on `8b382db63dd4b47fb32b8f4a199bcb0845700dee`: it failed on the same unaccepted advisory.
Its [raw gate decision](evidence/2026-10-05-braces/hosted-148/npm-audit-gate.json) and verified
artifact are also retained; the overall workflow was still unfinished at that observation.

The installed graph has these paths, all marked development dependencies in the lockfile:

```text
tailwindcss 3.4.15 -> chokidar 3.6.0 -> braces 3.0.3
tailwindcss 3.4.15 -> micromatch 4.0.8 -> braces 3.0.3
tailwindcss 3.4.15 -> fast-glob 3.3.3 -> micromatch 4.0.8 -> braces 3.0.3
```

`tailwind.config.ts` uses the repository literals `./index.html` and `./src/**/*.{ts,tsx}`;
PostCSS invokes Tailwind during compilation. No application-source imports of these packages
were identified. The current production dependency audit reports zero findings and the publish
layout copies static `wwwroot` assets. This supports a build/development/publishing exposure
assessment, not a claim that the package is unexploitable. Publishing can build a missing bundle;
a malicious repository/configuration change or future untrusted pattern input can still crash
the Node process. `npm audit --omit=dev` is exposure evidence only and must not replace the full gate.

### Available remediation and rejected shortcuts

Registry metadata rechecked on 2026-10-05 still ends at `braces` **3.0.3**. The latest
`micromatch` **4.0.8** and `fast-glob` **3.3.3** retain the affected chain; latest Tailwind 3
**3.4.19** still uses `chokidar ^3.6.0`, `fast-glob ^3.3.2`, and `micromatch ^4.0.8`.
There is no evidenced compatible patch or override today. Upgrading only Chokidar across majors
would leave the Micromatch path and would change its glob API contract. `brace-expansion` is a
different package; [PR #3031](https://github.com/rodoHasArrived/Meridian-main/pull/3031) currently
has zero changed files and cannot remediate this finding.

npm suggests **Tailwind 4.3.3**, a supported major-version migration using the
[official upgrade guide](https://tailwindcss.com/docs/upgrade-guide) and the matching
`@tailwindcss/vite` or `@tailwindcss/postcss` integration. This is the durable remediation
candidate. It requires reviewing configuration/CSS utility and Preflight changes, confirming
browser support (the guide requires Safari 16.4, Chrome 111, Firefox 128), testing the workstation
and published asset build, and re-auditing the complete resulting graph. A version-only forced
audit fix is not validated remediation. No migration or dependency override is applied here.

### Proposed bounded exception for human decision

| Field | Proposed decision terms |
| --- | --- |
| Scope | Only GHSA-vfj7-8cjw-p6xm in `braces` 3.0.3 through the documented Tailwind 3 development graph; severity ceiling **high**. No other advisory or runtime path is accepted. |
| Rationale | No compatible published patch; current inputs are repository-controlled build patterns and no shipped application runtime path was identified. Residual build availability risk remains. |
| Named decision owner | **@rodoHasArrived**, repository owner and default/CI CODEOWNER in `.github/CODEOWNERS`. Must explicitly approve these terms, reject them and keep certification blocked, or commission the Tailwind 4 migration. A delegate must be named in the human decision record. |
| Decision point | Before any exception is added to the active register or used for release certification. Record a dated human decision linked to the exact candidate commit and evidence. No approval, approver signature, or `accepted_on` date is recorded by this proposal. |
| Expiry | Fixed hard stop **2026-10-19 00:00 UTC** (2026-10-18 17:00 America/Phoenix), at most 14 days from this proposal; late approval does not extend it. Under the existing inclusive `review_by` logic, use **2026-10-18**, so the gate fails from 2026-10-19 UTC. |
| Review | Owner rechecks by **2026-10-12** and before every release, plus immediately for an upstream fix, new dependency path, severity increase, exploit evidence, or changed input/runtime exposure. No automatic renewal. |
| Exit | Prefer the supported migration or a newly published compatible fix; validate the complete graph and remove any now-stale acceptance in the same change. Without a validated fix or new explicit decision, expiry leaves certification blocked. |

Approval is conditional on the human owner verifying these mitigations on the candidate:

The current validator matches advisory, package, severity ceiling, and date; it does **not**
enforce package version, dependency path, development/runtime status, or candidate SHA. The
proposed graph/exposure boundary therefore depends on reviewed lockfile and candidate evidence.
Any new dependency path, runtime reachability, or untrusted-pattern input invalidates this scope:
remove the active mirror and keep certification blocked pending a new explicit human decision.

1. Keep glob/configuration inputs as reviewed repository literals; do not feed uploads, API data,
   or other untrusted patterns into Tailwind/Chokidar/Micromatch. Review changes to Tailwind,
   PostCSS, Vite, dependency manifests, and the lockfile for altered exposure.
2. Use disposable, time-bounded build runners. The current certification job has a 35-minute
   timeout, read-only contents permission, and checkout credentials are not persisted. A timeout
   limits runner exposure; it does not prevent a stack-exhaustion crash. Review other build and
   publish lanes separately before treating these controls as universal.
3. Preserve the full dependency audit, high-severity threshold, NuGet check, final two-gate
   assertion, and failure-time evidence upload. Do not omit development dependencies, suppress
   findings, substitute a package name, force a version, or edit the validator to get green.
4. After an explicit approval only, add a narrowly scoped record here and its machine mirror
   with the real approval date, named owner/approver, decision URL, rationale, and the fixed
   `review_by` above. Test expiry, severity escalation, stale entries and new advisories, then run
   the unchanged complete Production Certification workflow on the resulting commit. Retain the
   run/attempt, all four job results, raw audits, gate JSON, artifact IDs/digests/expiry, and exact
   commit. Local success or a prior main run does not certify that new commit.

Until that decision is recorded, this section remains a proposal and the active register stays
empty. A green certification workflow would still not supply separate release/operator approvals.

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
