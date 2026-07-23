# Examples

**Status:** supporting
**Owner:** core-team
**Reviewed:** 2026-07-19

This directory contains code scaffolds and templates used as starting points for implementing new Meridian components.

## Contents

| Directory | Purpose |
|-----------|---------|
| [`agent-improvement-loop/`](agent-improvement-loop/README.md) | Runnable notebook for tracing an Agents SDK analyst, generating Promptfoo evals, and producing a HALO-backed Codex handoff |
| [`provider-template/`](provider-template/README.md) | Skeleton files for implementing a new market data provider |

## Usage

Copy the relevant template files into your target directory and replace all `Template` / `TEMPLATE` placeholders with your component's actual name and values. For example, for an Alpaca-like provider replace `Template` with `YourProvider` and `TEMPLATE` with `YOURPROVIDER`. Implement only the files relevant to your use case — not every provider needs every template file.

See the individual README in each subdirectory for detailed quick-start instructions.
