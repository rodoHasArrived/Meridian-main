---
id: repo:accounting-workflows
tier: repo
scope: repo
file: .codex/memory/repo/accounting-workflows.md
tags:
  - accounting
  - financial-operations
  - reconciliation
  - ledger
  - workstation
load_when:
  skills:
    - meridian-blueprint
    - meridian-code-architecture
    - meridian-implementation-assurance
    - meridian-docs
    - modular-desktop-mvvm
    - dense-data-grid-inspector-panel
    - workstation-screen-composition
  paths:
    - docs/product/**
    - docs/roadmap/**
    - docs/ai/context/accounting-context.md
    - src/Meridian.Contracts/**
    - src/Meridian.FinancialOperations/**
    - src/Meridian.Ledger/**
    - src/Meridian.Ui.Shared/**
    - src/Meridian.Ui.Services/**
    - src/Meridian.Ui/dashboard/**
    - src/Meridian.Wpf/**
    - tests/Meridian.Tests/**
    - tests/Meridian.Wpf.Tests/**
  intents:
    - accounting
    - financial-operations
    - reconciliation
    - ledger
    - desktop-ui
    - browser-workstation
    - implementation
    - documentation
  branches: []
  tags:
    - accounting
    - financial-operations
    - reconciliation
    - ledger
  task:
    ids: []
    work_modes:
      - planning
      - implementation
      - validation
    intents:
      - accounting
      - financial-operations
      - reconciliation
      - ledger
      - desktop-ui
      - browser-workstation
    paths:
      - src/Meridian.Contracts/**
      - src/Meridian.FinancialOperations/**
      - src/Meridian.Ledger/**
      - src/Meridian.Ui.Shared/**
      - src/Meridian.Ui.Services/**
      - src/Meridian.Ui/dashboard/**
      - src/Meridian.Wpf/**
      - tests/Meridian.Tests/**
      - tests/Meridian.Wpf.Tests/**
exclude_when:
  intents:
    - ai-tooling
    - skill-routing
confidence: high
freshness: fresh
source_refs:
  - docs/product/README.md
  - docs/roadmap/data/roadmap-items.yml
  - docs/ai/context/accounting-context.md
  - src/Meridian.Contracts/README.md
  - src/Meridian.Ui.Shared/README.md
  - src/Meridian.Ui.Services/README.md
  - src/Meridian.Ui/dashboard/README.md
  - src/Meridian.Wpf/README.md
  - src/Meridian.FinancialOperations/OperationsContinuity/OperationsContinuityWorkflowService.cs
  - src/Meridian.FinancialOperations/AccountingSystem/AccountingSystemIntegrationService.cs
  - tests/Meridian.Tests/Application/OperationsContinuityWorkflowServiceTests.cs
  - tests/Meridian.Tests/Ui/AccountingSystemIntegrationServiceTests.cs
review_after: 2026-09-20
invalidates_when:
  - W5X Financial Operations product scope changes materially.
  - Accounting route ownership moves out of shared contracts or shared services.
  - Live external GL posting, live payment execution, or automation-origin governed mutations are explicitly enabled.
  - Operations Continuity close or reconciliation endpoint contracts change materially.
---

# Accounting Workflows Memory

Use this memory for Accounting, Financial Operations, reconciliation, ledger, close, external GL,
manual journal, and accounting workstation work.

- Treat Financial Operations scope from current source and roadmap evidence. Prior baselines and
  named productization targets are status signals, not project development restrictions.
- Accounting workflow behavior should start from shared contracts and services. Important seams
  include `src/Meridian.Contracts/`, `src/Meridian.FinancialOperations/`, `src/Meridian.Ledger/`,
  `src/Meridian.Ui.Shared/`, and `src/Meridian.Ui.Services/`; browser and WPF should project those
  DTOs rather than invent local accounting state.
- Accounting entries are double-entry only. Accounting UI must expose validation, source,
  approval, retained evidence, and audit trail before commit, and code must not silently create
  accounting records from unverified market data.
- Operations Continuity owns close readiness, approval policy, close-calendar, reconciliation
  break assignment/escalation/resolution, close-package publication, and evidence-package posture.
  Keep close scoring, blocker codes, retained package state, and governed mutations server/shared
  owned.
- External GL and QuickBooks lanes are read-only import/reconciliation evidence plus guarded export
  package review. Meridian-owned ledger truth remains authoritative, generated export artifacts
  retain hashes and disabled-posting posture, and live external posting is disabled unless a later
  release gate explicitly enables it.
- Manual journal, accounting configuration, close sign-off, late-adjustment, report-package
  certification, external GL export certification, payment approval, and similar governed commands
  carry action-origin metadata. Reviewed automation may draft support, but assistant or automation
  origins must not approve, post, certify, publish, resolve, sign off, retain governed evidence, or
  mutate authority.
- Private-capital activity, fund-event ledger records, capital-account subledgers, payment-intent
  evidence, and close cockpit rows are shared DTO projections. Do not grow cap-table
  administration, broad LP portal, native live-payment execution, full forecasting, or Backtesting
  Studio behavior from these Accounting slices.
- Browser Accounting surfaces live under `src/Meridian.Ui/dashboard/src/screens/accounting-screen*`
  and should stay thin over shared API clients and DTO mirrors. WPF Accounting registration starts
  in `src/Meridian.Wpf/Features/Accounting/AccountingFeatureModule.cs` with WPF pages and view
  models rendering the same shared posture.
- Narrow validation usually starts with focused `tests/Meridian.Tests` filters for
  `OperationsContinuityWorkflowServiceTests`, `WorkstationEndpointsTests.AccountingConfiguration`,
  `AccountingSystemIntegrationServiceTests`, `AccountingConfigurationServiceTests`, and related
  close/journal tests, plus `src/Meridian.Ui/dashboard` tests or `tests/Meridian.Wpf.Tests` when
  the browser or desktop projection changes.
