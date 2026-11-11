# Meridian — Visual Foundations

Grounded in the desktop app `src/Meridian.Wpf/Styles/*.xaml`. The language is **"Programmed Institutionalism"**: a light paper workstation, hairline structure, one terracotta accent, desaturated
semantics, no gradients or glow.

## Color

**Canvas & surfaces** — the v2.1 material hierarchy
- **Paper** `#F2F0EB` — the application canvas everything sits on
- **Rail / recessed** `#EBE8E1` — command bands, nav rail, sticky headers, inset wells
- **Plate** `#F8F7F3` — the primary workspace surface (panels, cards)
- **Well** `#FCFBF8` — data surfaces and raised cells; data reads brightest
- **Overlay** `#FFFFFF` — dialogs, menus, popovers
- Row hover `#EDEFF0`; selected surface `#E7EDF2` (interaction blue, not the brand)
- Chrome (brand bar + status bar) graphite `#29323A`, text `#F1EFEA`

**Borders** carry the structure — `#D2CDC3` default, `#BEB8AD` hover, `#AAA49A` strong,
`#E2DED5` subtle divider. **Focus is blue** `#2D6FB3` — the system talking, not the brand.

**Text** — primary `#20252B`, secondary `#535C65`, muted `#606870`, disabled `#A4ABB2`.

**Signal** — a single terracotta `#C56343` (active identity marker, crosshair, scenario series,
report covers). Controlled variant `#CB6E4E`, pointer-emphasized variant `#D2795A`. Use one
purposeful signal occurrence per composition. Primary actions are graphite; terracotta is never a
loss, error, action, or focus color.

**Interaction family** — focus `#2D6FB3`, selection `#496C8F`, selected surface `#E7EDF2`,
interaction hover `#DDE5EC`. Selection and focus are structural feedback, so they stay blue in
every brand skin.

**Semantic accents** are desaturated and always rendered as a **trio** — dim text · solid 1px
border · alpha-10 fill. Never solid fills.
- Success green `#2E7655`
- Danger red `#B54B42`
- Warning amber `#986717`
- Pending purple `#2F648A`

**Environment modes** — always visible: Live red `#B54B42` (real money), Paper slate `#5D7286`
(simulated), Fixture amber `#986717` (replay/recorded).

## Type (`ThemeTypography.xaml`)

- Display — IBM Plex Sans / Semibold, weight 600
- Body / UI — IBM Plex Sans, 14px / 20px line
- Data — IBM Plex Mono / Consolas, tabular-nums, for every price, id,
  timestamp, count

Ramp (px): page title 24 · section 16 · card title 13 · body 13 · **metric 28 (mono 700)** ·
data value 16 (mono 600) · label 9 (small-caps, muted). Labels use `AllSmallCaps`, so they
render as small-caps rather than ALL-CAPS.

## Elevation, radii, spacing (`ThemeSurfaces.xaml`)

- Radii: **2 / 4 / 6** — 2px chips, 4px controls and panels, 6px large sheets; never above 8. Structure
  comes from rules, not rounding
- Accent bars: 3px metric-card left border; 4px tone-inspector / queue-card left border
- Shadows: surfaces are **flat — `--shadow-card: none`**. The only elevation is a tight hard-edged
  **menu** shadow `0 2px 8px rgba(17,23,29,.16)` on detached overlays. Everything else is borders.
- Spacing rhythm (4px base): 64 report · 48 macro · 32 workspace · 24 section · 16 generous · 12 standard · 8 tight · 4 micro
- Padding: card 20 · compact card 16 · metric 18
- Chrome: 48px brand bar · 28px status bar · 224px nav rail · 36px standard row (dense 28 · review 44)

## Interaction & motion

- Primary button: hover = accent @ 80% α, press = `--accent-dim` (no movement, no offset shadow)
- Ghost button: hover `#EDEFF0`, press `#E7EDF2`, border darkens to focus on press
- Nav item: active = `#E7EDF2` wash + **3px terracotta left indicator**; hover `#EDEFF0`
- Table row: hover/selected = `#E7EDF2` + `inset 3px 0 0 #496C8F` left rail; zebra at `#FCFBF8`
- Input: hover darkens border; focus = blue border + 2px ring; error = red border
- Motion: 120–300ms standard cubic on color/background/border only. No springs, no entrance animation,
  no decorative loops. Live numbers update silently.

## Backgrounds

Flat solid surfaces. **No gradients, no textures, no photography, no glow.** The only
decorative asset is `assets/brand/meridian-hero.svg`. Depth is carried entirely by visible,
load-bearing borders \u2014 surfaces are flat, with no card shadow.

## Charts

Panel-white plot, grid `#E2DED5`, axis text `#606870`, frame rules `#AAA49A`, crosshair terracotta.
**Data ink is navy `#2E4B6E`** (`--chart-primary`) — the accent never draws an observed series.
Time recession: Today navy → “1M ago” steel (`--chart-compare-1`) → “3M ago” gray
(`--chart-compare-2`). Hypothetical/shocked curves are **dashed terracotta** (`--chart-scenario`)
so a scenario can never be read as observed data. Realized/unrealized splits pair navy with light
gray (`--chart-realized` / `--chart-unrealized`). Signed values keep the independent semantics:
equity/up green `#2E7655`, drawdown/down red `#B54B42`, warning ochre `#986717`; benchmark
`--chart-secondary` gray, dashed.
