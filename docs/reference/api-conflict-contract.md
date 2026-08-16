# API Conflict Contract

How Meridian's shared UI endpoints report an HTTP 409, and the one optimistic-concurrency shape
route families converge on as they are touched. Origin: issue #2694, which found seven distinct
409 body shapes for the same stale-version condition.

## Request side: `ExpectedVersion`

A write request that participates in optimistic concurrency carries the version the caller loaded
in a field named **`ExpectedVersion`** (`long` where the store versions numerically). This is the
dominant spelling across `Meridian.Contracts` (reporting governance, security master, ledger
posting commands, reconciliation, operations continuity) and the one new contracts must use. Do
not introduce `Version`, `RowVersion`, `IfMatch`, or HTTP `ETag`/`If-Match` headers for this
purpose; Meridian versions in the body.

Two existing families do not follow this rule yet. Both are recorded as exceptions retained until
their request DTOs are next reshaped, not as precedent; their 409 *responses* already follow the
canonical body below.

- **Ledger journal automation** binds the full work item (`AutomatedJournalScheduleWorkItem`),
  whose **`Version`** member is both the record's current version on read and the expected
  version on write — there is no separate `ExpectedVersion` field to send.
- **Reporting schedules** carry *no* caller version at all: `ReportingScheduleUpsertRequestDto`
  has neither `ExpectedVersion` nor an expected revision timestamp, and the store's
  retained-revision check runs against its own latest read during commit. Its 409 therefore
  detects only two writers racing the same commit window — a client that read, paused, and wrote
  later silently overwrites rather than receiving the advertised conflict. Closing that
  lost-update window means adding a caller-supplied expected revision to the DTO and its
  clients, deferred to a change that reshapes the contract.

## Response side: the canonical 409 body

A stale `ExpectedVersion` maps to `ApiProblemDetails.VersionConflict(...)`
(`src/Meridian.Ui.Shared/Endpoints/ProblemDetails/ApiProblemDetails.cs`), an RFC 7807 problem
document:

```json
{
  "type": "https://meridian.io/errors/version-conflict",
  "title": "Version Conflict",
  "status": 409,
  "detail": "Automated journal schedule 'sched-1' version is stale. Expected 6, current 7.",
  "instance": "/api/workstation/ledger/journal-automation/monthly-schedules",
  "traceId": "00-…",
  "timestamp": "2026-08-16T02:00:00Z",
  "resourceId": "sched-1",
  "expectedVersion": "6",
  "currentVersion": "7"
}
```

- `resourceId`, `expectedVersion`, and `currentVersion` are extension members. Each is optional:
  a producer that has no structured version data omits the member entirely (never `null`), so
  clients probe with presence checks.
- Version values are **strings**, formatted invariantly, because the families version differently:
  numeric versions (`"7"`), revision tokens, and retained-revision timestamps (round-trip `"o"`
  format, e.g. `"2026-08-16T02:00:00.0000000+00:00"`) all travel in the same members. Clients
  compare them opaquely — equality and display, not arithmetic.
- `currentVersion` is the value the client needs to refetch, show a diff, and replay the
  operator's edit without losing it.

A 409 that is **not** an optimistic-concurrency race — a business-state conflict such as a
schedule already running or an execution lease being held — uses
`ApiProblemDetails.Conflict(...)` instead (`type: https://meridian.io/errors/conflict`,
`title: "State Conflict"`). The two `type` URIs are the discriminator: `version-conflict` means
"refetch and retry"; `conflict` means the state itself refuses the operation.

## Converged route families

| Family | Version data carried |
| --- | --- |
| Ledger journal automation (`LedgerEndpoints.JournalAutomation.cs`) | `resourceId` (schedule id), numeric `expectedVersion`/`currentVersion` |
| Reporting governance (`FundStructureEndpoints.cs`, `FundStructureEndpoints.ReportingGovernance.cs`) | detail only — `ReportingGovernanceConcurrencyException` carries no structured versions yet |
| Reporting schedule authority (`FundStructureEndpoints.ReportingScheduleAuthority.cs`) | `resourceId` (schedule id), timestamp `expectedVersion`/`currentVersion` |

## Known divergences, converged as touched

Migration is deliberate and incremental — issue #2694 stays open until these converge:

- **Security Master workbench** (`WorkstationEndpoints.SecurityMasterWorkbench.cs`) returns
  `{ "error": "version-conflict", "currentVersion": 7 }` with a numeric version. Both the browser
  workstation (`security-master-workbench.api.ts`) and the WPF workstation
  (`SecurityPassportEditorViewModel`) parse that exact shape, so this family migrates only as a
  coordinated three-surface change.
- **Security Master conflict resolution** (`SecurityMasterEndpoints.cs`) returns an
  `ErrorResponse.Validation` body and a hand-titled problem for its two 409 paths.
- **Archive maintenance schedules** (`ArchiveMaintenanceEndpoints.cs`) returns an ad-hoc
  `{ error, ScheduleId, ExpectedRevision, ActualRevision }` body.
- **Reporting run readiness** (`FundStructureEndpoints.cs`) intentionally returns a typed
  `ReportingRunReadinessDto` body with its 409 — a readiness verdict, not a version race. If it
  stays typed, it should stay declared via `Produces<T>` as today.

When touching any of these, migrate the family to `VersionConflict`/`Conflict` in the same change
and move its row into the converged table above.
