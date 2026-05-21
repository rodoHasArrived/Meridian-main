---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-MCP-SERVER
path: src/Meridian.McpServer
status: active
owner_lane: Docs and Automation
last_reviewed: 2026-05-20
---

# src/Meridian.McpServer

## Purpose

McpServer exposes Meridian market-data and repository assistance surfaces through an MCP server.

## Layer responsibility

This layer owns MCP server composition, tool routing, and testable AI-facing integration behavior.

## Key folders and files

- `Meridian.McpServer.csproj` - MCP server project boundary.
- Server, tool, resource, and integration support files.

## Important workflows

Use this module when MCP clients need richer market-data or repository tooling than the minimal host provides.

## Diagrams

See `DIA-ASSURANCE-LOOP` in `docs/source/data/diagram-index.yml`.

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-MCP-SERVER -->
| Roadmap item | Title |
| --- | --- |
| `W6-LIVE-001` | Live-readiness governance |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-MCP-SERVER -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```bash
dotnet test tests/Meridian.McpServer.Tests/Meridian.McpServer.Tests.csproj --logger "console;verbosity=normal"
```

## Change rules

Keep tool contracts documented and avoid adding MCP behavior that bypasses normal validation or secret-safety rules.

## Related docs

- `docs/ai/navigation/README.md`
- `docs/ai/generated/repo-navigation.md`
