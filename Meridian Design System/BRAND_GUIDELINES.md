# Meridian Brand Guidelines

This root guide is a compatibility entry point for the June 2026 design-system
bundle. The active package guidance lives in `README.md`, `styles.css`,
`tokens/`, `guidelines/`, and `assets/brand/`.

## Brand Position

Meridian is an institutional trading, market-data, research, accounting, and
reporting workstation. The brand language is light, precise, and operational:
a paper canvas, plate work surfaces, graphite chrome bars and primary
actions, a blue interaction family, one terracotta signal, desaturated status
color, and hairline rules with flat panels.

The visual system is grounded in `src/Meridian.Wpf/Styles/*.xaml` and exported
for web consumers through the design-system token files.

## Assets

| Asset | Use |
| --- | --- |
| `assets/brand/meridian-mark.svg` | Primary mark for the workstation chrome and product identity |
| `assets/brand/meridian-mark-light.svg` | Light mark for near-black or colored surfaces |
| `assets/brand/meridian-mark-monochrome.svg` | Single-color contexts through `currentColor` |
| `assets/brand/meridian-symbol.svg` | Small-size symbol and favicon-style use |
| `assets/brand/meridian-wordmark.svg` | Horizontal wordmark |
| `assets/brand/meridian-wordmark-stacked.svg` | Narrow or vertical wordmark contexts |
| `assets/brand/meridian-tile.svg` | Scalable app tile |
| `assets/brand/meridian-tile-256.png` | Raster app tile |
| `assets/brand/meridian-hero.svg` | The only decorative hero/background asset in the package |

## Color And Type

Programmed Institutionalism, package 4.3.0 — paper canvas, graphite ink and chrome, a blue
interaction family, and one terracotta signal. Values are the light theme; the dark values are
in `tokens/colors-dark.css`.

- Canvas (paper): `#F2F0EB`
- Panel surface (plate): `#F8F7F3`; data wells `#FCFBF8`; headers and rails `#EBE8E1`
- Rules: `#D2CDC3` standard, `#AAA49A` strong
- Ink: `#20252B` primary, `#535C65` secondary, `#606870` muted
- Chrome bars and primary actions: graphite `#29323A`
- Signal: terracotta `#C56343` — brand, active identity markers, scenario series and report
  covers. Never a primary action, loss, error or focus colour. Signal-coloured text uses
  `--brand-signal-text`, not the raw signal.
- Interaction: focus `#2D6FB3`, selection `#496C8F` on `#E7EDF2`, links `#2A5F96`. Focus and
  selection are blue for every brand.
- Status colors: success `#2E7655`, danger `#B54B42`, warning `#986717`,
  information `#2F648A`, pending `#7A6B52`

The previous steel-blue identity is no longer a selectable brand: 4.3.0 removed the
`data-brand="steel"` preset. The white-label presets are indigo, emerald, rose, slate, cyan and
amber (`tokens/theme.css`); they replace the signal only, and an unknown `data-brand` value
renders the default signal.

Use `tokens/colors.css`, `tokens/typography.css`, `tokens/elevation.css`, and
`tokens/theme.css` rather than raw values in new package work.

Interface text is IBM Plex Sans and all data and numerics are IBM Plex Mono, tabular. Both are
self-hosted from `assets/fonts/` under the OFL, and the desktop client embeds the same two
families. Fallbacks are the system UI and monospace stacks.

## Usage Rules

- Keep one primary action per screen. It is graphite; the terracotta signal is never an action.
- Use semantic colors as text, border, and alpha wash trios; do not use solid
  status fills for routine badges.
- Preserve the graphite brand/status bars when showing workstation chrome.
- Keep marks proportional; do not rotate, skew, recolor with ad hoc palettes,
  add glow, or place marks on low-contrast backgrounds.
- Use line icons from `assets/icons/` for module identity and controls.
- Keep screenshots, previews, and templates on tracked package assets; do not
  depend on local-only upload paths.

## References

- `README.md` for the package overview and consumption model.
- `guidelines/VISUAL_FOUNDATIONS.md` for color, typography, radii, spacing,
  elevation, interaction, and chart rules.
- `guidelines/CONTENT_FUNDAMENTALS.md` for operator copy rules.
- `guidelines/ICONOGRAPHY.md` for the module icon set.
- `assets/brand/README.md` for asset inventory.
