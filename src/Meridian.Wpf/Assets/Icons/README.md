# Meridian Icon Asset Library

This directory contains the canonical SVG source icons for the Meridian UI. Each icon is:

- **24×24 viewBox** — scales cleanly to any size
- **Stroke-based** — uses `currentColor` so they inherit the surrounding text/foreground colour
- **1.5pt stroke width** with rounded caps and joins (consistent with Lucide / Heroicons style)
- **No fill** except for deliberate accent dots — purely outline for crispness at small sizes

These SVGs serve as the platform-agnostic source of truth. In the WPF application the
corresponding **Segoe MDL2 Assets** glyph codes (defined in
`src/Meridian.Wpf/Styles/IconResources.xaml`) are used for native rendering; the SVGs are
available for use in web dashboards, documentation, and future cross-platform targets.

---

## Workspace Icons

Legacy source filenames and WPF resource keys are retained for compatibility, but the
operator-facing workspace labels are the canonical Meridian roots.

| File | Workspace | Replaces |
|------|-----------|---------|
| `research.svg` | Strategy | 🔬 |
| `trading.svg` | Trading | ⚡ |
| `data-operations.svg` | Data | 🗂 |
| `governance.svg` | Accounting | 🛡 |

## Page Icons — Strategy Workspace

| File | Page | Replaces |
|------|------|---------|
| `dashboard.svg` | Dashboard | 📊 |
| `live-data.svg` | Live Data | 📈 |
| `order-book.svg` | Order Book | 📋 |
| `charting.svg` | Charting | 📉 |
| `data-browser.svg` | Data Browser | 🔍 |
| `data-sampling.svg` | Data Sampling | 🎲 |
| `symbol-storage.svg` | Symbol Storage | 💾 |

## Page Icons — Trading Workspace

| File | Page | Replaces |
|------|------|---------|
| `backtest.svg` | Backtest | 🧪 |
| `strategy-runs.svg` | Strategy Runs | 🚀 |
| `run-detail.svg` | Run Detail | 📑 |
| `run-ledger.svg` | Run Ledger | 📚 |
| `run-portfolio.svg` | Run Portfolio | 💼 |
| `run-mat.svg` | RunMat | 🎯 |
| `trading-hours.svg` | Trading Hours | 🕐 |

## Page Icons — Data Workspace

| File | Page | Replaces |
|------|------|---------|
| `symbols.svg` | Symbols | 🔤 |
| `backfill.svg` | Backfill | ⏮ |
| `provider-health.svg` | Provider Health | ❤️ |
| `data-sources.svg` | Data Sources | 📡 |
| `data-quality.svg` | Data Quality | ✓ |
| `data-calendar.svg` | Data Calendar | 📅 |
| `data-export.svg` | Data Export | 📤 |
| `storage-optimization.svg` | Storage Optimization | ⚙️ |
| `storage.svg` | Storage | 💿 |
| `archive-health.svg` | Archive Health | 🏥 |
| `event-replay.svg` | Event Replay | ▶️ |
| `index-subscription.svg` | Index Subscription | 📑 |
| `portfolio-import.svg` | Portfolio Import | 📥 |
| `watchlist.svg` | Watchlist | 👁 |
| `schedule-manager.svg` | Schedule Manager | ⏰ |
| `collection-sessions.svg` | Collection Sessions | 🎬 |

## Page Icons — Accounting and Settings Workspaces

| File | Page | Replaces |
|------|------|---------|
| `security-master.svg` | Security Master | 🔐 |
| `settings.svg` | Settings | ⚙️ |
| `diagnostics.svg` | Diagnostics | 🔧 |
| `service-manager.svg` | Service Manager | 🔌 |
| `admin-maintenance.svg` | Admin Maintenance | 🧹 |
| `retention-assurance.svg` | Retention Assurance | 📋 |
| `lean-integration.svg` | Lean Integration | 🔗 |
| `system-health.svg` | System Health | 📊 |
| `help.svg` | Help | ❓ |
| `keyboard-shortcuts.svg` | Keyboard Shortcuts | ⌨️ |

---

## WPF Usage

In WPF, icons are rendered through **Segoe MDL2 Assets** (a built-in Windows vector icon font).
The glyph codes are defined as `sys:String` resources in `IconResources.xaml` and used via
`{StaticResource IconXxx}` in TextBlock elements styled with `FontFamily="Segoe MDL2 Assets"`.

To render a workspace icon in XAML:

```xml
<!-- Compatibility key used for the canonical Strategy workspace icon. -->
<TextBlock Text="{StaticResource IconResearch}"
           Style="{StaticResource IconMediumStyle}" />
```

## React Dashboard Usage

The React dashboard uses **lucide-react** for most icons. For custom icons not covered by
lucide-react, import the SVG directly:

```tsx
import StrategyIcon from '@/assets/icons/research.svg?react';
// <StrategyIcon className="w-5 h-5" />
```
