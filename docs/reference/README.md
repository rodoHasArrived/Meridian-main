# Reference Documentation

**Owner:** Core Team
**Scope:** Engineering / Product — Reference
**Review Cadence:** When APIs, data models, or configuration options change

---

## Purpose

This directory contains lookup-oriented reference material: API endpoints, data model definitions, configuration options, and dependency inventories. Reference docs are comprehensive and precise — they answer "what is X?" rather than "how do I do Y?".

---

## What Belongs Here

- HTTP API endpoint reference (routes, request/response schemas)
- Data model and field definitions
- Environment variable and configuration reference
- Data uniformity and consistency guidelines
- External dependency inventory
- Design review memos documenting constraints and decisions

## What Does NOT Belong Here

- Step-by-step guides → use `development/` or `operations/`
- Provider setup instructions → use `providers/`
- Architecture narratives → use `architecture/`
- Generated API docs → use `generated/`

---

## Contents

| Document | Description |
| --- | --- |
| [API Reference](api-reference.md) | HTTP API endpoints, request/response schemas |
| [Data Dictionary](data-dictionary.md) | Data field definitions and types |
| [Data Uniformity](data-uniformity.md) | Cross-provider consistency guidelines |
| [Environment Variables](environment-variables.md) | Credential and configuration reference |
| [Open Source References](open-source-references.md) | Third-party library acknowledgements |
| [Design Review Memo](design-review-memo.md) | Key design constraints and decisions |
| [Reconciliation Break Taxonomy](reconciliation-break-taxonomy.md) | Versioned canonical break classes and reason codes used by ledger reconciliation |
| [Research Briefing Workflow](research-briefing-workflow.md) | Shared Research workspace briefing contracts, endpoint, and shell binding flow |
| [Strategy Promotion History Persistence](strategy-promotion-history.md) | Durable promotion decision chain fields and JSONL-backed history behavior |

See also: [DEPENDENCIES.md](../DEPENDENCIES.md) — full third-party package inventory.

---

## Related

- [Architecture Overview](../architecture/overview.md) — System design context
- [Provider Documentation](../providers/README.md) — Provider-specific setup
- [Generated Documentation](../generated/README.md) — Auto-generated API docs (do not edit)

---

_Reference docs are hand-authored and kept in sync with the codebase._
