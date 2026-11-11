# HandoverBrief

The returning operator's first surface. Track A of `guidelines/SESSION_CONTINUITY.md`.

```jsx
<HandoverBrief since="09:12" elapsed="4h 18m" actor="a.patel" owned={14}
  items={items} onOpen={openObject} onAcknowledge={moveMark}
  footNote="Mark stored per operator. Acknowledging moves it; it does not approve anything." />
```

## Rules

- **The window is explicit.** `since` + `elapsed`. Anchor it to the stored mark, never to "now minus
  something" — two consecutive opens must agree about what *since* means.
- **Four kinds, fixed order:** `breached` → `assigned` → `changed` → `resolved`. Severity sorts rows
  inside a kind. Do not reorder to put good news first.
- **Ownership is the filter.** Pass only objects the operator owns or watches, and pass `owned` so
  the brief can say *nothing moved on your 14 objects*. A brief that reports everything is a firehose.
- **Every row: statement, clock, actor.** `statement` is one sentence naming what happened and why it
  matters — not a log line. `LogTail` is for log lines.
- **Acknowledge moves the mark only.** Never wire it to an approval, an attestation, or a bulk action.
- **Empty is a statement.** The default empty render names the window and the object count; a blank
  panel reads as a failed load.
- `HandoverBriefChip` in `WorkstationTopbar` opens it; `NotificationCenter` remains the standing
  inbox for alerts. The brief is bounded by a session, an inbox is not.

## Composition

The arrival screen (`session-start`) renders it full width beside `ObjectTrailList`. An operational
screen puts it in a `LayoutRegion` that starts collapsed, so it is retrievable without occupying the
wall. Prints without the acknowledge and action controls.
