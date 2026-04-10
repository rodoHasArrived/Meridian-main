# DocFX API Documentation

DocFX generates browsable API documentation from XML doc comments in the C# source code, combined with the markdown guides in `docs/`.

## Prerequisites

- .NET 9.0 SDK
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
docfx docs/docfx/docfx.json

# Serve locally for preview (opens http://localhost:8080)
docfx docs/docfx/docfx.json --serve
```

Output is generated to `docs/_site/` (gitignored — rebuild each time).

## Project Structure

```
docs/docfx/
├── docfx.json          # DocFX configuration (source paths, templates, metadata)
├── filterConfig.yml    # API filter rules (exclude private/generated types)
├── api/
│   └── index.md        # API reference landing page
└── README.md           # This file
```

The `docfx.json` configuration pulls from:
- **API metadata**: All public `.csproj` files under `src/` — generates API reference from XML doc comments
- **Conceptual docs**: Markdown files under `docs/` — architecture, guides, operations, etc.
- **Table of contents**: `docs/toc.yml` — top-level navigation structure

### Projects included in API reference

| Assembly | Purpose |
|----------|---------|
| `Meridian` | Main entry point and host |
| `Meridian.Application` | Application services (pipeline, backfill, monitoring) |
| `Meridian.Contracts` | Shared DTOs, domain events, and interface contracts |
| `Meridian.Core` | Core abstractions (config, exceptions, logging) |
| `Meridian.Domain` | Domain model (collectors, market events, publishers) |
| `Meridian.ProviderSdk` | Provider SDK interfaces |
| `Meridian.Infrastructure` | Concrete provider adapters |
| `Meridian.Infrastructure.CppTrader` | CppTrader integration |
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
| `Meridian.Mcp` | MCP server tools |
| `Meridian.McpServer` | Standalone MCP server |

## Adding New Documentation

### New conceptual page

1. Create a `.md` file in the appropriate `docs/` subdirectory
2. Add an entry to the relevant `toc.yml` or parent `README.md`
3. Rebuild: `docfx docs/docfx/docfx.json`

### New API namespace

New namespaces are discovered automatically from source code. Ensure classes have XML doc comments (`///` summary tags) for useful output.

## Troubleshooting

| Issue | Solution |
|-------|----------|
| Missing API pages | Ensure the project builds successfully first (`dotnet build`) — DocFX needs compiled assemblies |
| Broken cross-references | Use `<see cref="ClassName"/>` in XML docs; DocFX resolves these to hyperlinks |
| Mermaid diagrams not rendered | Export to SVG/PNG and reference from markdown — Mermaid support depends on template |
| Stale output | Delete `docs/_site/` and rebuild |

## CI Integration

The `documentation.yml` workflow builds the DocFX site and publishes it to **GitHub Pages** by default.

- On every push to `main` (and on the weekly schedule), the `build-docfx` job installs DocFX, builds the full site from source, and the `deploy-pages` job deploys it to the repository's GitHub Pages environment.
- On pull requests the site is built and validated but not deployed, so review builds are kept separate from production.
- Manual runs via `workflow_dispatch` also trigger a full build and deployment.

The live documentation is served at `https://<org>.github.io/<repo>/` once GitHub Pages is enabled in repository settings (**Settings → Pages → Source: GitHub Actions**).
