# Meridian — Visual Foundations

Grounded in the desktop app `src/Meridian.Wpf/Styles/*.xaml`. The language is **"Institutional
Ops"**: a light paper workstation, hairline structure, one teal-blue accent, desaturated
semantics, no gradients or glow.

## Color

**Canvas & surfaces** (`ThemeTokens.xaml`)
- Window canvas `#DEE3EA` — the paper everything sits on
- Command bars / wells `#EBEFF4`
- Card / panel `#FFFFFF`; raised (metric tiles, inspector rails) `#F3F6F9`
- Row hover `#EAEEF3`; pressed / selected wash `#D7E5F1`
- Chrome (brand bar + status bar) near-black `#171A1F`, text `#F4F6F8`

**Borders** carry the structure — `#CBD3DC` default, `#ADB8C4` hover, `#99A5B2` strong,
`#2F6F8F` focus.

**Text** — primary `#22272E`, secondary `#4D5967`, muted `#59636F`, disabled `#889099`.

**Accent** — a single muted teal-blue `#2F6F8F` (primary buttons, focus rings, active nav,
crosshair). Pressed `#255B75`, vibrant `#3B82A6`. One accented action per screen.

**Semantic accents** are desaturated and always rendered as a **trio** — dim text · solid 1px
border · alpha-10 fill. Never solid fills.
- Success green `#16885F`
- Danger red `#BA3F55`
- Warning amber `#8A520E`
- Pending purple `#6F5BA7`

**Environment modes** — always visible: Live red `#BA3F55` (real money), Paper blue `#2F6F8F`
(simulated), Fixture amber `#8A520E` (replay/recorded).

## Type (`ThemeTypography.xaml`)

- Display — Segoe UI Variable Display / Semibold, weight 600
- Body / UI — Segoe UI Variable Text, 13px / 20px line
- Data — Cascadia Mono / JetBrains Mono / Consolas, tabular-nums, for every price, id,
  timestamp, count

Ramp (px): page title 24 · section 16 · card title 13 · body 13 · **metric 28 (mono 700)** ·
data value 16 (mono 600) · label 9 (small-caps, muted). Labels use `AllSmallCaps`, so they
render as small-caps rather than ALL-CAPS.

## Elevation, radii, spacing (`ThemeSurfaces.xaml`)

- Radii: unified **2px** across chips, controls, and cards/panels — one tight corner. Structure
  comes from borders, not rounding; the system never exceeds a 6px corner (large sheets only)
- Accent bars: 3px metric-card left border; 4px tone-inspector / queue-card left border
- Shadows: surfaces are **flat — no card shadow**. The only elevation is a tight hard-edged
  **menu** shadow `0 2px 6px rgba(0,0,0,.18)` on detached overlays. Everything else is borders.
- Spacing rhythm: 32 major · 24 section · 16 generous · 12 standard · 6 tight · 3 micro
- Padding: card 20 · compact card 16 · metric 18
- Chrome: 48px brand bar · 28px status bar · 224px nav rail · ~34px dense row

## Interaction & motion

- Primary button: hover = accent @ 80% α, press = `--accent-dim` (no movement, no offset shadow)
- Ghost button: hover `#EAEEF3`, press `#D7E5F1`, border darkens to focus on press
- Nav item: active = `#D7E5F1` wash + **3px teal-blue left indicator**; hover `#EAEEF3`
- Table row: hover/selected = `#D7E5F1` + `inset 3px 0 0 #2F6F8F` left rail; zebra at `#F3F6F9`
- Input: hover darkens border; focus = teal-blue border + 2px ring; error = red border
- Motion: 100–150ms ease on color/background/border only. No springs, no entrance animation,
  no decorative loops. Live numbers update silently.

## Backgrounds

Flat solid surfaces. **No gradients, no textures, no photography, no glow.** The only
decorative asset is `assets/brand/meridian-hero.svg`. Depth is carried entirely by visible,
load-bearing borders \u2014 surfaces are flat, with no card shadow.

## Charts

White plot `#FFFFFF`, surround `#FFFFFF`, grid `#D2D9E2`, axis text `#59636F`, border `#CBD3DC`,
crosshair teal-blue. Series: equity green `#16885F`, drawdown red `#BA3F55`, primary line
teal-blue `#2F6F8F`, secondary/benchmark `#6E8597` (dashed), warning amber `#8A520E`.
