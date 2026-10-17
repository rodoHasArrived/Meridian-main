# Examples

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-10-05

This directory contains code scaffolds and templates used as starting points for implementing new Meridian components.

## Contents

| Directory | Purpose |
|-----------|---------|
| [`agent-improvement-loop/`](agent-improvement-loop/README.md) | Runnable notebook for tracing an Agents SDK analyst, generating Promptfoo evals, and producing a HALO-backed Codex handoff |
| [`provider-template/`](provider-template/README.md) | Skeleton files for implementing a new market data provider |

## Choose a starting point

For a new market-data provider, follow the [provider template instructions](provider-template/README.md):
copy only the needed files and replace `Template` / `TEMPLATE` placeholders with the provider name.
Then follow the provider registration and validation guidance linked there.

For an agent evaluation experiment, follow the [agent improvement notebook guide](agent-improvement-loop/README.md).
It requires its own Python environment, an OpenAI API key for live execution, and evaluation setup;
it is not a provider scaffold. Keep experiment outputs and secrets out of committed documentation.
