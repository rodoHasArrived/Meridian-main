# Meridian — Visual Foundations

Grounded in the desktop app `src/Meridian.Wpf/Styles/*.xaml`. The language is **"Institutional
Ops"**: a light paper workstation, hairline structure, one teal-blue accent, desaturated
semantics, no gradients or glow.

## Color

**Canvas & surfaces** (`ThemeTokens.xaml`)
*Programmed Institutionalism v2.0 — warm paper, not cool concrete. The interface material is
drafting stock: surfaces are separated by fine rules and tonal steps, never by shadow.*

- Window canvas `#F2F0EC` (Soft Stone) — the drafting table everything sits on
- Command bars / wells `#EDEAE4`
- Card / panel `#FBFAF8` (Architect White); raised (metric tiles, inspector rails) `#F6F4F0`
- Row hover `#F0EEE9`; pressed / selected wash `#F2E3DB`
- Chrome (brand bar + status bar) warm black `#1F1D1A`, text `#F4F2ED`

**Borders** carry the structure — `#E4E3DE` default, `#C6C3BB` hover, `#AFABA1` strong,
`#A85436` focus. 1px rules do the separating; 2px only for emphasis.

**Text** — primary `#22252A` (Carbon), secondary `#4E5258` (Warm Graphite), muted `#5E666F`
(Slate), disabled `#94999F`.

**Accent** — a single copper `#A85436` (primary buttons, focus rings, active nav, crosshair).
Pressed `#8C4429`, vibrant `#C06B4A`. One accented action per screen. Deliberately *not* blue.

> The source brief proposed Terracotta `#D16A4A` or Copper `#B76841`. Both fail this package's
> WCAG AA gate as text on a card (3.43:1 and 3.97:1 against a 4.5:1 floor), so the hue is kept
> and the value taken down to `#A85436` — 5.05:1 as text, 5.27:1 under a white label. The lighter
> `#D16A4A` survives as `--accent-figure`, for the Meridian Field motif and report covers, where
> it never sits behind text.

**Semantic accents** are desaturated, independent of the brand accent, and always rendered as a
**trio** — dim text · solid 1px border · alpha-10 fill. Never solid fills. Never use the accent
for positive/negative values.
- Success, muted forest `#3A7A56`
- Danger, brick `#A8443C`
- Warning, amber ochre `#8A5C12`
- Pending, slate-violet `#5D5486`

**Environment modes** — always visible: Live brick `#A8443C` (real money), Paper copper `#A85436`
(simulated), Fixture ochre `#8A5C12` (replay/recorded).

**Dark mode** is material, not luminous: warm black `#14120F` canvas, graphite `#201D19` panels,
paper-white `#EFEBE4` text, and a lifted copper `#D98A64` carrying dark ink `#1A1511`. No glowing
grids, no saturated gradients, no glass.

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
