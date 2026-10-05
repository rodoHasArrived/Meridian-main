---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-DESIGN-DOCUMENTS
path: src/Meridian.Documents
status: active
owner_lane: Accounting and Ledger
last_reviewed: 2026-10-05
---

# src/Meridian.Documents

## Purpose

Bounded-context module for retained document evidence, manifests, report rendering, and Evidence
Vault storage-quota policy and durable reservations. Broader document-runtime ownership remains
shared with the Evidence Vault adapter.

## Layer responsibility

This module belongs to the Design Module layer. Keep changes within that ownership boundary and update the registry if the boundary changes.

## Key folders and files

- `src/Meridian.Documents` - registered source module root.
- `EvidenceStorageQuotaOptions.cs`, `EvidenceStorageQuotaCoordinator.cs`, and
  `EvidenceStorageReservation.cs` - configurable Evidence Vault package/count and tenant budgets,
  disk-headroom admission, durable reservation accounting, and process-owned recovery leases.
  The shared vault adapter supplies published usage and attempt-specific staging cleanup; this
  module owns admission and capacity reconciliation across concurrent processes.
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

Document intake and metadata workflows use shared contracts and the UI Shared Evidence Vault;
this module owns storage-quota policy and reservation coordination. The active V1 workflow retains uploaded,
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

Evidence storage reservations are persisted outside the readable vault under
`workstation/evidence-quota`. Each attempt holds an exclusive filesystem lease until publication or
cleanup. A root-wide filesystem gate serializes admission, actual-byte reconciliation, and
publication; recovery reclaims only attempts whose owner lease is available. Failed cleanup keeps
its reservation charged for a later retry. Package accounting includes artifact, UTF-8 manifest,
and index bytes; disk admission includes every tenant's unwritten reserved capacity. Publication
callbacks must write the scoped index last and preserve complete retained evidence during cleanup.
Recovery runs on the next intake/export admission, or explicitly through
`EvidenceStorageQuotaCoordinator.RecoverAbandonedAsync`. It never expires a live lease by age or
deletes published evidence. These guarantees apply to cooperating local-filesystem writers using
the same data root and quota configuration; they do not provide a distributed lease for filesystems
that do not honor exclusive file sharing.

### Evidence storage quota configuration

Shared workstation composition binds `EvidenceVault:StorageQuota` to
`EvidenceStorageQuotaOptions`. Direct service collections without an `IConfiguration` registration
use the defaults below. Direct callers can supply those options to
`FileEvidenceArtifactStore`; each store validates and freezes its own copy at construction.
Recreate stores or restart their hosts to apply changes consistently to all writers sharing a root.

| Option | Default | Meaning |
| --- | --- | --- |
| `MaxArtifactBytes` | `104857600` (100 MiB) | Per-artifact limit; may be lowered but cannot exceed the existing 100 MiB ceiling. |
| `MaxPackageBytes` | `1073741824` (1 GiB) | Logical artifact, manifest, and index bytes in one retained package. |
| `MaxArtifactsPerPackage` | `256` | Distinct retained artifacts per package. |
| `DefaultTenantBudgetBytes` | `10737418240` (10 GiB) | Published bytes plus active reservations for one tenant across all company scopes. |
| `TenantBudgetBytes` | `{}` | Case-insensitive tenant-ID budget overrides, in bytes. |
| `MinimumDiskHeadroomBytes` | `268435456` (256 MiB) | Required free disk space after all tenants' unwritten reservations. |

Package limits must be positive; tenant budgets and disk headroom may be zero. Logical byte
accounting includes UTF-8 metadata, rather than filesystem allocation units. Reservations start
from source-size estimates, extend before underestimated writes, and reconcile to actual written
bytes before index-last publication. Published usage is measured from retained files after restart.
Reservation journals and filesystem overhead are outside logical tenant/package totals; the disk
headroom floor provides operational margin.

This bounded PRD-105 slice covers intake/export admission and recovery. Retention/deletion policy,
broader document-runtime ownership, and quota admission for document-review metadata rewrites
remain outside this slice.

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
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~EvidenceStorageQuotaCoordinatorTests" /p:EnableWindowsTargeting=true
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
