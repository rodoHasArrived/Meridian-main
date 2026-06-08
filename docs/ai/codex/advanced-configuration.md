# Codex Advanced Configuration

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-06-08

Use this guide when a Meridian Codex setup needs more control than the quick start provides:
custom models or providers, profile overlays, sandbox and approval tuning, hooks, telemetry,
notifications, history, or terminal behavior. For first-run setup, start with
[`quickstart.md`](quickstart.md); for the repo-local Codex workflow index, use
[`README.md`](README.md).

## Choose The Smallest Configuration Surface

| Need | Prefer | Avoid |
| --- | --- | --- |
| Temporary one-run override | Dedicated CLI flag, then `--config key=value` | Editing shared config for a one-off run |
| Reusable personal setup | `~/.codex/<profile>.config.toml` plus `--profile <name>` | Legacy `[profiles.<name>]` tables in `config.toml` |
| Repository defaults | Trusted `<repo>/.codex/config.toml` | Putting credentials, provider auth, notifications, or telemetry in project config |
| OpenAI proxy or data-residency base URL | `openai_base_url` in user config | Defining a forbidden `[model_providers.openai]` override |
| Non-OpenAI/custom provider | `[model_providers.<id>]` in user config | Reusing reserved provider IDs: `openai`, `ollama`, or `lmstudio` |
| Machine notifications or telemetry | User-level `notify` or `[otel]` | Project-local commands that run on every clone |

## Configuration Loading Order

Codex layers configuration from broad to narrow scope. Later layers win for keys they are allowed to
set.

1. **System and managed policy**: host- or organization-managed defaults.
2. **User config**: `~/.codex/config.toml` under `CODEX_HOME`.
3. **Profile overlay**: `~/.codex/<profile>.config.toml` when `--profile <profile>` is passed.
4. **Trusted project config**: every `.codex/config.toml` from the project root to the current
   working directory, with the closest file winning on conflicts.
5. **CLI overrides**: dedicated flags such as `--model`, then generic `--config` values for one run.

Project-scoped `.codex/config.toml` files load only when the project is trusted. Relative paths in a
project config, such as `model_instructions_file`, are resolved relative to the `.codex/` folder that
contains the file.

### Project Config Safety Boundary

Keep host-local or credential-sensitive settings in user config. Project-local config is ignored for
keys that can redirect credentials, alter host request metadata, select profiles, or run machine
commands. Do **not** put these keys in a repo `.codex/config.toml`:

- `openai_base_url`
- `chatgpt_base_url`
- `apps_mcp_product_sku`
- `model_provider`
- `model_providers`
- `notify`
- `profile`
- `profiles`
- `experimental_realtime_ws_base_url`
- `otel`

Codex prints a startup warning when it sees one of those keys in a project-local config. Move
provider, notification, profile, and telemetry settings into user-level config files instead.

## Profiles

Profiles are named user-level overlays. They are useful for switching between fast implementation,
deep review, local OSS, or restricted-security modes without rewriting `~/.codex/config.toml`.

Profile names may contain letters, numbers, hyphens, and underscores. Each profile lives in its own
file and uses top-level keys:

```toml
# ~/.codex/deep-review.config.toml
model = "gpt-5.5"
model_reasoning_effort = "xhigh"
approval_policy = "on-request"
model_catalog_json = "/Users/me/.codex/model-catalogs/deep-review.json"
```

Run with the profile explicitly:

```bash
codex --profile deep-review
codex exec --profile deep-review "review this change"
```

For Codex 0.134.0 and later, migrate legacy profiles out of `~/.codex/config.toml`: remove
`profile = "profile-name"`, remove `[profiles.profile-name]`, and create
`~/.codex/profile-name.config.toml` with the same settings at the top level.

## One-Off CLI Overrides

Prefer a dedicated flag when Codex exposes one:

```bash
codex --model gpt-5.4
```

Use `-c` or `--config` for arbitrary keys. Values are parsed as TOML, so quote strings and any value
that your shell might split:

```bash
codex --config model='"gpt-5.4"'
codex --config sandbox_workspace_write.network_access=true
codex --config 'shell_environment_policy.include_only=["PATH","HOME"]'
```

Dot notation sets nested values, such as `mcp_servers.context7.enabled=false`. If Codex cannot parse
a value as TOML, it treats the value as a string.

## Local State Locations

Codex stores user-local state under `CODEX_HOME`, which defaults to `~/.codex`.

Common files include:

- `config.toml`: user-local configuration.
- `auth.json` or OS keychain/keyring entries: local credentials, depending on credential mode.
- `history.jsonl`: local transcript history when history persistence is enabled.
- Logs, caches, and other per-user state.

Team defaults, checked-in rules, and reusable capabilities belong in repo or system paths; secrets and
machine-specific commands stay in user config.

## Providers And Model Routing

### Built-In OpenAI Provider

If you only need to route the built-in OpenAI provider through a proxy, router, or data-residency
endpoint, set `openai_base_url` in user config:

```toml
openai_base_url = "https://us.api.openai.com/v1"
```

Do not define `[model_providers.openai]`; built-in provider IDs cannot be overridden.

### Custom Providers

A custom provider defines a base URL, wire API, authentication method, and optional headers. Custom
provider IDs cannot be `openai`, `ollama`, or `lmstudio`.

```toml
model = "gpt-5.4"
model_provider = "proxy"

[model_providers.proxy]
name = "OpenAI using LLM proxy"
base_url = "https://proxy.example.com/v1"
env_key = "OPENAI_API_KEY"
wire_api = "responses"
```

Optional request headers can be literal values or read from environment variables:

```toml
[model_providers.example]
http_headers = { "X-Example-Header" = "example-value" }
env_http_headers = { "X-Example-Features" = "EXAMPLE_FEATURES" }
```

For providers that require an external credential helper, use command-backed bearer-token auth:

```toml
[model_providers.proxy]
name = "OpenAI using LLM proxy"
base_url = "https://proxy.example.com/v1"
wire_api = "responses"

[model_providers.proxy.auth]
command = "/usr/local/bin/fetch-codex-token"
args = ["--audience", "codex"]
timeout_ms = 5000
refresh_interval_ms = 300000
```

The auth command receives no stdin and must print a non-empty token to stdout. Codex trims surrounding
whitespace. Set `refresh_interval_ms = 0` to refresh only after an authentication retry. Do not combine
`[model_providers.<id>.auth]` with `env_key`, `experimental_bearer_token`, or
`requires_openai_auth`.

### Amazon Bedrock

Codex includes a built-in `amazon-bedrock` provider. Select it directly and use the nested AWS block
for profile and region overrides:

```toml
model_provider = "amazon-bedrock"
model = "<bedrock-model-id>"

[model_providers.amazon-bedrock.aws]
profile = "default"
region = "eu-central-1"
```

If `profile` is omitted, Codex uses the standard AWS credential chain.

### OSS Mode And Local Providers

For local open-source providers such as Ollama or LM Studio, use `--oss`. If no provider is passed,
Codex uses `oss_provider`:

```toml
oss_provider = "ollama" # or "lmstudio"
```

### Azure And Per-Provider Tuning

```toml
[model_providers.azure]
name = "Azure"
base_url = "https://YOUR_PROJECT_NAME.openai.azure.com/openai"
env_key = "AZURE_OPENAI_API_KEY"
query_params = { api-version = "2025-04-01-preview" }
wire_api = "responses"
request_max_retries = 4
stream_max_retries = 10
stream_idle_timeout_ms = 300000
```

## Model Behavior Knobs

Use these when a provider and model support them:

```toml
model_reasoning_summary = "none"          # Disable summaries.
model_verbosity = "low"                   # Shorten Responses API output.
model_supports_reasoning_summaries = true # Force reasoning-summary support.
model_context_window = 128000             # Context window size.
```

`model_verbosity` applies to providers using the Responses API. Chat Completions providers ignore it.

## Approvals, Sandboxes, And Permissions

Approval policy controls when Codex pauses for human or automatic review. Sandbox mode controls file
and network boundaries.

```toml
approval_policy = "untrusted" # Other common values: "on-request", "never", or granular policy.
approvals_reviewer = "user"   # Or "auto_review" for eligible automatic review.
sandbox_mode = "workspace-write"
allow_login_shell = false     # Optional hardening for shell tools.
```

Granular approvals let selected prompt categories fail closed while others remain interactive:

```toml
approval_policy = { granular = {
  sandbox_approval = true,
  rules = true,
  mcp_elicitations = true,
  request_permissions = false,
  skill_approval = false
} }
```

Workspace-write sandbox tuning:

```toml
[sandbox_workspace_write]
exclude_tmpdir_env_var = false
exclude_slash_tmp = false
writable_roots = ["/Users/YOU/.pyenv/shims"]
network_access = false
```

Automatic review policy text is local unless managed policy overrides it:

```toml
[auto_review]
policy = """
Use your organization's automatic review policy.
"""
```

Use `sandbox_mode = "danger-full-access"` only when the surrounding environment already isolates the
process. In workspace-write environments, `.git/` and `.codex/` may remain read-only even when the
workspace is writable; commands such as `git commit` can still require approval outside the sandbox.

## Shell Environment Policy

Use `shell_environment_policy` to prevent subprocesses from inheriting unnecessary secrets while
preserving required paths and flags:

```toml
[shell_environment_policy]
inherit = "none"
set = { PATH = "/usr/bin", MY_FLAG = "1" }
ignore_default_excludes = false
exclude = ["AWS_*", "AZURE_*"]
include_only = ["PATH", "HOME"]
```

Patterns are case-insensitive globs. Keep `ignore_default_excludes = false` so Codex applies its
default key, secret, and token filters before your include or exclude rules.

## Hooks

Codex can load lifecycle hooks from `hooks.json` or inline `[hooks]` tables next to active config
layers. Common locations are:

- `~/.codex/hooks.json`
- `~/.codex/config.toml`
- `<repo>/.codex/hooks.json`
- `<repo>/.codex/config.toml`

Project-local hooks load only for trusted project `.codex/` layers. User-level hooks are independent
of project trust.

Inline TOML hooks use the same event structure as `hooks.json`:

```toml
[[hooks.PreToolUse]]
matcher = "^Bash$"

[[hooks.PreToolUse.hooks]]
type = "command"
command = '/usr/bin/python3 "$(git rev-parse --show-toplevel)/.codex/hooks/pre_tool_use_policy.py"'
timeout = 30
statusMessage = "Checking Bash command"
```

If the same layer contains both `hooks.json` and inline hooks, Codex loads both and warns. Prefer one
representation per layer.

## Project Root Detection

Codex discovers project configuration and `AGENTS.md` guidance by walking upward from the working
directory until it finds a project root. By default, `.git` marks the root.

Customize root detection with `project_root_markers`:

```toml
project_root_markers = [".git", ".hg", ".sl"]
```

Set an empty array to skip parent-directory discovery and treat the current working directory as the
root:

```toml
project_root_markers = []
```

## MCP Servers And Agent Roles

- Configure MCP servers in the dedicated MCP configuration docs for your Codex version.
- Configure subagent roles under `[agents]` in `config.toml` and keep role definitions user- or
  project-appropriate. In Meridian, repository-local role and skill inventory starts from
  [`README.md`](README.md) and `.codex/agents/`.

## Observability, Metrics, And Feedback

### OpenTelemetry Export

OTel export is disabled by default. Opt in with `[otel]`:

```toml
[otel]
environment = "staging"
exporter = "none"       # Use "otlp-http" or "otlp-grpc" to send events.
log_user_prompt = false # Keep prompt content redacted unless explicitly approved.
```

HTTP exporter example:

```toml
[otel]
exporter = { otlp-http = {
  endpoint = "https://otel.example.com/v1/logs",
  protocol = "binary",
  headers = { "x-otlp-api-key" = "${OTLP_TOKEN}" }
}}
```

gRPC exporter example:

```toml
[otel]
exporter = { otlp-grpc = {
  endpoint = "https://otel.example.com:4317",
  headers = { "x-otlp-meta" = "abc123" }
}}
```

Representative emitted events include conversation starts, API requests, SSE/WebSocket events, user
prompt metadata, tool decisions, and tool results. When metrics are enabled, Codex emits counters and
duration histograms for API, stream, WebSocket, tool, hook, MCP, memory, task, thread, and Windows
sandbox activity. Treat prompt logging, tool-output snippets, and exporter headers as security- and
privacy-sensitive.

### Anonymous Usage Metrics

Codex may send anonymous usage and health metrics separately from OTel export. Disable machine-wide
metrics collection with:

```toml
[analytics]
enabled = false
```

### Feedback Controls

Disable `/feedback` submission across Codex surfaces with:

```toml
[feedback]
enabled = false
```

## Reasoning Output Controls

Suppress noisy reasoning events in logs:

```toml
hide_agent_reasoning = true
```

Surface raw reasoning only when the model emits it and the workflow can safely handle it:

```toml
show_raw_agent_reasoning = true
```

Some providers, including some OSS models, do not emit raw reasoning; this setting has no visible
effect for them.

## Notifications

Use `notify` for external programs such as desktop notifiers, chat webhooks, or CI side-channel
updates. The command receives one JSON argument for supported events, currently including
`agent-turn-complete`:

```toml
notify = ["python3", "/path/to/notify.py"]
```

Common notification fields include `type`, `thread-id`, `turn-id`, `cwd`, `input-messages`, and
`last-assistant-message`.

Use built-in TUI notifications for terminal-local alerts:

- `tui.notifications`: enable/disable or restrict event types.
- `tui.notification_method`: `auto`, `osc9`, or `bel`.
- `tui.notification_condition`: `unfocused` or `always`.

`notify` runs an external program. `tui.notifications` stays inside the terminal UI.

## History And File Links

Disable local transcript persistence:

```toml
[history]
persistence = "none"
```

Cap history size and let Codex compact oldest entries when the cap is exceeded:

```toml
[history]
max_bytes = 104857600 # 100 MiB
```

Configure clickable file citations for supported terminal/editor integrations:

```toml
file_opener = "vscode" # or "cursor", "windsurf", "vscode-insiders", or "none"
```

## Project Instructions Discovery

Codex reads `AGENTS.md` and related fallback filenames into the first turn, bounded by these knobs:

- `project_doc_max_bytes`: maximum bytes read from each instruction file.
- `project_doc_fallback_filenames`: additional filenames to try when `AGENTS.md` is missing at a
  directory level.

In Meridian, keep root `AGENTS.md` compact and route detailed workflow guidance to canonical docs,
`.codex/skills/_shared/project-context.md`, and `docs/ai/codex/quickstart.md`.

## TUI Options

Codex interactive mode exposes TUI-specific settings under `[tui]`:

- `tui.notifications`
- `tui.notification_method`
- `tui.notification_condition`
- `tui.animations`
- `tui.alternate_screen`
- `tui.show_tooltips`

Use `tui.alternate_screen = "never"` when preserving terminal scrollback is more important than the
full-screen TUI experience.

## Safe Meridian Defaults

For this repository, prefer these defaults unless a task explicitly needs different behavior:

1. Keep provider credentials, telemetry exporters, notification commands, and profile overlays in
   user-level config.
2. Keep repo-local `.codex/config.toml` focused on safe project defaults.
3. Use `--profile` for repeatable modes such as deep review or restricted execution.
4. Use one-off `--config` overrides for experiments, then remove them from shell history if they
   include sensitive values.
5. Run the narrowest relevant validation command after changing AI documentation; for docs-only
   changes, use `git diff --check -- <paths>`.
