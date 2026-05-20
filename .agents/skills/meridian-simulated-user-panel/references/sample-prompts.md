# Sample Prompts

Use these as ready-made starters when invoking `meridian-simulated-user-panel`.

## Design Partner

```text
Use $meridian-simulated-user-panel in design_partner mode with this manifest:
{
  "mode": "design_partner",
  "artifact_type": "roadmap-review",
  "artifact_paths": ["docs/plans/example-roadmap-item.md"],
  "persona_set": {"panel": "core-finance", "required_roles": ["Owner-Operator"]},
  "focus_areas": ["adoption_and_positioning", "power_user_depth"],
  "constraints": ["Stay grounded in Meridian's current workstation direction."],
  "success_criteria": ["Tell me who would adopt this first and what would block them."]
}
```

## Release Gate

```text
Use $meridian-simulated-user-panel in release_gate mode with this manifest:
{
  "mode": "release_gate",
  "artifact_type": "workflow-walkthrough",
  "artifact_paths": [
    "artifacts/desktop-workflows/provider-flow/manifest.json",
    "artifacts/desktop-workflows/provider-flow/screenshots/01-start.png"
  ],
  "persona_set": {"panel": "operations-controls"},
  "focus_areas": ["workflow_fit", "trust_and_controls", "release_readiness"],
  "constraints": ["Do not invent hidden steps outside the manifest."],
  "success_criteria": ["Return ship, ship_with_caveats, or hold with clear blockers."]
}
```

## Usability Lab

```text
Use $meridian-simulated-user-panel in usability_lab mode with this manifest:
{
  "mode": "usability_lab",
  "artifact_type": "screen-review",
  "artifact_paths": [
    "docs/screenshots/desktop/fund-ledger.png",
    "docs/screenshots/desktop/approvals-panel.png"
  ],
  "persona_set": {"panel": "operations-controls"},
  "focus_areas": ["first_impression", "trust_and_controls"],
  "constraints": ["Cluster repeated complaints and highlight disagreements."],
  "success_criteria": ["Produce benchmarkable owner actions and cross-persona tensions."]
}
```

## Choose Mode From Artifacts

```text
Review this artifact bundle and choose whether the best invocation mode is design_partner,
release_gate, or usability_lab before running the simulated user panel. Then return the full
output contract.
```

## Prompt Tuning Tips

- Use a full review manifest when you want repeatable output or eval scoring.
- Prefer `workflow-walkthrough` when you already have `manifest.json`, screenshots, and smoke
  notes.
- Ask for `usability_lab` when you want repeated complaint clustering or trend comparison.
