# UML Diagrams

**Status:** Active  
**Owner:** Core Team  
**Reviewed:** 2026-03-15

This directory contains PlantUML source files (`.puml`) and committed PNG artifacts (`.png`) for architecture and workflow documentation.

> **📁 New Location:** These UML diagrams have been moved from `docs/uml/` to `docs/diagrams/uml/` to consolidate all visual assets under a single `docs/diagrams/` home.

---

## Diagram Inventory

| Diagram Type | Source (`.puml`) | Artifact (`.png`) | Description |
|---|---|---|---|
| Use Case | `use-case-diagram.puml` | `use-case-diagram.png` | System actors and high-level use cases |
| Sequence | `sequence-diagram.puml` | `sequence-diagram.png` | Real-time data collection flow |
| Sequence | `sequence-diagram-backfill.puml` | `sequence-diagram-backfill.png` | Historical backfill with provider fallback |
| Activity | `activity-diagram.puml` | `activity-diagram.png` | Main data collection process |
| Activity | `activity-diagram-backfill.puml` | `activity-diagram-backfill.png` | CLI/scheduled/gap-repair backfill process |
| State | `state-diagram.puml` | `state-diagram.png` | Provider connection lifecycle |
| State | `state-diagram-orderbook.puml` | `state-diagram-orderbook.png` | Order book freshness lifecycle |
| State | `state-diagram-trade-sequence.puml` | `state-diagram-trade-sequence.png` | Trade sequence validation lifecycle |
| State | `state-diagram-backfill.puml` | `state-diagram-backfill.png` | Backfill request lifecycle |
| Communication | `communication-diagram.puml` | `communication-diagram.png` | Component-level message exchange |
| Interaction Overview | `interaction-overview-diagram.puml` | `interaction-overview-diagram.png` | High-level workflow orchestration |
| Timing | `timing-diagram.puml` | `timing-diagram.png` | Real-time event timing |
| Timing | `timing-diagram-backfill.puml` | `timing-diagram-backfill.png` | Backfill operation timing |
| Sequence | `sequence-diagram-backtesting.puml` | `sequence-diagram-backtesting.png` | BacktestEngine replay loop, fill models, portfolio, and metrics |
| Sequence | `sequence-diagram-strategy-promotion.puml` | `sequence-diagram-strategy-promotion.png` | Strategy lifecycle: backtest → paper trading → live promotion |
| Class | `class-diagram-wpf-mvvm.puml` | `class-diagram-wpf-mvvm.png` | WPF MVVM hierarchy: BindableBase, ViewModels, Views, and Services |
| Sequence | `sequence-diagram-wal-durability.puml` | `sequence-diagram-wal-durability.png` | WAL + AtomicFileWriter crash-safe write path (ADR-007) |
| Sequence | `sequence-diagram-paper-trading.puml` | `sequence-diagram-paper-trading.png` | PaperTradingGateway order submission, synthetic fill, and risk validation (ADR-015) |

**Totals:** 18 PlantUML sources + 18 PNG artifacts.

---

## How to Render Locally

### Option 1: PlantUML CLI

```bash
# Install PlantUML (Java required)
brew install plantuml   # macOS
sudo apt-get install -y plantuml  # Ubuntu/Debian

# Render all diagrams to PNG in place
plantuml -tpng docs/diagrams/uml/*.puml
```

### Option 2: Docker

```bash
docker run --rm -v "$(pwd)/docs/diagrams/uml:/data" plantuml/plantuml -tpng /data/*.puml
```

### Option 3: VS Code Preview

Install the PlantUML extension (`jebbs.plantuml`) and preview with `Alt+D`.

---

## Automated Maintenance Workflow (GitHub Actions)

The repository includes `.github/workflows/update-diagrams.yml` to keep committed PNG artifacts in sync:

- Triggered on pushes to `main` that modify `docs/diagrams/uml/*.puml`
- Triggered manually via **Actions → Update Diagram Artifacts**
- Installs PlantUML and re-renders `docs/diagrams/uml/*.png`
- Auto-commits changed PNG files back to the branch

---

## Recommended Update Process

When editing UML docs:

1. Update the relevant `docs/diagrams/uml/*.puml` source files.
2. Re-render PNGs locally (`plantuml -tpng docs/diagrams/uml/*.puml`) **or** use the Actions workflow.
3. Verify each changed diagram is readable and semantically correct.
4. If files are added/renamed, update this README inventory table.

---

## Related Documentation

- [Architecture Overview](../../architecture/overview.md)
- [Domain Contracts](../../architecture/domains.md)
- [Diagrams Index](https://github.com/rodoHasArrived/Meridian/blob/main/archive/docs/README.md) — Graphviz DOT diagrams (C4, data flow, etc.)

---

*Last Updated: 2026-03-27*
