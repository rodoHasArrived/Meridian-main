# Inspiration Brief

This brief translates the uploaded reference images into Meridian-specific design guidance. The
local `uploads/` images are source material only and are not part of the tracked distributable
package; HTML previews must use tracked files under `assets/`. The references are inspiration only;
do not copy third-party branding, product names, navigation labels, or proprietary screen structure
into Meridian UI.

## Uploaded references used

| Reference | What it contributes |
|---|---|
| `uploads/ChatGPT Image Apr 24, 2026, 03_58_29 PM.png` | Target workstation mood: deep navy shell, monitor-like framing, compact masthead, persistent left rail, KPI row, chart/table pairing, cyan signal accents. |
| `uploads/pasted-1777329416322-0.png`, `uploads/pasted-1777386358669-0.png`, `uploads/pasted-1777387515947-0.png` | Custody and reporting density: horizontal toolbars, collapsible navigation, tabular status windows, selected-row detail panes, high row counts, narrow filter controls. |
| `uploads/pasted-1777385797260-0.png`, `uploads/pasted-1777385852742-0.png`, `uploads/pasted-1777385872213-0.png`, `uploads/pasted-1777385896784-0.png` | Product-guide walkthrough structure: annotated task flows, profile/pricing panels, left aggregate metrics, line-by-line detail tables, action confirmation steps. |
| `uploads/pasted-1777051293697-0.png` | Projection visual: fan/range scenarios with a forecast cut line and compact probability table. |
| `uploads/pasted-1777387641525-0.png`, `uploads/pasted-1777387675319-0.png` | Security master and reporting docs: master-detail identity panes, tabbed inspectors, canonical identifiers, report template grouping. |
| `uploads/Screenshot 2026-04-03 135932.png`, `uploads/Screenshot 2026-04-03 140237.png` | Financial SaaS counterpoints: entity search cards, chart workspace, analyst-facing left navigation, bright-data readability that Meridian should convert into dark cockpit structure. |

## Design deductions

1. **Use a masthead plus left rail for operator context.** The strongest references keep global
   identity, search, alerts, and tasks in a thin top bar while navigation stays persistent at left.
   Meridian should use this structure for workstation-scale pages instead of isolated cards.

2. **Pair summary metrics with inspectable evidence.** KPI rows are useful only when the table,
   chart, or detail pane underneath explains the number. Metric cards should sit above chart/table
   evidence, not replace it.

3. **Favor split workbenches for canonical records.** Security Master, custody positions, and
   report workflows all benefit from a list/detail split: searchable results on the left or top,
   canonical identifiers and next actions in the detail pane.

4. **Make filters and status controls compact.** The institutional references use narrow toolbars,
   segmented tabs, date controls, filter chips, and column controls. Meridian should keep those
   controls one line where possible and reserve large spacing for the workbench content.

5. **Use annotations as documentation, not production chrome.** Red pins and product-guide labels
   are useful for onboarding docs and screenshots, but the shipped product should express the same
   guidance through labels, empty states, banners, and clear action names.

6. **Keep Meridian visually distinct.** The references contain light grids, JPM/Goldman marks,
   orange Beta One accents, and legacy report panes. Meridian keeps the deep navy shell, cyan signal
   color, semantic state tokens, tighter radii, and line icons.

## Components this brief should influence

- `workstation-frame`: full-page shell with masthead, rail, and content surface.
- `operator-rail`: persistent workspace navigation with status metadata.
- `toolbar-strip`: dense one-line controls for filters, date scope, columns, export, refresh.
- `dense-table`: high-row-count grids with mono data, sticky headers, selected rows, and right
  aligned numeric columns.
- `split-workbench`: list/detail or chart/detail layout for canonical record inspection.
- `entity-summary`: canonical identifier blocks for Security Master, portfolio, custody, and
  reporting records.
- `annotation-callout`: documentation-only marker for product guides and screenshot walkthroughs.

## Copy guidance derived from references

- Prefer labels that name the workflow stage: `Import portfolio`, `Price bonds`, `Send to desk`,
  `Review exceptions`, `Validate replay`.
- Status text should say what changed and the current evidence: `4 pending trades`, `473 records`,
  `Last updated 19:19:17`, `Matched`, `Partial`, `Ready for review`.
- Avoid instructional clutter in production surfaces. Product-guide pages may say "Click Send to
  desk"; the application should show a button named `Send to desk` plus a confirmation state.
