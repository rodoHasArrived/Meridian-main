# Agent Improvement Loop Notebook

This example notebook demonstrates a trace-driven agent improvement flywheel for a fictional acquisition-diligence analyst. It uses the OpenAI Agents SDK to run a financial analyst agent, records traces, captures human and model feedback, converts the feedback into Promptfoo evals, runs a validation gate, and uses HALO to produce a Codex-ready harness handoff.

The notebook is adapted from the OpenAI Cookbook example [`examples/agents_sdk/agent_improvement_loop.ipynb`](https://github.com/openai/openai-cookbook/blob/main/examples/agents_sdk/agent_improvement_loop.ipynb) and is kept here as a runnable Meridian documentation example for teams experimenting with agent harness evaluation patterns.

## Contents

| File | Purpose |
| --- | --- |
| [`agent_improvement_loop.ipynb`](agent_improvement_loop.ipynb) | End-to-end notebook that creates synthetic diligence data, runs the traced agent, generates Promptfoo evals, runs HALO, and writes `codex_handoff.md`. |

## Prerequisites

Run from the repository root in a Python virtual environment with Node.js available:

```bash
python -m venv .venv
source .venv/bin/activate
pip install openai openai-agents halo-engine
export OPENAI_API_KEY=...
```

Promptfoo is executed through `npx`; install Node.js before running the eval-gate cells.

## Runtime artifacts

The notebook writes generated data, traces, evals, Promptfoo output, HALO context, and the final `codex_handoff.md` under:

```text
artifacts/agent_improvement_loop/
```

That location is intentionally outside `docs/` and is ignored by git via the repository's artifact rules.
