---
name: cos-runtime-development
description: Build and extend the Meridian Chief of Staff (CoS) runtime ADK scaffold. Use when Codex is asked to implement CoS runtime nodes, add new intent kinds, wire the runtime to Meridian MCP or workstation API surfaces, add an HTTP host wrapper, extend the node pipeline, or write tests for the ADK scaffold at tools/chief-of-staff-runtime/.
---

# Meridian CoS Runtime Development (ADK)

Implement and extend the Chief of Staff runtime ADK scaffold so it can be composed with Meridian's
MCP, workstation API, and evidence surfaces.

Read `../_shared/project-context.md` and `../_shared/codex-execution-contract.md` before starting.

## Use When

Use this skill when Codex is asked to build or extend any part of the Chief of Staff runtime:

Trigger examples:

- "Implement a new intent kind in the CoS runtime."
- "Add an HTTP host wrapper around the ADK scaffold."
- "Wire the CoS runtime to Meridian MCP tools."
- "Extend the EvidenceAggregationNode to query the workstation API."
- "Write tests for the CoS runtime node pipeline."
- "Complete the ADK scaffold so Codex can implement it."

## Do Not Use When

Use `meridian-blueprint` when the user only wants a design for the CoS runtime, and
`meridian-code-review` when the user only wants an audit.

Non-trigger examples:

- "Design the CoS runtime architecture only."
- "Review the ADK scaffold for issues."

## ADK Node Pipeline

The scaffold at `tools/chief-of-staff-runtime/runtime.py` defines six node boundaries that form
the intent-to-evidence pipeline. Implement within these boundaries:

| Node | Class | Responsibility |
| ---- | ----- | -------------- |
| Intent analysis | `IntentAnalysisNode` | Classify operator request into an `intent_kind` |
| Context assembly | `ContextAssemblyNode` | Attach session, workspace, and fund identity |
| Evidence aggregation | `EvidenceAggregationNode` | Query MCP/API sources and collect structured evidence |
| Recommendation synthesis | `RecommendationSynthesisNode` | Produce markdown + structured recommendations |
| Decision preparation | `DecisionPreparationNode` | Flag pending approvals and routing prerequisites |
| Trace emission | `TraceEmissionNode` | Emit structured trace records for evidence retention |

The `execute` function in `runtime.py` composes all six nodes and returns a `RuntimeResponse`.

## Integration Boundary

The .NET host calls the runtime via `IChiefOfStaffRuntimeClient` at:

- `POST /execute` — runs the full node pipeline
- `GET /health` — returns runtime status

Workstation API routes (in `src/Meridian.Ui.Shared/`):

- `POST /api/workstation/chief-of-staff/sessions`
- `GET /api/workstation/chief-of-staff/sessions/{sessionId}`
- `POST /api/workstation/chief-of-staff/sessions/{sessionId}/decisions`
- `POST /api/workstation/chief-of-staff/sessions/{sessionId}/export-trace`
- `GET /api/workstation/chief-of-staff/health`

Evidence is emitted using the existing evidence manifest retention surface at
`/api/workstation/evidence/.../export-manifest`.

## Workflow

1. Read `tools/chief-of-staff-runtime/runtime.py` and
   `docs/development/chief-of-staff-runtime.md` before editing.
2. Identify which node or integration boundary the task changes.
3. Implement within the existing node class boundaries; do not remove or bypass existing nodes.
4. Keep the runtime dependency-free where possible; only add a dependency when required for a
   specific MCP or workstation API integration.
5. Do not mutate Meridian ledger, reconciliation, readiness, or evidence source-of-truth state
   from within the CoS runtime. The runtime is advisory only.
6. Add tests under `tests/` or alongside the runtime for new node behavior.
7. Update `docs/development/chief-of-staff-runtime.md` when the integration boundary, node
   responsibilities, or configuration keys change.

## Key Files

| Path | Purpose |
| ---- | ------- |
| `tools/chief-of-staff-runtime/runtime.py` | ADK node pipeline scaffold — primary implementation target |
| `tools/chief-of-staff-runtime/README.md` | Node boundary overview and integration summary |
| `docs/development/chief-of-staff-runtime.md` | Integration boundary, API routes, and config reference |
| `src/Meridian.Ui.Shared/` | Workstation API shared contracts consumed by the CoS runtime client |
| `src/Meridian.Mcp/` | MCP tools and resources available for evidence aggregation |

## Handoffs

- Hand off to `meridian-blueprint` when the task requires redesigning node responsibilities or
  introducing new integration surfaces not covered by the existing scaffold.
- Hand off to `meridian-implementation-assurance` after completing a CoS runtime change to get
  evidence, docs sync, and AI catalog gate results.
- Hand off to `meridian-test-writer` if broader scenario coverage beyond node unit tests is needed.

## Validation

```bash
# Validate the Python scaffold runs without error
python3 -c "from tools.chief_of_staff_runtime.runtime import execute, health, RuntimeRequest; r = execute(RuntimeRequest('test', 'test readiness', 'TradingReadinessReview')); print(r.intent_kind)"

# Documentation drift check
python3 build/scripts/docs/check-ai-inventory.py --summary

# Codex skill catalog check
python3 build/scripts/docs/check-codex-skills.py --summary
```

If the Python module path is not on `sys.path`, run from the repository root or use:

```bash
PYTHONPATH=. python3 -c "from tools.chief_of_staff_runtime.runtime import health; print(health())"
```

## Deliverables

A complete CoS runtime task includes:

- updated or new node class in `runtime.py`
- any helper modules added alongside `runtime.py`
- updated `docs/development/chief-of-staff-runtime.md` when the API surface or config changes
- test coverage for new node behavior
- explicit validation evidence in the final response

## Output Standards

Every CoS runtime task response must include:

- exact files changed with a one-line reason for each change
- validation command and its pass/fail result
- any residual risks or follow-up gaps
- updated `docs/development/chief-of-staff-runtime.md` when integration boundary or config keys changed
- no unreferenced side-effects; every modified node and its downstream dependencies must be noted
