# Governance Report Packs

**Owner:** Governance / Fund Operations  
**Scope:** Local workstation API and artifact contracts  
**Status:** First governed artifact slice delivered; schema contract v1 frozen for DK2 pilot validation

---

## Purpose

Governance report packs publish immutable local artifacts for fund-operations reporting from the shared `FundOperationsWorkspaceReadService` and existing `ReportGenerationService`.

The first slice supports trial-balance report packs for a `fundProfileId`, with manifest, provenance, JSON/CSV section files, and an XLSX workbook when requested. It does not add WPF UI, cloud storage, or Postgres migrations.

---

## Local Storage

Packages are stored under:

```text
%LocalAppData%\Meridian\workstation\governance-report-packs\<fund-key>\<report-id>\
```

Each package contains:

- `manifest.json` - immutable `FundReportPackSnapshotDto`
- `provenance.json` - source lineage and source snapshot hash
- `trial-balance.json` when `Json` is requested
- `trial-balance.csv` when `Csv` is requested
- `asset-class-sections.json` when `Json` is requested
- `asset-class-sections.csv` when `Csv` is requested
- `report-pack.xlsx` when `Xlsx` is requested

The API returns relative artifact paths rooted below `governance-report-packs`; it does not return download URLs.

---

## Schema Contract

Governed report packs use the frozen local export contract:

| Field | Value |
| --- | --- |
| `contractName` | `governance-report-pack` |
| `schemaVersion` | `1` |
| Minimum readable schema version | `1` |

Generation requests may provide `expectedSchemaVersion`. When supplied, it must match the current schema version (`1`); unsupported versions return `400` from the API or `ArgumentException` from the service layer.

The file repository validates the manifest, provenance payload, and artifact metadata before writing. It also skips incompatible future-version manifests during history/detail reads instead of mixing unknown contracts into DK2 pilot evidence.

---

## Routes

| Method | Route | Behavior |
| --- | --- | --- |
| `POST` | `/api/fund-structure/report-pack-preview` | Existing preview-only route. Unchanged. |
| `POST` | `/api/fund-structure/report-packs` | Generates and persists a report-pack artifact package. |
| `GET` | `/api/fund-structure/report-packs?fundProfileId=<id>&limit=<n>` | Lists newest generated packages for a fund. |
| `GET` | `/api/fund-structure/report-packs/{reportId}` | Returns a persisted manifest by report id, or `404`. |

Validation:

- blank `fundProfileId` returns `400`
- blank `auditActor` returns `400`
- empty or unsupported `formats` returns `400`
- unknown `reportId` returns `404`
- empty fund data still generates a package and carries warnings in the manifest

---

## Generate Request

`FundReportPackGenerateRequestDto`

| Field | Required | Notes |
| --- | --- | --- |
| `fundProfileId` | Yes | Fund workspace scope. |
| `auditActor` | Yes | Operator or service identity requesting the package. |
| `reportKind` | No | Defaults to `TrialBalance`. |
| `formats` | No | Defaults to `[Json, Csv, Xlsx]`. |
| `asOf` | No | Defaults to current UTC time. |
| `currency` | No | Defaults from linked fund accounts, then `USD`. |
| `correlationId` | No | Generated when omitted. |
| `decisionRationale` | No | Optional audit note. |
| `expectedSchemaVersion` | No | Must be `1` when supplied. |

Supported `GovernanceReportArtifactFormatDto` values:

- `Json`
- `Csv`
- `Xlsx`

---

## Snapshot Contract

`FundReportPackSnapshotDto` is the persisted manifest returned by generation and detail routes.

Important fields:

- `contractName`
- `schemaVersion`
- `reportId`
- `fundProfileId`
- `displayName`
- `reportKind`
- `currency`
- `asOf`
- `generatedAt`
- `auditActor`
- `correlationId`
- `decisionRationale`
- `provenance`
- `artifacts`
- `warnings`

Each `FundReportPackArtifactDto` includes:

- `artifactKind`
- `format`
- `relativePath`
- `sizeBytes`
- `checksumSha256`
- `schemaVersion`

---

## Provenance

`FundReportPackProvenanceDto` captures:

- schema version
- related run ids
- journal and ledger entry counts
- trial-balance row count
- reconciliation run and open-break counts
- Security Master resolved and missing coverage counts
- source snapshot hash

The source snapshot hash is derived from ordered source inputs and summary counts, not from the generated artifact file bytes.

---

## Implementation Anchors

- Contracts: `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs`
- Service orchestration: `src/Meridian.Ui.Shared/Services/FundOperationsWorkspaceReadService.cs`
- Local repository: `src/Meridian.Ui.Shared/Services/GovernanceReportPackRepository.cs`
- XLSX writer: `src/Meridian.Storage/Export/XlsxWorkbookWriter.cs`
- Routes: `src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.cs`
- Registration: `src/Meridian.Ui.Shared/Endpoints/UiEndpoints.cs`
