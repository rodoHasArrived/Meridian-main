# Meridian — Content Fundamentals

The product is an operator workstation for traders and data engineers. Copy is written the way
a careful operator speaks: terse, factual, evidence-first. No marketing, no hand-holding.

## Voice

- **Evidence-first.** State the fact, then the proof: "Backfill complete · 412,008 bars · 0 gaps".
- **Declarative & imperative.** Actions are verb-first commands: "Run backfill", "Close
  position", "Halt session". Status is a plain statement, not a sentence about feelings.
- **No fluff.** No exclamation points, no adjectives like "powerful" or "seamless", no emoji,
  no anthropomorphizing ("Oops!", "We're sorry"). The system reports; it does not apologize.

## Casing

- Sentence case for UI copy, buttons, and titles ("Run backfill", "Security Master").
- **Small-caps** for eyebrow labels, table headers, and badges — the desktop sets
  `Typography.Capitals=AllSmallCaps`, so "net liquidation" renders as small-caps, not ALL-CAPS.

## Person

- Imperative verb-first for actions; avoid "I"/"we".
- "You" only in empty states and destructive confirmations ("You have no open positions",
  "This will cancel 4 working orders").

## Numbers & time

- All data is mono and tabular. Prices to fixed decimals (4dp for equities), explicit signs on
  deltas (`+1.84%`, `-921.00`).
- Counts use thin separators: `412,008`.
- Timestamps are UTC with a trailing `Z`: `2026-06-09 20:00:00Z`. Relative times are explicit:
  "00:00:04 ago".

## Status & errors

Name the system, the time, and the evidence:
- "Provider offline · Polygon last seen 14:02:11Z"
- "Gap scan complete · 0 gaps · 13:50:00Z"
- "Order rejected · exceeds position limit (200)"

Severity maps to the semantic trio (success / warning / danger / info), never to tone of voice.

## Environment language

Always name the environment: **Live**, **Paper**, **Fixture**. Destructive language escalates
only in Live ("This sends a real order to IBKR").
