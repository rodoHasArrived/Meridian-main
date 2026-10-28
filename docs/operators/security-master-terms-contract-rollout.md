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
owner; and the capability floor and the remediation of a 5b gate hit, which the 5b pull requests
add to the placeholders below. This procedure follows the writer-inventory and drain precedent of
the [governed edit rollout](security-master-governed-edit-rollout.md).

## Prerequisites

- A deployment change record naming the release owner, the accepting operator and, before any real
  calendar file is installed, the calendar data's operations owner.
- A complete node inventory for each environment: every server host that composes the storage
  feature (`StorageFeatureRegistration`), including background and import workers, and every WPF
  desktop install. Desktops matter even though they serve no projections: they post amortization
  in-process and, when `MERIDIAN_SECURITY_MASTER_CONNECTION_STRING` is set, write Security Master
  terms in-process, and their traffic cannot be routed.
- Read access to each node's log from host start. A server's log goes to its configured sink. A
  desktop is a windowed program with no console and no log file: its `ILogger` output reaches only
  the generic host's default providers (console, debug and EventSource; the Windows event log takes
  warnings and above), because the desktop host adds no file logging provider
  ([WPF README](../../src/Meridian.Wpf/README.md)). None of them keeps an Information line without
  extra setup, so step 2 starts each desktop from PowerShell with its console output kept. That
  needs PowerShell on the desktop and a sign-in as the operator who uses it.
- A read-only `psql` session against each environment's Security Master, ledger and
  asset-operations schemas, with deployment-approved credentials.

## Execution Context

- Database checks: `psql` against the verified target database. Do not retain connection strings
  in the change record.
- Log checks: a server's log sink; on a desktop, the capture file that
  [Capturing a desktop's lines](#capturing-a-desktops-lines) produces. The lines below are written
  once, when the host starts, so a capture begun later never sees them. A desktop starts its host
  only after sign-in, once its main window is shown.
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

#### Capturing a desktop's lines

The desktop's console logger still writes when its standard output is redirected, so start it from
PowerShell with that output kept in a file. On each desktop, as the operator who uses it:

1. **Find the executable, then close every desktop window.** Open the desktop from the browser
   workstation as usual, run `(Get-Process -Name Meridian.Desktop).Path` and note the path (the
   Meridian setup installs it as `<install root>\desktop\Meridian.Desktop.exe`). Close the
   desktop, and check that `Get-Process -Name Meridian.Desktop -ErrorAction SilentlyContinue`
   prints nothing: a second desktop hands its launch to the one already running and exits without
   logging.
2. **Start it with its output kept**, in one PowerShell session:

   ```powershell
   $exe = '<path from action 1>'
   $capture = Join-Path $env:TEMP ('meridian-desktop-capability-{0}-{1:yyyyMMddTHHmmss}.log' -f $env:COMPUTERNAME, (Get-Date))
   $env:Logging__Console__FormatterOptions__SingleLine = 'true'
   $env:Logging__Console__FormatterOptions__TimestampFormat = 'yyyy-MM-ddTHH:mm:ss.fffzzz '
   Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -RedirectStandardOutput $capture
   ```

   The two variables put each entry on one line with its timestamp and category. They reach only
   programs started from this session, and change nothing else on the desktop.
3. **Sign in and wait for the main window.** The desktop starts its host, and with it the calendar
   lines and the capability line, only once the main window is shown.
4. **Read the lines** in the same session:

   ```powershell
   Select-String -Path $capture -SimpleMatch 'business-day calendar', 'Security Master terms capability:', 'Application started' | ForEach-Object Line
   ```

   Each match reads `<timestamp> info: <category>[<event id>] <message>`, or `warn:` for a refused
   calendar file. `Application started`, from `Microsoft.Hosting.Lifetime`, follows the host's
   start: a capture that has it but no capability line comes from a build below 5a.
5. **Record the build:**

   ```powershell
   $dll = Join-Path (Split-Path $exe) 'Meridian.Desktop.dll'
   (Get-Item $dll).VersionInfo.ProductVersion
   (Get-FileHash -Algorithm SHA256 $dll).Hash
   ```

6. **Close the desktop**, and open it from the browser workstation again as usual.

Keep the capture file in the change record under its generated name, with the executable path and
the product version and SHA-256 of `Meridian.Desktop.dll`. Started this way by the same Windows
user, the desktop reads the same configuration file, and so loads calendars from the same data
root, as when the browser workstation opens it; it lacks only the launch ticket, which picks its
opening page. If the executable cannot be started this way, for example on a desktop installed from
the separate MSIX package, record the desktop as the paragraph above describes.

### 3. Before 5b-2 deploys: the stored-data gates (each environment)

Each check is expected to find nothing: AQ1 and AQ2 report no hits, and Q1, Q1b and QR return no
rows. Record the result, the revision of this page the queries were copied from, the database and
schemas, the timestamp and the operator. QR guards the economic-replay refusal that 5b-1 adds, so
run it before 5b-1 deploys as well.

- **AQ1 and AQ2, again.** As in step 1.
- **Encoding check.** Run it first in the Security Master session. Q1, Q1b and QR trim from class
  names the whitespace .NET trims, Unicode spaces included, and their Unicode escapes match code
  points only in a UTF8 database, which nothing in Meridian enforces, so the block refuses any other
  encoding.

  ```sql
  do $encoding$
  begin
      if current_setting('server_encoding') <> 'UTF8' then
          raise exception 'Q1, Q1b and QR trim Unicode spaces only in a UTF8 database; this one is %',
              current_setting('server_encoding');
      end if;
  end
  $encoding$;
  ```

- **Q1, stored containers.** It counts Bond, CertificateOfDeposit and StructuredCredit rows that
  populate a top-level `scheduleConventions`, `callSchedule` or `putSchedule` at any stamp, or carry
  a non-canonical casing of those keys with any value, excluding StructuredCredit profile
  envelopes. A hit is a stray container: once the write versions rise, an amend that echoes it is
  refused, and a create or import that carries it without the stored row would declare it. The
  class filter matches what `SecurityAssetClassCatalog.GetOrDefault` resolves to these classes: the
  names and their aliases in any casing, with the surrounding whitespace .NET trims. Names and keys
  are folded under the C collation, as in AQ1. A value is populated, as
  `SecurityAssetTermsSchema.IsPopulatedTermValue` decides, when it holds a string, number or
  boolean at any depth, so `null`, `[]`, `[{}]` and `{"paymentCalendars": []}` are not. A profile
  envelope is what `SecurityTermsEnvelope.IsProfileGovernedPayload` decides: a `customProfileId`
  string that is not blank, or a `profileFields` object, both under that exact key. Replace
  `<deployment_schema>` with the Security Master schema.

  ```sql
  select k.asset_class, kv.key, count(distinct s.security_id) as securities
  from <deployment_schema>.securities s
  join (values ('bond', 'Bond'), ('corporatebond', 'Bond'), ('municipalbond', 'Bond'),
               ('certificateofdeposit', 'CertificateOfDeposit'),
               ('structuredcredit', 'StructuredCredit'), ('mortgagebacked', 'StructuredCredit'),
               ('mortgagebackedsecurity', 'StructuredCredit'), ('mbs', 'StructuredCredit'),
               ('assetbacked', 'StructuredCredit'), ('assetbackedsecurity', 'StructuredCredit'),
               ('abs', 'StructuredCredit')) k(name, asset_class)
    on k.name = lower(regexp_replace(s.asset_class collate "C", '^[\s\u0085\u00A0\u1680\u2000-\u200A\u2028\u2029\u202F\u205F\u3000]+|[\s\u0085\u00A0\u1680\u2000-\u200A\u2028\u2029\u202F\u205F\u3000]+$', '', 'g'))
  cross join lateral (select case when jsonb_typeof(s.asset_specific_terms) = 'object'
                                  then s.asset_specific_terms else '{}'::jsonb end) t(terms)
  cross join lateral jsonb_each(t.terms) kv
  where lower(kv.key collate "C") in ('scheduleconventions', 'callschedule', 'putschedule')
    and (kv.key collate "C" not in ('scheduleConventions', 'callSchedule', 'putSchedule')
         or jsonb_path_exists(kv.value, '$.** ? (@.type() != "null" && @.type() != "array" && @.type() != "object")'))
    and not (k.asset_class = 'StructuredCredit'
             and ((jsonb_typeof(t.terms->'customProfileId') = 'string'
                   and (t.terms->>'customProfileId') collate "C" ~ '[^\s\u0085\u00A0\u1680\u2000-\u200A\u2028\u2029\u202F\u205F\u3000]') is true
                  or (jsonb_typeof(t.terms->'profileFields') = 'object') is true))
  group by 1, 2
  order by 1, 2;
  ```

- **Q1b, swap leg blocks.** The same over the `scheduleConventions` of every Swap leg object, in
  every array under `legs`, `swapLegs` or `cashFlowLegs` in any casing, not only the one the
  mapping reads. A value under those keys that is not an array holds no legs.

  ```sql
  select legs.key as container, kv.key, count(distinct s.security_id) as securities
  from <deployment_schema>.securities s
  cross join lateral jsonb_each(case when jsonb_typeof(s.asset_specific_terms) = 'object'
                                     then s.asset_specific_terms else '{}'::jsonb end) legs
  cross join lateral jsonb_array_elements(case when jsonb_typeof(legs.value) = 'array'
                                               then legs.value else '[]'::jsonb end) leg(value)
  cross join lateral jsonb_each(case when jsonb_typeof(leg.value) = 'object'
                                     then leg.value else '{}'::jsonb end) kv
  where lower(regexp_replace(s.asset_class collate "C", '^[\s\u0085\u00A0\u1680\u2000-\u200A\u2028\u2029\u202F\u205F\u3000]+|[\s\u0085\u00A0\u1680\u2000-\u200A\u2028\u2029\u202F\u205F\u3000]+$', '', 'g')) = 'swap'
    and lower(legs.key collate "C") in ('legs', 'swaplegs', 'cashflowlegs')
    and lower(kv.key collate "C") = 'scheduleconventions'
    and (kv.key collate "C" <> 'scheduleConventions'
         or jsonb_path_exists(kv.value, '$.** ? (@.type() != "null" && @.type() != "array" && @.type() != "object")'))
  group by 1, 2
  order by 1, 2;
  ```

- **QR, economic-only replay.** It counts economic-only Bond, CertificateOfDeposit and
  StructuredCredit events whose economic document shows a declared schedule: a non-null accrual
  `businessDayConvention` or `holidayCalendar`, or a non-empty `call.callSchedule` or
  `call.putSchedule`. An event is economic-only when its payload has the economic-definition shape
  (`classification` and `economicTerms`) and no retained `legacyAssetSpecificTerms`. Its class is
  the payload's `legacyAssetClass`, or its `typeName` when that is blank, resolved as in Q1, and the
  payload keys are matched as the serializer writes them. A list counts as non-empty unless it is
  absent, `null` or `[]`.

  ```sql
  select k.asset_class, count(*) as events, count(distinct e.security_id) as securities
  from <deployment_schema>.security_events e
  cross join lateral (
      select e.payload->'economicTerms'->'accrual'->'businessDayConvention',
             e.payload->'economicTerms'->'accrual'->'holidayCalendar',
             e.payload->'economicTerms'->'call'->'callSchedule',
             e.payload->'economicTerms'->'call'->'putSchedule',
             case when jsonb_typeof(e.payload->'legacyAssetClass') = 'string'
                       and (e.payload->>'legacyAssetClass') collate "C" ~ '[^\s\u0085\u00A0\u1680\u2000-\u200A\u2028\u2029\u202F\u205F\u3000]'
                  then e.payload->>'legacyAssetClass'
                  else e.payload->>'typeName' end
  ) d(convention, calendar, calls, puts, class_name)
  join (values ('bond', 'Bond'), ('corporatebond', 'Bond'), ('municipalbond', 'Bond'),
               ('certificateofdeposit', 'CertificateOfDeposit'),
               ('structuredcredit', 'StructuredCredit'), ('mortgagebacked', 'StructuredCredit'),
               ('mortgagebackedsecurity', 'StructuredCredit'), ('mbs', 'StructuredCredit'),
               ('assetbacked', 'StructuredCredit'), ('assetbackedsecurity', 'StructuredCredit'),
               ('abs', 'StructuredCredit')) k(name, asset_class)
    on k.name = lower(regexp_replace(d.class_name collate "C", '^[\s\u0085\u00A0\u1680\u2000-\u200A\u2028\u2029\u202F\u205F\u3000]+|[\s\u0085\u00A0\u1680\u2000-\u200A\u2028\u2029\u202F\u205F\u3000]+$', '', 'g'))
  where jsonb_typeof(e.payload) = 'object'
    and e.payload ? 'classification' and e.payload ? 'economicTerms'
    and coalesce(jsonb_typeof(e.payload->'legacyAssetSpecificTerms'), 'null') = 'null'
    and (coalesce(jsonb_typeof(d.convention), 'null') <> 'null'
         or coalesce(jsonb_typeof(d.calendar), 'null') <> 'null'
         or case jsonb_typeof(d.calls) when 'array' then jsonb_array_length(d.calls) > 0
                                       when 'null' then false else d.calls is not null end
         or case jsonb_typeof(d.puts) when 'array' then jsonb_array_length(d.puts) > 0
                                      when 'null' then false else d.puts is not null end)
  group by 1
  order by 1;
  ```

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

- Step 1 and step 3: every check finds nothing in every environment, recorded.
- Step 2 and step 4: every node in the inventory has a recorded capability line that names
  `security-master-declared-schedule-v1`, and every node that serves projections lists the same
  calendars.
- No step is skipped for desktops. A gate with a missing node or a missing result is a hold.

## Failure and Recovery

| Symptom | Diagnostic | Safe next action |
| --- | --- | --- |
| AQ1 or AQ2 finds rows | The query output and the hit's source column | Hold the deploy; remediate as the 5a-0 resolution pass describes, then rerun |
| A node logs no capability line | Its build identity; whether the capture began before host start | Upgrade the node, or capture again from a restart; hold 5b-2 until its line is recorded |
| A desktop capture file is empty | Whether another desktop was still running: the new one hands its launch over and exits before its host is built | Close every desktop window and repeat [the capture](#capturing-a-desktops-lines) from action 1 |
| A node's line differs from the one its build should log (another declared mapping or other terms versions) | The capability line and the node's build | Align the node's build with the fleet; hold 5b-2 |
| Calendar lists differ between projection nodes | The capability lines; the per-calendar lines and Warnings | Align the files ([calendar procedure](business-day-calendar-files.md)), restart, and compare again |
| A calendar file is refused at start | The Warning naming the file and the reason | Fix the file as the calendar procedure describes; its id stays unresolvable until then |
| Q1, Q1b or QR returns rows | The query output: class, key or container, and counts | Hold 5b-2, and 5b-1 for QR; follow the remediation the 5b pull requests add here |
| A rollback below a floor is proposed | The floor table above | Do not roll back below it; roll forward or to an allowed build |

## Evidence and Handoff

The release owner keeps, per environment: the node inventory with each node's capability line and
timestamp (for a desktop, its capture file and the version and SHA-256 of `Meridian.Desktop.dll`),
the AQ1, AQ2, Q1, Q1b and QR results, the calendar parity comparison, the owner
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
