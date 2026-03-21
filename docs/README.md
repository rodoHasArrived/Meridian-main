# Meridian Documentation

**Version:** 1.7.0  
**Last Updated:** 2026-03-20

Welcome to the Meridian documentation. This guide will help you find the information you need, whether you're a developer, operator, or user.

> Tip: Start with the audience section that matches your role, then use the reference and status sections for deeper details.

---

## Quick Start

- **New Users** → [Getting Started Guide](getting-started/README.md)
- **Developers** → [Repository Organization Guide](development/repository-organization-guide.md)
- **Trading workstation migration** → [Migration Blueprint](plans/trading-workstation-migration-blueprint.md)
- **Operators** → [Operator Runbook](operations/operator-runbook.md)
- **Contributors** → See [Contributing](#contributing) section below or [Where should this doc go?](development/documentation-contribution-guide.md#where-should-this-doc-go)

---

## Documentation Zones

The documentation is organized into three zones by audience and purpose:

| Zone | Folders | Audience |
|------|---------|----------|
| **Product** | `getting-started/`, `providers/`, `operations/` | Users and Operators |
| **Engineering** | `architecture/`, `adr/`, `development/`, `integrations/`, `reference/`, `diagrams/` | Developers |
| **Governance** | `status/`, `evaluations/`, `audits/`, `security/` | Core Team, Stakeholders |

> **Generated docs:** The `generated/` directory contains auto-generated files. **Do not edit them manually** — they are overwritten on each generation run. See [Generated Documentation](generated/README.md) for details.

---

## Documentation by Audience

### For Users

Getting the system running and using its features.

- [Getting Started](getting-started/README.md) — Quick start guide
- [Help & FAQ](HELP.md) — Comprehensive user and operator guide
- [Provider Setup Guides](providers/) — Setup instructions for each data provider
  - [Alpaca Setup](providers/alpaca-setup.md)
  - [Interactive Brokers Setup](providers/interactive-brokers-setup.md)
  - [IB Free Equity Reference](providers/interactive-brokers-free-equity-reference.md)
- [Provider Comparison](providers/provider-comparison.md) — Compare features and costs
- [Backfill Guide](providers/backfill-guide.md) — Historical data backfill procedures
- [Data Dictionary](reference/data-dictionary.md) — Explanation of data fields
- [Environment Variables](reference/environment-variables.md) — Credential and configuration reference

### For Developers

Building, extending, and testing the system.

- [Repository Organization Guide](development/repository-organization-guide.md) — **START HERE**
- [Provider Implementation Guide](development/provider-implementation.md) — Add new providers
- [Central Package Management](development/central-package-management.md) — NuGet version management
- [GitHub Actions Summary](development/github-actions-summary.md) — CI/CD overview
- [Build Observability](development/build-observability.md) — Build metrics and diagnostics
- [Documentation Contribution Guide](development/documentation-contribution-guide.md) — Writing and maintenance standards
- [Documentation Automation](development/documentation-automation.md) — Auto-generation scripts
- [Adding Custom Rules](development/adding-custom-rules.md) — Build system rule customization
- [Expanding Scripts](development/expanding-scripts.md) — Script development patterns

#### Desktop Development

- [WPF Implementation Notes](development/wpf-implementation-notes.md) — WPF desktop app development
- [Desktop Testing Guide](development/desktop-testing-guide.md) — Testing desktop services
- [Desktop Improvements Guide](evaluations/desktop-platform-improvements-implementation-guide.md) — Platform improvements
- [Desktop Support Policy](development/policies/desktop-support-policy.md) — Support and maintenance policy
- [UI Fixture Mode Guide](development/ui-fixture-mode-guide.md) — Offline development with mock data

### For Operators

Deploying, monitoring, and maintaining the system.

- [Deployment Guide](operations/deployment.md) — Deployment procedures
- [Operator Runbook](operations/operator-runbook.md) — Day-to-day operations
- [Performance Tuning](operations/performance-tuning.md) — Optimization guide
- [High Availability](operations/high-availability.md) — HA configuration
- [Service Level Objectives](operations/service-level-objectives.md) — SLO definitions
- [Portable Data Packager](operations/portable-data-packager.md) — Data packages
- [MSIX Packaging](operations/msix-packaging.md) — Desktop app packaging

### For Architecture & Design

Understanding system design.

- [Architecture Overview](architecture/overview.md) — High-level system architecture
- [Trading Workstation Migration Blueprint](plans/trading-workstation-migration-blueprint.md) — Target product, workspace, and run-model migration
- [Layer Boundaries](architecture/layer-boundaries.md) — Project dependency rules
- [Storage Design](architecture/storage-design.md) — Storage architecture
- [Deterministic Canonicalization](architecture/deterministic-canonicalization.md) — Data normalization
- [Desktop Layers](architecture/desktop-layers.md) — Desktop app architecture
- [Why This Architecture](architecture/why-this-architecture.md) — Design rationale
- [ADRs](adr/) — Architecture Decision Records

---

## Project Status & Planning

- [**Project Roadmap**](status/ROADMAP.md) — **Primary planning document**
- [Trading Workstation Migration Blueprint](plans/trading-workstation-migration-blueprint.md) — Workflow-centric product migration plan
- [**Feature Inventory**](status/FEATURE_INVENTORY.md) — Per-feature implementation status
- [**Improvements Tracker**](status/IMPROVEMENTS.md) — Consolidated improvement tracking
- [TODO Tracking](status/TODO.md) — Auto-scanned TODO comments
- [Changelog](status/CHANGELOG.md) — Version history
- [Production Status](status/production-status.md) — Production readiness
- [Evaluations & Audits Summary](status/EVALUATIONS_AND_AUDITS.md) — Consolidated assessment results

---

## Reference Documentation

- [API Reference](reference/api-reference.md) — HTTP API endpoints
- [Data Dictionary](reference/data-dictionary.md) — Data model definitions
- [Data Uniformity](reference/data-uniformity.md) — Consistency guidelines
- [Environment Variables](reference/environment-variables.md) — Credential and config reference
- [Dependencies Reference](DEPENDENCIES.md) — Third-party package inventory
- [Generated Documentation](generated/) — Auto-generated docs (do not edit manually)
- [Diagrams](diagrams/) — System diagrams: Graphviz DOT (C4, data flow) and [UML](diagrams/uml/) (sequence, state, activity)

---

## Integrations

- [QuantConnect Lean Integration](integrations/lean-integration.md) — Backtesting
- [F# Integration](integrations/fsharp-integration.md) — F# domain logic
- [Language Strategy](integrations/language-strategy.md) — C#/F# language implementation strategy

---

## Evaluations & Audits

In-depth assessments of architecture, providers, and code quality.

> Both `evaluations/` and `audits/` serve the **Governance zone**. Use `evaluations/` for technology and architecture evaluations, brainstorms, and improvement proposals. Use `audits/` for targeted code-quality audits and cleanup analyses.

- [Evaluations & Audits Summary](status/EVALUATIONS_AND_AUDITS.md) — Consolidated overview (start here)
- [Evaluations](evaluations/README.md) — Technology and architecture evaluations
- [Code Audits](audits/README.md) — Code quality audits and cleanup opportunities

---

## Security

- [Known Vulnerabilities](security/known-vulnerabilities.md) — Assessed dependency vulnerabilities

---

## AI Assistant Guides

- [AI Overview](ai/README.md) — AI assistant documentation overview
- [Claude Instructions](ai/claude/) — Claude-specific guides
- [GitHub Copilot Instructions](ai/copilot/instructions.md) — Copilot config
- [AI Known Errors](ai/ai-known-errors.md) — Common AI mistakes to avoid

---

## Archived Documentation

Historical or deprecated documentation superseded by newer guides. Active documents may carry a temporary deprecation banner before they are moved, but the long-term home for those records is always `docs/archived/`.

- [Archived Documentation Index](archived/INDEX.md) — Complete list with context and replacement pointers
- [Archived Documentation README](archived/README.md) — Archive policy, categories, and current contents

---

## Contributing

### How to Contribute

1. **Read the guides:**
   - [Repository Organization Guide](development/repository-organization-guide.md)
   - [Documentation Contribution Guide](development/documentation-contribution-guide.md)

2. **Follow conventions:**
   - File naming: PascalCase for C# files
   - Project structure: See organization guide
   - Testing: `dotnet test` must pass

3. **Submit PR:**
   - Small, focused changes
   - Clear commit messages
   - Reference issues

### Documentation Standards

See [Documentation Contribution Guide](development/documentation-contribution-guide.md) for complete standards.

Quick rules:
- **Audience:** State who the doc is for (Users, Developers, Operators)
- **Date:** Include "Last Updated" date in front matter
- **Format:** Use GitHub Flavored Markdown
- **Examples:** Include runnable code examples
- **Links:** Use relative paths for internal links
- **Naming:** Use kebab-case (lowercase with hyphens)

---

## Getting Help

1. **Search documentation** — Use GitHub search or Ctrl+F
2. **Check FAQ** — See [HELP.md](HELP.md)
3. **Ask the community** — Open a Discussion
4. **Report bugs** — Open an Issue

---

## Documentation Maintenance Checklist

When you update docs in a PR:

1. Add or update links in this index if navigation changes.
2. Update each touched document's **Last Updated** date.
3. Validate internal links and examples.
4. Keep status/planning docs aligned with implementation changes.

---

## Directory Structure

```
docs/
│
│  ── PRODUCT ZONE (user- and operator-facing) ──────────────────────
├── getting-started/        # Quick start guides for new users
├── providers/              # Provider-specific setup guides
├── operations/             # Deployment and maintenance guides
│
│  ── ENGINEERING ZONE (developers and architects) ──────────────────
├── architecture/           # Architecture documentation and design rationale
├── adr/                    # Architecture Decision Records (numbered)
├── development/            # Developer guides, tooling, CI/CD
├── integrations/           # Integration guides (QuantConnect Lean, F#)
├── reference/              # API docs, data dictionary, env variables
├── diagrams/               # All visual assets
│   └── uml/                # UML diagrams (PlantUML sources + PNGs; canonical location)
│
│  ── GOVERNANCE ZONE (status, reviews, security) ────────────────────
├── status/                 # Active project tracking (roadmap, changelog)
├── evaluations/            # Technology and architecture evaluations
├── audits/                 # Code quality audits and cleanup analyses
├── security/               # Security documentation
│
│  ── TOOLING / META ─────────────────────────────────────────────────
├── ai/                     # AI assistant instructions and guides
├── generated/              # Auto-generated docs (do not edit manually)
├── archived/               # Historical documents (see INDEX.md)
├── docfx/                  # DocFX API documentation config
└── README.md               # This file
```

---

*Documentation maintained by core team. Last update: 2026-03-20*
