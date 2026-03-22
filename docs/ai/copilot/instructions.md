# Meridian - Copilot Instructions

**Last Updated:** 2026-03-16

> **Note:** For comprehensive project context, see [CLAUDE.md](../../../CLAUDE.md) in the repository root. For the master AI resource index, see [docs/ai/README.md](../../archive/docs/README.md).

## Coding Agent Optimization (GitHub Best Practices)

This repository uses native Copilot instruction files to improve agent output quality:

- Repository-wide guidance: `.github/copilot-instructions.md`
- Path-specific guidance: `.github/instructions/*.instructions.md`
- Environment bootstrap workflow: `.github/workflows/copilot-setup-steps.yml`

When assigning work to AI coding agents, prefer issues/prompts that include:

1. Clear problem statement.
2. Explicit acceptance criteria (including required tests).
3. Expected files/areas to change.
4. Any risk boundaries (security, prod critical paths, sensitive logic).

Use PR review comments to iterate in batches so the agent can address full feedback in one pass.

## Quick Start Checklist for Copilot Sessions

Before producing code, Copilot should:

1. Read repository-level instructions in `.github/copilot-instructions.md`.
2. Read any path-specific instruction file under `.github/instructions/` that matches touched files.
3. Review `docs/ai/ai-known-errors.md` and apply relevant prevention checks.
4. Confirm acceptance criteria include required validation commands.
5. Document assumptions and constraints directly in the PR description.

## Repository Overview

**Meridian** is a high-performance, cross-platform market data collection system for real-time and historical market microstructure data. It's a production-ready .NET 9.0 solution with F# domain libraries, supporting multiple data providers (Interactive Brokers, Alpaca, NYSE, Polygon, StockSharp) and offering flexible storage options.

| Attribute | Value |
|-----------|-------|
| **Project Type** | .NET Solution (C# and F#) |
| **Target Framework** | .NET 9.0 |
| **Languages** | C# 13, F# 8.0 |
| **Source Files** | 704 (692 C#, 12 F#) |
| **Test Files** | 241 across 4 test projects |
| **Main Projects** | 13 + 4 test + 1 benchmark |
| **Architecture** | Event-driven, monolithic core with optional UI projects |
| **Desktop App** | WPF (Windows) |

## AI Error Registry Workflow

Before implementing changes, review `docs/ai/ai-known-errors.md` and apply relevant prevention checks.
When an AI-caused regression is identified in GitHub, add label `ai-known-error` so the `AI Known Errors Intake` job in `.github/workflows/documentation.yml` can open a PR that records it.

## Build & Test Commands

**IMPORTANT:** Always use `/p:EnableWindowsTargeting=true` flag on non-Windows systems to avoid NETSDK1100 errors.

```bash
# Restore dependencies (ALWAYS run first)
dotnet restore Meridian.sln /p:EnableWindowsTargeting=true

# Build
dotnet build Meridian.sln -c Release --no-restore /p:EnableWindowsTargeting=true

# Run core tests
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj -c Release /p:EnableWindowsTargeting=true

# Run F# tests
dotnet test tests/Meridian.FSharp.Tests/Meridian.FSharp.Tests.fsproj -c Release /p:EnableWindowsTargeting=true

# Run WPF tests (Windows only)
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj -c Release /p:EnableWindowsTargeting=true

# Run UI service tests (Windows only)
dotnet test tests/Meridian.Ui.Tests/Meridian.Ui.Tests.csproj -c Release /p:EnableWindowsTargeting=true

# Run all tests
dotnet test -c Release /p:EnableWindowsTargeting=true
```

### Test Framework

- **Framework:** xUnit
- **Mocking:** Moq, NSubstitute
- **Assertions:** FluentAssertions
- **Coverage:** coverlet

### Make Commands (Alternative)

```bash
make help           # Show all available commands
make build          # Build the project
make test           # Run tests
make run-ui         # Run with web dashboard
make docker         # Build and start Docker container
make doctor         # Run environment diagnostics
```

### Running the Application

```bash
# Basic run (smoke test with no provider)
dotnet run --project src/Meridian/Meridian.csproj

# Run with web dashboard
dotnet run --project src/Meridian/Meridian.csproj -- --ui --http-port 8080

# Historical backfill
dotnet run --project src/Meridian/Meridian.csproj -- --backfill --backfill-provider stooq --backfill-symbols SPY,AAPL
```

## Project Structure

### Solution Layout

```
Meridian/
├── src/
│   ├── Meridian/              # Main console application & entry point
│   ├── Meridian.Application/  # Application services, commands, pipelines
│   ├── Meridian.Core/         # Core domain models, exceptions, config
│   ├── Meridian.Domain/       # Domain collectors, events, models
│   ├── Meridian.Contracts/    # Shared contracts, DTOs, API models (leaf, no deps)
│   ├── Meridian.Infrastructure/ # Provider implementations, data sources
│   ├── Meridian.ProviderSdk/  # Provider SDK interfaces & attributes
│   ├── Meridian.Storage/      # Storage sinks, archival, packaging
│   ├── Meridian.FSharp/       # F# domain library (12 files)
│   ├── Meridian.Ui/           # Web dashboard UI
│   ├── Meridian.Ui.Services/  # Shared UI services (cross-platform)
│   ├── Meridian.Ui.Shared/    # Shared UI endpoints & contracts
│   └── Meridian.Wpf/          # WPF desktop app (Windows)
├── tests/
│   ├── Meridian.Tests/        # Core C# unit tests
│   ├── Meridian.FSharp.Tests/ # F# unit tests
│   ├── Meridian.Wpf.Tests/    # WPF service tests (Windows only)
│   └── Meridian.Ui.Tests/     # UI service tests
├── benchmarks/                           # BenchmarkDotNet performance tests
├── build/                                # Build tooling (Python, Node.js, .NET)
├── config/                               # Configuration files
├── docs/                                 # Comprehensive documentation (148 Markdown files)
├── deploy/                               # Deployment configs (Docker, k8s, systemd)
└── scripts/                              # Automation & diagnostic scripts
```

For the complete repository tree with all 700+ files, see [`CLAUDE.md`](../../../CLAUDE.md) § Repository Structure.

## CI/CD Workflow

**GitHub Actions:** 26 workflows in `.github/workflows/`. For the full inventory, see [`CLAUDE.actions.md`](../claude/CLAUDE.actions.md).

Key workflows:
- `pr-checks.yml` — PR validation (format, build, test, coverage, AI review)
- `test-matrix.yml` — Multi-platform test matrix (Windows, Linux, macOS)
- `code-quality.yml` — Formatting, analyzers, AI quality suggestions
- `security.yml` — CodeQL, dependency review, secret detection
- `desktop-builds.yml` — WPF builds and MSIX packaging
- `documentation.yml` — Doc generation, TODO scanning, AI error intake
- `nightly.yml` — Full build + test + AI failure diagnosis

## Development Practices

### Configuration Management

- **NEVER commit credentials:** `appsettings.json` is gitignored
- **Use environment variables for secrets:** `ALPACA_KEY_ID`, `ALPACA_SECRET_KEY`, etc.
- **Copy sample config:** Always start with `cp config/appsettings.sample.json config/appsettings.json`

### Code Style

- C# 13 with nullable reference types enabled
- Implicit usings enabled
- Structured logging with semantic parameters (never string interpolation)
- `CancellationToken` on all async methods
- `sealed` classes by default
- Central Package Management — never add `Version=` to `<PackageReference>`

For the full conventions reference, see [`CLAUDE.md`](../../../CLAUDE.md) § Critical Rules and § Coding Conventions.

## Common Issues & Workarounds

| Issue | Solution |
|-------|----------|
| Build fails with NETSDK1100 on Linux/macOS | Use `/p:EnableWindowsTargeting=true` (set in `Directory.Build.props`) |
| `appsettings.json` not found | `cp config/appsettings.sample.json config/appsettings.json` |
| Data or logs directories missing | `mkdir -p data logs` or `make setup-config` |
| Docker build fails | Ensure `appsettings.json` exists before building |
| Tests fail due to missing config | Tests should mock configuration; check test setup |
| NU1008 error on restore | Remove `Version=` from `<PackageReference>` (CPM is active) |

## Quick Decision Tree

| Task | Where to Look |
|------|---------------|
| Adding new functionality | Appropriate layer in `src/`, follow existing patterns |
| Fixing a bug | Add test first in `tests/`, then fix |
| Working with providers | `src/Meridian.Infrastructure/Adapters/` |
| Storage changes | `src/Meridian.Storage/` |
| WPF desktop | `src/Meridian.Wpf/` |
| Run tests | `dotnet test tests/Meridian.Tests/` |
| Build | `dotnet restore && dotnet build -c Release` (both with `/p:EnableWindowsTargeting=true`) |
| Start app | `dotnet run --project src/Meridian/Meridian.csproj -- --ui` |

## Related Resources

- **Master AI index:** [`docs/ai/README.md`](../../archive/docs/README.md)
- **Root context:** [`CLAUDE.md`](../../../CLAUDE.md)
- **Error prevention:** [`docs/ai/ai-known-errors.md`](../ai-known-errors.md)
- **Code review:** [`.github/agents/code-review-agent.md`](../../../.github/agents/code-review-agent.md)
- **Prompt templates:** [`.github/prompts/README.md`](../../../.github/prompts/README.md)
- **CI/CD details:** [`docs/ai/claude/CLAUDE.actions.md`](../claude/CLAUDE.actions.md)
