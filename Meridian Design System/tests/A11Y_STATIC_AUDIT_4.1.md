# Accessibility — static audit of the 4.1 surfaces

**Date:** 2026-08-15 · **Method:** source inspection against the `guidelines/ACCESSIBILITY.md`
matrices · **Executed by:** design-system author, no browser and no assistive technology available.

## What this document is, and is not

This is a **static** audit: roles, accessible names, tab stops, focus handling and print behaviour
read out of the component source and compared with the house matrices. It is the part of the deferred
F-4 work that can be done honestly in this environment.

It is **not** the assistive-technology run. `tests/BROWSER_TEST_PLAN.md` remains *prepared, not
executed*: nothing here may be quoted as an NVDA, JAWS or VoiceOver result, and no conformance claim
in `guidelines/ACCESSIBILITY.md` is upgraded by it. Three classes of defect are invisible to a static
pass and are the reason the run still matters:

1. **Announcement order and verbosity** — a composed name can be correct and still unusable.
2. **Live-region timing** — whether `role="status"` fires once, twice, or is swallowed.
3. **Focus after DOM removal** — collapsing a region or closing a panel while focus is inside it.

## Surfaces audited

`WorkspaceLayout` (provider, bar, grid, region) · `ObjectTrail` (strip, list) · `HandoverBrief`
(panel, chip) · `AdapterDiagnostics` (panel, posture chip). Four modules, sixteen exports.

## Defects found and fixed in this pass

| # | Surface | Defect | Fix |
|---|---|---|---|
| 1 | `AdapterDiagnostics` row | The column strip is `aria-hidden`, so a row announced as *"Retrying IBKR order adapter 6m 12s ago 412ms 2 / 14 14:33:12Z"* — six numbers with no meaning attached. | Each row now carries a composed `aria-label` that names every figure (*"p99 412 milliseconds against a 250 millisecond budget, over budget"*), including the overdue-key and no-schedule states. |
| 2 | `AdapterDiagnostics` row | `aria-expanded` with no `aria-controls`; the detail block was an unnamed `div`. | `aria-controls` → `id` on the detail, which is now a `role="group"` named for its adapter. |
| 3 | `ObjectTrail` recents | `aria-haspopup="true"` promised a menu; the panel is a disclosure group, so menu key expectations (arrows, Home/End) were never met. | `aria-haspopup` removed, `aria-controls` added, and the trigger names itself (*"Recent objects, 3 in this session"*). |
| 4 | `WorkspaceLayout` region | Collapse and expand controls had `aria-expanded` with nothing to control. | `aria-controls` on both, `id` on the region body. |
| 5 | `WorkspaceLayout` divider | `aria-valuenow` alone reads as a bare number ("58") with no unit or referent. | `aria-valuetext`: *"58% Adapters, 42% Evidence"*. |
| 6 | `WorkspaceLayout` bar | *Save* and *Reset* were unnamed beyond their visible words — ambiguous out of context, and there are two of them per screen once a region is popped out. | `aria-label` naming the layout: *"Reset layout Incident review to its saved arrangement"*. |
| 7 | `WorkspaceLayout` bar | A hidden region was reported as static text (*"1 region hidden"*) with no keyboard route back — the law's rule 5 (never lose a pane) was violated by the `hidden` path even though `collapsed` honoured it. | The note became a **button** — *Show N hidden* — naming the regions it restores. |
| 8 | `WorkspaceLayout` bar | Dirty state was a visual chip only; a keyboard user who resized by arrow key got no confirmation that anything had changed. | `role="status"` on *Modified*. |
| 9 | `WorkspaceLayout` bar | Dead `pendingName` state left from the naming flow. | Removed. |

## Verified as already conformant

- **Every interactive control has a visible focus ring** through `--focus-ring`; no `outline:none`
  without a replacement in any of the four modules.
- **One tab stop per divider**, `role="separator"`, `aria-orientation` set per grid direction,
  `ArrowLeft/Right` (row) and `ArrowUp/Down` (column) resize by 4% with `minSize` floors respected.
- **Collapse never removes a region from the tab order** — it becomes a labelled, focusable rail.
- **Compound rows are single buttons**, not nested interactives; `HandoverBrief` renders a `div` row
  with its own action button when an item carries one, so no button nests inside a button.
- **Version-moved notices** are `role="status"`, not alerts: a navigation notice is not an
  interruption.
- **`unconfigured` and every other posture carry a text label**, never colour alone (1.4.1).
- **Print** drops controls and keeps evidence in all four modules; the trail hides entirely.
- **Chips and badges** take their meaning from `SeverityBadge`, which is already in the audited set.

## Open — cannot be closed without a browser and AT

| Check | Surface | Why it needs the run |
|---|---|---|
| Announcement order of the composed row name | `AdapterDiagnostics` | A 40-word name may be correct and still too long to use; only listening tells. |
| Focus after collapse | `WorkspaceLayout` | Collapsing a region whose descendant holds focus — the rail should receive it. Not verifiable statically. |
| Focus after the detail closes | `AdapterDiagnostics` | Focus should stay on the row button; React keeps it, but AT focus can drift. |
| Live-region behaviour of *Modified* | `WorkspaceLayout` bar | Whether arrow-key resizing announces once or on every 4% step. |
| Trail panel dismissal | `ObjectTrail` | `Escape` closes via a document listener; whether AT users can discover the disclosure at all. |
| Handover brief at 1280 × 900, terminal density | `HandoverBrief` | Three-column row grid reflow; 44px review-row floor under the densest scope. |
| Dark theme contrast of the new severity washes | all four | `scripts/check_contrast.py` covers tokens, not these compositions. |

## Recommendation

Run the matrix in `tests/BROWSER_TEST_PLAN.md` §4.1 before any release claims 4.1 conformance. Until
then, the honest statement is the one this system has always made: *prepared, not executed*.
