# Current export audit

**Reviewed:** 2026-11-11 · **Source:** PR #3501, initial head `7e580e1729`

The branch advanced to `a81d7afb45` during this task with an existing dashboard
palette migration. That concurrent `src/` work is preserved. This export
follow-up changes the design-system package; it does not replace or rewrite the
dashboard migration or WPF.

This review concerns the existing package in PR #3501. It does not describe the
older package on the repository's main branch. `release.json` declares 4.3.0
stable, while this working tree also carries later additions labeled unreleased
in `CHANGELOG.md`. The declared release channel, historical QA reports, and
verification of this expanded export are separate facts.

## Evidence and census

The initial manifest contains **351 public exports across 210 source modules,
135 specimen cards, 30 template entries, 680 token records, and 11 themes**.
Its namespace is `MeridianDesignSystem_205d17`. These figures were read directly
from `_ds_manifest.json`; the generated README and metrics still described the
earlier 334-export release when this audit began.

`docs/QA_REPORT_4.3.0.md` records the upstream September release run: 49 unit
checks, a Chromium keyboard/ARIA/responsive matrix, and declared assistive-
technology and Firefox/WebKit deferrals. That report remains historical evidence
for its stated tree. It does not establish that the expanded PR tree passed.

## Findings

| Priority | Initial evidence | Consequence | Treatment in the expanded export |
| --- | --- | --- | --- |
| P1 | `scripts/validate_release.py` failed six of seven gates on the PR tree | The upstream stable claim could be mistaken for current verification | Repair and rerun current gates; retain the original baseline and upstream reports |
| P1 | `api/families.json` covered 334 exports while the manifest carried 351; 17 names were unclassified | Family consumers could miss public symbols and maturity information | Classify the new exports and regenerate the family contract before the identity census |
| P2 | Governance, inventory, and release-identity scripts opened `readme.md`, but the file is `README.md` | Case-sensitive systems raised `FileNotFoundError` | Use the actual filename; test generation and subsequent `--check` on Linux |
| P2 | Generated README language declared stable while saying deferred QA made it “not a release candidate” | Consumers received contradictory maturity guidance | Render the declared channel consistently; scope historical QA and current verification separately |
| P2 | `README.md` and `docs/METRICS.md` retained the old census; catalog and scaffold checks reported drift | Counts, component discovery, and template behavior could disagree | Regenerate owned outputs from the current manifest and canonical scaffold |
| P2 | `docs/PERFORMANCE.md` described Google Fonts egress, but `tokens/fonts.css` loads ten local IBM Plex font files with OFL licenses | Consumers could configure unnecessary network access or omit required assets | Document the local font closure and measure current assets rather than reusing historical payload figures |
| P2 | The error row in `guidelines/STATES_AND_ANTIPATTERNS.md` advised hiding stale data; its connection contract and `guidelines/TEMPLATE_CONTRACT.md` require retained last-good evidence | Failure recovery could erase useful context or produce inconsistent stale-data behavior | Make first-load failure and failed refresh explicit: retain labeled last-good evidence and block unsafe writes |
| P2 | The package carries generated bundles but lacked a reproducible local distribution handoff tied to the current sources | A copied archive could conceal stale bundles or omit dependencies | Add source/build integrity metadata, a repeatable build, archive inventory, and checksum verification |

The initial release-gate results were:

| Gate | Initial result |
| --- | --- |
| Governance | Failed: case-sensitive README lookup |
| Inventory | Failed: case-sensitive README lookup |
| Families | Failed: 17 unclassified exports and stale output |
| Identity | Failed: family/manifest mismatch |
| Catalog | Failed: generated index drift |
| Scaffold | Failed: canonical loader/runtime drift in template copies |
| Extension compile plan | Passed; this validates the plan, not a bundle rebuild |

No baseline or test expectation should be widened to make these findings disappear.
The final command record belongs with the delivered archive, after the export's
source and generated output are complete.

## Current verification record

The following checks were exercised during this export task on the expanded tree:

| Check | Observed result and scope |
| --- | --- |
| Inventory, families, identity, catalog, scaffold, extension plan | Passed their current static checks; generated API documentation and landing identity now participate in drift checks |
| Whole release validator | Six gates passed; governance remained failed with 67 legacy/template findings and 25 warnings. No baseline reset or whole-release PASS is claimed |
| Build freshness and release integrity regressions | Passed; 12 release tests cover the current distribution checks |
| Agency regression harness | Passed using `MERIDIAN_CHROMIUM_PATH=/usr/bin/chromium npm run test:agency` |
| Existing runtime suites | Unit 49/49, action contract 29/29, semantic safety 46/46, books loop 61/61: 185 assertions passed in the final current-tree run, with no runtime errors or failed requests |
| Static browser conformance | Passed: zero blocking findings, 52 advisory review findings, and no failed parity checks; specimen-card iframe matrix was not run |
| Dashboard design-system contract | `vitest run src/design-system-contract.test.ts`: 28/28 passed after preserving concurrent commit `a81d7afb45` |
| Skill packages | `python build/scripts/docs/validate-skill-packages.py`: validated 13 packages |
| Broad retained Python suite | 30 tests ran with one failure and 14 errors from obsolete checker API expectations and legacy contrast checks; the whole suite is not green |
| Ordinary HTML state reference | 11 conditions in two themes, four desktop widths from 1280 to 1920, and 200% zoom passed the exercised checks; no remote requests or runtime errors |
| Automated accessibility on the reference | Zero reported violations in the tested condition/theme combinations; color-contrast checks marked incomplete still require manual review |
| Landing page | Chromium at 1366 × 768: canonical identity, 351/30/680/135 census, README/preview/audit/export links, no script errors, no horizontal overflow |

The final current-tree run is recorded above separately from upstream history.
The full specimen-card iframe matrix, NVDA/VoiceOver and Firefox/WebKit journeys
remain unrun. Full monorepo `bash scripts/ci.sh` was not run for this standalone
package follow-up; hosted integration checks remain authoritative for the PR.

## Operational coverage

The current package already has substantial operating contracts; a duplicate
state architecture is unnecessary:

- `guidelines/STATES_AND_ANTIPATTERNS.md`: five surface states, eleven value
  conditions, restricted-value redaction, and connection continuity.
- `guidelines/TEMPLATE_CONTRACT.md`: first-load versus retained-evidence
  precedence, invalid selection, restored evidence, and stale review invalidation.
- `guidelines/ACTION_SEMANTICS.md`: in-flight, landed, unlanded, and rejected
  instructions, including same-key recovery when the outcome is unknown.
- `guidelines/VALUE_ENVELOPE.md` and `guidelines/TIME_SEMANTICS.md`: value,
  condition, authority, and the clocks needed to interpret a financial figure.
- The decision, optimistic-editing, and assurance components: staged changes,
  conflicts, reasoned reverts, and signatures bound to the reviewed state.

Consumers must combine these contracts per workflow: partial totals identify
their missing scope; stale values retain source/as-of/age; read-only and locked
states explain which actions remain permitted; denied access does not reveal
restricted values; conflicts preserve the local draft and require an explicit
compare/reload/reapply choice. A readiness color is never authorization, and a
balanced journal is not permission to post.

## Boundaries and remaining validation

**Host dependency.** `.dc.html` entries use Design Component elements and JSX.
They still need that host/runtime. A copied template is not proven runnable by
opening it as plain HTML. The ordinary HTML smoke page and pinned local React
runtime test bundle consumption separately from full host-dependent templates.

**Font dependency.** Keep `styles.css`, its token import closure, font files, and
both OFL notices together. Installed local faces can alter font loading; browser
screenshots are needed to assess geometry on the target Windows environment.

**Token migration.** The package uses IBM Plex typography, terracotta brand/
scenario signal, blue focus/selection, and its own material hierarchy. Token
name compatibility does not imply equal values in Meridian's application. The
parity manifest explicitly declares `valuesShared: false`; integrating this
export is a separate app migration. This work does not modify WPF.

**Maturity.** Use the regenerated `api/families.json` for each export's stability;
the package's stable channel does not make every export stable. Review the 4.0
removed table APIs and template remaps in `docs/UPGRADING.md` before integrating
older consumers. Preserve the current namespace and exact public manifest.

**Residual callback fault boundary.** `OptimisticProvider` calls `onSettle` inside
the commit/revert `try` block. A consumer callback that throws after a successful
transport result can be classified as a rejected commit or failed revert. Keep
settlement callbacks nonthrowing and treat this as a follow-up fault-isolation
fix; these checks do not establish safe behavior for a throwing callback.

**Residual chart interaction.** `ScenarioSelector` updates controlled selection
on arrow keys but does not move focus or implement Space/Enter on its div-based
radios. Its prompt records that limitation. `ChartAnnotations` supplies no
independent keyboard controls, and peer marks in `PeerComparisonStrip` rely on
native titles. Include consequential annotation/peer values in an accessible
adjacent table or inspector, and complete the selector interaction before using
it as a keyboard-dependent production control. These components were not
established as fully accessible by the ordinary HTML reference's automated run.

**Verification scope.** A passing Python release gate covers its static checks.
It does not prove every template, screen-reader journey, Windows Edge behavior,
server permission, financial calculation, or live execution integration. Keep
unrun matrix portions explicit and retain the upstream deferred items. Synthetic
preview and transport outcomes must remain labeled as demonstrations.

Build/export commands, archive contents, dependency scope, and integrity checks
are documented in [Export handoff](EXPORT_4_3_0.md).
