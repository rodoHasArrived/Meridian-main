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

## Accounting reads

`get_run_trial_balance(runId, asOf)` reads the workstation trial-balance endpoint at an explicit
inclusive timestamp using the same issued-session flow as the other ledger tools. Configure
`MDC_API_URL` (default `http://localhost:8080/`) and either `MDC_LEDGER_SESSION_TOKEN` or
`MDC_LEDGER_USERNAME`/`MDC_LEDGER_PASSWORD`, as described below. HTTPS is required for non-loopback
hosts; redirects are disabled. Authentication, tenant scope and run
visibility remain enforced by the workstation API. The tool does not open ledger archives directly,
mutate books, or expose refused response bodies. Large responses direct callers to the export flow.
Never commit session tokens.

## Important workflows

Use this module for MCP host behavior and AI-assisted tool/resource access.

### Ledger reads

`LedgerTools` forwards journal, current/as-of trial-balance, account-summary, running-balance,
per-symbol and accounting-completeness reads to the workstation API. Run tools accept a retained
`runId`; posted-book tools accept a `periodId`, and completeness requires the exact book, period,
aggregate and financial-account scope. The API applies its usual permissions and tenant visibility;
these tools do not read journal files directly or grant posting authority.

Set `MDC_API_URL` to the API origin (default `http://localhost:8080/`). HTTPS is required for
non-loopback hosts. HTTP password sign-in on loopback is supported only when the server explicitly
declares its `LocalWorkstation` posture and receives a loopback connection. Other server postures
issue Secure session cookies and require an HTTPS API URL, including on localhost; the client
honors those cookie flags and reports a configuration error instead of downgrading them.
Configure one of these authentication modes in the MCP process's secret
environment settings:

- `MDC_LEDGER_SESSION_TOKEN`: the value of an already-issued Meridian `mdc-session` cookie,
  without the cookie name or other headers. Obtain it through the deployment's existing operator
  sign-in flow (`POST /api/auth/login`). The token is a secret with the session's expiry and
  revocation behavior. Replace it when the operator signs out or it expires.
- `MDC_LEDGER_USERNAME` and `MDC_LEDGER_PASSWORD`: an existing enabled Meridian operator account.
  The client signs in through `POST /api/auth/login`, keeps the issued session cookie in process
  memory, and renews it once if a read returns 401. Concurrent reads share that renewal. If a
  token and credentials are both configured, the token is tried first, then the same configured
  operator credentials may establish a fresh session.

The account must have a server-configured company and the applicable strategy/ledger read
permissions. Tenant and company are resolved from that authenticated account; caller-supplied
tenant headers never establish scope. `MDC_API_KEY` alone cannot authenticate these scoped
session-owned reads. If the deployment additionally requires an API key at its login endpoint,
that existing key may be configured and is sent only on the sign-in request. Its existing policy
must already allow that sign-in; otherwise use an issued operator session. No role or API gate
is widened by the MCP client.

Redirects are disabled. Credentials and session cookies are never returned by tools or persisted
by the MCP client. A 403 or 404 is propagated without another sign-in or a fallback identity.
Journal and symbol tools use `offset`/`limit` pagination (default 500, maximum 10,000).

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

## Audit hardening (2026-11)

- **Repository path boundary.** `RepoPathService.ResolveInsideRoot` compares paths case-sensitively except on Windows and macOS. It resolves symbolic links on every existing path component before checking containment, and tolerates a trailing separator in `MDC_REPO_ROOT`. A symlink inside the repository that points outside it is rejected.

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
