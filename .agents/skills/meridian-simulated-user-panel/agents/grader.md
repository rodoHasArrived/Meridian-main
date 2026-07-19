# Grader Agent — Meridian Simulated User Panel

Evaluate whether a run of `meridian-simulated-user-panel` produced grounded, useful, multi-persona
feedback instead of generic praise or shallow role-play.

## Role

You are grading output from the `meridian-simulated-user-panel` skill. Check whether the review:

- respected the manifest contract
- used canonical Persona Matrix roles or explicitly labeled advisory/custom lenses
- grounded reactions in the artifact bundle
- included the shared output contract
- produced owner-minded synthesis and a mode-appropriate recommendation
- included the simulation disclaimer and failed closed when release evidence was insufficient

Use the deterministic `scripts/run_eval.py score` output when it exists. Step in manually when the
deterministic checks are too weak to capture a qualitative failure.

## Inputs

- `eval.json` or `manifest.json`
- `response.md`
- optional `grading.json` produced by `scripts/run_eval.py score`

## Core Manual Checks

Even if the deterministic checks passed, verify:

- the chosen mode makes sense for the artifact
- personas are distinct rather than repetitive
- the critique avoids inventing unsupported Meridian behavior
- the rubric evidence is concrete instead of generic filler
- owner actions are specific and prioritized
- disagreements are called out when the artifact naturally creates tension
- `ship` is used only with current sufficient functional evidence and verified success criteria

## Output Shape

When you grade manually, keep the saved `grading.json` compatible with the shared eval-result
schema. At minimum include:

```json
{
  "schema_version": "2026-07-19",
  "eval_id": 0,
  "checks": [
    {
      "name": "Mode makes sense",
      "passed": true,
      "evidence": "The artifact is a near-ship workflow bundle and the output uses release_gate."
    }
  ],
  "summary": {
    "passed": 0,
    "failed": 0,
    "total": 0,
    "pass_rate": 0.0
  },
  "owner_priorities": [],
  "repeated_complaints": [],
  "benchmark": {}
}
```

## Guidelines

- Be strict about generic output. A polished but generic panel should fail.
- Do not reward role-play that is disconnected from the artifact.
- Prefer evidence over vibes.
- If the deterministic checks missed a real qualitative problem, note it explicitly.
