# Business-Day Calendar Files

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-10-28

## Purpose and Scope

Use this procedure to install, change and verify the business-day calendar files a node loads at
start. The Security Master projection resolves a record's declared `paymentCalendars` against them
([ADR-025](../adr/025-contractual-schedule-kernel.md#applications)); an id the node has not loaded
blocks that record's projection with a reason naming the id, and is never replaced by weekends
only. Success means every node that serves Security Master projections loads the same calendars,
with the same versions and hashes, and refuses no file.

No holiday data ships in the repository, and none is required: a node with no calendar files loads
only the built-in `WEEKENDS` calendar. Installing a real calendar waits on the governance
conditions below, in particular a named operations owner. This procedure does not cover sourcing
holiday data. The fleet-wide parity gate it feeds is in the
[terms contract rollout](security-master-terms-contract-rollout.md).

## Prerequisites

- The calendar data's operations owner, named in the change record before any real calendar file
  is installed (see [Governance](#governance)).
- The node's data root: the `DataRoot` of the configuration file the host starts with, or the
  legacy `Storage.BaseDirectory`, default `data`. A relative value resolves against the
  configuration file's directory, or that directory's parent when it is named `config`. A server
  uses it as its storage root and the WPF desktop reads it through its configuration store. The
  `MDC_DATA_ROOT` environment variable does not move it, including the value the lifecycle
  supervisor sets; to put the calendars under that directory, set `DataRoot` in the configuration
  file. The start-up line for each loaded file prints its path, which confirms the directory once a
  file loads.
- Write access to `<dataRoot>/calendars` and the ability to restart the host through its
  controller or [lifecycle supervisor](../reference/lifecycle-control-plane.md).

## Execution Context

- Platform: the host's own operating system; any shell.
- Working directory: `<dataRoot>/calendars` on the node. Create it if it does not exist.
- Logs: the host's log from start. A server writes these lines through Serilog to its console and
  to `<dataRoot>/_logs/meridian-<yyyyMMdd>.log`, under the same data root as `calendars`; a host
  the lifecycle supervisor starts keeps no console, so read the file. A host started with
  `MDC_LOG_LEVEL` at `Warning` or above writes only the Warnings. On a WPF desktop, `ILogger`
  output reaches only the generic host's default providers
  ([WPF README](../../src/Meridian.Wpf/README.md)), none of which keeps these Information lines
  without extra setup. Start the desktop with its console output kept in a file, as
  [Capturing a desktop's lines](security-master-terms-contract-rollout.md#capturing-a-desktops-lines)
  in the terms contract rollout describes, and keep that file. A desktop logs these lines only
  after sign-in, once its main window is shown.

## File Format

One calendar per file, named `{ID}.json`, directly under `<dataRoot>/calendars`. Subdirectories and
files whose names do not end in `.json` (in any case) are ignored. The example uses the fictitious
id `ZZTEST`; do not copy its dates into a real calendar.

```json
{ "calendarId": "ZZTEST", "version": 1, "weekendDays": ["Saturday","Sunday"],
  "coverage": { "from": "2026-01-01", "to": "2026-12-31" },
  "holidays": ["2026-01-01"] }
```

| Member | Rule |
| --- | --- |
| `calendarId` | An uppercase ASCII letter followed by 1 to 15 uppercase ASCII letters or digits (`^[A-Z][A-Z0-9]{1,15}$`), equal to the file name stem exactly: `ZZTEST.json` holds `ZZTEST`. `WEEKENDS` is reserved for the built-in calendar. A joint calendar is never a file: a record lists several ids in `paymentCalendars`, and the node joins them (`GBLO+USNY`). |
| `version` | An integer of at least 1, raised on every change to the file's content. |
| `weekendDays` | Exact English day names, `Monday` to `Sunday`. Numbers, abbreviations and other casings are refused, and at least one day must be a working day. |
| `coverage` | `from` and `to`, both required, as `yyyy-MM-dd`, with `from` on or before `to`. This is the data owner's attestation that `holidays` is complete over that range. |
| `holidays` | Dates as `yyyy-MM-dd`; `[]` when there are none. Order and duplicates do not matter. |

Every member is required, members are matched exactly in camelCase, and an unknown or repeated
member refuses the file, so a misspelt key cannot silently drop data. A date outside coverage still
gets an answer from the weekend mask and the listed holidays; a schedule that asks about one
reports it as a coverage gap in its evidence and logs it at Debug, without blocking.

## Procedure

1. **Prepare the file outside the directory.** Write it under another name or path, check it
   against the format above, and raise `version` above the version the node currently logs for that
   id. Any content change needs a new version: a holiday added or removed, a weekend change or a
   coverage change. The hash changes with any content change, but only a version bump tells nodes
   and the data owner which content is current; never ship changed content under an old version.
2. **Move it into place.** Move the finished file into `<dataRoot>/calendars` as `{ID}.json` in one
   step (a rename on the same volume), so the host never reads a partial file.
3. **Restart the host.** Files are read once at host start; there is no hot reload.
4. **Read the start-up lines.** For each loaded file the node logs, at Information:

   ```text
   Loaded business-day calendar ZZTEST version 1 hash <hash> coverage 2026-01-01 to 2026-12-31 from <dataRoot>/calendars/ZZTEST.json
   ```

   and then one capability line that ends with the full calendar list:

   ```text
   Security Master terms capability: declared mapping security-master-declared-schedule-v1, schedule kernel contractual-schedule-kernel-v1, terms versions ..., business-day calendars 1: ZZTEST v1 <hash>
   ```

   Any refused file is logged as a Warning instead (see the table below). In a server's log file
   each entry starts with its timestamp, level (`[INF]` or `[WRN]`), thread and category
   (`Meridian.Storage.Calendars.FileBusinessDayCalendarSource` for the calendar lines,
   `Meridian.Application.Composition.SecurityMasterTermsCapabilityHostedService` for the capability
   line). A `--mode desktop` server composes the storage feature in its UI server and in its
   collector, so it logs each line twice. To list the lines:

   ```bash
   grep -hi -e 'business-day calendar' -e 'Security Master terms capability:' <dataRoot>/_logs/meridian-*.log
   ```

   ```powershell
   Select-String -Path '<dataRoot>\_logs\meridian-*.log' -SimpleMatch 'business-day calendar', 'Security Master terms capability:' | ForEach-Object Line
   ```
5. **Verify the hash.** Compare the hash with the one the data owner recorded for this id and
   version, and with every other node that serves projections.
6. **Record the result** as described under Evidence and Handoff.

## Hash Verification

The hash is the calendar's content hash, a lowercase hexadecimal SHA-256 over a canonical rendering
of the id, version, sorted weekend days, sorted distinct holidays and coverage bounds. It is not a
hash of the file's bytes, so `sha256sum` of the file will not match it, and reformatting the file or
reordering its holidays or weekend days does not change it. The id is hashed as written, which is
one reason ids must be canonical.

To verify a calendar:

- The data owner loads the file on one reference node and records the hash its start-up line
  logs for that id and version.
- Every node that serves Security Master projections must log the same `ID v{version} {hash}`
  entries in its capability line, or `none` on every node. Two nodes that log one id and version
  with different hashes hold different data: one of them has a file that changed without a version
  bump.
- Calendar sets must be identical across those nodes; an empty set is allowed.

## Refusal Warnings and Fixes

A refused file is logged once at start, as a Warning in one of three forms:

```text
Refused business-day calendar file {path}: {reason} Nothing from this file is loaded; calendar {id} still resolves, from {loaded path}.
Refused business-day calendar file {path}: {reason} Nothing from this file is loaded; calendar WEEKENDS still resolves, to the built-in weekends-only calendar.
Refused business-day calendar file {path}: {reason} The calendar it names stays unresolvable; no weekends-only calendar is substituted.
```

Nothing from a refused file is loaded, and the other files still load. The form depends on the id
the refused file names: its `calendarId`, when it parsed with one, and then its file name stem,
each compared ignoring case.

- **Still resolves, from another file.** Another file that loaded defines that id, so records that
  name it use that file's calendar, not the refused file's content. Check that the loaded file is
  the one the data owner supplied, and fix or remove the refused one.
- **Still resolves, to the built-in calendar.** The id is `WEEKENDS`, which always resolves to the
  built-in calendar.
- **Stays unresolvable.** No file that loaded defines the id. A record that names it is blocked with
  "Declared paymentCalendars name calendars this node has not loaded: '{id}'; a missing calendar is
  never replaced by weekends only." until the file is fixed and the host restarted.

| Reason in the Warning | Fix |
| --- | --- |
| `the file is not a valid calendar document: {parser message}` | Malformed JSON, an unknown or repeated member, a value of the wrong kind (a number in `weekendDays`, a holiday or coverage date not written `yyyy-MM-dd`), or a document that is not an object. Correct the JSON. |
| `the file holds no calendar document.` | The file is `null`. Write the document. |
| `the file could not be read: {message}` | Fix the file's permissions or the disk error, then restart. |
| `required member(s) missing: {members}.` | Add every listed member (`coverage.from` and `coverage.to` are listed separately). |
| `version must be at least 1 (found {n}).` | Use a version of 1 or more, above the version the node last logged for that id. |
| `calendarId '{id}' is not a canonical calendar id (an uppercase ASCII letter followed by 1 to 15 uppercase ASCII letters or digits).` | Use a canonical id: no lowercase letters, separators or `+`, starting with a letter, 2 to 16 characters. |
| `calendarId '{id}' does not equal the file name stem '{stem}' exactly.` | Rename the file to `{calendarId}.json`, with the same case. |
| `calendarId '{id}' is reserved for the built-in weekends-only calendar.` | Remove the file; `WEEKENDS` always resolves to the built-in calendar. |
| `weekendDays entry '{name}' is not exactly one of the English day names Monday to Sunday.` | Spell each day in full with an initial capital, such as `Saturday`. |
| `every day of the week is a weekend day; a calendar needs at least one working day.` | Remove at least one day from `weekendDays`. |
| `coverage from {from} is after coverage to {to}.` | Correct the coverage bounds. |
| `the calendar data is invalid: {message}` | The calendar itself rejected the data; correct what the message names. |
| `calendar id '{id}' is defined by more than one file ({files}).` | Both files are refused. Only files that pass every other check count: a file refused for another reason is not a duplicate, and its Warning names the file the id still resolves from. Keep one file for the id (two files that pass can share an id only through the case of the `.json` extension on a case-sensitive file system). |

Two Warnings concern the directory itself, and both leave only `WEEKENDS` loaded. The first means
a file is in the directory's place, and the second that the directory could not be listed, for
example because the host's account may not read it:

```text
Business-day calendar path {path} is a file, not a directory; only the built-in WEEKENDS calendar is loaded.
Business-day calendar directory {path} could not be listed; only the built-in WEEKENDS calendar is loaded.
```

A missing directory logs nothing and loads only `WEEKENDS`.

## Governance

Decided by the owner on 2026-10-28:

- Ids are FpML business-centre codes in the canonical grammar above. Exchange codes are not used
  for payment calendars.
- Coverage is the data owner's attestation that the holiday list is complete over its range.
- Any content change ships under a higher version.
- A named operations owner maintains the calendar files. The owner is to be named before any real
  file is installed; until then, install none.
- Interim retention: no calendar evidence needs retaining while declared projections are not
  persisted and cannot post, because the ledger gate refuses them. A discounted-cash-flow valuation
  of a declaring record, also computed on request, names each calendar's id, purpose and content
  hash among its inputs. Lifting the ledger gate needs a retention decision, together with journal
  identity keyed on schedule period identity.
- A diagnostics endpoint listing each node's calendars stays optional; the start-up lines are the
  verification.

## Expected Results

- Each installed file produces one `Loaded business-day calendar` line (two on a `--mode desktop`
  server, one per graph) and no Warning.
- The capability line lists every installed calendar with its version and hash, and the list is
  identical on every node that serves Security Master projections.

## Failure and Recovery

| Symptom | Diagnostic | Safe next action |
| --- | --- | --- |
| A file is refused | The Warning's reason and form | Fix the file per the table above, move it into place again and restart; when the Warning says the id still resolves from another file, confirm that file is the data owner's |
| A file is installed but no `Loaded business-day calendar` line or Warning appears, and the capability line reads `business-day calendars 0: none` | The directory the node reads: `calendars` under the configuration file's `DataRoot`, which `MDC_DATA_ROOT` does not move | Move the file under that directory, or set `DataRoot` in the configuration file, then restart |
| A projection is blocked naming an id | The node's capability line: is the id listed? | Install or fix the calendar, or correct the record's `paymentCalendars`; nothing falls back to weekends only |
| Hashes differ between nodes for one id and version | The `Loaded business-day calendar` line on each node | Replace the divergent file with the data owner's file under a new version on every node, then restart and compare again |
| A calendar was changed in error | The previous file and its version | Reinstall the previous content under a new, higher version on every node; never reuse a version for different content |

Removing a file and restarting makes its id unresolvable again: records that name it are blocked,
and nothing else changes, because declared projections are not persisted and cannot post.

## Evidence and Handoff

Record in the change record, per node: the file name, id, version and hash, the
`Loaded business-day calendar` line and the capability line with their timestamps, any Warning,
and the parity comparison across nodes. Name the operations owner who supplied the data and the
reference hash. Do not record credentials or unrelated configuration.

## Related References

- [Business-Day Calendar](../domain/business-day-calendar.md#file-backed-source)
- [Security Master terms contract rollout](security-master-terms-contract-rollout.md)
- [ADR-025 applications](../adr/025-contractual-schedule-kernel.md#applications)
- [File-backed source](../../src/Meridian.Storage/Calendars/FileBusinessDayCalendarSource.cs)
- [Calendar file document and JSON context](../../src/Meridian.Storage/Calendars/BusinessDayCalendarFileDocument.cs)
