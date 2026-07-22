---
id: repo:architecture
tier: repo
scope: repo
file: .codex/memory/repo/architecture.md
tags:
  - architecture
  - product-scope
  - surfaces
  - mdif
load_when:
  skills:
    - meridian-blueprint
    - meridian-code-architecture
    - meridian-implementation-assurance
  paths:
    - docs/architecture/**
    - docs/domain/**
    - docs/ai/context/**
    - src/Meridian.Wpf/**
    - src/Meridian.Ui/dashboard/**
    - src/Meridian.Ui.Shared/**
    - src/Meridian.Ui.Services/**
  intents:
    - architecture
    - implementation
    - desktop-ui
    - browser-workstation
  branches: []
  tags:
    - architecture
    - mdif
  task:
    ids: []
    work_modes:
      - planning
      - implementation
    intents:
      - architecture
      - desktop-ui
      - browser-workstation
    paths:
      - docs/architecture/**
      - docs/domain/**
      - docs/ai/context/**
      - src/Meridian.Wpf/**
      - src/Meridian.Ui/dashboard/**
      - src/Meridian.Ui.Shared/**
      - src/Meridian.Ui.Services/**
exclude_when:
  intents:
    - ai-tooling
    - skill-routing
confidence: high
freshness: fresh
source_refs:
  - AGENTS.md
  - .codex/skills/_shared/project-context.md
  - docs/product/meridian-design-document.md
  - docs/architecture/meridian-development-intelligence-framework.md
  - docs/architecture/module-map.md
review_after: 2026-09-19
invalidates_when:
  - Product scope or W5X target docs change materially.
  - Active operator UI surfaces change.
  - MDIF context-loading rules change.
---

# Repository Architecture Memory

Use this memory for broad generation, architecture-sensitive refactors, and UI-surface work.

- Meridian is a .NET 10 fund-management and trading-platform codebase.
- Current productization is evidence-led: use source and roadmap status to choose the active slice;
  prior baselines and named productization targets are status signals, not project development
  restrictions.
- Active operator UI work is browser-first in `src/Meridian.Ui/dashboard/`; `src/Meridian.Wpf/`
  product/UI work is deferred until explicitly reactivated.
- Browser workstation production assets live under `src/Meridian.Ui/wwwroot/workstation/`.
- `src/Meridian.Ui.Services/` and `src/Meridian.Ui.Shared/` are shared API/read-model support
  surfaces for browser workstation flows and retained WPF compatibility.
- Visible root operator navigation should remain `Trading`, `Portfolio`, `Accounting`,
  `Reporting`, `Strategy`, `Data`, and `Settings`.
- There is no mobile development lane. Do not create native mobile clients or mobile-first
  workflows; responsive browser validation is allowed only for the browser workstation.
- Broad generation, domain modeling, workflow design, and architecture-sensitive refactors should
  load the MDIF framework, vision, domain model, relevant domain dictionary, and AI context packs
  before implementation.
