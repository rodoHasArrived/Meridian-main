# Known Vulnerabilities

This document is the central registry for dependency vulnerabilities that have been assessed, risk-accepted, or remediated in Meridian.

## Registry Policy

- **Scope:** NuGet, npm, Docker image, GitHub Action, and other third-party dependency findings.
- **Single source of truth:** Security workflow exceptions must point back to this file rather than duplicating rationale inline.
- **Required fields for accepted risk:** package, advisory/CVE, source, justification, mitigation, review cadence, and named owner/approver.
- **Review cadence:** Accepted vulnerabilities must be reviewed at least quarterly and removed promptly when an upstream fix becomes available.
- **Workflow integration:** the `dependency-evidence` job in `.github/workflows/production-certification.yml` is the enforcing gate. npm findings route through `build/scripts/ci/validate-npm-audit.py` against `build/config/security/npm-audit-accepted-advisories.json`, which fails closed on both unaccepted and stale entries. `Directory.Build.props` may suppress a NuGet restore-audit finding only by exact advisory URL after the same accepted-risk review; note that `NuGetAuditSuppress` does not affect `dotnet list package --vulnerable`, so a suppressed NuGet advisory still reds the gate.

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
