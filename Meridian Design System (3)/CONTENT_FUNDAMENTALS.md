# Content Fundamentals

Meridian is a tool for professional operators — quants, traders, data engineers, fund admins. The copy reflects that: it's precise, observational, and never markets itself.

## Voice

**Operator‑to‑operator.** Meridian assumes you already know what a backfill, a paper session, a drawdown, and a replay verification are. It does not define them. It describes what it's doing, what state it's in, and what changed.

Good:
- "Working and partially filled orders remain visible in real time so you can cancel, replace, or monitor fill progress without leaving the cockpit."
- "Paper thresholds, drawdown limits, and buying‑power constraints are evaluated on every order submission and displayed here for operator review."
- "System health, symbol subscription state, and per-symbol data quality stay visible across Trading, Portfolio, Accounting, Reporting, Strategy, Data, and Settings."

Avoid:
- Empty enthusiasm: "Meridian makes trading easy!" — no.
- Consumer onboarding: "Let's get started 🚀" — no.
- Hand‑holding: "Click here to begin" — the operator knows.

## Tone

- **Matter‑of‑fact.** State conditions, don't editorialize. "Paper risk cockpit" not "Monitor your risk like a pro."
- **Technical precision.** Name the exact thing: `RunPortfolio`, `SecurityMaster`, `PaperSessionReplayVerification`, `Wave 3 Delivery`. These are internal terms of art and using them signals competence.
- **System‑perspective, not marketing‑perspective.** The product reports; it doesn't persuade.

## Casing

- **Sentence case** for everything normal: titles, button labels, menu items, descriptions.
  - "New order", "Cancel all", "Create session", "Verify replay"
- **Title Case** for nav labels and workspace names.
  - "Trading", "Portfolio", "Accounting", "Reporting", "Strategy", "Data", "Settings"
- **ALL CAPS** — reserved, with wide tracking (`0.14em`–`0.24em`) — for:
  - Eyebrow labels ("TRADING LANE", "ROUTE CONTEXT", "BROKERAGE WIRING")
  - Environment badges ("PAPER", "LIVE", "RESEARCH")
  - Table column headers
- **camelCase / PascalCase** preserved when referring to identifiers, API types, or domain terms (`orderId`, `SessionInfo`, `PaperSessionDetail`).

## Pronouns

- **Third‑person system description** for what Meridian is doing: "*Live exposure, marks, and unrealized P&L are refreshed from the live execution layer each time the workspace loads.*"
- **Second person ("you")** sparingly, when addressing an operator action: "*…so you can act on fill progress without context‑switching.*"
- **Never** first‑person ("we believe", "our platform"). Meridian is a tool, not a company speaking.

## Numbers & identifiers

- **Tabular** (monospace): every currency value, count, percentage, timestamp, order ID, session ID, symbol, ISIN.
- **Signed where direction matters**: `+$0`, `+100K`, `−$2.4K`, `0%`.
- **Unit at the end**, not scattered: "12ms latency", "$1.2M exposure".
- **Placeholders** use the em dash `—` when a value is unavailable, not "N/A" or empty.
- **Separators** between metadata fields: `·` (middle dot) surrounded by spaces. Example: `api-gateway · 14:32:07`.

## Punctuation quirks

- **Em dash, not hyphen**, when breaking a thought: "Paper thresholds — and drawdown limits — are evaluated every order."
- **Ellipsis** (`…`) for async/in‑progress labels: "Submitting…", "Refreshing…", "Loading activity feed…".
- **Trailing punctuation omitted** on table cells, badges, and metric labels.

## Microcopy patterns

| Context | Pattern | Example |
|---|---|---|
| Empty state | "No X. Action phrase." | "No paper sessions active. Create one above to start tracking execution." |
| Loading | Verb + ellipsis | "Booting workstation shell", "Loading activity feed…" |
| Error | "X failed." + reason | "Order submission failed.", "Evaluation failed." |
| Success | Past tense fact + ID | "Order submitted — ord‑a8f2c", "Promoted. Promotion ID: prm‑9341." |
| Confirm | Imperative verb | "Cancel all", "Close position", "Verify replay" |
| Toolbar status | Count + state + scope | "473 records · Securities · 26 Apr 26" |
| Selected row detail | Entity + action evidence | "T326117E183 · Pending · Settles 29 Apr 26" |
| Product-guide callout | Action label + outcome | "Send to desk opens the quote request review." |

## Image-inspired workflow copy

The uploaded product-guide and custody references show useful operator patterns, but production
Meridian copy should stay terse:

- Use workflow stage labels such as "Import portfolio", "Price bonds", "Review exceptions",
  "Verify replay", and "Send to desk".
- Show evidence near each action: row counts, timestamps, session IDs, owner, status, and next
  action.
- In docs or walkthrough previews, callouts may say "Click Send to desk". In the application,
  the button should simply be named "Send to desk" and the resulting state should explain what
  happened.
- Avoid long explanatory sidebars inside the workstation. Use compact banners, row details, and
  selected-record inspectors.

## Never

- Emoji in product copy. (The codebase migrated away from them — the README for `Icons/` literally lists which emoji each SVG replaces.)
- Exclamation marks. Ever.
- Marketing phrases ("blazingly fast", "powerful", "seamless", "effortless", "best‑in‑class").
- "AI", "intelligent", "smart" as adjectives for features.
- Hashtags, trailing sparkles, announcement banners.

## Signature phrases (real, from the codebase — safe to reuse)

- "Operator Workstation"
- "Workflow‑centric shell"
- "Cockpit"
- "Drill‑in", "Shared run drill‑ins"
- "Paper session", "Live session", "Replay verification"
- "Ledger", "Security master", "Reconciliation"
- "Provider readiness", "Wave N delivery"
