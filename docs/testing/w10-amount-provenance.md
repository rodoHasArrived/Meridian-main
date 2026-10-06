# W10-PROV-001 posted and generated amount workflows

**Status:** posted-line and generated trial-balance slices implemented; validation recorded below
**Reviewed:** 2026-10-06

Browser Accounting Ledger Explorer and WPF Posted Ledger now select an individual posted debit or
credit amount. Both request the same `ledger-amount` subject packet and display the same retained
scope, source identifiers, content digests, status, and warnings.

The subject ID is `{journalEntryId:D}:{entryId:D}:debit|credit`. Query scope includes fund profile,
ledger book, and period; tenant and company come from the authenticated session. The server checks
retained fund ownership and the book/period/journal/entry relationship before returning an amount.
Vault source references retain a canonical subject that includes fund/book/period and that exact
journal/entry/side, plus independently retained tenant/company scope. Neither account names nor
symbols establish an association. Journal-wide, report-wide, or unrelated case data is not proof.

A source must still exist, match its retained content digest and retention timestamp, and have
accepted review. Missing, unavailable, stale, or unreviewed support produces review-required;
ambiguous identity, changed digest, or foreign support blocks proof. The clients clear old proof
on selection/scope changes and ignore late responses. The ledger record by itself is not sufficient
for Ready status.

The compatibility report-pack reader requires typed amount bindings and exact scoped lineage.
Even correctly scoped typed report pointers remain `ReviewRequired` until their source content can
be verified; their presence alone never produces `Ready`. Label-only historical manifests fail
closed. The governed reporting path now emits generated trial-balance bindings in its retained source
population. Other report families, NAV, and portfolio-wide activation remain outside this bounded
slice. The canonical roadmap item remains in progress.

Verified posted-source links carry the selected amount subject, fund, book, period, and expected
manifest digest to the existing vault route. Opening a link re-evaluates the retained source bytes
and current review state and verifies the opened manifest's subject and digest. If support changes
after the drawer loads, the guarded request returns `409 ledger-amount-proof-stale` rather than
presenting the changed evidence as the previously verified proof.

The review corrections normalize legacy `_vault` route whitespace and casing before checking the
retained subject's permission. Trailing-dot subject aliases are rejected, and vault requests without
a resolvable scoped identity cannot fall back to reporting access. The browser discards invalid
amount selections, so returning to a tab, book, or period cannot reopen an earlier proof. WPF
closes proof before replacing journal rows, including pending reads, and derives available bases
from both journal and trial-balance responses. A GAAP-only open-period journal remains available
when the trial balance returns 404; either response order preserves an available basis selection.

## Generated report amounts and one retained population

Open a governed generated run and inspect its retained trial-balance amounts. Each amount uses the
stable subject `report:{runId}:{amountId}` and the run's retained tenant/company/fund/book/period
scope. The amount ID identifies the canonical trial-balance account row, and its binding retains
exact contributing journal and line IDs. Account labels do not establish proof.

Every ledger capture reads the complete scoped as-of population once. PostgreSQL captures journal,
period status/version, and tax-lot history within the same repeatable-read, read-only transaction.
Period rows, historical balances, tax-relief projections, financial statements, and amount bindings
all derive from this retained population. Its serialized inputs, content hash, sequence boundary,
and counts are retained in the source checkpoint. The checkpoint digest binds the entire population,
including historical entries that do not appear in period activity. Primary-document rendering
replays those inputs; current-source revalidation remains a separate release gate.

Generated amount proof requires every contributing line's exact reviewed source evidence to verify.
Sources from opening balances retain their original posting period and scoped subject while the
manifest link guards the selected report amount. Missing, altered, unreviewed, or foreign support
blocks the complete amount and removes inspectable source routes. Opening a verified source link
rechecks the report population, all contributors, and source bytes before returning the manifest.
Old runs without retained populations expose no generated amount proof and require recertification.

The source regression pauses capture, commits backdated period and historical postings, and proves
period rows, statements, provenance, artifact contents, and signature reproduce from the original
serialized population. A second capture sees the later postings. The PostgreSQL test also commits a
period reopen and wash-sale history while the capture is paused, proving all three reads share one
transaction snapshot. Retention tests reject missing, altered, foreign, and coherently rebound
populations; tax-relief tests exercise nonempty serialized tuple posting lines.

## 2026-10-06 generated-report validation

- Snapshot/storage filter: **193 passed, 0 failed, 0 skipped**, including real PostgreSQL
  repeatable-read concurrency, retained period authority, complete population replay, source
  checkpoint tampering, historical currency, and nonempty tax-relief serialization.
- Generated report proof: **23 passed, 0 failed, 0 skipped**, including exact historical/current
  support, guarded source opening, scope/permission denial, corrupt bindings, and rehashed
  population or certified-dataset mismatch. The combined regression run also passed its other
  **194** reporting and posted-proof cases.
- Browser report/proof filter: **98 passed** across seven suites; TypeScript, Vite production build,
  and targeted ESLint passed.
- Chromium report-detail interactions passed at **1920 × 1080** and **1366 × 768**, plus 125% scaling:
  exact retained selection, historical-source opening, blocked evidence, Tab focus trapping,
  Escape dismissal/focus restoration, and no overflow or console/framework errors.

Browser interactions use controlled HTTP fixtures. The PostgreSQL tests independently establish
live database snapshot consistency; no live browser-to-storage flow is claimed. Full repository CI
results are recorded after the remaining gate completes.

## Validation evidence

Initial targeted validation completed on 2026-10-01. All three targeted filters were rerun on
2026-10-02 after the authorization, browser selection, and WPF basis-arrival review corrections.
The totals below include the earlier WPF node and artifact regressions.

| Check | Result | Coverage and limits |
| --- | --- | --- |
| Backend provenance filter | **61 passed, 0 failed, 0 skipped** | Includes 40 posted-amount cases with real file-backed intake, retained review, journal references, HTTP packets, and guarded manifest opening; also compatibility service and authorization tests. |
| Browser provenance filter | **144 passed across 6 suites** | Selection, exact scope, cross-fund/name/symbol collisions, missing/stale/foreign/ambiguous support, late responses, malformed guarded links/artifacts, and removal of text inference. Final run used one Vitest worker. |
| WPF provenance and Posted Ledger filter | **70 passed, 0 failed, 0 skipped** | 42 amount-proof cases, 20 existing cases, and 8 basis-arrival cases; includes node/artifact ambiguity, exact identity/kind/retention checks, and actual Page/AutomationPeer amount, manifest, and close interactions with a controlled API client. Full WPF source, XAML, and test assembly compiled. |
| Browser TypeScript and production build | **Passed** | `npm run build` runs `tsc --noEmit` and Vite; regenerated the tracked workstation bundle. |
| Targeted ESLint | **0 errors** | Four existing Financial Record Explorer hook-dependency warnings. |
| Edge rendering and interaction | **Passed with mocked HTTP** | Actual `/workstation/accounting/ledger` page; amount selection, guarded source-link opening, Escape dismissal, missing support, and foreign-fund rejection; zero console errors or framework overlays and no horizontal clipping at 1440 pixels. |
| Roadmap registry | **0 errors, 0 warnings** | Canonical registry validated and generated roadmap outputs refreshed. |
| Full `bash scripts/ci.sh` and GitHub Actions | [PR #3041 validation record](https://github.com/rodoHasArrived/Meridian-main/pull/3041) | The PR records the latest full-gate outcome and required hosted checks. Targeted results do not replace those checks. |

The browser smoke uses explicit bootstrap, ledger, evidence-packet, and guarded-manifest HTTP
fixtures with real GUID formats, SHA-256-shaped digests, and canonical scoped artifact references.
It verifies source-link navigation and query binding but does **not** establish live backend,
database, or source-integrity results. Backend tests independently exercise actual retained source
files and endpoint revalidation. WPF interaction tests use controlled API responses and actual WPF
controls. No single live-host browser-to-storage or desktop-to-storage smoke is claimed.

The 2026-10-02 WPF parity run adds 14 regressions for duplicate evidence nodes, duplicate artifact
identifiers (including one valid and one foreign reference), mismatched artifact identity, node
subject, artifact kind, retention timestamp, retained status, route, hash, canonical subject kind,
and missing nodes or artifacts. The desktop now establishes uniqueness before checking source
content, matching the browser's validation order. Every rejected case withholds all proof and
disables the manifest command without issuing a manifest request.

The subsequent review regressions cover six allowed vault aliases and five rejected Windows path
aliases, four browser selection round trips/late-response cases, and eight WPF response-arrival,
missing-summary, scope-reset, and proof-invalidation cases. Browser round trips were also exercised
in Edge at 1440 by 1050 with mocked HTTP in the same mounted document. The modal's outgoing
navigation used history/popstate, and the visible Ledger tab handled return navigation; a document
marker ruled out a full reload. These checks establish client state behavior, not a live storage flow.

Run the backend and WPF filters from the repository root:

```bash
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj -c Release --filter 'FullyQualifiedName~LedgerAmountProvenanceServiceTests|FullyQualifiedName~LedgerAmountProvenanceEndpointTests|FullyQualifiedName~PostedLedgerAmountProvenanceTests|FullyQualifiedName~EvidenceEndpoints_DeclareSubjectAwarePermissionsAndTenantCompanyScope' --no-restore --disable-build-servers -m:1 -p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=true -p:CreateHardLinksForCopyLocalIfPossible=true
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj --no-restore --filter 'FullyQualifiedName~LedgerAmountProofInteractionTests|FullyQualifiedName~PostedLedgerViewModelTests|FullyQualifiedName~PostedLedgerBasisArrivalTests' -p:EnableWindowsTargeting=true -p:EnableFullWpfBuild=true -p:NodeReuse=false -p:CreateHardLinksForCopyLocalIfPossible=true -p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=true -m:1 --disable-build-servers --logger 'console;verbosity=minimal'
```

Run the browser checks from `src/Meridian.Ui/dashboard/`:

```bash
npx vitest run src/components/meridian/proof-drawer.test.tsx src/components/meridian/number-passport.test.ts src/lib/ledger-amount-proof-api.test.ts src/components/meridian/financial-record-explorer.test.tsx src/screens/finance-standard-pages-screen.test.tsx src/screens/accounting-screen.posted-ledger.view-model.test.ts --maxWorkers=1
npm run build
```

An earlier parallel browser rerun hit an existing asynchronous timing assertion in the posted-ledger
view-model suite during concurrent compilation. The final six-suite run passed unchanged with one
worker. Initial .NET/WPF attempts exhausted local disk; task-owned generated output compression
and standard MSBuild hardlink options allowed both final targeted runs to complete without disabling
or changing tests. Existing unrelated compiler warnings remain. The initial full gate exposed an outdated assertion that every evidence read route required reporting-only metadata. The corrected assertion verifies the exact subject-aware permission set for the four shared routes and preserves single-permission checks elsewhere; a new HTTP regression also denies ledger-only access to reporting packets, graphs, and manifests. The expanded 50-test filter passed before the full-gate restart.

The first hosted schema-control and documentation runs identified derived-output drift. The
contract/dependency manifests, data-object catalog, API coverage, roadmap diagram, examples,
and WPF screen inventory were regenerated from this implementation. Schema tooling tests passed
67/67; inventory validation reported 10 modules, 117 files, and zero errors. The schema candidate
reported no migration or policy errors and no physical schema drift. Hosted verification of the
refreshed outputs is recorded on the PR; local PostgreSQL verification is not claimed.

| Test source | Validated behavior |
| --- | --- |
| `tests/Meridian.Tests/Ui/PostedLedgerAmountProvenanceTests.cs` | Real retained file intake and human review through an amount packet and manifest; sibling and cross-fund collisions; exact five-dimensional scope; missing, stale, duplicate, or altered evidence; permissions; guarded manifest revalidation after packet load. |
| `tests/Meridian.Tests/Ui/LedgerAmountProvenanceServiceTests.cs` | Stable legacy amount bindings, exact reconciliation identities, foreign scopes, preserved blocking severity, and review-required unverified source content. |
| `tests/Meridian.Tests/Ui/LedgerAmountProvenanceEndpointTests.cs` | Compatibility endpoint permission and scope behavior, including rejection of historical label lookup. |
| `tests/Meridian.Tests/Ui/EvidenceWorkflowFabricTests.cs` | The targeted subject-aware metadata fact verifies exact route permissions and retained tenant/company scope. |
| `tests/Meridian.Wpf.Tests/ViewModels/LedgerAmountProofInteractionTests.cs` | Shared packet consumption, selection and scope changes, late-response suppression, missing/foreign/ambiguous evidence, and guarded manifest subject/digest checks. |
| `tests/Meridian.Wpf.Tests/ViewModels/PostedLedgerBasisArrivalTests.cs` | Journal/summary arrival orders, GAAP-only open periods without a summary, basis union and selection preservation, scope resets, and invalidation of loaded or pending proof before row replacement. |

## Operator acceptance path

1. Open Accounting Ledger Explorer (browser) or Posted Ledger (WPF), select a fund/book/period,
   and select a posted line's debit or credit amount.
2. Confirm the proof identifies only that journal entry, line, side, and exact retained scope.
3. Confirm Ready requires the selected amount's retained and reviewed source evidence.
4. Remove, expire, alter, or replace a supporting source with a foreign reference. The amount must
   become review-required or blocked and must never borrow another line/fund's source.
5. Switch amount/book/period during a pending read. Previous or late proof must not appear under
   the new selection. Dismiss the drawer and verify focus returns to the amount control.
