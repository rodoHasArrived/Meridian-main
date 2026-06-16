# Parallel AI Task Manifest Template

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-06-04

Use this template when two or more agents/lanes run in parallel on one request.

## Manifest

```yaml
task_id: <short-id>
coordinator: <owner>
goal: <single-sentence outcome>
mode: <Lightweight|Standard|Deep Review>

working_memory:
  last_synced: <timestamp>
  codebase_snapshot: <branch plus dirty/clean summary>
  active_claims:
    - lane_id: <lane id>
      files: [<paths this lane may write>]
      status: <planned|editing|ready-for-integration|blocked|merged>
  open_assumptions:
    - <assumption needing confirmation>
  validation_snapshot:
    - command: <command>
      outcome: <pass|fail|blocked|not-run>
      reuse_until: <file or condition that invalidates this proof>

lanes:
  - lane_id: lane-1
    owner: <agent/person>
    validation_owner: <owner who reruns final checks if integration invalidates reuse>
    scope_in:
      - <owned files or folders>
    scope_out:
      - <explicit exclusions>
    inputs_loaded:
      - <files read>
    inspected_files:
      - <files or folders scanned during discovery>
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
- Keep the `working_memory` block current whenever lane ownership, codebase drift, validation reuse,
  or merge risk changes. Use [`working-memory.md`](working-memory.md) for the full ledger contract.
- Record files already inspected to prevent duplicate discovery across lanes.
- Record one validation owner per lane so reuse and rerun responsibility stays explicit.
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
    validation_owner: agent-a
    scope_in: [docs/ai/model-routing-policy.json, scripts/ai/setup.sh]
    scope_out: [src/**]
    inspected_files: [docs/ai/model-routing-policy.json, scripts/ai/setup.sh, docs/ai/tooling/README.md]
    planned_validation: ["python3 -c 'import json,sys;json.load(open(sys.argv[1]))' docs/ai/model-routing-policy.json"]
  - lane_id: lane-orchestration
    owner: agent-b
    validation_owner: agent-b
    scope_in: [scripts/ai/route-maintenance.sh, tests/scripts/test_route_maintenance_classification.py]
    scope_out: [docs/**]
    inspected_files: [scripts/ai/route-maintenance.sh, tests/scripts/test_route_maintenance_classification.py]
    planned_validation: ["python3 -m unittest tests.scripts.test_route_maintenance_classification"]
  - lane_id: lane-planning
    owner: agent-c
    validation_owner: coordinator
    scope_in: [docs/ai/parallel-task-manifest-template.md, docs/ai/agent-handoff-checklist.md]
    inspected_files: [docs/ai/parallel-task-manifest-template.md, docs/ai/agent-handoff-checklist.md, docs/ai/work-modes.md]
    planned_validation: ["python3 build/scripts/docs/check-ai-handoff.py --strict"]

integration_order: [lane-token, lane-orchestration, lane-planning]

rollback_plan:
  - trigger: handoff/routing parity check fails after merge
    action: revert lane-planning doc edits, keep code lanes
```

