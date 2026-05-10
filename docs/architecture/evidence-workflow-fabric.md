# Evidence Workflow Fabric

The evidence workflow fabric is an additive workstation read model that joins readiness, run review,
reconciliation, report-pack, provider-trust, and export evidence into reusable packets. It is not a
parallel workflow engine. Existing workflow definitions and action routing remain the authority for
operator actions.

## Runtime Shape

- `Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs` defines the public DTOs for subjects,
  nodes, graph edges, completeness, templates, packets, and manifest exports.
- `Meridian.Ui.Shared.Evidence.IEvidenceContributor` is the extension seam. Contributors read from
  existing services and return evidence nodes, lineage edges, actions, required ids, and warnings.
- `EvidenceGraphService` composes contributors, deduplicates evidence ids, rejects edges pointing to
  missing nodes, applies template no-orphan warnings, and computes packet completeness.
- `EvidenceTemplateRegistry` maps existing workflow ids to required and optional evidence kinds.
- `IEvidenceArtifactStore` writes manifest-only JSON under `DataRoot/workstation/evidence/`.
- Strategy-run packets attach route-based ledger artifact refs to the `run-ledger` node when a
  ledger summary exists: `ledger-journal` routes to
  `/api/workstation/runs/{runId}/ledger/journal`, and `ledger-trial-balance` routes to
  `/api/workstation/runs/{runId}/ledger/trial-balance`.
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
- `GET /api/workstation/evidence/templates`

Unsupported subject kinds return `400`. Missing subjects return `404`. Missing optional services
return a packet with warnings when a subject can still be resolved.

## Storage And Safety

Version 1 export writes a JSON manifest only. It stores packet metadata, completeness, evidence
references, actions, and warnings when requested. It does not copy raw broker statements, provider
credentials, tokens, or binary evidence bundles.

Manifest paths are generated from sanitized subject kind/id segments and retained under the resolved
data root:

```text
DataRoot/workstation/evidence/<subject-kind>/<subject-id>/<timestamp>-manifest.json
```

Schema versioning starts at `1`; future incompatible manifest changes should add a migration or
reader compatibility path before changing the persisted shape.

## Browser Workbench

The browser dashboard exposes `/reporting/evidence`, plus subject query parameters:

```text
/reporting/evidence?subjectKind=strategy-run&subjectId=<id>
```

The Evidence Workbench renders packet completeness, grouped evidence nodes, graph lineage,
missing/stale evidence, related work-item ids, warnings, validation results, and manifest export
results. React components render UI only; packet loading, grouping, labeling, and command state live
in the workbench view model.

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
