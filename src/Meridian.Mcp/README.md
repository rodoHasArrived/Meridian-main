---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-MCP
path: src/Meridian.Mcp
status: active
owner_lane: Docs and Automation
last_reviewed: 2026-05-20
---

# src/Meridian.Mcp

## Purpose

Meridian MCP is the minimal MCP host surface for tools, prompts, and resources.

## Layer responsibility

This layer should expose AI/tooling access without mixing MCP mechanics into UI or application feature code.

## Key folders and files

- `Meridian.Mcp.csproj` - minimal MCP host project.
- Tool, resource, and prompt entrypoints for MCP clients.

## Important workflows

Use this module for MCP host behavior and AI-assisted tool/resource access.

## Diagrams

See `DIA-ASSURANCE-LOOP` in `docs/source/data/diagram-index.yml`.

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-MCP -->
| Roadmap item | Title |
| --- | --- |
| `W6-LIVE-001` | Live-readiness governance |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-MCP -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```bash
dotnet run --project src/Meridian.Mcp/Meridian.Mcp.csproj -- --help
```

## Change rules

Keep MCP access explicit, documented, and separate from user-facing workflow behavior.

## Related docs

- `docs/ai/navigation/README.md`
- `docs/ai/generated/repo-navigation.md`
