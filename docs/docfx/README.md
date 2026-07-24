# DocFX API Documentation

**Status:** supporting-generated
**Owner:** core-team
**Reviewed:** 2026-07-19

DocFX generates browsable API documentation from XML doc comments in the C# source code, combined with the markdown guides in `docs/`.

## Prerequisites

- .NET 10 SDK
- DocFX (installed as a .NET global tool)

```bash
dotnet tool update -g docfx
```

## Building Documentation

From repository root:

```bash
# Build the project first so DocFX can extract XML documentation
dotnet build Meridian.sln -c Release /p:EnableWindowsTargeting=true /p:GenerateDocumentationFile=true

# Full build (API docs + conceptual docs)
docfx docfx.json

# Serve locally for preview (opens http://localhost:8080)
docfx docfx.json --serve
```

Output is generated to `docs/_site/` (gitignored — rebuild each time).

The canonical DocFX configuration now lives at the repository root in `docfx.json`. The `docs/docfx/` folder holds API filters, the tracked API landing page, and this guide. Generated API metadata under `docs/docfx/api/` is rebuilt by DocFX and ignored by Git.

## Project Structure

```text
docfx.json              # Canonical DocFX configuration (repo root)
docs/docfx/
├── filterConfig.yml    # API filter rules (exclude private/generated types)
├── api/
│   ├── index.md        # API reference landing page
│   └── *.yml           # Generated API metadata, ignored by Git
└── README.md           # This file
```

The root `docfx.json` configuration pulls from:

- **API metadata**: All public `.csproj` files under `src/` — generates API reference from XML doc comments
- **Conceptual docs**: Markdown files under `docs/` — architecture, guides, operations, etc.
- **Table of contents**: `docs/toc.yml` — top-level navigation structure

Metadata scratch files such as `docfx-log.json`, `temp-metadata-only.json`,
`docs/docfx/api/.manifest`, and `docs/docfx/api/*.yml` are local generated
output and should not be committed. If API metadata contains absolute paths from
an older checkout, delete the generated metadata and regenerate it from the
current repository root before publishing documentation.

### Projects included in API reference

| Assembly | Purpose |
| ---------- | --------- |
| `Meridian` | Main entry point and host |
| `Meridian.Application` | Application services (pipeline, backfill, monitoring) |
| `Meridian.Contracts` | Shared DTOs, domain events, and interface contracts |
| `Meridian.Core` | Core abstractions (config, exceptions, logging) |
| `Meridian.Domain` | Domain model (collectors, market events, publishers) |
| `Meridian.ProviderSdk` | Provider SDK interfaces |
| `Meridian.Infrastructure` | Concrete provider adapters |
| `Meridian.Storage` | Storage sinks, WAL, archival, and export |
| `Meridian.Execution` | OMS, paper trading, and brokerage adapters |
| `Meridian.Execution.Sdk` | Brokerage gateway SDK |
| `Meridian.Backtesting` | Backtesting engine |
| `Meridian.Backtesting.Sdk` | Strategy SDK |
| `Meridian.Strategies` | Strategy lifecycle and portfolio tracking |
| `Meridian.Risk` | Risk validation rules |
| `Meridian.Ledger` | Double-entry ledger |
| `Meridian.Ui.Services` | UI service abstractions |
| `Meridian.Ui.Shared` | Shared HTTP endpoints and UI services |
| `Meridian.Mcp` | Stdio MCP host for AI tooling, tools, resources, and prompts |

## Adding New Documentation

### New conceptual page

1. Create a `.md` file in the appropriate `docs/` subdirectory
2. Add an entry to the relevant `toc.yml` or parent `README.md`
3. Rebuild: `docfx docfx.json`

### New API namespace

New namespaces are discovered automatically from source code. Ensure classes have XML doc comments (`///` summary tags) for useful output.

## Troubleshooting

| Issue | Solution |
| ------- | ---------- |
| Missing API pages | Ensure the project builds successfully first (`dotnet build`) — DocFX needs compiled assemblies |
| Broken cross-references | Use `<see cref="ClassName"/>` in XML docs; DocFX resolves these to hyperlinks |
| Mermaid diagrams not rendered | Export to SVG/PNG and reference from markdown — Mermaid support depends on template |
| Stale output | Delete `docs/_site/` and rebuild |

## CI Integration

The `documentation.yml` workflow validates the tracked documentation automation outputs and refreshes Mermaid, UML, and WPF UI diagram artifacts when documentation-facing files change.

- On pull requests it rebuilds the tracked docs outputs and fails if committed generated content has drifted.
- On pushes to `main` it performs the same refresh/gate steps for the default branch.
- Manual runs via `workflow_dispatch` are available when you want a dedicated documentation refresh pass.

DocFX site generation remains a manual/local step (`docfx docfx.json`) rather than a GitHub Pages deployment workflow.
