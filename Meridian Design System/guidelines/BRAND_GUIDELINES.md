# Meridian Brand Guidelines

This root guide is a compatibility entry point for the June 2026 design-system
bundle. The active package guidance lives in `readme.md`, `styles.css`,
`tokens/`, `guidelines/`, and `assets/brand/`.

## Brand Position

Meridian is an institutional trading, market-data, research, accounting, and
reporting workstation by Meridian Systems Inc. The brand language is
**Programmed Institutionalism v1.0**: calm, precise, quietly intelligent — a
warm paper canvas, architect-white work surfaces, graphite chrome bars, one
terracotta accent, desaturated independent status color, fine 1px rules, and
almost no elevation. Motto: **Measure · Understand · Converge**.

The signature mark is the **Meridian Field** — one vertical axis, repeated
dots, radial geometry, a single terracotta dot. It represents observation,
navigation, measurement, and convergence. Use it on reports, login,
onboarding, empty states, and splash — never behind tables. Identity source:
`assets/brand/identity-sheet.png`.

## Assets

| Asset | Use |
| --- | --- |
| `assets/brand/meridian-mark.svg` | Primary mark for the workstation chrome and product identity |
| `assets/brand/meridian-mark-light.svg` | Light mark for near-black or colored surfaces |
| `assets/brand/meridian-mark-monochrome.svg` | Single-color contexts through `currentColor` |
| `assets/brand/meridian-wordmark.svg` | Horizontal wordmark |
| `assets/brand/meridian-wordmark-stacked.svg` | Narrow or vertical wordmark contexts |
| `assets/brand/meridian-tile.svg` | Scalable app tile |
| `assets/brand/meridian-tile-256.png` | Raster app tile |
| `assets/brand/meridian-hero.svg` | The only decorative hero/background asset in the package |

## Color And Type

- Canvas (Paper): `#F2F0EB`
- Command bars, nav rail and inset wells (Recessed): `#EBE8E1`
- Card and panel surface (Plate): `#F8F7F3`
- Data surfaces and raised cells (Well): `#FCFBF8`
- Chrome bars (Graphite): `#29323A`
- Brand/scenario signal (Terracotta): `#C56343` — never for actions, focus, or positive/negative values
- Status colors: positive (forest) `#2E7655`, negative (brick)
  `#B54B42`, warning (amber ochre) `#986717`, information (blue) `#2F648A`
- Interaction (never brand): focus `#2D6FB3`, selection `#496C8F`, selected surface `#E7EDF2`
- Identity swatches (brand sheet): Paper `#F2F0EB` · Plate `#F8F7F3` · Slate
  `#AAA49A` · Graphite `#29323A` · Terracotta `#C56343` · Field slate `#5D7286`

Use `tokens/colors.css`, `tokens/typography.css`, `tokens/elevation.css`, and
`tokens/theme.css` rather than raw values in new package work.

Interface type is IBM Plex Sans (modern grotesk); ALL data is IBM Plex Mono
with tabular numerals — prices never visually jump. The identity wordmark is
letterspaced (tracking ≈ +140); rendered in IBM Plex Sans until the original
wordmark face is supplied.

## Usage Rules

- Use graphite for primary actions; reserve terracotta for one purposeful brand or scenario signal per composition.
- Use semantic colors as text, border, and alpha wash trios; do not use solid
  status fills for routine badges.
- Preserve the near-black brand/status bars when showing workstation chrome.
- Keep marks proportional; do not rotate, skew, recolor with ad hoc palettes,
  add glow, or place marks on low-contrast backgrounds.
- Use line icons from `assets/icons/` for module identity and controls.
- Keep screenshots, previews, and templates on tracked package assets; do not
  depend on local-only upload paths.

## References

- `readme.md` for the package overview and consumption model.
- `guidelines/VISUAL_FOUNDATIONS.md` for color, typography, radii, spacing,
  elevation, interaction, and chart rules.
- `guidelines/CONTENT_FUNDAMENTALS.md` for operator copy rules.
- `guidelines/ICONOGRAPHY.md` for the module icon set.
- `assets/brand/README.md` for asset inventory.
