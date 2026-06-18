# Meridian Brand Guidelines

This root guide is a compatibility entry point for the June 2026 design-system
bundle. The active package guidance lives in `README.md`, `styles.css`,
`tokens/`, `guidelines/`, and `assets/brand/`.

## Brand Position

Meridian is an institutional trading, market-data, research, accounting, and
reporting workstation. The brand language is light, precise, and operational:
a paper canvas, white work surfaces, near-black chrome bars, one muted
teal-blue accent, desaturated status color, hairline borders, and shallow
shadow.

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

- Canvas: `#ECEFF3`
- Command bars and inset wells: `#F5F7FA`
- Card and panel surface: `#FFFFFF`
- Chrome bars: `#171A1F`
- Primary accent: `#2F6F8F`
- Status colors: success `#16885F`, danger `#BA3F55`, warning `#B7791F`,
  pending `#6F5BA7`

Use `tokens/colors.css`, `tokens/typography.css`, `tokens/elevation.css`, and
`tokens/theme.css` rather than raw values in new package work.

Desktop parity uses Segoe UI Variable for display/body text and Cascadia Mono
for data. Browser and package fallbacks include JetBrains Mono and system UI
families.

## Usage Rules

- Keep one primary teal-blue action per screen.
- Use semantic colors as text, border, and alpha wash trios; do not use solid
  status fills for routine badges.
- Preserve the near-black brand/status bars when showing workstation chrome.
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
