# Task context packet

Insert exact sentence immediately after Useful Commands heading with blank lines: Pilot handoffs identify the source revision used for retained validation evidence. Synchronize shared context while preserving all other content.

Packets provide partial context and never override applicable instructions.

## Scope

- `.agents/skills/_shared/project-context.md`
- `.claude/skills/_shared/project-context.md`
- `.codex/skills/_shared/project-context.md`

## Instructions to read in full

- `.codex/skills/_shared/project-context.md`
- `AGENTS.md`

## Validation candidates (not executed)

- `python build/scripts/docs/check-ai-navigation-freshness.py --max-age-days 14`

## Warnings

- No matching source-module owner; inspect the nearest README for ownership and proof.
- Excerpts are partial context. Read applicable instructions in full; packets never override them.

## Omitted context

- `src/Meridian.Application/Pipeline/EventPipeline.cs`: source missing
- `src/Meridian.Contracts`: source missing
- `src/Meridian.Contracts/Lifecycle`: source missing
- `src/Meridian.Contracts/Operations`: source missing
- `src/Meridian.Core/Serialization/MarketDataJsonContext.cs`: source missing
- `src/Meridian.Launcher/StartupOutcomeReceiptMonitor.cs`: source missing
- `src/Meridian.Setup/InstallationTransaction.cs`: source missing
- `src/Meridian.Setup/PayloadPackage.cs`: source missing
- `src/Meridian/Program.cs`: source missing
- `docs/operators/browser-workstation-installer.md`: source missing
- `docs/reference/lifecycle-control-plane.md`: source missing
- `docs/reference/verified-operation-outcomes.md`: source missing
- `docs/ai/README.md`: source missing
- `docs/ai/ai-known-errors.md`: source missing
- `docs/ai/navigation/README.md`: source missing
- `src/Meridian.Application/Composition`: source missing
- `src/Meridian.Application/Pipeline`: source missing
- `src/Meridian.Core/Serialization`: source missing
- `src/Meridian.Launcher/LauncherCommandPolicy.cs`: source missing
- `src/Meridian.Launcher/Program.cs`: source missing
- `src/Meridian.LifecycleSupervisor/LifecycleStartupOutcome.cs`: source missing
- `src/Meridian.LifecycleSupervisor/LifecycleSupervisorRuntime.cs`: source missing
- `src/Meridian.LifecycleSupervisor/Program.cs`: source missing
- `src/Meridian.Setup/Program.cs`: source missing

## Selected source excerpts

### .agents/skills/_shared/project-context.md:21

target file

````
## Platform Snapshot

- Meridian is a .NET 10 operational-finance and trading-platform codebase in active delivery; fund
  management is a first-class specialization, not the root model for every workflow.
- The repo already contains strong provider, storage, replay, backtesting, execution, ledger,
  QuantScript, MCP, and workstation foundations.
- Current delivery direction is evidence-led: use current source, the roadmap registry, and the
  design charter to decide whether a prior baseline, named productization target, or later expansion
  lane is the right scope.
- Treat prior baselines and named productization targets as roadmap/status evidence, not development
  ceilings.
- Expansion lanes such as Backtesting Studio, live-readiness, treasury payment execution,
  alternative asset operations, forecasting/scenario engines, enterprise risk, client portal, and
  no-code workflow design can proceed when current source, roadmap, or user direction supports them.
- MDIF is the required context spine for broad generation, domain modeling, workflow design, and
  architecture-sensitive refactors: load the MDIF framework, vision, domain model, relevant domain
  dictionary pages, and context packs before implementation.
- Operator UI work runs across two active co-equal lanes: the browser workstation in
  `src/Meridian.Ui/dashboard/` and the reactivated WPF desktop workstation in `src/Meridian.Wpf/`.
- `src/Meridian.Wpf/` is an active product/UI lane; its immediate focus is web-UI parity
  (`W8-WPF-PARITY-001`, see `docs/engineering/plans/wpf-web-ui-alignment-plan.md`) over the existing
  Windows desktop shell, compatibility, tests, launch automation, and desktop validation.
- `src/Meridian.Ui/dashboard/` remains an active browser-based workstation lane, with production
  assets built into `src/Meridian.Ui/wwwroot/workstation/`.
- `src/Meridian.Ui.Services/` and `src/Meridian.Ui.Shared/` provide shared API/read-model layers
  that should support the browser workstation and retained WPF compatibility without duplicating
  business logic.
- **No mobile development lane:** do not create mobile applications, mobile-specific product
  surfaces, native iOS/Android clients, MAUI clients, React Native clients, Flutter clients, or
  mobile-first workflows. Responsive browser validation is allowed only to keep the browser
  workstation usable at supported viewport sizes.
- Keep top-level operator navigation to seven workspaces: `Trading`, `Portfolio`, `Accounting`,
  `Reporting`, `Strategy`, `Data`, and `Settings`. Legacy `Research`, `Data Operations`, and
  `Governance` names remain compatibility aliases, not visible root workspaces.

---

````

### .claude/skills/_shared/project-context.md:21

target file

````
## Platform Snapshot

- Meridian is a .NET 10 operational-finance and trading-platform codebase in active delivery; fund
  management is a first-class specialization, not the root model for every workflow.
- The repo already contains strong provider, storage, replay, backtesting, execution, ledger,
  QuantScript, MCP, and workstation foundations.
- Current delivery direction is evidence-led: use current source, the roadmap registry, and the
  design charter to decide whether a prior baseline, named productization target, or later expansion
  lane is the right scope.
- Treat prior baselines and named productization targets as roadmap/status evidence, not development
  ceilings.
- Expansion lanes such as Backtesting Studio, live-readiness, treasury payment execution,
  alternative asset operations, forecasting/scenario engines, enterprise risk, client portal, and
  no-code workflow design can proceed when current source, roadmap, or user direction supports them.
- MDIF is the required context spine for broad generation, domain modeling, workflow design, and
  architecture-sensitive refactors: load the MDIF framework, vision, domain model, relevant domain
  dictionary pages, and context packs before implementation.
- Operator UI work runs across two active co-equal lanes: the browser workstation in
  `src/Meridian.Ui/dashboard/` and the reactivated WPF desktop workstation in `src/Meridian.Wpf/`.
- `src/Meridian.Wpf/` is an active product/UI lane; its immediate focus is web-UI parity
  (`W8-WPF-PARITY-001`, see `docs/engineering/plans/wpf-web-ui-alignment-plan.md`) over the existing
  Windows desktop shell, compatibility, tests, launch automation, and desktop validation.
- `src/Meridian.Ui/dashboard/` remains an active browser-based workstation lane, with production
  assets built into `src/Meridian.Ui/wwwroot/workstation/`.
- `src/Meridian.Ui.Services/` and `src/Meridian.Ui.Shared/` provide shared API/read-model layers
  that should support the browser workstation and retained WPF compatibility without duplicating
  business logic.
- **No mobile development lane:** do not create mobile applications, mobile-specific product
  surfaces, native iOS/Android clients, MAUI clients, React Native clients, Flutter clients, or
  mobile-first workflows. Responsive browser validation is allowed only to keep the browser
  workstation usable at supported viewport sizes.
- Keep top-level operator navigation to seven workspaces: `Trading`, `Portfolio`, `Accounting`,
  `Reporting`, `Strategy`, `Data`, and `Settings`. Legacy `Research`, `Data Operations`, and
  `Governance` names remain compatibility aliases, not visible root workspaces.

---

````

### .codex/skills/_shared/project-context.md:22

target file

````
## Platform Snapshot

- Meridian is a .NET 10 operational-finance and trading-platform codebase in active delivery; fund
  management is a first-class specialization, not the root model for every workflow.
- The repo already contains strong provider, storage, replay, backtesting, execution, ledger,
  QuantScript, MCP, and workstation foundations.
- Current delivery direction is evidence-led: use current source, the roadmap registry, and the
  design charter to decide whether a prior baseline, named productization target, or later expansion
  lane is the right scope.
- Treat prior baselines and named productization targets as roadmap/status evidence, not development
  ceilings.
- Expansion lanes such as Backtesting Studio, live-readiness, treasury payment execution,
  alternative asset operations, forecasting/scenario engines, enterprise risk, client portal, and
  no-code workflow design can proceed when current source, roadmap, or user direction supports them.
- MDIF is the required context spine for broad generation, domain modeling, workflow design, and
  architecture-sensitive refactors: load the MDIF framework, vision, domain model, relevant domain
  dictionary pages, and context packs before implementation.
- Operator UI work runs across two active co-equal lanes: the browser workstation in
  `src/Meridian.Ui/dashboard/` and the reactivated WPF desktop workstation in `src/Meridian.Wpf/`.
- `src/Meridian.Wpf/` is an active product/UI lane; its immediate focus is web-UI parity
  (`W8-WPF-PARITY-001`, see `docs/engineering/plans/wpf-web-ui-alignment-plan.md`) over the existing
  Windows desktop shell, compatibility, tests, launch automation, and desktop validation.
- `src/Meridian.Ui/dashboard/` remains an active browser-based workstation lane, with production
  assets built into `src/Meridian.Ui/wwwroot/workstation/`.
- `src/Meridian.Ui.Services/` and `src/Meridian.Ui.Shared/` provide shared API/read-model layers
  that should support the browser workstation and retained WPF compatibility without duplicating
  business logic.
- **No mobile development lane:** do not create mobile applications, mobile-specific product
  surfaces, native iOS/Android clients, MAUI clients, React Native clients, Flutter clients, or
  mobile-first workflows. Responsive browser validation is allowed only to keep the browser
  workstation usable at supported viewport sizes.
- Keep top-level operator navigation to seven workspaces: `Trading`, `Portfolio`, `Accounting`,
  `Reporting`, `Strategy`, `Data`, and `Settings`. Legacy `Research`, `Data Operations`, and
  `Governance` names remain compatibility aliases, not visible root workspaces.

---

````

