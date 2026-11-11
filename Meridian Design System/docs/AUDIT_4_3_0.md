# Current export audit

**Reviewed:** 2026-11-11 · **Source:** PR #3501, initial head `7e580e1729`

This document preserves checkpoint-specific evidence. The
[continuation status](#continuation-status) distinguishes later repairs and
merged-tree verification from the earlier audit results.

The branch advanced to `a81d7afb45` with an existing dashboard palette migration;
that work is preserved. The initial review covered the package export. The user
then expanded scope to integrate the package into the production browser
workstation, starting from `4782c83013`. The integration follow-up below records
that additional work; WPF remains outside its scope.

This review concerns the existing package in PR #3501. It does not describe the
older package on the repository's main branch. `release.json` declares 4.3.0
stable, while this working tree also carries later additions labeled unreleased
in `CHANGELOG.md`. The declared release channel, historical QA reports, and
verification of this expanded export are separate facts.

The initial export follow-up preserved the existing dashboard (`a81d7afb45`) and
WPF (`2e78ac6654`) palette migrations. Production integration then expanded the
browser changes. Incoming commits `0cc7555bd5` and `e4e935c098`, including broader
WPF IBM Plex typography and dashboard fundamentals, were merged while retaining
the expanded browser token bridge, adapters, and validation. Windows/WPF
rendering was not independently certified by this browser/package audit.

## Evidence and census

The initial manifest contains **351 public exports across 210 source modules,
135 specimen cards, 30 template entries, 680 token records, and 11 themes**.
Its namespace is `MeridianDesignSystem_205d17`. These figures were read directly
from `_ds_manifest.json`; the generated README and metrics still described the
earlier 334-export release when this audit began.

The latest metadata refresh after `1f01910387a2` records **743 tokens**, adding
39 brand-scoped/default records to the immediately preceding 704-record manifest.
The census remains 351 public exports, 210 source modules, 135 cards, 30 templates
and 11 themes. The initial 680-token count above remains the starting census.

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

## Initial expanded-tree verification record (historical)

The following checks were exercised during the initial export follow-up. They
remain evidence for that checkpoint, not results reassigned to later merges:

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
| Archive integrity | Payload/inventory checksums verified and repeat export matched byte-for-byte; exact file counts are recorded in the delivered inventory. The earlier extracted reference passed 22 accessibility states and desktop interaction checks over loopback HTTP |
| Ordinary HTML state reference | 11 conditions in two themes, four desktop widths from 1280 to 1920, and 200% zoom passed the exercised checks; no remote requests or runtime errors |
| Automated accessibility on the reference | Zero reported violations in the tested condition/theme combinations; color-contrast checks marked incomplete still require manual review |
| Landing page | Chromium at 1366 × 768: canonical identity, 351/30/680/135 census, README/preview/audit/export links, no script errors, no horizontal overflow |

That initial expanded-tree run is recorded separately from upstream history.
The full specimen-card iframe matrix, NVDA/VoiceOver and Firefox/WebKit journeys
were not run at that checkpoint. Direct `file://` navigation was blocked by the
session's Chromium administrator policy; this evidence used loopback HTTP with
no external requests. Full monorepo `bash scripts/ci.sh` was not run for the
standalone package follow-up; hosted integration checks remain authoritative.

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
scenario signal, blue focus/selection, and its own material hierarchy. The
browser workstation and WPF theme tokens were migrated to this palette in the
same pull request, so the parity manifest now declares `valuesShared: true` and
`scripts/check_token_parity.py` compares every name both sides declare in all
four theme scopes, and `upstreamValueDivergences` is empty. See
[Application integration](#application-integration) for what moved in each
direction.

**Maturity.** Use the regenerated `api/families.json` for each export's stability;
the package's stable channel does not make every export stable. Review the 4.0
removed table APIs and template remaps in `docs/UPGRADING.md` before integrating
older consumers. Preserve the current namespace and exact public manifest.

**Observer fault boundary — resolved.** The initial review found that a throwing
settlement observer could reclassify a successful transport result. Settlement
and ledger-subscriber callbacks now have a separate fault boundary, including
rejected promises they return. Confirmed edits stay confirmed; successful
newest-first compensations stay reverted; later observers still run. Completed
operations do not reject merely because an observer failed, avoiding a false
signal to retry a confirmed write. Faults go to `globalThis.reportError`, with a
console fallback. The host still owns recovery of its failed observer work; no
live write transport is certified here.

**Chart interaction — repaired within the tested scope.** `ScenarioSelector` uses native button radios with
Space/Enter activation, arrow/Home/End focus and selection, and a reachable
fallback for a missing saved selection without silently changing the model.
Read-only options remain disabled. Noninteractive `ChartAnnotations` expose
band, event and callout descriptions through a named SVG group.
`PeerComparisonStrip` provides a text table of subject, median, peer values and
rank when its accessible label is supplied; consumers must provide that label
for the alternative to render. These descriptions do not add editing commands
to passive marks. DOM/keyboard regressions cover the implemented contracts;
NVDA/VoiceOver and Firefox/WebKit journeys remain separate, unrun validation.

**Verification scope.** A passing Python release gate covers its static checks.
It does not prove every template, screen-reader journey, Windows Edge behavior,
server permission, financial calculation, or live execution integration. Keep
unrun matrix portions explicit and retain the upstream deferred items. Synthetic
preview and transport outcomes must remain labeled as demonstrations.

Build/export commands, archive contents, dependency scope, and integrity checks
are documented in [Export handoff](EXPORT_4_3_0.md).

## Application integration

The integration pass compared the package with the two consumers in the monorepo
and changed whichever side was worse. It ran alongside the workstation
foundation integration in `0cc7555bd5` and was merged onto it. Static gates and
unit tests back these statements; no browser, screen-reader or Windows run was
made for them.

**Package changed to match the application**

- State and severity chip text reads a text-safe variant of its hue again. The
  4.3.0 retone had wired every `*-fg` to the raw hue, which measured 3.3-4.5:1
  on its own wash in light mode; the workstation had kept the `-dim` wiring.
  `--brand-field-dim` and `--pending-dim` are declared under the names the
  workstation uses (one component read `--pending-dim` before anything
  defined it), and `--brand-signal-text` is new for signal-coloured text.
- The ten `--theme-*` contract names, `--theme-border-focus` and the four
  chrome inputs follow dark mode. They resolved to their light literals on a
  dark screen; both dark blocks now re-point them.
- `--series-estimated` and `--viz-band-outline` are restated for forced light.
- `--accent-a10` is declared again, and 76 references to 19 names the package
  never declared now name the canonical token, so they stop painting a light
  literal in dark mode. `--theme-font-mono` names the same fallbacks as
  `--font-data`.
- More than a thousand `var(--token, literal)` fallbacks carried a previous
  palette and now carry the value the token resolves to.
- Component defects the workstation ports had already fixed:
  `ReconciliationPanel` match checkboxes (threw on first tick), `toNumber`
  dropping the sign of a formatted negative, `LedgerTable` inventing running
  balances and losing header focus on sort, `Pagination` jumping back to a
  stale page, `Sparkline` / `EquityCurve` / `CandleChart` on one or zero
  points, `JournalEntryForm` labelling and a colliding datalist id, and
  scroll regions that keyboard users could not reach. `Checkbox` forwards
  `aria-label`.
- The brand art's live `<text>` runs name IBM Plex Sans, the face the package
  ships, not Inter.

**Application changed to match the package**

The concurrent workstation integration made most of the application-side
changes (fonts, focus and selection roles, paper mode, radii, primary button
states). This pass added:

- The last hard-coded copper `rgba()` literals in the workstation stylesheets
  now derive from `--focus`, the row tokens or `--ws-accent`.
- `--chart-band` and `--chart-dd` take the package values, and `var()`
  fallback literals across the dashboard carry the value their token
  resolves to.
- Order quantity and price fields reject exponent and hex notation, the
  operations status vocabulary has one normaliser, `AccountTree` is keyboard
  navigable, tables have empty states, and scroll regions are focusable.
- WPF `OnAccentTextColor` is the dark ink the package puts on a terracotta
  fill.

**Gate changes.** `check_token_parity.py` gained two passes: the theme inputs
must flip in dark, and every token name declared by both the package and the
workstation (224 at this commit) must resolve to the same value in light, auto
dark, manual dark and forced light. No difference is recorded: the Review
severity chip, the last one, is the terracotta signal on both sides by the
owner's decision. In `check_contrast.py`, rows that held
`--accent` and `--accent-dim` to the text threshold now hold
`--brand-signal-text` to it and the signal to the 3:1 non-text threshold,
matching the 4.3.0 role of each token. The paper and pending chip rows measure
against their own wash. The workstation button check measures the graphite
states the button paints. The bundle-parity check skips sources the manifest no
longer exports, and test files are not scanned as call sites. No threshold was
lowered.

**Historical diagnostics at this integration checkpoint.** The contrast check
reported 12 findings about six white-label presets, and governance still
reported 67 findings and 25 warnings. These are retained checkpoint results;
the continuation repairs and checkpoint-specific reruns below supersede their
use as current status. No consumer in this repository set `data-brand` at that review.

**Incoming white-label preset fix.** The six `data-brand` presets state a dark accent trio
and a ghost fill again, as the previous package version did, and three rules
in `tokens/theme.css` carry a brand's accent family into auto dark, manual dark
and forced light. Before this a brand lost its accent under `data-theme="dark"`
and `data-theme="light"`, and under an OS-dark preference kept its light accent
beneath a white label. The auto-dark `--theme-*` re-points moved under
`:root:not([data-theme="light"])`: left on a bare `:root` they stayed active
under forced light and made `--accent: var(--theme-accent)` a cycle for a brand
that states no accent. Computed in Chromium for 24 combinations (OS light/dark,
no theme / dark / light, no brand / indigo / amber / an unknown brand): every
accent, label, ghost and focus value is the intended one and every label
clears 4.5:1. `check_contrast.py` no longer requires a brand to restate the
focus token, because the focus ring is the interaction blue for every brand;
it passes with zero findings. No consumer in this repository sets
`data-brand`.

**Historical governance result for that white-label checkpoint.** The gate
remained at 67 findings and 25 warnings; the continuation repairs below supersede
that earlier result.

## Production browser integration follow-up

The browser workstation now consumes the package through its native TypeScript
design-system adapters, synchronized brand/font assets, and the canonical role
bridge in `src/Meridian.Ui/dashboard/src/styles/index.css`. This keeps the
production application's routing, state, financial workflows, and shared API
contracts in their existing owners.

The integration includes local IBM Plex Sans/Mono fonts and OFL notices; brand
assets and typed imports; package-value light/dark token scopes; and distinct
graphite action, terracotta signal, blue focus/selection/link, and financial
severity roles. The asset synchronization checks run before dev/build and in
Vite, so the package copy cannot silently drift from its source assets.

Auxiliary stylesheet review found direct Segoe/Cascadia declarations, copper
focus/selection, effective hardcoded table header/zebra/row-cap surfaces, and a
dark trust-menu item inside a paper dropdown. These now consume semantic font,
interaction, workspace, and chrome roles. Accounting/reconciliation selection,
keyboard focus, dense-table details, navigation, and workflow continuity use the
same mappings. Financial severity and environment meanings remain independently
expressed through their own roles and text.

All 15 auxiliary stylesheets parse with PostCSS. Focused asset synchronization
and application/component verification must be reported at their actual scope;
the earlier export smoke page and upstream QA remain separate evidence. The
source module README documents integration ownership and synchronization
commands. No package-level conformance claim substitutes for exercising the
rendered production workstation.

The production integration baseline is `4782c83013`. The merged WPF source now
declares IBM Plex Sans for interface text and IBM Plex Mono for data in
`src/Meridian.Wpf/Styles/ThemeTypography.xaml`. That concurrent work is preserved;
browser validation does not establish Windows/WPF rendering or identical
cross-platform metrics. SVG labels on the strategy, live-quotes, and shared
historical charts read the browser's semantic `--font-data` stack alongside the
auxiliary CSS consumers.

### Production integration verification

The merged production source checkpoint is `5e9f5b6801`. Application checks below
are separate from the standalone package and upstream release checks above.

| Check | Observed scope |
| --- | --- |
| Full dashboard suite before the incoming merge | 4,843 tests passed, zero failed, across 424 files in 53 batches |
| Focused regression matrix after the merge | 327 tests passed across 67 files; separate passport checks passed 23 tests and masthead/app checks passed five tests |
| Production build after the merge | `npm run build` passed TypeScript checking and Vite production output; the existing chunk-size warning remained nonfatal |
| Strict TypeScript check | Passed on the merged source; normal TypeScript checking also passed in the production build |
| Dashboard lint | Zero errors; 97 warnings retained |
| Canonical asset verification | 77 synchronized files passed the byte check |
| Generated routes/catalog and evidence-reader regression | Two focused tests passed |
| Rendered browser workstation | 22 Chromium checks passed: Trading/Accounting across light/dark at 1920 × 1080, 1440 × 900, and 1366 × 768; seven workspace routes; Portfolio MSFT proof Enter/Escape and focus return; four OS/manual appearance cases; Compact/Default/Spacious density settings with 28/36/44px rows; Ctrl+K palette keyboard operation |
| Browser accessibility | Three fresh scenes reported zero automated violations: Portfolio MSFT proof in light appearance, Settings explicit Light under a dark OS preference, and Settings explicit Dark |
| Native surface paint | Trial Balance/Ledger source-component borders were solid 1px with canonical light/dark `#D2CDC3` / `#2C3640`; selected cells used blue `#E7EDF2` / `#2C4054`; no uncaught exceptions, failed assets, or unexpected console errors |

The full-suite count identifies the pre-merge source checkpoint; merged build,
targeted regression, and rendered checks have their own scope above. The final
`smoke:design-system` report passed and records the actual workstation source
with synthetic data/component fixtures. Seventeen unhandled fixture API paths
remain explicit in the harness report; this is not posted-backend validation.
These Chromium checks do not claim
Windows/Edge rendering, browser zoom coverage, screen-reader conformance, or
whole-product production certification. Those browser checks did not, by
themselves, repair the package governance and Python-suite failures present at
that checkpoint; the later package work is recorded below.

## PR review repairs

The follow-up on `0cc7555bd5` preserves the concurrent workstation foundation
integration. Interaction Depth now keeps its grid component identity across
query, selection and model updates, and row context actions use the actual
right-clicked row key. Header context menus cannot reuse an earlier selection.
`tests/interaction-depth-regression.mjs` exercises typing/focus, keyboard
selection, virtualized scroll continuity and the resulting row mutation receipt
in Chromium. The incoming `324b439b9f` tree passed this Chromium regression and
the package's `build:check`; the refreshed archive includes the rebuilt source
bundle and these repairs.

The eight earlier action-bridge, action-envelope, settings-key, continuity,
formatting and date findings were verified against their existing fixes;
their source behavior was preserved. The CI secret finding was an independently
verified SHA-256 of `components/data/KeyValueGrid.jsx` in the two build-provenance
files. Its scanner exception matches only that complete digest in those exact
paths. Negative controls confirm that other content in those files and the same
digest elsewhere remain detectable. That review retained the then-open package
gaps; the continuation status below records their later treatment.

The merge retained incoming generated diagrams and documentation, font notices,
and mask/currentColor workspace icons alongside the expanded browser integration.
Forty-two targeted navigation, font-output, and design-system contract tests
passed on the merged source, and the combined TypeScript/Vite production build
passed. This incoming-review evidence is separate from the
`5e9f5b6801` production matrix above; the original full-suite and package
governance scopes are retained rather than silently reassigned to another tree.

## Component adoption follow-up

This pass reviews package/runtime compatibility and the existing dashboard
adapters. The application continues to use its own React and TypeScript
components; the global package bundle and preview React runtime are not loaded
into production. Shared names alone are not a compatibility guarantee.

| Surface | Review result and integration |
| --- | --- |
| Cash Ladder | Reuses shared `Select`/`Input`, retaining blank/default, Enter/apply, scenario and account-reset behavior. The interactive SVG is a named group with pressed bucket controls. |
| Provider integration workbench | Reuses shared `Select`/`TextArea` for template/capability and JSON/CSV drafts; labels, disabled states, service validation and activation boundaries are retained. |
| Shared Tabs | Instance-specific generated ids, labelled/focusable panels and preserved explicit panel links. Market Data tab URL changes retain focus; scope/hash navigation still hands off focus. |
| Shared MetricCard | Uses the current workstation surface/border roles, correcting pale cards with dark-theme text while retaining nested light scopes. |
| DenseDataTable | Nonselectable/empty scroll areas are named keyboard stops; selectable-row navigation is preserved. |
| Package ScenarioSelector / OptimisticProvider | Source-level Chromium regressions cover keyboard behavior and synchronous observer fault isolation; the generated export is rebuilt. |

The package's standalone release/browser and agency checks remain scoped to
synthetic examples, not every template or live financial workflow. At this
component-adoption checkpoint, governance reported **67 failures and 25
warnings**. Comparison with an extracted copy of `ec33af6168` confirmed the same
finding set then, while the six other release gates passed. This historical
comparison is not the result of the later validation repairs. No governance
rule or baseline was weakened to obtain it.

`npm run test:components` runs the two new source-level browser regressions;
set `MERIDIAN_CHROMIUM_PATH` when using a system Chromium. Dashboard integration
coverage includes Cash Ladder and provider screen behavior, tab identity/focus,
route focus, table keyboard access, design contracts and the production build.
The Cash Ladder API is absent from the fixture server, so browser control QA
uses an explicit synthetic response; that is not backend-integration evidence.

Incoming follow-up evidence recorded at `d37e226f13`:

- Dashboard: 4,851 tests across 425 files in the two full shards; final focused
  reruns include the added font-publication case and latest keyboard regressions.
  Taking the latest passing result per file yields **4,853 unique tests in 426
  files**, with no failed or skipped assertions. Production build/type check,
  lint (zero errors, 97 existing hook warnings), typography floor and
  design-system ratchets pass. Full monorepo `bash scripts/ci.sh` was not run.
- Package: build/source freshness, 12 release tests, release integrity, new
  component regressions, existing agency regressions and standalone browser
  checks pass. The standalone run covers 22 condition/theme accessibility states,
  four desktop widths and its 200% zoom check, with no external requests.
- Production dashboard assets: system Chromium (Browser plugin unavailable),
  light/dark at 1920×1080, 1440×900 and 1366×768. Scenario/threshold and failure
  recovery, provider draft editing and tab/panel focus are exercised with
  synthetic data. Cash Ladder and provider Settings each report zero automated
  WCAG A/AA violations in the tested states, zero page exceptions and zero failed
  requests. This does not certify other routes, native Windows/Edge, assistive
  technology or backend writes. Screenshots remain local review evidence.
- Concurrent PR updates through `324b439b9f` are retained. Generated browser
  assets are rebuilt, and source hashes and the repository tree are refreshed.
  Hosted checks remain authoritative; the PR stays unmerged and shared history is not rewritten.

### Combined validation after the incoming component fixes

The merged source retaining `d37e226f13` passed the TypeScript/Vite production
build, the strict TypeScript check, and a focused matrix of 110 tests across six
files. Two additional Settings
workflow tests passed under a name filter; 47 unrelated cases were intentionally
excluded from that targeted invocation, not asserted as passing.

The combined workstation smoke passed all 22 cases and three fresh accessibility
scans with zero reported violations. Native Accounting borders/selection, nested
light scopes, and all seven canonical SVG masks/currentColor passed; there were
no unexpected errors or missing assets. The same 17 fixture API gaps remain
explicit. This is synthetic Chromium evidence, without Windows/Edge,
assistive-technology, zoom, or live-backend certification.

Independent checks of the incoming package passed the ScenarioSelector keyboard
and optimistic-observer Chromium regressions, source/build freshness, and export
integrity. Those incoming checks closed the selector and synchronous observer
gaps at that checkpoint. The continuation expands observer isolation and chart
data alternatives and records the remaining verification separately.

## Incoming developer integration and accessibility evidence

This incoming checkpoint records gallery/workbench and integration verification
before the continuation and merge of `0c10785dfc03`. Its counts and outstanding
findings below belong to that checkpoint; the continuation status records later
repairs and reruns.

The follow-up retains concurrent integration and token-reconciliation commits
through `27ab256c10`, without rewriting the shared PR history. It preserves the
4.3.0 identity, component APIs, native adapters, seven root workspaces, operator
preferences, financial statuses, and production workflows.

- The package gallery reads all 135 cards and their groups/metadata from the
  manifest. Search and group filters combine, counts and direct links remain
  visible, catalogue/preview failures offer Retry, and browsing starts with no
  preview. At most one specimen document is loaded. A loaded document is explicitly
  distinguished from component conformance and existing specimen runtimes remain.
- `/workstation/__design-system` is lazy-loaded only in fixture-only Vite
  development, before application data providers mount. It uses actual native
  buttons, badges, validation, dense-table and inspector adapters with three fixed
  synthetic USD records. Appearance, density, explicit-light surface, data state,
  and button state are URL controls. Reset restores defaults. Runtime probes
  verify zero API requests, storage reads/writes, and companion broadcasts.
- Selected rows use interaction-blue rails and surface-specific secondary and
  semantic text roles. Existing darker semantic shades keep text readable on
  selected/tinted surfaces without changing status hues or fills. Destructive
  button text uses the existing red text shade. Actual-cascade probes measured
  minimum selected semantic contrast 4.63:1 (including badge washes) and
  destructive label contrast 6.02:1. Ready text on a selected dark row is 4.80:1.
- `ratchets:update` validates the existing baseline, rejects new offenders and
  per-file growth even when another file improves, treats equality as a no-op,
  and writes valid reductions atomically. Rejected updates preserve baseline bytes.
- `check:design-system` at the root/dashboard verifies locked installed direct
  dependencies before consistency, focused contracts, gallery, contrast, package
  freshness and export-integrity checks. It never installs, regenerates checked-in
  assets, updates a baseline, or launches a browser. Its separate browser command
  reuses the maintained workstation Playwright smoke infrastructure.
- Primary-action contrast resolves actual package/workstation base, hover,
  pressed and foreground roles across automatic/manual dark and forced light.
  HSL channels, aliases and sRGB color mixes are supported; required unresolved
  values fail. All 24 required pairs pass, minimum 6.00:1.

The source command passes 23 tooling tests, 126 focused workstation assertions,
five gallery model tests, 17 contrast-resolution tests and 13 release-integrity
tests, alongside consistency/freshness gates. Tooling cases cover malformed and
stale dependencies, stage failure propagation, paths containing spaces and
shrink-only baseline failure/reduction behavior. The retained ScenarioSelector
and optimistic-observer source regressions cover keyboard navigation and
transport/observer fault separation; their public contracts remain unchanged.

The retained whole-package governance gate and broad Python suite remain
separate from this focused integration command. No baseline was increased or
rule suppressed to obtain a passing focused result. The full monorepo CI script
was not run locally. Real Windows builds/tests and required hosted checks remain
the merge authority; Linux stub builds are not WPF evidence.

At that incoming checkpoint, the full contrast checker reported 18
retained brand-theme findings (custom focus/dark identity/required branded pairs).
The whole-package governance result was 67 failures and 25 warnings. The
unfiltered Python suite ran 45 tests with one legacy contrast failure and 14
obsolete governance-API errors; the 17 focused contrast and 13 integrity tests
passed separately. These historical broader findings did not change the
focused command's scope; their later repairs are recorded below.


The incoming dashboard sweep covered **430 test files and 4,947 passing
assertions**, taking the latest passing result per file. Earlier attempts remain
in the local evidence: two Trading class assertions were aligned with the
contrast-corrected destructive variant, and one Reporting accessibility timeout
passed on retry. The missing-mode workbench gate and incoming Data/Settings
contrast changes also pass focused regressions. Lint reports zero errors and 97
existing hook warnings; typography debt remains 539 allowlisted occurrences,
with no increase.

The merged package passes ScenarioSelector/observer and financial-agency browser
regressions, 22 standalone accessibility states, four desktop viewports, 200%
zoom and the 135-card gallery journey. These package scenes report zero remote
requests and uncaught runtime errors. Workstation build provenance uses the
committed dashboard lockfile under Node 24; all ten production fonts are WOFF2
with checked TTF-source provenance and local OFL notices. The native workbench
module is absent from the production asset closure.

A separate redacted scan of all 29 PR-history commits reports two findings in
`c77ced17b0` build-provenance files. Both are the independently recomputed public
`KeyValueGrid.jsx` SHA-256, not a credential. Existing scanner configuration and
baselines were preserved; hosted Secret Scan passes on the reconciled baseline.


The final selected-badge correction strengthens text ten percent toward the
existing theme ink direction only inside selected rows. Tests and browser probes
composite each badge wash over the selected surface; the global semantic hue and
fill contracts remain. The masthead Paper label/dot use the canonical dark-surface
slate tone regardless of the workspace appearance. The maintained browser profile
checks their actual composed contrast instead of accepting a root token alone.


## Continuation status

The continuation repairs governance-checker API drift, retained template
preview/state behavior, metadata validation, semantic text roles and color
fallbacks, observer fault isolation, and chart keyboard/data alternatives.
These changes preserve the initial findings above as history instead of
rewriting that earlier run as green.

Verified package checkpoint after integrating the incoming commits through
`0c10785dfc03`, before the subsequent brand runtime-alias update through
`1f01910387a2`:

- All seven release gates and the complete **127-test Python suite** passed.
  The initial 30-test, incoming 45-test and intermediate 99-test results
  retain their checkpoint-specific scope.
- The full contrast check passed across 8,115 color fallbacks, 10,944 bare
  variables, 1,882 bundled rules and 29 assets. Token parity passed for 227 shared
  names with zero divergences. Forced-light inputs now resolve to their light
  identities under an OS-dark preference without alias cycles; cycle checking
  includes dependencies inside `var()` fallbacks.
- Metadata synchronization gives unconditional defaults precedence over
  conditional dark declarations, including when the old manifest points to the
  conditional source. Conditional-only tokens remain represented. All 13 focused
  metadata tests passed as part of the complete suite.
- The strict full specimen matrix passed **135/135 cards** with zero recorded
  conformance, runtime, console, asset or unexpected remote-request errors. The
  generated bundle remained byte-identical through the run. Earlier partial
  runs and font failures remain historical evidence, not waived assertions.
- The brand/appearance browser matrix passed 28 states and 1,932 required AA
  contrast pairs. The latest gallery verification passed five model tests and
  the 135-card browser journey with zero reported axe violations.
- The package build/source freshness check passed. Release integrity passed for
  1,346 curated files. The archive checksum must be associated with the delivered
  source and final documentation content when the artifact is regenerated.

At that checkpoint, managed dashboard `npm run check:design-system` passed end
to end:
126 focused tests across 13 files, plus assets, ratchets, typography, gallery,
parity, contrast, build freshness, release and export checks. A separate native
raw-color/contract matrix passed 45 tests across ten files. Production browser
verification passed nine scenes and 200%-equivalent reflow; font checks verified
ten WOFF2 assets and two license files. The latest managed browser coverage
passed 64 cases and 15 axe scans with zero reported violations.

The earlier standalone browser run passed 22 theme/condition accessibility
states with zero recorded axe, remote-request or runtime errors. The focused
dashboard matrix passed 196 tests and the workstation smoke passed 22 cases at
their respective checkpoints. The incoming 4,947-assertion full-dashboard run
above retains its own source and scope; these results are not combined into a
new unrun full-suite count.

Final verification after the brand runtime-alias update, 743-token metadata
refresh and integration through `db7902` passed the complete **131-test Python
suite** and full contrast check. All seven release gates passed after the
runtime-alias update. The parity suite passed 12 tests, and the complete parity
check passed with 502 declared names, 227 shared names and zero findings. The
fresh brand browser matrix passed 28 states and 1,932 AA pairs; all 60 actual
dark-role mappings matched their intended identities. The final native browser
coverage passed 64 cases and 15 axe scans with zero reported violations.

The last incoming Modal/comment-governance fixes retained the already-correct
button type and standalone identity; all 25 governance tests passed. The
component bundle and public runtime are byte-identical to those exercised by
the strict 135-card run, so that component proof remains applicable. Source-hash
and README checks reported zero errors. The initial and intermediate Python
counts above remain historical; 131 is the final complete-suite result.

The documentation generators were attempted and each generation step exited
successfully, but the aggregate `verify-docs` check did not converge against the
uncommitted worktree; it is not recorded as a passing documentation gate. The
`dotnet` CLI is absent from this execution environment, so full aggregate .NET
validation could not run locally.

The specimen collector uses an explicit local cache of the exact React 18.3.1,
React DOM 18.3.1 and Babel 7.29.0 peer bytes and verifies their SHA-384 pins.
**84 source peer-script references lack declared SRI** in the inspected cards.
They remain recorded as missing source integrity; cached-byte verification
does not add an integrity attribute or establish live CDN availability. Unknown
remote requests remain blocked and fail the run. See
[`tests/SPECIMEN_MATRIX.md`](../tests/SPECIMEN_MATRIX.md) for the runner's scope,
raw findings and preparation instructions.

NVDA/VoiceOver, Firefox/WebKit, native Windows/Edge rendering and full monorepo
`bash scripts/ci.sh` remain unrun for this continuation. Automated Chromium
checks and passing static gates do not establish those results, every template
host workflow, real provider integration or live financial execution. Hosted
checks remain the integration authority; the PR is not merged by this audit.
