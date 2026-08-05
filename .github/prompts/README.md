# AI Assistant Prompts

This directory contains reusable prompt templates for AI assistants (Claude, Copilot, ChatGPT, etc.) working with the Meridian codebase.

## Available Prompts

| Prompt | Description | Use When |
|--------|-------------|----------|
| [project-context.prompt.yml](project-context.prompt.yml) | Project overview, conventions, and architecture | Starting any task, need project context |
| [code-review.prompt.yml](code-review.prompt.yml) | Comprehensive code review guidelines | Reviewing PRs or code changes |
| [add-data-provider.prompt.yml](add-data-provider.prompt.yml) | Guide for adding new data providers | Implementing new market data integrations |
| [provider-implementation-guide.prompt.yml](provider-implementation-guide.prompt.yml) | Detailed IMarketDataClient patterns | Implementing streaming providers |
| [operations-continuity-core.prompt.yml](operations-continuity-core.prompt.yml) | Operations continuity core implementation workflow | Building fund-account operational/accounting continuity workflow |
| [write-unit-tests.prompt.yml](write-unit-tests.prompt.yml) | Unit test generation guidelines | Writing tests for components |
| [explain-architecture.prompt.yml](explain-architecture.prompt.yml) | In-depth architecture explanation | Understanding system design |
| [troubleshoot-issue.prompt.yml](troubleshoot-issue.prompt.yml) | Diagnose and resolve an issue | Debugging problems |
| [optimize-performance.prompt.yml](optimize-performance.prompt.yml) | Performance optimization guidance | Improving hot paths |
| [runtime-observability-diagnostics.prompt.yml](runtime-observability-diagnostics.prompt.yml) | Runtime observability and diagnostics hardening | Improving logs, metrics, tracing, health state, and diagnostic bundles |
| [configure-deployment.prompt.yml](configure-deployment.prompt.yml) | Deployment configuration help | Setting up environments |
| [add-export-format.prompt.yml](add-export-format.prompt.yml) | Export format implementation | Adding new export types |
| [wpf-debug-improve.prompt.yml](wpf-debug-improve.prompt.yml) | WPF debugging and improvement guide | Fixing or completing WPF UI work |
| [wpf-design-system-screen-impact.prompt.yml](wpf-design-system-screen-impact.prompt.yml) | WPF design-system screen impact map | Finding and updating the screens needed for a target page |
| [simulate-user-panel.prompt.yml](simulate-user-panel.prompt.yml) | Generic manifest-driven user-panel review | Running any simulated-user-panel mode from a review manifest |
| [simulate-user-panel-design-partner.prompt.yml](simulate-user-panel-design-partner.prompt.yml) | Product-shaping critique for early ideas and surfaces | Roadmap review, owner feedback, and design-partner critique |
| [simulate-user-panel-release-gate.prompt.yml](simulate-user-panel-release-gate.prompt.yml) | Near-ship release gate for workflows and screens | Calling ship blockers, caveats, and trust gaps |
| [simulate-user-panel-usability-lab.prompt.yml](simulate-user-panel-usability-lab.prompt.yml) | Benchmark-oriented user-panel review | Clustering repeated complaints and comparing runs |
| [simulate-user-panel-choose-mode.prompt.yml](simulate-user-panel-choose-mode.prompt.yml) | Route artifacts to the right user-panel mode first | When the artifact is clear but the mode is not |

## How to Use

### With GitHub Copilot Chat

Reference a prompt in your chat:

```
@workspace /explain Use the explain-architecture prompt to explain the event pipeline
```

### With Claude Code

The prompts work as context for Claude Code sessions. Reference the project context:

```
Read .github/prompts/project-context.prompt.yml and use it as context for this task: [your task]
```

### Manual Use

Copy the system message content from any prompt file and use it as context for your AI assistant.

## Prompt Structure

Each prompt follows a standard structure:

```yaml
name: Prompt Name
description: What this prompt helps with
# Model-agnostic prompt - works with any capable LLM
messages:
  - role: system
    content: |
      Context and instructions for the AI...
  - role: user
    content: |
      Template with {{placeholders}} for user input...
```

## Quick Reference

### Development Tasks

- **New provider**: `add-data-provider.prompt.yml` + `provider-implementation-guide.prompt.yml`
- **Operations continuity core**: `operations-continuity-core.prompt.yml`
- **New export format**: `add-export-format.prompt.yml`
- **Write tests**: `write-unit-tests.prompt.yml`
- **Code review**: `code-review.prompt.yml`
- **WPF design-system page routing**: `wpf-design-system-screen-impact.prompt.yml`
- **Simulated user testing**: `simulate-user-panel.prompt.yml`
- **Design-partner critique**: `simulate-user-panel-design-partner.prompt.yml`
- **Release gate**: `simulate-user-panel-release-gate.prompt.yml`
- **Usability lab**: `simulate-user-panel-usability-lab.prompt.yml`
- **Choose mode first**: `simulate-user-panel-choose-mode.prompt.yml`

### Understanding & Troubleshooting

- **Architecture questions**: `explain-architecture.prompt.yml`
- **Debug issues**: `troubleshoot-issue.prompt.yml`
- **Performance problems**: `optimize-performance.prompt.yml`
- **Runtime diagnostics gaps**: `runtime-observability-diagnostics.prompt.yml`

### DevOps

- **Deployment setup**: `configure-deployment.prompt.yml`

## CI-Derived Prompts

Some prompts are derived from CI/CD workflow run results to help address specific failures. The
legacy prompt-generation GitHub Actions workflow is archived; use the local generator script when
these files need to be refreshed.

### How It Works

1. A monitored workflow completes with a failure.
2. An operator runs `build/scripts/docs/generate-prompts.py` with the target workflow name.
3. The script fetches available run context, classifies failures, and writes targeted
   `.prompt.yml` files.
4. The operator reviews the diff and commits only the prompt updates that still match current
   project guidance.

### Auto-Generated Prompt Types

| Prompt | Generated When |
|--------|---------------|
| `fix-build-errors.prompt.yml` | Build failures (CS/NU/NETSDK errors) |
| `fix-test-failures.prompt.yml` | Test failures (failed assertions, test runs) |
| `fix-code-quality.prompt.yml` | Code quality warnings (CA/SA/IDE rules) |
| `fix-security-issues.prompt.yml` | Security vulnerabilities (CVEs, CodeQL) |
| `fix-docker-issues.prompt.yml` | Docker build/deployment failures |
| `fix-performance-regression.prompt.yml` | Performance benchmark regressions |
| `workflow-results-code-quality.prompt.yml` | Code quality workflow run summary |
| `workflow-results-test-matrix.prompt.yml` | Test matrix workflow run summary |
| `workflow-results-{name}.prompt.yml` | Summary prompt for any other workflow |

### Local Refresh

Run the generator locally:

```bash
python build/scripts/docs/generate-prompts.py \
  --workflow test-matrix.yml \
  --output .github/prompts/ \
  --summary
```

Archived workflow context is tracked in
[`../../archive/docs/workflows/legacy-github-actions-2026-05-18.md`](../../archive/docs/workflows/legacy-github-actions-2026-05-18.md).

## Adding New Prompts

### Manual Prompts

1. Create a new `.prompt.yml` file in this directory
2. Follow the existing structure (name, description, messages)
3. Include relevant project context in the system message
4. Add placeholders (`{{variable}}`) for user-provided values
5. Update this README with the new prompt

### CI-Derived Prompt Categories

CI-derived prompt files are refreshed with `build/scripts/docs/generate-prompts.py`. To add
support for new failure categories, edit that script and add entries to the `FAILURE_CATEGORIES`
dict.

## Related Resources

- [CLAUDE.md](../../CLAUDE.md) - Main project instructions for AI assistants
- [copilot instructions (extended)](../../docs/ai/copilot/instructions.md) - Detailed GitHub Copilot guidance
- [repository copilot-instructions](../copilot-instructions.md) - Native repository-wide Copilot coding-agent instructions
- [agents/](../agents/) - AI agent configurations

---

**Last Updated**: 2026-05-20
