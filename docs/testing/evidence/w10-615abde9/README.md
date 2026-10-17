# W10 acceptance evidence package — candidate 615abde9

**Status:** supporting evidence complete; live acceptance pending
**Owner:** Accounting and Ledger / Workstation Shell and UX / designated operator
**Reviewed:** 2026-10-02

Application candidate: `615abde90001ab33bd6e58e545edc7fce635e254`, tree
`9372fc96135e0203c0eb25fc105ed67b1d90f231`,
[PR #3048](https://github.com/rodoHasArrived/Meridian-main/pull/3048).
The [operator packet](../../w10-mark-seam-operator-acceptance.md) owns the walkthrough.
Implementation remains **LOT → MARK → SEAM → RECON**; operator sessions run **SEAM before MARK**.

The user reports no existing non-production population or Windows session. There are
**zero actual operator acceptance decisions** and no live retained accounting session.
All 12 criterion decisions and all 44 browser/desktop case rows explicitly remain pending
in [case-records.json](case-records.json). These are recorded gaps, not passed criteria.

| Evidence | Meaning and reproduction |
| --- | --- |
| [Population definition](population.json) | Versioned provisioning specification: two accounts/schedules, eight positions/seven securities, no overlap. Predicted initial 5 blocked/2 affected; provisioned identities and actual counts null. |
| [Criterion review](criteria-review.md) | Exact criterion-to-case/source/test map, full-scope provisioning owners, carrying-value/snapshot prerequisites and exclusions. Links pin the application SHA. |
| [Browser report](browser/browser-evidence.md) | 37 rendered observations, 43 annotated screenshots and 173 target request/response bodies. Thirty-six bounded checks passed; B1 repair failed. One fixture-derived position; these captures do not represent the planned eight-position population. |
| [Desktop report](desktop/desktop-evidence.md) | Source/test inventory and eight passing static structure checks. No rendered WPF screenshots or Windows test execution. Exact Windows SEAM-before-MARK commands included. |
| [Hosted snapshot](hosted-checks.json) | Same-candidate run/job/step conclusions. Required quality/integration gates failed; Windows run cancelled. Prior successful commits cannot fill these gates. |
| [Shared service support](server/server-evidence.md) | Actual focused run, test counters/names, candidate TRX and refusal/repair coverage. Bounded tests do not establish retained population or operator authorization. |
| [Validation record](validation.json) | Full local CI attempt timed out at its 15-minute bound after passing several .NET groups; retained log and completed-group results. Documentation-lane result is recorded separately. No full CI pass is claimed. |
| [Integrity manifest](manifest.json) | SHA-256 of every retained artifact and candidate source inputs. The manifest excludes itself; Git binds its identity. |

Open findings: B1 browser detail-version repair failure, D1 flattened desktop shared
blocker fields, D2 missing desktop schedule preview, D3 unimplemented governed mark
override expiry, C1 failed/cancelled candidate checks. See the packet and per-client
reports for reproduction, evidence and limits. No implementation was changed to make
these preparation results appear accepted.

## Reproduce the supporting evidence

Run packet integrity from the repository root:

```text
python3 docs/testing/evidence/w10-615abde9/verify_packet.py
```

It checks checksums, candidate source inputs, all criterion/case records, population
denominators and explicit pending decisions. Success establishes package integrity,
not operator acceptance.

For application checks, preserve the packet directory from its documentation commit
and use a clean isolated checkout of the application candidate. The retained scripts
accept `--root` so later documentation commits do not change the application under test:

```text
git worktree add ../w10-3048-candidate 615abde90001ab33bd6e58e545edc7fce635e254
python3 docs/testing/evidence/w10-615abde9/desktop/inspect_desktop_support.py --root ../w10-3048-candidate
```

Use the maintained dependency restore in that candidate checkout, then follow the exact
browser launch/capture command in the browser report and focused .NET command in the
server report. Capture output in a separate directory; never overwrite this package to
hide a different result. The browser harness supplies route responses and prevents a
simulated click from authorizing financial mutation. Its visible annotation identifies
simulation, candidate and pending operator decision.

## Complete live acceptance

Provision the planned population through its owners, retain actual IDs, hashes, versions,
scope, policy and quantities/carrying values, and verify both installed clients/host at
the candidate. Use authenticated durable PostgreSQL/storage profiles, actual operator
roles and secure credential references. The seeded demo and fixture route responses do
not provide this authority.

Run every SEAM case in both clients before MARK. Preserve before/after screenshots,
full responses, controlled contributor/mark changes, actual command refusals, repair
inputs, reload evidence and maker/checker decisions. M10 remains blocked until a
governed override lifecycle exists on a newly pinned candidate; inert expired metadata
tags are not an expiry mechanism. A source change requires a new candidate package.

Enter an accept/reject/defer verdict only after the designated operator supplies it,
with identity, UTC time, rationale and criterion evidence. Leave every undecided gate
pending and both roadmap rows `in_progress`.
