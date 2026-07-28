# Known Vulnerabilities

This document is the central registry for dependency vulnerabilities that have been assessed, risk-accepted, or remediated in Meridian.

## Registry Policy

- **Scope:** NuGet, npm, Docker image, GitHub Action, and other third-party dependency findings.
- **Single source of truth:** Security workflow exceptions must point back to this file rather than duplicating rationale inline.
- **Required fields for accepted risk:** package, advisory/CVE, source, justification, mitigation, review cadence, and named owner/approver.
- **Review cadence:** Accepted vulnerabilities must be reviewed at least quarterly and removed promptly when an upstream fix becomes available.
- **Workflow integration:** `.github/workflows/security.yml` may filter or annotate accepted findings only when they are documented here. `Directory.Build.props` may suppress a NuGet audit finding only by exact advisory URL after the same accepted-risk review.

## Accepted Vulnerabilities

### KV-2026-001 — DotNetZip 1.16.0 - Path Traversal (GHSA-xhg6-9j5j-w4vf)

**CVE:** CVE-2024-48510
**Severity:** High
**Advisory:** https://github.com/advisories/GHSA-xhg6-9j5j-w4vf
**Ecosystem:** NuGet (transitive)
**Affected Package:** DotNetZip 1.16.0

**Description:**
DotNetZip 1.16.0 contains a path traversal vulnerability that allows a remote attacker to potentially execute arbitrary code by crafting malicious paths in zip archives.

**Source:**
Transitive dependency from QuantConnect.Lean packages (required for backtesting integration):
- QuantConnect.Lean 2.5.17414
- QuantConnect.Lean.Engine 2.5.17414
- QuantConnect.Common 2.5.17414
- QuantConnect.Indicators 2.5.17414

Current NuGet audit surfaces:
- `src/Meridian/Meridian.csproj`
- `tests/Meridian.Tests/Meridian.Tests.csproj`
- `benchmarks/Meridian.Benchmarks/Meridian.Benchmarks.csproj`

**Status:**
- **No fix available** - DotNetZip is deprecated and no longer maintained
- **Cannot be upgraded** - Baked into QuantConnect.Lean binary dependencies
- **Alternative:** ProDotNetZip 1.19.0+ has a fix, but cannot be used as a drop-in replacement for transitive dependencies

**Risk Assessment:**
**LOW** - Vulnerability requires extracting user-provided zip files, which is not a use case in this application.

**Mitigations:**
1. Application does not accept or extract user-provided zip files
2. All zip file operations are internal (data packaging/archival) with trusted sources
3. No code paths expose zip extraction to external input
4. QuantConnect integration is isolated and does not process untrusted archives

**Remediation Plan:**
- Monitor QuantConnect.Lean releases for updated DotNetZip dependency
- Consider submitting PR to QuantConnect to update their dependency
- Re-evaluate if application requirements change to include zip extraction from external sources

**NuGet Audit Disposition:** Suppressed by exact advisory URL in `Directory.Build.props`; do not suppress `NU1903` globally or disable NuGet audit.
**Tracking Reference:** Filtered in `.github/workflows/security.yml` during the NuGet vulnerability scan and suppressed for NuGet audit in `Directory.Build.props` after the accepted-risk review.
**Review Date:** 2026-05-17
**Next Review:** 2026-08-17 (quarterly)
**Approved By:** Security maintainers / repository owners

### KV-2026-002 — react-router 7.12.0–8.2.0 - RSC Mode CSRF Bypass (GHSA-qwww-vcr4-c8h2)

**CVE:** —
**Severity:** High
**Advisory:** https://github.com/advisories/GHSA-qwww-vcr4-c8h2
**Ecosystem:** npm (transitive)
**Affected Package:** react-router (via `react-router-dom` 7.18.1 in `src/Meridian.Ui/dashboard`)

**Description:**
React Router's server-side RSC/framework mode allows a CSRF bypass that can execute route
actions. The advisory range is `>=7.12.0 <8.3.0` and there is no patched 7.x release.

**Risk Assessment:**
**LOW** - The dashboard is a client-only Vite SPA with no React Router server runtime, so the
vulnerable server-side code path is unreachable in the shipped artifact. This assessment was
recorded in the
[2026-07-27 production-readiness audit](../engineering/production-readiness-audit-2026-07-27.md)
(section 4).

**Mitigations:**
1. No React Router server runtime (RSC/framework mode) is bundled, configured, or reachable.
2. `react-router-dom` is held at 7.18.1, the last 7.x patch line, so no fixes are dropped.
3. Do **not** apply `npm audit fix --force`; the suggested downgrade to 7.11.0 would shed seven
   minors of fixes without addressing the advisory.

**Remediation Plan:**
- Migrate the dashboard to `react-router` v8 (73 importing files) as its own change, or
- Adopt a patched 7.x release if one appears, then remove this acceptance.

**npm Audit Disposition:** Accepted in
`build/config/security/npm-audit-accepted-advisories.json` and enforced fail-closed by
`build/scripts/ci/validate-npm-audit.py` in the `Production Certification`
dependency-evidence job. Any other high/critical npm advisory still fails the gate, and this
acceptance expires at its review date.
**Review Date:** 2026-07-28
**Next Review:** 2026-10-28 (quarterly)
**Approved By:** core-team (per the 2026-07-27 production-readiness audit acceptance)

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
- **On every PR:** Quick dependency scan for Critical/High vulnerabilities
- **Weekly (Monday 5:00 UTC):** Full security suite including CodeQL and SAST
- **On-demand:** Via workflow_dispatch with optional full scan

See `.github/workflows/security.yml` for configuration.
