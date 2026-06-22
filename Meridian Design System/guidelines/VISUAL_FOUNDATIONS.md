# Meridian — Visual Foundations

Grounded in the desktop app `src/Meridian.Wpf/Styles/*.xaml`. The language is **"Institutional
Ops"**: a light paper workstation, hairline structure, one teal-blue accent, desaturated
semantics, no gradients or glow.

## Color

**Canvas & surfaces** (`ThemeTokens.xaml`)
- Window canvas `#ECEFF3` — the paper everything sits on
- Command bars / wells `#F5F7FA`
- Card / panel `#FFFFFF`; raised (metric tiles, inspector rails) `#FAFBFC`
- Row hover `#F1F4F7`; pressed / selected wash `#E6EEF5`
- Chrome (brand bar + status bar) near-black `#171A1F`, text `#F4F6F8`

**Borders** carry the structure — `#D7DCE2` default, `#B8C2CC` hover, `#AAB4BF` strong,
`#2F6F8F` focus.

**Text** — primary `#22272E`, secondary `#4D5967`, muted `#6E7781`, disabled `#9AA4AF`.

**Accent** — a single muted teal-blue `#2F6F8F` (primary buttons, focus rings, active nav,
crosshair). Pressed `#255B75`, vibrant `#3B82A6`. One accented action per screen.

**Semantic accents** are desaturated and always rendered as a **trio** — dim text · solid 1px
border · alpha-10 fill. Never solid fills.
- Success green `#16885F` (dim `#126C4D`)
- Danger red `#BA3F55` (dim `#983244`)
- Warning amber `#B7791F` (dim `#946216`)
- Pending purple `#6F5BA7` (dim `#58478A`)

**Environment modes** — always visible: Live red `#BA3F55` (real money), Paper blue `#2F6F8F`
(simulated), Fixture amber `#B7791F` (replay/recorded).

## Type (`ThemeTypography.xaml`)

- Display — Segoe UI Variable Display / Semibold, weight 600
- Body / UI — Segoe UI Variable Text, 13px / 20px line
- Data — Cascadia Mono / JetBrains Mono / Consolas, tabular-nums, for every price, id,
  timestamp, count

Ramp (px): page title 22 · section 15 · card title 14 · body 13 · **metric 24 (mono 600)** ·
data value 18 (mono 600) · label 10 (small-caps, muted). Labels use `AllSmallCaps`, so they
render as small-caps rather than ALL-CAPS.

## Elevation, radii, spacing (`ThemeSurfaces.xaml`)

- Radii: 4px chips/badges · 6px buttons/inputs · 8px cards/panels
- Accent bars: 3px metric-card left border; 4px tone-inspector / queue-card left border
- Shadows: **card** `0 1px 1px rgba(0,0,0,.08)`, **elevated** `0 1px 2px rgba(0,0,0,.10)` —
  that is the entire elevation system. Everything else is borders.
- Spacing rhythm: 24 section · 16 card · 12 compact · 8 tight
- Padding: card 20 · compact card 16 · metric 18
- Chrome: 48px brand bar · 28px status bar · 224px nav rail · ~34px dense row

## Interaction & motion

- Primary button: hover = accent @ 80% α, press = `--accent-dim` (no movement, no offset shadow)
- Ghost button: hover `#F1F4F7`, press `#E6EEF5`, border darkens to focus on press
- Nav item: active = `#E1EAF2` wash + **3px teal-blue left indicator**; hover `#E9EEF3`
- Table row: hover/selected = `#E6EEF5` + `inset 3px 0 0 #2F6F8F` left rail; zebra at `#FAFBFC`
- Input: hover darkens border; focus = teal-blue border + 2px ring; error = red border
- Motion: 100–150ms ease on color/background/border only. No springs, no entrance animation,
  no decorative loops. Live numbers update silently.

## Backgrounds

Flat solid surfaces. **No gradients, no textures, no photography, no glow.** The only
decorative asset is `assets/brand/meridian-hero.svg`. Depth is hairlines + a whisper of shadow.

## Charts

White plot `#FBFCFD`, surround `#FFFFFF`, grid `#DDE3EA`, axis text `#6E7781`, border `#CBD3DC`,
crosshair teal-blue. Series: equity green `#16885F`, drawdown red `#BA3F55`, primary line
teal-blue `#2F6F8F`, secondary/benchmark `#7A9DB3` (dashed), warning amber `#B7791F`.
