# W10-PROV-001 first posted amount workflow

**Status:** implementation validation in progress  
**Reviewed:** 2026-10-01

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
closed. Report generation does not yet emit those new bindings; report/NAV/portfolio-wide activation
remains outside this first posted-line slice. The canonical roadmap item remains in progress.

Verified posted-source links carry the selected amount subject, fund, book, period, and expected
manifest digest to the existing vault route. Opening a link re-evaluates the retained source bytes
and current review state and verifies the opened manifest's subject and digest. If support changes
after the drawer loads, the guarded request returns `409 ledger-amount-proof-stale` rather than
presenting the changed evidence as the previously verified proof.

## Validation evidence

Targeted validation completed on 2026-10-01. All commands ran against this implementation.

| Check | Result | Coverage and limits |
| --- | --- | --- |
| Backend provenance filter | **48 passed, 0 failed, 0 skipped** | Includes 28 posted-amount cases with real file-backed intake, retained review, journal references, HTTP packets, and guarded manifest opening; also compatibility service and authorization tests. |
| Browser provenance filter | **140 passed across 6 suites** | Selection, exact scope, cross-fund/name/symbol collisions, missing/stale/foreign/ambiguous support, late responses, malformed guarded links/artifacts, and removal of text inference. Final run used one Vitest worker. |
| WPF provenance and Posted Ledger filter | **48 passed, 0 failed, 0 skipped** | 28 new cases and 20 existing cases; includes actual Page/AutomationPeer amount, manifest, and close interactions with a controlled API client. Full WPF source, XAML, and test assembly compiled. |
| Browser TypeScript and production build | **Passed** | `npm run build` runs `tsc --noEmit` and Vite; regenerated the tracked workstation bundle. |
| Targeted ESLint | **0 errors** | Four existing Financial Record Explorer hook-dependency warnings. |
| Edge rendering and interaction | **Passed with mocked HTTP** | Actual `/workstation/accounting/ledger` page; amount selection, guarded source-link opening, Escape dismissal, missing support, and foreign-fund rejection; zero console errors or framework overlays and no horizontal clipping at 1440 pixels. |
| Roadmap registry | **0 errors, 0 warnings** | Canonical registry validated and generated roadmap outputs refreshed. |
| Full `bash scripts/ci.sh` | **Running** | Targeted results do not replace the full repository gate or required GitHub Actions checks. |

The browser smoke uses explicit bootstrap, ledger, evidence-packet, and guarded-manifest HTTP
fixtures with real GUID formats, SHA-256-shaped digests, and canonical scoped artifact references.
It verifies source-link navigation and query binding but does **not** establish live backend,
database, or source-integrity results. Backend tests independently exercise actual retained source
files and endpoint revalidation. WPF interaction tests use controlled API responses and actual WPF
controls. No single live-host browser-to-storage or desktop-to-storage smoke is claimed.

Run the backend and WPF filters from the repository root:

```bash
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter 'FullyQualifiedName~LedgerAmountProvenanceServiceTests|FullyQualifiedName~LedgerAmountProvenanceEndpointTests|FullyQualifiedName~PostedLedgerAmountProvenanceTests' --no-restore --disable-build-servers -m:1 -p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=true -p:CreateHardLinksForCopyLocalIfPossible=true
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj --no-restore --filter 'FullyQualifiedName~LedgerAmountProofInteractionTests|FullyQualifiedName~PostedLedgerViewModelTests' -p:EnableWindowsTargeting=true -p:EnableFullWpfBuild=true -p:NodeReuse=false -p:CreateHardLinksForCopyLocalIfPossible=true -p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=true -m:1 --disable-build-servers --logger 'console;verbosity=minimal'
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
or changing tests. Existing unrelated compiler warnings remain.

| Test source | Validated behavior |
| --- | --- |
| `tests/Meridian.Tests/Ui/PostedLedgerAmountProvenanceTests.cs` | Real retained file intake and human review through an amount packet and manifest; sibling and cross-fund collisions; exact five-dimensional scope; missing, stale, duplicate, or altered evidence; permissions; guarded manifest revalidation after packet load. |
| `tests/Meridian.Tests/Ui/LedgerAmountProvenanceServiceTests.cs` | Stable legacy amount bindings, exact reconciliation identities, foreign scopes, preserved blocking severity, and review-required unverified source content. |
| `tests/Meridian.Tests/Ui/LedgerAmountProvenanceEndpointTests.cs` | Compatibility endpoint permission and scope behavior, including rejection of historical label lookup. |
| `tests/Meridian.Wpf.Tests/ViewModels/LedgerAmountProofInteractionTests.cs` | Shared packet consumption, selection and scope changes, late-response suppression, missing/foreign/ambiguous evidence, and guarded manifest subject/digest checks. |

## Operator acceptance path

1. Open Accounting Ledger Explorer (browser) or Posted Ledger (WPF), select a fund/book/period,
   and select a posted line's debit or credit amount.
2. Confirm the proof identifies only that journal entry, line, side, and exact retained scope.
3. Confirm Ready requires the selected amount's retained and reviewed source evidence.
4. Remove, expire, alter, or replace a supporting source with a foreign reference. The amount must
   become review-required or blocked and must never borrow another line/fund's source.
5. Switch amount/book/period during a pending read. Previous or late proof must not appear under
   the new selection. Dismiss the drawer and verify focus returns to the amount control.
