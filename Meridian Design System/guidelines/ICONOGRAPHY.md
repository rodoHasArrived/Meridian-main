# Meridian — Iconography

Meridian ships its **own line-icon module set** — 47 icons in `assets/icons/`, one per product
module (dashboard, security-master, backtest, charting, trading, order-book, data-quality,
provider-health, …).

## Drawing

- 24px grid, **1.5px strokes**, round caps/joins, no fills.
- `stroke="currentColor"` — icons inherit the color of their context (muted in a resting nav
  item, teal-blue when active, text-color in a button). Do not hard-code icon colors.
- Geometric and restrained — they read as instrument markings, not illustrations.

## Usage

- Nav rail and module identity at 16–20px.
- Buttons may take a leading icon at 14–16px.
- Keep one icon per nav item; never decorate body copy with icons.

## What not to do

- **No icon font, no emoji — ever.**
- Don't import a foreign icon set (Lucide, Heroicons, Material) wholesale. If you need a glyph
  the set doesn't cover, draw it to match (24px grid, 1.5px stroke, currentColor) or go
  text-only.
- Unicode glyphs are allowed only as functional marks inside data: `⌕` search, `Ctrl K`
  keyboard hint, `Δ` delta, `·` separator, `↑↓` sort arrows.

## Brand marks (`assets/brand/`)

- `meridian-mark.svg` — primary mark (use on the dark `#171A1F` chrome bar)
- `meridian-mark-light.svg` — light mark for dark/colored contexts
- `meridian-mark-monochrome.svg` — single-color contexts
- `meridian-wordmark.svg`, `meridian-wordmark-stacked.svg`
- `meridian-tile-256.png`, `meridian-symbol.svg`, `meridian-hero.svg`
