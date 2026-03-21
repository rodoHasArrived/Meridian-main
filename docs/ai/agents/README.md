# AI Agent Definitions

This directory is the navigation index for AI **agent definitions** used in the Meridian
project. Copilot agent files live in `.github/agents/`; Claude agent files live in `.claude/agents/`.
Both sets are kept in sync so that Claude and Copilot have access to equivalent tooling.

---

## GitHub Copilot Agents (`.github/agents/`)

### ADR Generator Agent

**File:** [`.github/agents/adr-generator.agent.md`](../../../.github/agents/adr-generator.agent.md)
**Used by:** GitHub Copilot agents

Creates well-structured, comprehensive Architectural Decision Records (ADRs) for the
`/docs/adr/` directory. Follows a standardised format with YAML front matter, coded bullet
points (POS-NNN, NEG-NNN, ALT-NNN, IMP-NNN, REF-NNN), and a 15-item quality checklist.

**Workflow:**
1. Gather required information (title, context, decision, alternatives, stakeholders)
2. Determine the next sequential 4-digit ADR number from `/docs/adr/`
3. Generate the markdown document following the required structure
4. Save to `/docs/adr/adr-NNNN-[title-slug].md`

---

### Blueprint Mode Agent

**File:** [`.github/agents/blueprint-agent.md`](../../../.github/agents/blueprint-agent.md)
**Used by:** GitHub Copilot agents
**Claude Code equivalent:** the corresponding Blueprint resources in `.claude/agents/` and `.claude/skills/`.

Translates a single prioritised idea into a complete, code-ready technical design document.
Supports three depth modes (`full`, `spike`, `interface-only`) and produces a 9-step blueprint:

| Step | Section |
|------|---------|
| 1 | Scope (in/out/assumptions/depth) |
| 2 | Architectural Overview (context diagram + design decisions) |
| 3 | Interface & API Contracts (C# 13, F# 8, REST/WebSocket) |
| 4 | Component Design (namespace, type, lifetime, concurrency) |
| 5 | Data Flow (happy path + error path) |
| 6 | XAML & UI Design (UI features only) |
| 7 | Test Plan (unit, integration, infrastructure) |
| 8 | Implementation Checklist (phased, effort estimate) |
| 9 | Open Questions & Risks |

**Pipeline position:**
```
Brainstorm → Roadmap → Blueprint (this agent) → Implementation → Code Review → Testing
```

---

### Code Review Agent

**File:** [`.github/agents/code-review-agent.md`](../../../.github/agents/code-review-agent.md)
**Used by:** GitHub Copilot agents and the corresponding Claude Code review skill

Defines the canonical **7-lens code review framework** for the Meridian codebase:

| Lens | Focus Area | Key Checks |
|------|-----------|------------|
| 1 | MVVM Architecture Compliance | Thin code-behind, BindableBase, dependency boundaries |
| 2 | Real-Time Performance | Blocking calls, hot-path allocations, channel policies, ADR-014 |
| 3 | Error Handling & Resilience | Exception hierarchy, provider resilience, shutdown paths |
| 4 | Test Code Quality | Naming, AAA structure, async patterns, isolation |
| 5 | Provider Implementation Compliance | Interface completeness, rate limiting, reconnection |
| 6 | Cross-Cutting Concerns | Dependency rules, C#/F# interop, benchmark conventions |
| 7 | Storage & Pipeline Integrity | AtomicFileWriter, WAL flush ordering, sink registration |

The Claude Code equivalent is the corresponding review skill documented in the AI resource indexes.

---

### Documentation Agent

**File:** [`.github/agents/documentation-agent.md`](../../../.github/agents/documentation-agent.md)
**Used by:** GitHub Copilot agents
**Claude Code equivalent:** the corresponding documentation agent in `.claude/agents/`.

Handles documentation maintenance and quality tasks:

- Keeping `CLAUDE.md`, `docs/ai/`, and other AI resources current
- Documentation quality checks (broken links, stale content, formatting)
- Automated intake for the `ai-known-errors.md` registry
- Generating and updating architecture docs, changelogs, and API references

---

### Brainstorming & Ideation Agent

**File:** [`.github/agents/brainstorm-agent.md`](../../../.github/agents/brainstorm-agent.md)
**Used by:** GitHub Copilot agents
**Claude Code equivalent:** the corresponding brainstorm resources in `.claude/skills/`.

Generates high-value, implementable ideas for the Meridian platform. Supports 11
brainstorm modes (Open Exploration, Problem-Focused, Persona-Focused, Domain-Focused, Competitive,
Quick Wins, Architecture/Refactoring, User Growth/Adoption, Technical Debt, UX/Information Design,
Skill Improvement). Produces a summary table + narrative ideas + synthesis with competitive signals.

---

### Provider Builder Agent

**File:** [`.github/agents/provider-builder-agent.md`](../../../.github/agents/provider-builder-agent.md)
**Used by:** GitHub Copilot agents
**Claude Code equivalent:** the corresponding provider-builder resources in `.claude/skills/`.

Builds complete, architecturally compliant data provider adapters via a 12-step guided process.
Covers `IMarketDataClient` (streaming), `IHistoricalDataProvider` (backfill), and
`ISymbolSearchProvider` (symbol search) with rate limiting, reconnection, attribute decoration,
DI registration, and a matching test scaffold.

---

### Test Writer Agent

**File:** [`.github/agents/test-writer-agent.md`](../../../.github/agents/test-writer-agent.md)
**Used by:** GitHub Copilot agents
**Claude Code equivalent:** the corresponding test-writing resources in `.claude/skills/`.

Generates idiomatic xUnit + FluentAssertions tests with correct async patterns, isolation, naming
conventions, and mock setup for all major Meridian component types. Applies 7 universal quality rules
and selects from 8 named patterns (A–H) based on the component type.

---

### Cleanup Agent

**File:** [`.github/agents/cleanup-agent.md`](../../../.github/agents/cleanup-agent.md)
**Used by:** GitHub Copilot agents
**Claude Code equivalent:** the corresponding cleanup agent in `.claude/agents/`.

Removes dead code, duplication, anti-patterns, and stale documentation without changing
observable behaviour. Covers 7 categories:

| # | Category |
|---|---------|
| 1 | Dead code removal (C# / F#) |
| 2 | Anti-pattern correction (Meridian-specific) |
| 3 | Duplication consolidation |
| 4 | WPF code-behind mechanical cleanup |
| 5 | Documentation cleanup (stale refs, broken links) |
| 6 | Central Package Management (CPM) compliance |
| 7 | ADR attribute cleanup (`[DataSource]`, `[ImplementsAdr]`) |

---

### Bug Fix Agent

**File:** [`.github/agents/bug-fix-agent.md`](../../../.github/agents/bug-fix-agent.md)
**Used by:** GitHub Copilot agents

Traces failures from symptom to root cause across all Meridian layers and applies the minimal
correct fix with a mandatory regression test. Follows a 5-step workflow:

| Step | Action |
|------|--------|
| 1 | Reproduce — confirm the failure with exact CLI / test command |
| 2 | Isolate — identify the layer (provider, pipeline, storage, WPF, F#) |
| 3 | Diagnose — apply layer-specific diagnostic lens |
| 4 | Fix — minimal change at the root cause, following all CLAUDE.md rules |
| 5 | Regression test — new test that was red before, green after |

---

### Performance Agent

**File:** [`.github/agents/performance-agent.md`](../../../.github/agents/performance-agent.md)
**Used by:** GitHub Copilot agents

Identifies and eliminates hot-path bottlenecks across streaming pipelines, storage sinks, and
provider adapters. Operates only on **measured** bottlenecks (BenchmarkDotNet / dotnet-counters).

| Category | Key Fix Pattern |
|----------|----------------|
| Per-tick allocations | Span\<T\>, ArrayPool, LoggerMessage.Define |
| Blocking I/O | Remove `Task.Run` wrappers; use true async I/O |
| Channel saturation | `EventPipelinePolicy.Default.CreateChannel<T>()` (ADR-013) |
| JSON overhead | `MarketDataJsonContext.Default.*` source-generated context (ADR-014) |
| LINQ in hot path | Replace with `foreach` + pre-allocated buffers |
| String interning | `SymbolTable.Intern()` for repeated symbol keys |

---

## Claude Code Agents (`.claude/agents/`)

### Claude Blueprint Agent

**Location:** `.claude/agents/`
**Used by:** Claude Code
**Copilot equivalent:** [`.github/agents/blueprint-agent.md`](../../../.github/agents/blueprint-agent.md)
**Skill equivalent:** the Blueprint skill documented under `.claude/skills/`

Blueprint Mode specialist. Translates a single prioritised idea into a complete, code-ready
technical design document — interfaces, component designs, data flows, XAML sketches, test plans,
and implementation checklists — grounded in Meridian's actual stack.

---

### Claude Cleanup Agent

**Location:** `.claude/agents/`
**Used by:** Claude Code
**Copilot equivalent:** [`.github/agents/cleanup-agent.md`](../../../.github/agents/cleanup-agent.md)

Cleanup specialist for the Meridian repository. Removes dead code, duplication,
anti-patterns, and stale documentation across C# 13, F# 8, WPF, and .NET 9 source files —
without changing observable behaviour. Covers 7 categories: dead code, anti-pattern correction,
duplication consolidation, WPF code-behind cleanup, documentation cleanup, CPM compliance,
and ADR attribute cleanup.

---

### Claude Documentation Agent

**Location:** `.claude/agents/`
**Used by:** Claude Code
**Copilot equivalent:** [`.github/agents/documentation-agent.md`](../../../.github/agents/documentation-agent.md)

Documentation maintenance specialist for the Meridian repository. Keeps docs accurate,
comprehensive, and up-to-date across the AI guidance system (`docs/ai/`), architecture docs,
provider docs, developer guides, `CLAUDE.md`, and the `ai-known-errors.md` registry.

---

## Symmetry Map

| Capability | Copilot Agent | Claude Agent / Skill |
|-----------|--------------|---------------------|
| ADR creation | `adr-generator.agent.md` | *(Copilot-only)* |
| Blueprint / technical design | `blueprint-agent.md` | Blueprint resources in `.claude/agents/` and `.claude/skills/` |
| Code review (7 lenses) | `code-review-agent.md` | Corresponding Claude Code review skill |
| Brainstorming & ideation | `brainstorm-agent.md` | Corresponding Claude Code brainstorm resources |
| Provider implementation | `provider-builder-agent.md` | Corresponding Claude Code provider-builder resources |
| Test generation | `test-writer-agent.md` | Corresponding Claude Code test-writing resources |
| Documentation maintenance | `documentation-agent.md` | Corresponding Claude Code documentation agent |
| Code cleanup / anti-pattern fix | `cleanup-agent.md` | Corresponding Claude Code cleanup agent |
| Bug diagnosis & fix | `bug-fix-agent.md` | *(Copilot-only)* |
| Performance profiling & optimisation | `performance-agent.md` | *(Copilot-only)* |

---

## Agent Discovery

GitHub Copilot discovers agent definition files from `.github/agents/`. When assigning work to
Copilot agents, referencing these files in the issue or prompt body improves output quality.

**Claude Code** discovers agents from `.claude/agents/` and skills from `.claude/skills/`.

---

## Related Resources

| Resource | Purpose |
|----------|---------|
| [`docs/ai/skills/README.md`](../skills/README.md) | Claude Code skill details |
| [`docs/ai/README.md`](../README.md) | Master AI resource index |
| [`docs/ai/ai-known-errors.md`](../ai-known-errors.md) | Error prevention registry |
| [`docs/ai/instructions/README.md`](../instructions/README.md) | Path-specific Copilot instructions |

---

*Last Updated: 2026-03-18*
