# DocFX API Documentation

DocFX generates browsable API documentation from XML doc comments in the C# and F# source code, combined with the markdown guides in `docs/`.

## Prerequisites

- .NET 9.0 SDK
- DocFX (installed as a .NET global tool)

```bash
dotnet tool update -g docfx
```

## Building Documentation

From repository root:

```bash
# Full build (API docs + conceptual docs)
docfx docs/docfx/docfx.json

# Serve locally for preview (opens http://localhost:8080)
docfx docs/docfx/docfx.json --serve
```

Output is generated to `docs/docfx/_site/`.

## Project Structure

```
docs/docfx/
├── docfx.json          # DocFX configuration (source paths, templates, metadata)
└── README.md           # This file
```

The `docfx.json` configuration pulls from:
- **API metadata**: All `.csproj` files under `src/` — generates API reference from XML doc comments
- **Conceptual docs**: Markdown files under `docs/` — architecture, guides, operations, etc.
- **Table of contents**: `docs/toc.yml` — top-level navigation structure

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
| Stale output | Delete `docs/docfx/_site/` and rebuild |

## CI Integration

The `documentation.yml` workflow builds the DocFX site and publishes it to **GitHub Pages** by default.

- On every push to `main` (and on the weekly schedule), the `build-docfx` job installs DocFX, builds the full site from source, and the `deploy-pages` job deploys it to the repository's GitHub Pages environment.
- On pull requests the site is built and validated but not deployed, so review builds are kept separate from production.
- Manual runs via `workflow_dispatch` also trigger a full build and deployment.

The live documentation is served at `https://<org>.github.io/<repo>/` once GitHub Pages is enabled in repository settings (**Settings → Pages → Source: GitHub Actions**).
