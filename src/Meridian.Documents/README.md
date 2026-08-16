---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-DESIGN-DOCUMENTS
path: src/Meridian.Documents
status: active
owner_lane: Accounting and Ledger
last_reviewed: 2026-07-27
---

# src/Meridian.Documents

## Purpose

Physical bounded-context module project for retained document attachments, document evidence, manifests, and document-management ownership conformance.

## Layer responsibility

This module belongs to the Design Module layer. Keep changes within that ownership boundary and update the registry if the boundary changes.

## Key folders and files

- `src/Meridian.Documents` - registered source module root.
- `FinancialReportDocumentRenderer.cs` - client-grade QuestPDF (PDF) + ClosedXML (XLSX) renderer that
  implements the ledger's `ILedgerReportBinaryRenderer` seam. Output is made deterministic (fixed
  document metadata/timestamps, canonical zip ordering) so re-rendering a pack reproduces the bytes.
  The statement of changes in partners' capital renders through a bespoke layout (from the ledger's
  `PartnersCapitalStatementLayout`) rather than the generic table: a fund-economics NAV context strip,
  role-labelled per-partner rows, an ownership-share column, a bold total, and a reconciliation note in
  the PDF, and a dedicated XLSX sheet whose money and ownership cells are typed numbers (accounting and
  percent formats) so the deliverable can be summed and pivoted without retyping. Every other statement
  keeps the generic tabular rendering.
- `DocumentsServiceCollectionExtensions.cs` - `AddFinancialReportDocumentRenderer` composition helper
  that registers the renderer for the `ILedgerReportBinaryRenderer` seam. The workstation host calls
  it (see `WorkstationServiceCollectionExtensions`), flipping governed ledger exports off the
  dependency-free plain-text fallback so the governed report pack is the client deliverable. The
  shared `LedgerClientReportExportService` (in `Meridian.Ui.Shared`) is the single export seam the
  browser and WPF workstations both route through. Reporting uses that same seam for governed
  capital-account `Pdf`, `Xlsx`, and `ClientPackage` outputs: the certified producer passes the
  exact checkpoint-bound `LedgerFinancialReportPack` through once and receives the complete
  PDF/XLSX pair, selecting one canonical document for a standalone format or both for the package.
  It does not project the pack into a second `ClientGradeReportRenderer` model.

## Important workflows

Document intake is implemented through shared contracts and the UI Shared Evidence Vault until this
design module owns a dedicated runtime service. The active V1 workflow retains uploaded,
API-supplied, local-file, or imported-file-reference sources as immutable vault artifacts, records
source hash, received timestamp, source channel, source path/route reference, actor, tenant/scope,
document classification, object links, extraction status, reviewer state, and audit trail, then
freezes that metadata into searchable manifests and request lists for close, report, tax, and audit
packages. The shared vault identity also carries a public frozen manifest snapshot so package
consumers can read retained documents, support requests, object links, and content hash without
parsing internal manifest JSON.

OCR and AI extraction must stay behind `IEvidenceDocumentExtractor`. The default implementation only
normalizes operator-supplied deterministic metadata and fixture fields; later OCR or LLM extraction
should return the same contract without gaining authority to post journals, approve evidence,
release payments, or certify reports.

## Diagrams

`DIA-ASSURANCE-LOOP`

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-DESIGN-DOCUMENTS -->
| Roadmap item | Title |
| --- | --- |
| `W4-RPT-001` | Governed report pack readiness |
| `W5-ACCT-001` | Accounting records and operational evidence |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-DESIGN-DOCUMENTS -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```bash
dotnet build src/Meridian.Documents/Meridian.Documents.csproj /p:EnableWindowsTargeting=true
```

## Optional conditional sections

Add only the sections that apply to this module:

- `### Plans and roadmap`
- `### End-user value`
- `### Benchmarks and performance`
- `### Operational evidence`
- `### Security and credentials`
- `### API and contract notes`
- `### Migration and archive notes`

## Change rules

Preserve the module boundary declared in `docs/source/data/source-modules.yml` and update the nearest docs when behavior or workflow semantics change.

## Related docs

- `docs/source/README.md`
- `docs/source/generated/source-module-index.md`
- `docs/architecture/module-map.md`
