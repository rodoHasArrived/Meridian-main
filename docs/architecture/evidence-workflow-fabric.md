# Evidence Workflow Fabric

The evidence workflow fabric is an additive workstation read model that joins readiness, run review,
reconciliation, report-pack, provider-trust, Security Master conflict, operations approval, and
export evidence into reusable packets. It is not a parallel workflow engine. Existing workflow
definitions and action routing remain the authority for operator actions.

## Runtime Shape

- `Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs` defines the public DTOs for subjects,
  nodes, graph edges, completeness, templates, packets, and manifest exports.
- `Meridian.Ui.Shared.Evidence.IEvidenceContributor` is the extension seam. Contributors read from
  existing services and return evidence nodes, lineage edges, actions, required ids, and warnings.
- `EvidenceGraphService` composes contributors, deduplicates evidence ids, rejects edges pointing to
  missing nodes, applies template no-orphan warnings, and computes packet completeness.
- `EvidenceTemplateRegistry` maps existing workflow ids to required and optional evidence kinds.
- `IEvidenceArtifactStore` writes retained evidence bundles under `DataRoot/workstation/evidence/`,
  assigns vault identities, maintains a lookup index, stores manifest JSON, and copies retained
  local artifact payloads when artifact references provide a file path.
- Strategy-run packets attach route-based ledger artifact refs to the `run-ledger` node when a
  ledger summary exists: `ledger-journal` routes to
  `/api/workstation/runs/{runId}/ledger/journal`, and `ledger-trial-balance` routes to
  `/api/workstation/runs/{runId}/ledger/trial-balance`.
- Security Master conflict packets use `security-master-conflict` subjects. The `open` subject
  summarizes the open conflict queue; a GUID subject id resolves a specific conflict when the
  configured conflict service can load it. Open conflicts link to the durable reconciliation-case
  work-item id shape `security-master:conflict:{conflictId}` and retain route-only references to
  the Security Master conflict and resolution APIs.
- Operations approval packets use `approval` subjects. The `current` subject resolves the latest
  operations-continuity workflow, while a GUID subject id resolves a specific close workflow.
  Approval status, audit timeline, close checklist, report-pack readiness, and route-only decision
  links come from `IOperationsContinuityWorkflowService` so browser and WPF clients share the same
  close sign-off evidence.
- The pilot readiness artifact carries those same ledger artifact refs so the golden-path CI
  dashboard can prove ledger evidence is both present and route-only without fetching route content
  or reading server-side journal files.

## Workstation API

The shared UI endpoint layer exposes:

- `GET /api/workstation/evidence/subjects`
- `GET /api/workstation/evidence/subjects/{subjectKind}/{subjectId}/packet`
- `GET /api/workstation/evidence/subjects/{subjectKind}/{subjectId}/graph`
- `POST /api/workstation/evidence/subjects/{subjectKind}/{subjectId}/validate`
- `POST /api/workstation/evidence/subjects/{subjectKind}/{subjectId}/export-manifest`
- `GET /api/workstation/evidence/vault/{vaultId}/manifest`
- `GET /api/workstation/evidence/templates`

Unsupported subject kinds return `400`. Missing subjects return `404`. Missing optional services
return a packet with warnings when a subject can still be resolved.

## Storage And Safety

Version 1 export writes a JSON manifest and can retain local artifact payloads referenced by
retained evidence nodes. Route-only artifacts remain route references. Local retained files are
copied into the vault bundle, hashed, size-recorded, and listed on the vault identity. The store does
not copy provider credentials or tokens, and retained files are capped to avoid unbounded memory or
disk growth during workstation export. Retained payloads must carry canonical subject kind and id
linkage at the vault write boundary; unsupported subject kinds or missing linkage are rejected before
the file is copied so retained artifacts cannot become orphan evidence.

Export responses include a vault id, retained route, and manifest path so the browser can reopen the
retained manifest. When payload artifacts are copied, the vault identity uses `file-bundle` storage
and records each retained artifact path, hash, source route, canonical subject, and size. Manifest
exports with no copied artifacts retain the `file-manifest` storage kind.

Manifest paths are generated from sanitized subject kind/id segments and retained under the resolved
data root:

```text
DataRoot/workstation/evidence/<subject-kind>/<subject-id>/<timestamp>-manifest.json
DataRoot/workstation/evidence/_vault/<vault-id>/artifacts/<artifact-kind>-<artifact-id-hash>.<ext>
```

Schema versioning starts at `1`; future incompatible manifest changes should add a migration or
reader compatibility path before changing the persisted shape.

## Browser Workbench

The browser dashboard exposes `/reporting/evidence`, plus subject query parameters:

```text
/reporting/evidence?subjectKind=strategy-run&subjectId=<id>
/reporting/evidence?subjectKind=security-master-conflict&subjectId=open
/reporting/evidence?subjectKind=approval&subjectId=current
```

The Evidence Workbench renders packet completeness, grouped evidence nodes, graph lineage,
missing/stale evidence, related work-item ids, warnings, validation results, manifest export
results, and retained vault artifact bundles. Export results show the vault id, storage kind,
retained artifact paths, hashes, sizes, source routes, and canonical subjects when the backend
returns a file-backed vault identity. It also surfaces the packet-scoped Meridian Assurance score,
assurance components, breached Evidence SLA assessments, and orphan-evidence ids from
`EvidenceCompleteness`. React components render UI only; packet loading, grouping, labeling,
assurance/SLA/orphan labels, retained-bundle labels, and command state live in the workbench view
model.

## Extension Guidance

Add a contributor when a real user-facing evidence packet needs a new source. A contributor should:

- reuse an existing read service or repository
- return empty nodes with warnings when optional source services are absent
- use stable evidence ids scoped to the subject
- attach artifact references instead of copying source files; route-only references should leave
  `path` and `hash` empty until a retained manifest/export flow owns those files
- add required ids only for evidence the packet must validate
- avoid creating duplicate operator next-action links when an inbox or workflow action already owns
  the route
