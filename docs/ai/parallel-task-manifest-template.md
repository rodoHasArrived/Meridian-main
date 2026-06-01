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
