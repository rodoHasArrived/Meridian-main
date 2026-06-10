# UML Diagrams

**Status:** Active
**Owner:** Core Team
**Reviewed:** 2026-06-10

This directory contains PlantUML source files (`.puml`) and committed PNG artifacts (`.png`) for architecture and workflow documentation.

> **Location note:** These UML diagrams were moved from `docs/uml/` to `docs/diagrams/uml/`. Current registered Mermaid architecture sources live under `docs/architecture/diagrams/`.

---

## Diagram Inventory

| Diagram Type | Source (`.puml`) | Committed artifacts | Description |
|---|---|---|---|
| Use Case | `use-case-diagram.puml` | `Use Case Diagram - Meridian.{png,svg}`, `use-case-diagram.png` | System actors and high-level use cases |
| Sequence | `sequence-diagram.puml` | `Sequence Diagram - Real-Time Data Collection Flow.{png,svg}`, `sequence-diagram.png` | Real-time data collection flow |
| Sequence | `sequence-diagram-backfill.puml` | `Sequence Diagram - Historical Backfill Flow.{png,svg}`, `sequence-diagram-backfill.png` | Historical backfill with provider fallback |
| Activity | `activity-diagram.puml` | `Activity Diagram - Data Collection Process Flow.{png,svg}`, `activity-diagram.png` | Main data collection process |
| Activity | `activity-diagram-backfill.puml` | `Activity Diagram - Historical Backfill Process.{png,svg}`, `activity-diagram-backfill.png` | CLI/scheduled/gap-repair backfill process |
| State | `state-diagram.puml` | `State Diagram - Provider Connection States.{png,svg}`, `state-diagram.png` | Provider connection lifecycle |
| State | `state-diagram-orderbook.puml` | `State Diagram - Order Book Stream States.{png,svg}`, `state-diagram-orderbook.png` | Order book freshness lifecycle |
| State | `state-diagram-trade-sequence.puml` | `State Diagram - Trade Sequence Validation States.{png,svg}`, `state-diagram-trade-sequence.png` | Trade sequence validation lifecycle |
| State | `state-diagram-backfill.puml` | `State Diagram - Backfill Request States.{png,svg}`, `state-diagram-backfill.png` | Backfill request lifecycle |
| Communication | `communication-diagram.puml` | `Communication Diagram - Component Message Exchange.{png,svg}`, `communication-diagram.png` | Component-level message exchange |
| Interaction Overview | `interaction-overview-diagram.puml` | `Interaction Overview Diagram - System Workflow.{png,svg}`, `interaction-overview-diagram.png` | High-level workflow orchestration |
| Timing | `timing-diagram.puml` | `Timing Diagram - Event Processing Timeline.{png,svg}`, `timing-diagram.png` | Real-time event timing |
| Timing | `timing-diagram-backfill.puml` | `Timing Diagram - Backfill Operation Timeline.{png,svg}`, `timing-diagram-backfill.png` | Backfill operation timing |
| Sequence | `sequence-diagram-backtesting.puml` | `Sequence Diagram - Backtesting Engine.{png,svg}` | BacktestEngine replay loop, fill models, portfolio, and metrics |
| Sequence | `sequence-diagram-strategy-promotion.puml` | `Sequence Diagram - Strategy Promotion Lifecycle.{png,svg}` | Strategy lifecycle: backtest -> paper validation -> live-readiness governance |
| Class | `class-diagram-wpf-mvvm.puml` | `Class Diagram - WPF MVVM Architecture.{png,svg}` | WPF MVVM hierarchy: BindableBase, ViewModels, Views, and Services |
| Sequence | `sequence-diagram-wal-durability.puml` | `Sequence Diagram - WAL Durability and Crash-Safe Writes.{png,svg}` | WAL + AtomicFileWriter crash-safe write path (ADR-007) |
| Sequence | `sequence-diagram-paper-trading.puml` | `Sequence Diagram - Paper Trading Order Execution.{png,svg}` | PaperTradingGateway order submission, synthetic fill, and risk validation (ADR-015) |

**Totals:** 18 PlantUML sources, 18 titled SVG artifacts, 18 titled PNG artifacts, and 13 legacy direct-name PNG aliases for older diagrams.

---

## How to Render Locally

### Option 1: PlantUML CLI

```bash
# Install PlantUML (Java required)
brew install plantuml   # macOS
sudo apt-get install -y default-jre-headless && \
  wget -q https://github.com/plantuml/plantuml/releases/download/v1.2025.2/plantuml-1.2025.2.jar \
       -O /usr/local/share/plantuml.jar   # Ubuntu/Debian

# Render all diagrams to PNG and SVG in place
plantuml -tpng docs/diagrams/uml/*.puml
plantuml -tsvg docs/diagrams/uml/*.puml
```

### Option 2: Docker

```bash
# PNG output
docker run --rm -v "$(pwd)/docs/diagrams/uml:/data" plantuml/plantuml -tpng /data/*.puml
# SVG output
docker run --rm -v "$(pwd)/docs/diagrams/uml:/data" plantuml/plantuml -tsvg /data/*.puml
```

### Option 3: VS Code Preview

Install the PlantUML extension (`jebbs.plantuml`) and preview with `Alt+D`.

---

## Automated Maintenance Workflow (GitHub Actions)

The repository includes `.github/workflows/documentation.yml` to keep committed diagram artifacts in sync:

**Triggers:**
- Push or pull request activity that modifies any of:
  - `docs/diagrams/uml/*.puml` — PlantUML source edits
  - `docs/architecture/diagrams/*.mmd` — Mermaid source edits
  - `docs/generated/source/diagrams/*.mmd` — generated Mermaid source edits
  - `docs/diagrams/**/*.dot` — Graphviz/DOT source edits
  - `npm run generate-diagrams` (which invokes the canonical package script) — UI diagram generator updates
  - `src/Meridian.Wpf/**/*.xaml` or `src/Meridian.Wpf/**/*.cs` — WPF source changes that affect UI diagrams
- Manually via **Actions → Documentation Automation**

**What it does:**
1. Runs the docs automation profile to refresh tracked generated documentation outputs
2. Re-renders Mermaid source snapshots from roadmap/source registries
3. Runs `npm run generate-diagrams` to regenerate auto-derived DOT sources from WPF XAML
4. Converts every `docs/diagrams/uml/*.puml` file to both `.svg` and `.png` using PlantUML
5. Fails if committed generated artifacts are out of date or contain whitespace issues

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
- [Diagrams Index](../README.md) — Graphviz DOT diagrams (C4, data flow, etc.)

---

*Last Updated: 2026-06-10*
