# Archived Design Folder

## Migration decision

The files from `docs/design/` were source-material for historical design-system usage notes.
They were moved during migration to avoid duplicate design documentation in active docs lanes.
The active design-system source remains `Meridian Design System/` plus the WPF and browser
workstation token/component implementations.

## Archived files

- [design-system-readme-dark-cockpit-legacy.md](design-system-readme-dark-cockpit-legacy.md)
- [design-system-usage.md](design-system-usage.md)
- [meridian-design-document.md](meridian-design-document.md)

## Replacement guidance

- Canonical design-system content resides in `Meridian Design System/`.
- WPF token and style usage is governed by `src/Meridian.Wpf/Styles/`.
- Browser workstation primitives live under `src/Meridian.Ui/dashboard/src/components/`.
- For new design/product references, use [docs/product](../../../docs/product/README.md) and [docs/reference](../../../docs/reference/README.md).
