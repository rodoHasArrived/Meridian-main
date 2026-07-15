# Excel Onboarding Workbook & Provider Connection UX Brainstorm (2026-07)

> **Mode:** Problem-Focused / UX — the request asks how to improve two specific experiences:
> connecting data providers, and populating the security master, entities, profiles, ledger, and
> related stores by preparing a file from an Excel template and uploading it. This session goes
> deep on that workflow rather than exploring broadly.
>
> **Grounding:** fresh code exploration of
> `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.DataUploads.cs`,
> `src/Meridian.Application/SecurityMaster/SecurityMasterImportService.cs`,
> `src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.cs`,
> `src/Meridian.Ui/dashboard/src/screens/data-screen.tsx` (`DataUploadIntakePanel`),
> `src/Meridian.Ui/dashboard/src/features/fund-structure/entity-setup-wizard.tsx`,
> `src/Meridian.Ui/dashboard/src/components/data/provider-setup-panel.tsx`,
> `src/Meridian.Ledger/` (`ChartOfAccounts`, `JournalEntry`), and
> `src/Meridian.FSharp/Domain/FundStructure.fs`, plus
> `.claude/skills/_shared/project-context.md` and the competitive-landscape reference.
>
> **Continuity:** prior sessions covered provider *lifecycle* management (setup wizard with live
> certification, credential vault, capability browser — 2026-06-25), statement connectors and the
> reconciliation onboarding wedge (2026-07-01, `W5X-CONNECT-001` / `W5X-STMT-ONBOARD-001`), and
> security-master governance (as-of reads, corporate-action consensus — 2026-07-05). This session
> deliberately targets the **untouched seam between them**: bulk master-data population via a
> prepared Excel workbook, and the moment where a freshly connected provider meets a freshly
> imported instrument universe.

---

## The Headline Finding

**The "template download → upload" pattern already half-exists — it just stops before it counts.**

The Data workspace ships a working template catalog + preview system
(`WorkstationEndpoints.DataUploads.cs` + `DataUploadIntakePanel`): seven CSV templates with field
specs, sample rows, setup checklists, and mapping guidance; a preview endpoint that parses,
validates headers, retains the raw file as evidence, and returns row-level issues. Two of those
templates are exactly the domains in this request — `asset-information` (security master) and
`entity-configuration` (funds/entities/ownership). But:

1. **Nothing commits.** Only bank statements have a committing import
   (`POST /api/workstation/data/uploads/bank-statements/import` →
   `IFundAccountService.IngestBankStatementAsync`). Asset and entity uploads end at preview — the
   operator validates a file and then has nowhere to send it.
2. **No Excel anywhere.** `AcceptedFileExtensions = [".csv"]`, and there is no XLSX package in
   `Directory.Packages.props`. The "prepared file" experience today is a raw CSV with a header row —
   no field descriptions, no dropdowns, no sample formatting once it leaves the browser.
3. **The strongest import engine is invisible to the browser.** `ISecurityMasterImportService`
   (`/api/security-master/import`, CSV/JSON, progress tracking, conflict detection) is only wired
   to the WPF `SecurityMasterViewModel`; the browser security-master screen is fixture-backed and
   read-only.
4. **Entities have a single-record wizard, not a bulk path.** `EntitySetupWizard` →
   `/api/fund-structure/setup-drafts/validate|create` is a solid governed draft seam, but an
   onboarding with 30 entities means 30 wizard passes.
5. **The chart of accounts and opening balances have no import path at all.** Accounts are
   registered programmatically (`ChartOfAccounts.Register`) or journal lines are hand-keyed in
   `JournalEntryForm`.

Everything below assembles these existing pieces into the experience the request describes: *the
system hands you a professionally prepared Excel workbook, you fill it in, you upload it, you
review what it understood, and you commit it into the governed stores.*

---

## Ideas at a Glance

| # | Idea | Effort | Audience | Impact | Depends On |
|---|------|--------|----------|--------|------------|
| 1 | The Meridian Onboarding Workbook (real `.xlsx` template generator) | M | I, Q | High | — |
| 2 | Workbook upload with multi-sheet staged review | M | I, Q | High | 1 |
| 3 | Commit rails per domain + annotated results workbook | M–L | I | High | 2 |
| 4 | "Get connected" onboarding hub: providers ↔ imported universe handshake | M | I, H | Med–High | 3 (partial) |
| 5 | Round-trip bulk maintenance: export → edit in Excel → diff → amend | L | I | Med–High | 1–3 |

Effort key: **S** = days, **M** = 1–2 weeks, **L** = 1+ month.
Audience key: **H** = hobbyist, **Q** = academic, **I** = institutional/fund-ops.

> **Implementation status (2026-07-15):** Ideas 1 and 2 are now implemented. Rather than adding
> ClosedXML, the builder and parser reuse Meridian's existing Office-format seams —
> `Meridian.Storage.Export.XlsxWorkbookWriter` for generation and the
> `CsvPartnerFileParser` ZipArchive/shared-string technique for reading — so no new package
> dependency was introduced. Because `XlsxWorkbookWriter` is a values-only writer, the workbook
> ships an **Instructions** sheet, header-only data tabs, a visible **Field reference** sheet
> (label / required / example / description per field), and a `_meta` sheet stamping template id and
> schema version, in place of the styled dropdowns, header comments, and frozen panes the original
> sketch envisioned (those remain a follow-up if a richer writer is adopted). New surface:
> `GET /api/workstation/data/uploads/templates/workbook` (Idea 1) and
> `POST /api/workstation/data/uploads/workbook/preview` (Idea 2, multi-sheet preview with per-cell
> and cross-sheet `parent_entity_id` validation). Idea 3's commit rails remain the next step.

---

## Idea 1 — The Meridian Onboarding Workbook

Today the "template" an operator downloads is a `data:` URL CSV assembled client-side by
`buildDataUploadTemplateCsv` — a header row and one sample line. Every field description, required
flag, allowed-value list, setup checklist, and mapping note that the backend already publishes in
`DataUploadTemplateDto` is only visible while the browser panel is open. The moment the file lands
in Excel, all of that guidance is gone — which is precisely when the operator (or the fund
administrator they forwarded it to) needs it.

The idea: generate a **genuine Excel workbook server-side** and make it the flagship download in
the Data workspace. One workbook, one tab per domain:

- **Instructions** — the setup checklist and mapping guidance already carried by each
  `DataUploadTemplateDto`, rendered as a readable first sheet with a domain-by-domain progress
  table.
- **Securities** (`asset-information` schema), **Entities & Funds** (`entity-configuration`
  schema), **Accounts**, **Chart of Accounts** (new — see Idea 3), **Opening Balances** (new),
  and the existing statement templates for operators who want one file for everything.
- Each data tab gets what Excel is actually good at: a **frozen, styled header row** with required
  columns visually distinct; **header comments** populated from each field's description text;
  **data-validation dropdowns** for enum columns sourced from the real taxonomies —
  `SecurityReferenceTaxonomyCatalog` for asset classes, `LegalEntityType` and `AccountType` from
  `FundStructure.fs`, `LedgerAccountType` for account classification, ISO currency lists — backed
  by a hidden reference sheet so lists aren't clipped by Excel's inline-validation length limit;
  date columns pre-formatted as ISO dates; one or two greyed example rows marked as such.
- A hidden **meta sheet** stamping template id, schema version, and generation timestamp per tab,
  so the upload side (Idea 2) can auto-detect what it received and whether the schema has drifted.

**The user moment:** on the `/data` upload panel, next to the per-template CSV link, a single
prominent action — *"Download onboarding workbook (.xlsx)"* — with a domain multi-select for
operators who only need one tab. Opening the file in Excel feels like receiving a prepared
implementation pack from a consultant: instructions up front, guided tabs, dropdowns that stop
invalid values at the point of typing.

**Implementation shape:** add ClosedXML (MIT-licensed) to `Directory.Packages.props` (central
package management — no versions in project files), and build an `ExcelWorkbookTemplateBuilder` in
`Meridian.Ui.Shared` that is a pure projection of `BuildDataUploadTemplateCatalog()` — the same
field specs drive CSV, the browser panel, and the workbook, so the three can never drift. Expose
`GET /api/workstation/data/uploads/templates/workbook?domains=...` beside the existing catalog
route. Keep the DTO additions in `Meridian.Contracts` so the WPF parity lane can offer the same
download without forking.

**Tradeoffs:** this is Meridian's first Office-format dependency — it should be fenced inside one
builder/parser service so nothing else grows an Excel habit. Excel data validation has sharp
edges (255-char inline list limit, locale-sensitive dates); the hidden-reference-sheet approach
and ISO-formatted date columns mitigate but need tests with real files saved by real Excel/
LibreOffice. Template versioning becomes a contract: the meta sheet makes stale workbooks
detectable instead of silently mis-parsed.

**Effort/audience:** M. The catalog metadata already exists; the work is the builder, the endpoint,
and file-format tests. Biggest beneficiaries are institutional/fund-ops onboarding, where the
workbook is usually filled by someone who never sees Meridian's UI.

---

## Idea 2 — Workbook Upload with Multi-Sheet Staged Review

Accept the workbook back. Extend the preview endpoint to `.xlsx`
(`AcceptedFileExtensions`), parse each data tab with the same fenced Excel service, resolve each
sheet to its template via the hidden meta sheet (falling back to sheet-name matching for
hand-built files), and run three validation layers:

1. **Per-cell schema validation** — the checks the preview endpoint already does for CSV (required
   headers, missing values), now with real cell addressing: *"Entities!D7 — unknown entity_type
   'LP Feeder'; allowed values are Fund, ManagementCompany, GeneralPartner, …"*.
2. **Cross-sheet referential validation** — the layer no single-file CSV flow can do: every
   account row's `entity_id` must exist in the Entities sheet or in the live fund structure;
   `parent_entity_id` chains must resolve without cycles; opening-balance rows must reference
   securities from the Securities sheet or the live security master; currency codes must be valid
   everywhere.
3. **Against-system validation** — rows that would collide with existing records are flagged as
   prospective updates vs. creates (feeding Idea 3's idempotency).

The raw workbook is retained through the existing evidence-retention path
(`MERIDIAN_DATA_UPLOAD_ROOT`, the `W5X-EVIDENCE-001` lane), same as CSV uploads today.

**The user moment:** `DataUploadIntakePanel` grows from a single-template preview card into an
**import review surface**: tabs across the top mirroring the workbook's sheets, each with a status
chip (Ready for review / Needs repair / Empty), a preview grid with issue cells highlighted
in-place, an issue list grouped by sheet, and honest totals ("212 rows parsed, showing first 10").
The commit action (Idea 3) stays disabled until every non-empty sheet is Ready — the operator
always knows exactly what stands between them and a committed import, and the guidance is always
"fix these cells in Excel and re-upload" with cell addresses they can jump to.

**Implementation shape:** extend `DataUploadPreviewResultDto` to a multi-sheet shape (per-sheet
headers, row counts, issues, status) in `Meridian.Contracts/Workstation/DataUploadDtos.cs`; add the
XLSX branch beside `ParseDataUploadCsv`; revisit `DataUploadMaxFileBytes` (5 MB is tight for a
multi-tab workbook with formatting). The browser view-model work extends
`data-screen.view-model.ts` (`buildDataUploadPanelState`, `previewDataUpload`) and its existing
test suite; endpoint behavior lands in `WorkstationDataUploadEndpointTests`.

**Tradeoffs:** parsing spreadsheets humans edited is defensive programming — merged cells, stray
formatting rows, formulas where values were expected, Excel's 1900-date quirks, numbers stored as
text. The parser should normalize aggressively and complain precisely, never guess silently.
Cross-sheet validation needs a deterministic ordering contract (entities before accounts before
balances) that Idea 3 reuses. The CSV path stays untouched for existing users and scripted
uploads.

**Effort/audience:** M once Idea 1's Excel service exists. Same audiences as Idea 1.

---

## Idea 3 — Commit Rails per Domain + Annotated Results Workbook

This is the half that makes the whole workflow real: validated sheets need somewhere to go. The
bank-statement import (`POST .../bank-statements/import` → parse → validate → retain evidence →
ingest → typed result DTO) is the working shape; replicate it per domain, routing each into the
seam that already owns that data:

- **Securities** → map rows to `CreateSecurityRequest` and drive the existing
  `ISecurityMasterImportService.ImportAsync` — which already returns
  imported/skipped/failed/conflict counts and live progress via
  `ISecurityMasterIngestStatusService`. One honest caveat surfaced in code: the security-master
  endpoint group only maps when PostgreSQL is configured (`StorageFeatureRegistration`). The
  intake panel must detect that and say *"Security Master import requires PostgreSQL storage"*
  rather than presenting a dead button.
- **Entities, funds, accounts** → build fund-structure setup drafts in bulk through the wizard's
  existing seam (`POST /api/fund-structure/setup-drafts/validate` then `create`), preserving the
  draft → validate → create governance the wizard established; account rows route through
  `IFundAccountService`.
- **Chart of accounts** → net-new template committing through the governed FinancialOperations
  path into `ChartOfAccounts.Register` (colon-delimited paths like `Assets:Cash:Brokerage`,
  `LedgerAccountType` validated by the Idea 1 dropdown *and* the server).
- **Opening balances** → never a direct post. Generate one balanced opening `JournalEntry`
  (offset to an `Equity:OpeningBalances` account) and land it as a **governed draft in the
  automated-journal workbench queue** — the same draft → approve → post lifecycle the
  `AutomatedJournalIntakeRunner` lanes use, so `JournalEntry.IsBalanced` and period locks and
  approval gates all apply to imported history exactly as they do to system-generated entries.

Around all four rails, two shared behaviors:

- **Idempotency.** Each commit carries an import batch id; rows upsert by natural key (ticker +
  ISIN/FIGI for securities, `entity_id`, account path). Re-uploading the same workbook yields
  skip counts, not duplicates. The preview *is* the dry run.
- **The annotated results workbook.** After commit, offer the operator their own workbook back
  with `Status` and `Message` columns appended per row — *created / updated / skipped
  (duplicate) / failed (reason)* — plus a summary block on the Instructions tab. Failures
  round-trip in the medium the operator works in: filter the Status column in Excel, fix, delete
  the succeeded rows or leave them (idempotency makes that safe), re-upload.

**The user moment:** the review surface's commit button executes per-sheet in dependency order
with the security-master progress feed streaming row counts. The completion state is a per-domain
scorecard — "Securities: 45 created, 3 updated, 2 failed · Entities: 12 created · Opening
balances: 1 draft awaiting approval in the journal workbench" — with the results-workbook download
and a deep link to the approval queue. Every committed run is recorded as an evidence-backed
import run: who, when, retained source file, row outcomes, created record ids.

**Tradeoffs:** partial-failure semantics must be explicit — per-row commit with a complete outcome
report (matching `SecurityMasterImportResult`'s existing philosophy) beats all-or-nothing for
onboarding-scale files, but it means the operator can land a half-imported universe; the results
workbook and idempotent re-upload are the recovery story. Cross-domain ordering is a hard
contract (entities → accounts → securities → balances). Full rollback is deliberately out of
scope for v1 — security-master deactivation and draft discard already exist as manual undo paths
and the import-run record makes them targetable.

**Effort/audience:** M–L (securities and entities rails are mostly wiring; chart-of-accounts and
opening-balance rails are net-new). This is the institutional onboarding unlock.

---

## Idea 4 — The "Get Connected" Onboarding Hub

The request's two halves — connect providers, populate data — are today two unrelated corners of
the product: provider modules live in Settings (`ProviderSetupPanel`, the provider connection
center, per-provider cards with test/verify), uploads live in Data. A new operator has to already
know Meridian's information architecture to complete day zero.

The idea: a **guided onboarding hub** on the Data workspace (surfaced prominently by
`FirstRunExperienceService` when the system is empty, reachable any time after) that sequences the
existing pieces as one checklist:

1. **Connect your providers** — embeds the existing `ProviderSetupPanel` cards with their
   test-connection and restart-required affordances; step completes when at least one provider
   tests green.
2. **Download the onboarding workbook** (Idea 1).
3. **Upload and review** (Idea 2).
4. **Commit** (Idea 3).
5. **Verify coverage** — the genuinely new step, and the handshake between the two halves: cross
   the just-imported instrument universe against the provider capability matrix
   (`GET /api/providers/capability-matrix`) and report *"23 of 25 imported instruments are
   streamable from your enabled providers; 2 fixed-income instruments have no historical
   provider — enable Provider X or expect gaps"*, with contextual one-click actions: enable the
   suggested provider, subscribe symbols to the universe, queue an initial backfill, open the
   coverage-gaps panel.

**The user moment:** a calm five-step strip with per-step status, each step expanding in place.
The empty-database first-run experience stops being "a dashboard of zeros" and becomes a short,
finishable path with a visible end state: providers green, universe loaded, coverage verified.
Checklist state persists, so an onboarding interrupted at step 3 resumes there.

**Implementation shape:** a `data-screen` region (own view-model + tests, following the
capability-matrix/coverage-gaps view-model pattern) composing existing endpoints; step 5 is a
small read-model join of imported security asset classes against the capability matrix, placed in
`Meridian.Ui.Shared` so WPF parity can consume it. No new stores beyond checklist persistence.

**Tradeoffs:** deliberate overlap risk with the 2026-06-25 "Provider Setup Wizard" theme — this
idea should *compose* the setup panel, not build a second wizard. The coverage join needs an
honest mapping from imported asset classes to provider capabilities (the capability matrix is
provider-level, not instrument-level; over-promising coverage here would undermine trust —
under-claim and link to the real coverage-gaps panel).

**Effort/audience:** M. Highest value for institutional onboarding and for the hobbyist first-run
experience — the same strip serves both, with fewer steps completed.

---

## Idea 5 — Round-Trip Bulk Maintenance

Onboarding is the first upload, not the last. The same workbook becomes the **bulk edit surface**
for data that already lives in the system: an *"Export to workbook"* action on the security-master
and entity screens produces the Idea 1 workbook **pre-filled with current records**, the operator
edits in Excel (rename a ticker batch after a rebrand, fill missing ISINs, re-parent entities),
and re-uploads. Because the meta sheet stamps an as-of version, the upload side can diff each row
against current state and stage **amendments, not creates**: changed fields per row rendered as a
diff ("coupon_rate 4.25 → 4.50"), routed through the existing amendment commands
(`AmendSecurityTermsRequest`, alias upserts, fund-structure updates) with the same review-gate UX
as Idea 2.

**The user moment:** the review surface gains a third row state beside create/error — *amend* —
showing old → new per field, with unchanged rows collapsed. The operator approves a batch of 40
amendments in one screen instead of 40 edit dialogs, and the amendment run lands in the same
evidence-backed import-run history.

**Implementation shape:** the export is the Idea 1 builder fed from the live query services
instead of empty rows; the diff layer compares parsed rows to current read models before choosing
create vs. amend per row. Security master's event-sourced history and existing amend/deactivate
commands make this natural; entities route through the setup-draft seam's update path.

**Tradeoffs:** concurrent-edit drift is the real risk — the as-of stamp lets the server reject or
flag rows whose underlying record changed since export ("stale: re-export or confirm overwrite").
Some fields are provider-governed (consensus-managed identifiers) and must export as visibly
read-only (styling + hidden column marker) and import as ignored-with-warning. This idea is L and
should only start once Ideas 1–3 have proven the format.

**Effort/audience:** L. Institutional operators doing ongoing data stewardship — the difference
between Meridian being "where data was onboarded once" and "where data is maintained."

---

## Synthesis

**Highest-leverage move:** Ideas 1+2 together — the workbook and its upload path — are the
experience the request describes, and they're cheap relative to their perceived value because the
template catalog, field specs, validation plumbing, and evidence retention already exist. But the
**decisive** idea is 3: without commit rails the workbook is preview theater. Ship 1 → 2 → 3 as
one arc, securities rail first (it reuses `ISecurityMasterImportService` nearly verbatim), then
entities/accounts, then the net-new chart-of-accounts and opening-balance rails.

**Platform bets:** (a) the fenced Excel builder/parser service — it serves all five ideas and the
previously flagged certified-XLSX report export (2026-07-06 session), giving Meridian one audited
Office-format seam instead of scattered spreadsheet code; (b) the import-run evidence record —
every committed batch with retained source, row outcomes, and created ids — which extends the
`W5X-EVIDENCE-001` Evidence Vault lane to master data and is what makes Idea 5's amendment
history and any future rollback tooling possible.

**Cross-cutting theme:** every rail deliberately lands in an *existing governed seam* (security
master import service, fund-structure setup drafts, automated-journal draft queue) rather than
writing to stores directly — imports get approval gates, balance enforcement, and lineage for
free, and the WPF parity lane inherits the whole workflow through shared contracts.

**Sequencing:** 1 → 2 → 3 (securities → entities/accounts → CoA/opening balances) → 4 → 5. Idea 4
can start its provider-checklist steps in parallel with 3, since steps 1–3 of the hub only compose
existing surfaces.

**Competitive signals:** the enterprise fund-ops platforms Meridian positions against (Enfusion,
Clearwater-class systems) all onboard client data through implementation-consultant-driven Excel
loading templates — the pattern is industry-standard but locked behind services engagements;
making it self-serve directly serves the emerging-manager wedge identified on 2026-07-05
(shadow-close onboarding kit, launch pack). Bloomberg's data-lineage tagging maps to the
import-run evidence record. Databento and Polygon are API-only with no operational onboarding at
all — the workbook is a differentiator no data-vendor competitor will build, because it only makes
sense when you own the ledger, entity, and security-master spine behind it.
