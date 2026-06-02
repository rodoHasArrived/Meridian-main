# Parallel AI Task Manifest Template

Use this template when two or more agents/lanes run in parallel on one request.

## Manifest

```yaml
task_id: <short-id>
coordinator: <owner>
goal: <single-sentence outcome>
mode: <Lightweight|Standard|Deep Review>

lanes:
  - lane_id: lane-1
    owner: <agent/person>
    scope_in:
      - <owned files or folders>
    scope_out:
      - <explicit exclusions>
    inputs_loaded:
      - <files read>
    planned_validation:
      - <commands>
    dependencies:
      - <lane ids this lane depends on>
    handoff_to:
      - <lane ids receiving output>

merge_risks:
  - risk: <overlap/conflict risk>
    mitigation: <how to avoid or resolve>

integration_order:
  - <lane id>   # order lanes should be merged/integrated to reduce conflicts

rollback_plan:
  - trigger: <condition that means the merged result is wrong>
    action: <how to revert this lane's output safely>

shared_facts:
  - <validated fact with source path>

open_assumptions:
  - <assumption needing confirmation>

validation_reuse:
  - command: <existing command evidence>
    source_lane: <lane id>
    still_valid_when: <conditions>
    rerun_when: <file changes or risk triggers>

context_budget:
  required_context:
    - <must-read files for next lane>
  optional_context:
    - <nice-to-read files>
  output_budget:
    summary_lines_max: <line budget>
    raw_log_policy: <when full logs are allowed>
```

## Usage Rules

- Keep lane scopes disjoint unless overlap is explicitly approved by the coordinator.
- Record files already inspected to prevent duplicate discovery across lanes.
- Reuse prior validation evidence when files are unchanged; rerun when touched files differ.
- Summarize command output; attach raw logs only when diagnosis requires full output.
- Keep `shared_facts` evidence-backed with concrete file paths, not inferred summaries.
- Distinguish validated facts from open assumptions so downstream lanes know what still needs proof.
- Set `integration_order` so lanes that touch shared files merge in a predictable sequence.
- Give every lane a `rollback_plan` so a bad result can be reverted without unwinding the others.

## Worked Example

```yaml
task_id: ai-tooling-3lane
coordinator: docs/ai/agent-handoff-checklist.md
goal: Improve AI agent tooling across token, orchestration, and planning lanes.
mode: Standard

lanes:
  - lane_id: lane-token
    owner: agent-a
    scope_in: [docs/ai/model-routing-policy.json, scripts/ai/setup.sh]
    scope_out: [src/**]
    planned_validation: ["python3 -c 'import json,sys;json.load(open(sys.argv[1]))' docs/ai/model-routing-policy.json"]
  - lane_id: lane-orchestration
    owner: agent-b
    scope_in: [scripts/ai/route-maintenance.sh, tests/scripts/test_route_maintenance_classification.py]
    scope_out: [docs/**]
    planned_validation: ["python3 -m unittest tests.scripts.test_route_maintenance_classification"]
  - lane_id: lane-planning
    owner: agent-c
    scope_in: [docs/ai/parallel-task-manifest-template.md, docs/ai/agent-handoff-checklist.md]
    planned_validation: ["python3 build/scripts/docs/check-ai-handoff.py"]

integration_order: [lane-token, lane-orchestration, lane-planning]

rollback_plan:
  - trigger: handoff/routing parity check fails after merge
    action: revert lane-planning doc edits, keep code lanes
```

