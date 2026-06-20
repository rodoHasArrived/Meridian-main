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
- Current productization centers on the closed W1-W5 operational record baseline and active W5X
  targets: shared Financial Record Explorers and the Financial Operations control center.
- Active operator UI surfaces are `src/Meridian.Wpf/` and `src/Meridian.Ui/dashboard/`.
- Browser workstation production assets live under `src/Meridian.Ui/wwwroot/workstation/`.
- `src/Meridian.Ui.Services/` and `src/Meridian.Ui.Shared/` are shared API/read-model support
  surfaces for desktop and browser workstation flows.
- Visible root operator navigation should remain `Trading`, `Portfolio`, `Accounting`,
  `Reporting`, `Strategy`, `Data`, and `Settings`.
- There is no mobile development lane. Do not create native mobile clients or mobile-first
  workflows; responsive browser validation is allowed only for the browser workstation.
- Broad generation, domain modeling, workflow design, and architecture-sensitive refactors should
  load the MDIF framework, vision, domain model, relevant domain dictionary, and AI context packs
  before implementation.
