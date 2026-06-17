# Design System Usage

**Status:** active  
**Owner:** core-team  
**Reviewed:** 2026-06-14

Use this as the design-system entry for UI styling and shared visual standards.

Current design source: `D:\Meridian-main\Meridian Design System`.

## Source of Truth

- Primary design tokens and shared components live in:
  - `Meridian Design System/`
- Canonical product framing remains in:
  - [`docs/product/meridian-design-document.md`](../product/meridian-design-document.md)

## UI Cleanup Rules

- Prefer shared tokens/components over hardcoded colors, spacing, and borders.
- Prefer existing workflow primitives over duplicate one-off screens.
- Keep layout and visibility rules in one shared component rather than copied per view.
- Keep semantic styling names (status, spacing, emphasis) consistent with the component API.
- Keep WPF and dashboard styles aligned at the surface level where shared interaction semantics overlap.

## Migration Note

If a page needs temporary styling to unblock behavior, keep it scoped and add a cleanup follow-up item in the backlog; avoid committing stable duplication.

## Related Docs

- [`docs/architecture/project-structure.md`](../architecture/project-structure.md)
- [`docs/architecture/module-map.md`](../architecture/module-map.md)
- [`docs/engineering/README.md`](../engineering/README.md)
