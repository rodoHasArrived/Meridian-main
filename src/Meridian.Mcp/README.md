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
- `Tools/RepoEditTools.cs` - preview/apply wrappers for deterministic scoped repo edits.
- `Tools/ToolProcessRunner.cs` and `Tools/ToolProcessExecution.cs` - bounded tool execution and
  operating-system process containment.
- Tool, resource, and prompt entrypoints for MCP clients.

## Important workflows

Use this module for MCP host behavior and AI-assisted tool/resource access.

### Preview-first repository edits

The repo edit tools expose a thin MCP wrapper over `build/scripts/ai/ai-edit-tool.py`:

- `preview_repo_edit(recipeJson, scope)` creates a JSON and Markdown plan under
  `.codex/tmp/ai-edit-plans/` and returns a compact summary.
- `explain_repo_edit_plan(planPath)` summarizes touched files, edit counts, risk flags, and
  suggested validation from an existing plan.
- `apply_repo_edit(planPath)` applies only a saved plan after the Python tool verifies per-file
  SHA-256 hashes and planned snippets.

The MCP layer validates JSON arguments, resolves plan paths through `RepoPathService`, and keeps all
rewrite policy in the CLI. It must not bypass the CLI guardrails or write directly to repository
files.

Repository-edit subprocesses run inside a kill-on-close Job Object on Windows and a dedicated
process group on Linux. Windows child creation uses an explicit standard-handle allowlist. Linux
startup verifies the `setsid` process group before releasing the edit tool and keeps that group
identity pinned until parent cleanup. Cancellation, deadline expiry, and a root-process exit
terminate the containment boundary before bounded output draining, preventing an ordinary
descendant from continuing a repository mutation. Linux hosts must provide the util-linux `setsid`
executable and `/bin/sh`; startup fails closed when either containment prerequisite is unavailable.
The Linux boundary is process-group based rather than cgroup based, so repository-edit tools must
not deliberately create a new session or otherwise detach themselves from the inherited process
group.

## Diagrams

See `DIA-ASSURANCE-LOOP` in `docs/source/data/diagram-index.yml`.

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-MCP -->
| Roadmap item | Title |
| --- | --- |
| `W7-LIVE-001` | Live-readiness governance |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-MCP -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```bash
dotnet run --project src/Meridian.Mcp/Meridian.Mcp.csproj -- --help
dotnet build src/Meridian.Mcp/Meridian.Mcp.csproj
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~ToolProcessRunnerTests"
python -m unittest build.scripts.ai.tests.test_ai_edit_tool
```

## Change rules

Keep MCP access explicit, documented, and separate from user-facing workflow behavior.

## Related docs

- `docs/ai/navigation/README.md`
- `docs/ai/generated/repo-navigation.md`
