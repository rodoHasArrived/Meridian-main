# Iconography

## System

Meridian uses a **single, unified line‑icon language** across all surfaces:

- **24×24 viewBox**, stroke‑only (no fills except deliberate accent dots)
- **1.5pt stroke width**, rounded caps, rounded joins
- **`currentColor`** — icons inherit the surrounding `color`. Never use hardcoded hex on an icon.
- **No emoji. No unicode glyphs.** (The WPF app originally used emoji — the `Icons/README.md` in the repo lists every emoji each SVG replaced. Never re‑introduce them.)

This matches the visual language of **Lucide** / **Heroicons outline** — not by coincidence, but deliberately. Custom Meridian icons live alongside lucide‑react icons in the dashboard and are visually indistinguishable.

## What's in `assets/icons/`

47 custom SVGs covering every workspace, page, and utility action. Grouped:

- **Workspaces:** `research`, `trading`, `data-operations`, `governance`
- **Research pages:** `dashboard`, `live-data`, `order-book`, `charting`, `data-browser`, `data-sampling`, `symbol-storage`
- **Trading pages:** `backtest`, `strategy-runs`, `run-detail`, `run-ledger`, `run-portfolio`, `run-mat`, `trading-hours`
- **Data Ops pages:** `symbols`, `backfill`, `provider-health`, `data-sources`, `data-quality`, `data-calendar`, `data-export`, `storage-optimization`, `storage`, `archive-health`, `event-replay`, `index-subscription`, `portfolio-import`, `watchlist`, `schedule-manager`, `collection-sessions`, `account-portfolio`, `aggregate-portfolio`
- **Governance pages:** `security-master`, `settings`, `diagnostics`, `service-manager`, `admin-maintenance`, `retention-assurance`, `lean-integration`, `system-health`, `help`, `keyboard-shortcuts`

The canonical `assets/icons/README.md` (copied from the repo) is the authoritative inventory.

## Usage

### In HTML

```html
<img src="assets/icons/trading.svg" alt="" class="size-4" style="color: hsl(var(--primary));" />
```

Because these SVGs use `currentColor`, wrapping them in an element whose `color` is set — or inlining the SVG — gives full tint control.

### In React (dashboard)

The web dashboard uses **`lucide-react`** as the default icon set:

```tsx
import { RadioTower, Wallet, ClipboardList } from "lucide-react";

<RadioTower className="h-4 w-4 text-primary" />
```

For custom icons that lucide doesn't have:

```tsx
import ResearchIcon from '@/assets/icons/research.svg?react';
<ResearchIcon className="w-5 h-5" />
```

### In WPF

The desktop app uses **Segoe MDL2 Assets** glyph codes (Windows built‑in icon font), declared in `Styles/IconResources.xaml`. The SVG set is the cross‑platform source of truth that keeps glyph choices aligned with the web surface.

## Sizing

| Context | Size | Class |
|---|---|---|
| Inline with text | 14px | `size-3.5` / `h-3.5 w-3.5` |
| Default UI | 16px | `size-4` / `h-4 w-4` |
| Nav / button primary | 20px | `size-5` / `h-5 w-5` |
| Section heading | 24px | `size-6` / `h-6 w-6` |
| Logo mark | 48px | `h-12 w-12` |

## Color

- Match surrounding text: inherit `currentColor`.
- **Active nav / primary action:** `text-primary`.
- **Muted / supplementary:** `text-muted-foreground`.
- **Semantic:** `text-success`, `text-warning`, `text-danger`.
- Never color‑code icons purely for decoration.

## Brand mark

`assets/brand/meridian-mark.svg` — the Meridian symbol. A vertical meridian beam with a rising market path (gradient from deep blue → cyan → green), capped with a mint glow at the top and a subtle horizon curve at the bottom. Used as:

- App icon (macOS, Windows — rendered over a navy tile in `meridian-tile-256.png`)
- Sidebar logo (48px, on a `primary/12` background, inside a rounded square)
- Login illustration (full size, on navy)

`meridian-wordmark.svg` pairs the mark with a **MERIDIAN** wordmark and the descriptor **RESEARCH / TRADING / DATA OPS / GOVERNANCE** — reserved for marketing surfaces, about panes, and splash screens.

**Do not recolor the brand mark.** The gradient is specified in the SVG and is the one place a multi‑stop gradient is allowed.

## Unicode characters

A few are allowed as typographic punctuation — never as icons:

- **`·`** — middle dot, space‑flanked, between metadata fields (`api-gateway · 14:32:07`)
- **`—`** — em dash, for ranges and pauses
- **`…`** — ellipsis for async ("Loading…", "Submitting…")
- **`/`** — counters ("3 / 5 providers online")

Nothing else. No `★`, no `▸`, no arrows — use lucide `ArrowRight`, `ChevronRight`, etc.
