# Upgrading

One page per behavior change a consumer might feel. Additive releases aren't listed.

## → 1.16

**Dark `--accent-dim` is now lighter than the accent** (`#3C6688` → `#609BC9`). Pressed primary
buttons and accent-dim text in dark mode get brighter, not darker — this fixes two AA failures
(pressed ink 3.11:1; accent-dim-as-text ~2.5:1). Light mode is unchanged. If you used
`--accent-dim` in dark expecting a *dark* fill (e.g. a custom hover on a light-text element),
switch that usage to `--accent-ghost` or a wash.

**Dark `--text-muted` lightened** (`#8893A0` → `#8F9AA7`) so muted captions stay AA on hover
rows. Purely a legibility bump; no action needed.

**Keyboard events on data surfaces.** `DepthLadder` (with `onPriceClick`), `CoverageMatrix`
(with `onCellClick`), and `WorksheetGrid` now handle Arrow/Home/End (+Ctrl/Page variants) keys
and call `preventDefault` on them. If a parent listened for those keys while focus was inside
these components, it will no longer receive them.

## → 1.10

**Table row heights now follow the density token.**
`DenseDataTable` and `FilteredDataTable` previously hardcoded `rowHeight = 40` (dense: 32) for
both CSS and virtualization math. They now read `--theme-row-height` (32 compact · 40 cozy ·
48 spacious), so `DensityToggle` / `body[data-theme-density]` actually resizes tables.

- **If you never set density:** cozy default is 40px — no visible change.
- **If you set `data-theme-density`:** tables will now (correctly) change height. If a screen
  relied on tables ignoring density, pass an explicit `rowHeight={40}` to opt out.
- **If you passed `rowHeight`:** unchanged — an explicit prop still wins.

**OrderTicket fields.** If you styled `.mds-ticket__in`/`.mds-ticket__lbl` from outside (you
shouldn't have), those class names are gone — it now composes `Input`/`Select`.

## → 1.9

**Text-on-fill colors.** If you built custom components copying the old `color: white` on
accent/semantic fills, switch to `var(--text-on-accent)` (accent fills) or `var(--text-on-fill)`
(mode/semantic fills) — dark mode flips these to dark ink, and the `white-on-fill` governance
rule now flags hardcoded white in `components/`.

**Overlay focus.** `Dialog`/`Drawer`/`Modal` now trap Tab. If a consumer screen depended on
tabbing out of an open dialog into the page (unlikely, non-conformant), that no longer works.
