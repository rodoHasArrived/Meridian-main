# Design System Usage

**Status:** Active
**Owner:** Core Team
**Reviewed:** 2026-05-18

The Meridian Design System lives in `D:\Meridian-main\Meridian Design System`.
Treat it as the current design source of truth for browser workstation and
retained desktop compatibility work.

## Key Assets

| Path | Purpose |
| --- | --- |
| `Meridian Design System/README.md` | Design-system overview |
| `Meridian Design System/VISUAL_FOUNDATIONS.md` | Visual principles, spacing, type, and surfaces |
| `Meridian Design System/colors_and_type.css` | Token reference |
| `Meridian Design System/preview/` | Reference previews and component states |
| `Meridian Design System/assets/icons/` | Canonical Meridian icon assets |
| `Meridian Design System/scripts/check_design_system_governance.py` | Governance checker |

## Cleanup Rules

- Prefer shared dashboard primitives and design-system tokens over one-off
  screen-specific colors, spacing, card styles, and table styles.
- Keep semantic workflow states on the shared success, warning, danger, info, and muted contract.
  Browser evidence/readiness surfaces map `Ready` to success, review/stale posture to warning,
  blocker posture to danger, current/running posture to info, and secondary metadata to muted
  outline badges. WPF queue and inspector cards map `Success`/`Warning`/`Danger`/`Info` through
  shared tone resources. Screenshot/evidence automation must fail blank, low-entropy, stale,
  wrong-route, or manifest-orphan captures before they can become durable evidence.
- Keep workflow labels, disabled reasons, selected state, and accessibility
  labels in view models or shared read models where the screen architecture
  supports it.
- Do not redesign whole workflows during cleanup. Normalize obvious duplicated
  styling only when the intended shared pattern is already clear.
- Keep responsive browser validation in scope for the browser workstation; do
  not create mobile-specific product surfaces.
- Archive older one-off design notes under `archive/docs/` after their useful
  guidance has been folded into this page, the design-system assets, or the
  relevant workflow documentation.
