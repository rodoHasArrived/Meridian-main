# Security Master Terms Contract Rollout

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-10-28

## Purpose and Scope

Use this procedure to roll out increment 5 of the Security Master schedule-terms work, the contract
step that makes declared schedule conventions and call and put schedules writable. It lands in four
parts, each deployed to every node before the next one's gates are checked:

| Part | What it changes | Writes anything differently? |
| --- | --- | --- |
| 5a-0 | `OpenLotAmortization` refuses a populated `scheduleConventions` and reads call, put and conventions terms in every source and casing | No |
| 5a | One decode predicate; the file-backed calendar source and the startup capability line; the declared mode of the Security Master projection; the ledger gate refuses declared projections | No |
| 5b-1 | The write rules, keyed on writability, so neutral until the raise | No |
| 5b-2 | The raise: Bond and CertificateOfDeposit write terms version 3 and StructuredCredit version 4 | Yes, for declaring records only (stamp model S2) |

Success means that every node, WPF desktops included, runs 5a before 5b-2's first declaring write,
that every gate below returned its expected result in each environment, and that each result is in
the deployment change record. The source design is the
[extensibility review](../architecture/security-master-extensibility-review.md) (priority 5,
increment 5), with [ADR-024](../adr/024-terms-payload-format-and-version-policy.md#applications)
and [ADR-025](../adr/025-contractual-schedule-kernel.md#applications).

Out of scope: installing calendar data, which follows
[Business-Day Calendar Files](business-day-calendar-files.md) and waits on a named operations
owner; and the 5b queries, whose text the 5b pull requests add to the placeholders below. This
procedure follows the writer-inventory and drain precedent of the
[governed edit rollout](security-master-governed-edit-rollout.md).

## Prerequisites

- A deployment change record naming the release owner, the accepting operator and, before any real
  calendar file is installed, the calendar data's operations owner.
- A complete node inventory for each environment: every server host that composes the storage
  feature (`StorageFeatureRegistration`), including background and import workers, and every WPF
  desktop install. Desktops matter even though they serve no projections: they post amortization
  in-process and, when `MERIDIAN_SECURITY_MASTER_CONNECTION_STRING` is set, write Security Master
  terms in-process, and their traffic cannot be routed.
- Read access to each node's log from host start. A server's log goes to its configured sink. A
  desktop's `ILogger` output reaches only the generic host's default providers (console, debug and
  EventSource; the Windows event log takes warnings and above), because the desktop host adds no
  file logging provider ([WPF README](../../src/Meridian.Wpf/README.md)).
- A read-only `psql` session against each environment's Security Master, ledger and
  asset-operations schemas, with deployment-approved credentials.

## Execution Context

- Database checks: `psql` against the verified target database. Do not retain connection strings
  in the change record.
- Log checks: the node's log sink, or a capture attached before the host starts. The lines below
  are written once, at host start, so a capture attached later never sees them.
- Deployment: the environment's normal deployment controller and
  [lifecycle supervisor](../reference/lifecycle-control-plane.md). Record the actual commands and
  receipts in the change record.

## Procedure

### 1. Before 5a-0 deploys: the amortization gate (each environment)

Run AQ1 (stored securities) and AQ2 (retained amortization instruction snapshots) exactly as
written in the
[5a-0 resolution pass](../architecture/security-master-extensibility-review.md#resolution-pass--2026-10-28-priority-5-increment-5a-0-amortization-refuses-declared-schedule-conventions)
of the extensibility review. Both are expected to find nothing. Record the counts, the database
and schemas, the timestamp and the operator. A hit is remediated before the deploy, while the
running build can still read and reverse it. 5a is built on 5a-0, so these results also cover 5a.

### 2. 5a on every node: the inventory gate

Deploy 5a to every node in the inventory, desktops included. At host start each 5a node logs one
Information line from `Meridian.Application.Composition.SecurityMasterTermsCapabilityHostedService`.
With no calendar files installed, a 5a node logs exactly:

```text
Security Master terms capability: declared mapping security-master-declared-schedule-v1, schedule kernel contractual-schedule-kernel-v1, terms versions Bond r3/w1, CertificateOfDeposit r3/w1, StructuredCredit r4/w2, Swap r2/w1, business-day calendars 0: none
```

- `declared mapping` names the frozen declared-schedule mapping; every node must name the same one.
- `terms versions` lists each class whose read (`r`) and write (`w`) terms versions differ. The
  list above is 5a's, and 5b-1's, which raises nothing. 5b-2 is expected to drop Bond,
  CertificateOfDeposit and StructuredCredit from it; its pull request records its exact line here.
- `business-day calendars` gives the count of loaded calendar files and lists each as
  `ID v{version} {hash}`, sorted by id. The built-in `WEEKENDS` is never listed.

Before the capability line, the same start logs one line per loaded calendar file
(`Loaded business-day calendar {id} version {version} hash {hash} coverage {from} to {to} from
{path}`) and one Warning per refused file. For each node, record its identity, kind (server or
desktop), build, the full capability line and its timestamp.

A node that logs no capability line runs a build below 5a (4b or 5a-0), or its log was not captured
from host start. A desktop whose line cannot be captured is recorded with its installed artifact
identity and the reason, and counts as below 5a until its line is captured.

### 3. Before 5b-2 deploys: the stored-data gates (each environment)

Each check is expected to return 0. Record the counts, the query revision, the database and
schemas, the timestamp and the operator.

- **AQ1 and AQ2, again.** As in step 1.
- **Q1, stored containers.** *Placeholder: increment 5b-2 adds the query here.* It counts Bond,
  CertificateOfDeposit and StructuredCredit rows that populate a top-level `scheduleConventions`,
  `callSchedule` or `putSchedule` at any stamp, or carry a non-canonical casing of those keys with
  any value, excluding StructuredCredit profile envelopes. A hit is a stray container: once the
  write versions rise, an amend that echoes it is refused, and a create or import that carries it
  without the stored row would declare it.
- **Q1b, swap leg blocks.** *Placeholder: increment 5b-2 adds the query here.* The same over the
  `scheduleConventions` of every Swap leg, under `legs`, `swapLegs` or `cashFlowLegs`.
- **QR, economic-only replay.** *Placeholder: increment 5b-1 adds the query here, with its replay
  refusal.* It counts economic-only Bond, CertificateOfDeposit and StructuredCredit events whose
  economic document shows a declared schedule: a non-null accrual `businessDayConvention` or
  `holidayCalendar`, or a non-empty `call.callSchedule` or `call.putSchedule`.

### 4. Before 5b-2 deploys: fleet and calendar parity

- **Every node runs 5a or later.** Check the step 2 inventory again for each environment, desktops
  included, against each node's current capability line.
- **Calendar parity.** Every node that serves Security Master projections reports an identical
  calendar list in its capability line: the same ids, versions and hashes, or `none` on every one.
  An empty set is allowed. The cash-flow service is composed by `StorageFeatureRegistration`, so
  these are the server hosts; the WPF desktop's own graph does not compose it. Installing real
  calendar files waits on the named operations owner.
- **Owner confirmations and companion changes.** The change record cites the owner's confirmation
  of the declared semantics (2026-10-28) and the merge of the HTTP 400 refusal change that 5b-1 is
  built on.
- **Capability floor.** *Placeholder: increment 5b-1 adds the floor table and its startup check,
  and 5b-2 adds the step that writes the floor row as the deploy's first action.*

### 5. During the 5b-2 rollout: edit routing and drains

- **Route Security Master edits to 5b-2 nodes.** Under stamp model S2 only declaring rows are
  affected: a 5a or 5b-1 node refuses a full-payload amend (the WPF edit screen,
  `SecurityTermBlocks.BuildPatch` and the approved field-edit merge) of a row stamped above its
  write version, while deactivations and patch-less amends still work when the row re-serializes
  exactly. Desktops cannot be routed, so a desktop below 5b-2 refuses such edits with a reason until
  it is upgraded; nothing is lost. Records that declare nothing keep their stamps and bytes.
- **Drain before any rollback of a 5b-2 node.** Staged workbench field edits that populate a
  versioned container fail at merge on 5a and 5b-1 nodes, as 4a's staged `legs` revisions did.
  Merge or withdraw them first.
- **No asset-class rebuild while nodes run different builds.**

### 6. Rollback floors

| Part | Floor | Why |
| --- | --- | --- |
| 4a | Once a contract node has written a version-2 stamp (version 3 for StructuredCredit), no node may roll back below 4a | Older nodes refuse those rows on read |
| 4b | Once a Bond or CertificateOfDeposit row stamped 3, or a StructuredCredit row stamped 4, exists, no node may roll back below 4b | Older nodes refuse those rows on read |
| 5a (with 5a-0 beneath it) | Rolls forward and back freely until 5b-2's first declaring write; from then on no node, desktops included, may run below 5a | A 4b node would ignore the block in projections, let them post and amortize a block-only declaring Bond on the bullet grid; a 5a-0 node would refuse the amortization but project and post the legacy dates |
| 5b-2 | May roll back to 5b-1 or 5a, never lower; drain staged container edits first | Declaring rows stay readable on 5a, which reads through 3 (4 for StructuredCredit), and are followed or blocked identically |

Each floor is observable from the capability line; 5b-1's capability floor is to enforce it.

## Expected Results

- Step 1 and step 3: every query returns 0 in every environment, recorded.
- Step 2 and step 4: every node in the inventory has a recorded capability line that names
  `security-master-declared-schedule-v1`, and every node that serves projections lists the same
  calendars.
- No step is skipped for desktops. A gate with a missing node or a missing result is a hold.

## Failure and Recovery

| Symptom | Diagnostic | Safe next action |
| --- | --- | --- |
| AQ1 or AQ2 finds rows | The query output and the hit's source column | Hold the deploy; remediate as the 5a-0 resolution pass describes, then rerun |
| A node logs no capability line | Its build identity; whether the capture began before host start | Upgrade the node, or capture again from a restart; hold 5b-2 until its line is recorded |
| A node's line differs from the one its build should log (another declared mapping or other terms versions) | The capability line and the node's build | Align the node's build with the fleet; hold 5b-2 |
| Calendar lists differ between projection nodes | The capability lines; the per-calendar lines and Warnings | Align the files ([calendar procedure](business-day-calendar-files.md)), restart, and compare again |
| A calendar file is refused at start | The Warning naming the file and the reason | Fix the file as the calendar procedure describes; its id stays unresolvable until then |
| Q1, Q1b or QR finds rows (once added) | The query output | Hold 5b-2; follow the remediation the 5b pull requests add here |
| A rollback below a floor is proposed | The floor table above | Do not roll back below it; roll forward or to an allowed build |

## Evidence and Handoff

The release owner keeps, per environment: the node inventory with each node's capability line and
timestamp, the AQ1, AQ2, Q1, Q1b and QR results, the calendar parity comparison, the owner
confirmations, the drain and routing decisions, and any rollback decision with the floor it
respected. Each receipt names its environment, artifact or revision, timestamp and responsible
person. Mark unperformed checks pending; this procedure records no production action by itself.

## Related References

- [Business-Day Calendar Files](business-day-calendar-files.md)
- [Security Master Governed Edit Rollout](security-master-governed-edit-rollout.md)
- [ADR-024 applications](../adr/024-terms-payload-format-and-version-policy.md#applications)
  (readable before writable, the decode predicate, the stamp model and the rollback floors)
- [ADR-025 applications](../adr/025-contractual-schedule-kernel.md#applications) (the declared
  mapping, the calendar files and the ledger gate)
- [Startup capability line](../../src/Meridian.Application/Composition/SecurityMasterTermsCapabilityHostedService.cs)
- [Business-Day Calendar](../domain/business-day-calendar.md)
