# Generated Documentation

This folder contains documentation that is **auto-generated** from code annotations, build scripts, and repository structure. Do not edit these files manually; they will be overwritten on the next generation run.

## Files

| File | Source | Description |
|------|--------|-------------|
| `project-context.md` | Code annotations | Key interfaces, services, and project structure |
| `repository-structure.md` | Directory scan | Full repository file tree |
| `provider-registry.md` | Provider attributes | Registered data providers and capabilities |
| `adr-index.md` | `docs/adr/` folder | Architecture Decision Record index |
| `workflows-overview.md` | `.github/workflows/` | CI/CD workflow summary |

## How generation works

Generation is triggered by:
- The `documentation.yml` GitHub Actions workflow (on push/PR to docs paths)
- The `make docs` Makefile target
- Manual execution of scripts in `build/scripts/docs/`

Key scripts:
- `build/scripts/docs/update-claude-md.py` - Regenerates `CLAUDE.md` and context files
- `build/scripts/docs/generate-structure-docs.py` - Generates repository structure
- `build/scripts/docs/scan-todos.py` - Scans codebase for TODO comments
- `build/dotnet/DocGenerator/` - .NET-based documentation generator

## Adding new generated files

1. Create the generation logic in `build/scripts/docs/` or `build/dotnet/DocGenerator/`
2. Add the output file to this folder
3. Update the table above
4. Add the generation step to the `documentation.yml` workflow

---

*This README is hand-maintained. The files it describes are auto-generated.*
