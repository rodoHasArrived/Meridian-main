# Meridian WPF — Performance & UI/UX Refinement Audit

**Date:** 2026-06-14
**Scope:** `src/Meridian.Wpf/` desktop workstation, with focus on the shared
design-system resource dictionaries (highest leverage — changes propagate app-wide).

> **Validation note:** the Linux build sandbox was unavailable during this pass
> (out of disk space), so these changes were **not** compiled or run against
> `Meridian.sln`. They are deliberately conservative (additive setters and a
> standard scrollbar template), but should be verified with:
> `dotnet build Meridian.sln -c Release /p:EnableWindowsTargeting=true`
> and a visual smoke test of grids, lists, and scroll regions.

---

## Changes applied

All edits are in `src/Meridian.Wpf/Styles/AppStyles.xaml`.

### Performance

1. **Implicit `DataGrid` virtualization tuning.** Added container recycling and
   row/column virtualization with pixel-based smooth scrolling:
   `EnableRowVirtualization`, `EnableColumnVirtualization`,
   `ScrollViewer.CanContentScroll=True`, `VirtualizingPanel.IsVirtualizing=True`,
   `VirtualizingPanel.VirtualizationMode=Recycling`,
   `VirtualizingPanel.ScrollUnit=Pixel`.
   Recycling reuses row containers instead of re-creating them on scroll, which
   materially reduces allocations and GC pressure on dense surfaces (blotters,
   activity logs, security master). Behavior-preserving — grids already
   virtualized by default; this only changes the virtualization *mode*.

2. **Implicit `ListView` virtualization tuning.** Same recycling + UI
   virtualization setters for long lists.

### UI/UX refinements

3. **Keyboard focus visuals restored on three button styles.**
   `SecondaryButtonStyle`, `DangerButtonStyle`, and `SubtleButtonStyle` set
   `FocusVisualStyle="{x:Null}"` but had no replacement, so keyboard users got
   **no focus indication** (an accessibility gap). Added `IsKeyboardFocused`
   triggers that draw the focus-accent border — matching the pattern already
   used by `RoundedButtonTemplate` / `PrimaryButtonTemplate` in
   `ThemeControls.xaml`. Mouse appearance is unchanged.

4. **Slim themed scrollbar.** The app had no `ScrollBar` style, so it rendered
   the default chunky Win32-era scrollbar everywhere. Added a thin (12px),
   palette-matched scrollbar with a rounded thumb that brightens on hover/drag.
   The thumb and page-button styles are **keyed** (not implicit `Thumb`) so
   `GridSplitter`, `Slider`, and other thumb-based controls are unaffected. The
   `Track` orientation is template-bound so horizontal and vertical bars both
   render correctly from the single template.

---

## Recommended next (deliberately NOT auto-applied — higher risk / need build validation)

- **Freeze shared theme brushes** (`ThemeTokens.xaml`). ~150 `SolidColorBrush`
  resources are unfrozen. Freezing static brushes (`po:Freeze="True"`) lowers
  memory and speeds rendering. Safe *only* after confirming none are used as
  the animated target of `FlashHighlightStoryboard` (which animates a
  `Border.Background` color). Worth doing, but verify per-brush before a
  blanket change.

- **`ActivityLogGridControl` uses a non-virtualizing `ItemsControl`** inside a
  `StackPanel`. For long timelines this realizes every row. Converting it to a
  virtualizing host is valuable but structural — it needs a bounded-height
  scroll container to actually virtualize, so it requires layout review to
  avoid introducing a nested scroll region. Recommend handling as a focused,
  testable change.

- **Text rendering mode.** For a dense small-font workstation,
  `TextOptions.TextFormattingMode="Display"` at window/page root sharpens UI
  text. Apply per-shell-window and visually verify rather than app-wide.

- **`DropShadowEffect` on cards/tooltips** (`ElevatedCardStyle`,
  `DarkTooltipStyle`). `DropShadowEffect` is a render-thread blur pass; on
  surfaces that repeat many times, prefer a 1px border + subtle background step,
  or cache with `BitmapCache`. Low priority; measure first.
