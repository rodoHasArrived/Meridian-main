# Meridian — Visual Foundations

Package 4.3.0. The language is **"Programmed Institutionalism"**: a paper workstation, hairline
structure, graphite ink and chrome, a blue interaction family, one terracotta signal, desaturated
semantics, no gradients or glow. The values below are the light theme in `tokens/colors.css`; the
desktop client (`src/Meridian.Wpf/Styles/*.xaml`) carries the same palette.

## Color

**Canvas & surfaces** — the material hierarchy
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
report covers). Controlled variant `#CB6E4E`, pointer-emphasized variant `#D2795A`, ghost wash
`#F6E6DF`. Use one purposeful signal occurrence per composition. Terracotta is never a primary
action, loss, error, or focus color. At 3.74:1 on the plate it is a borders-and-icons color, not
a text ink: signal-colored text uses `--brand-signal-text`.

**Primary action** — graphite `#29323A` (`--brand-axis`) under paper ink `#F5F3EE`. One primary
action per screen.

**Interaction family** — focus `#2D6FB3`, selection `#496C8F`, selected surface `#E7EDF2`,
interaction hover `#DDE5EC`, link `#2A5F96`. Selection and focus are structural feedback, so
they stay blue in every brand skin; a `data-brand` preset replaces the signal only.

**Semantic accents** are desaturated, independent of the signal, and always rendered as a
**trio** — dim text · solid 1px border · alpha-10 fill. Never solid fills for routine status.
- Success green `#2E7655`
- Danger red `#B54B42`
- Warning amber ochre `#986717`
- Information blue `#2F648A`
- Pending taupe `#7A6B52`

**Environment modes** — always visible: Live red `#B54B42` (real money), Paper slate `#5D7286`
(simulated), Fixture amber `#986717` (replay/recorded).

**Dark mode** is material, not luminous: blue-black canvas `#11171D`, plate `#171E25`,
paper ink `#F1EFEA`, focus `#5C9FE5`, and a lifted terracotta `#D88466` carrying dark ink
`#14100E`. No glowing grids, no saturated gradients, no glass.

## Type (`tokens/typography.css`)

- Display and body / UI — IBM Plex Sans; body 14px / 20px line
- Data — IBM Plex Mono, slashed-zero tabular numerals, for every price, id, timestamp, count
- Both families are self-hosted from `assets/fonts/`; the desktop client embeds the same two

Ramp (px): editorial 48 (report covers only) · workspace 28 · page title 22 · data-medium 18 ·
section 16 · card title 14 · body 14 (dense 13 · review 15) · **metric 28 (mono, bold)** ·
data value 16 (mono, semibold) · caption 12 · label 11 · micro-label 10 (small-caps, muted).

## Elevation, radii, spacing (`tokens/elevation.css`)

- Radii: **2 / 4 / 6** — 2px chips and data plates, 4px controls and panels (`--radius-card`),
  6px overlays; dialogs 8px, the ceiling. Structure comes from rules, not rounding.
- Shadows: surfaces are **flat — `--shadow-card: none`**. The only elevation is a tight,
  hard-edged **menu** shadow `0 2px 8px rgba(17,23,29,.16)` on detached overlays that genuinely
  float above the plane. Everything else is borders.
- Spacing rhythm (4px base): 64 report · 48 macro · 32 workspace · 24 section · 16 generous ·
  12 standard · 8 tight · 4 micro
- Panel padding tracks density: 16 standard, 12 tight
- Chrome: 44px brand bar · 28px status bar · 224px nav rail · 36px standard row
  (compact 28 · spacious 44; the review row is 44 at standard density)

## Interaction & motion

- Primary button: graphite; hover and press mix the fill 88% and 78% toward the plate (no
  movement, no offset shadow). The signal is never a button fill.
- Ghost button: plate fill; hover `#EDEFF0`, press `#E7EDF2`, border firms on hover and press
- Nav item: active = `#E7EDF2` wash + terracotta identity indicator; hover `#EDEFF0`
- Selection: `#E7EDF2` surface with the `#496C8F` selection mark — blue, never terracotta
- Input: hover darkens border; focus = blue border + 2px blue ring; error = red border
- Motion: 80–300ms on the standard cubic `cubic-bezier(0.2, 0, 0, 1)`, on color, background and
  border. No springs, no entrance animation, no decorative loops. Live numbers update silently.

## Backgrounds

Flat solid surfaces. **No gradients, no textures, no photography, no glow.** The only
decorative asset is `assets/brand/meridian-hero.svg`. Depth is carried entirely by visible,
load-bearing borders — surfaces are flat, with no card shadow.

## Charts

Plate plot `#F8F7F3`, grid `#E2DED5`, axis text `#606870`, frame rules `#AAA49A`, crosshair
terracotta. Color is assigned by series role (`tokens/visualization.css`), never per chart.
**The observed series is graphite ink `#29323A`** (`--chart-primary`, paper in dark) — the signal
never draws observed data. Prior period is slate `#7890A4` (`--chart-compare-1`) and the second
comparison neutral `#8C918F` (`--chart-compare-2`). Hypothetical/shocked curves are **dashed
terracotta** (`--chart-scenario`) so a scenario can never be read as observed data.
Realized/unrealized splits pair navy `#2E4B6E` with light gray (`--chart-realized` /
`--chart-unrealized`). Signed values keep the independent semantics: equity/up green `#2E7655`,
drawdown/down red `#B54B42`, warning ochre `#986717`; benchmark `--chart-secondary`, dashed.
