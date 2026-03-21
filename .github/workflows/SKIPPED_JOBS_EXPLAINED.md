# Understanding Skipped Jobs in GitHub Actions

## Why Do Jobs Show as "Skipped"?

When you see a job marked as "skipped" in a GitHub Actions workflow run, it's often **expected behavior** and not an error. GitHub Actions evaluates all jobs in a workflow, even if they have conditionals (`if:` statements) that prevent them from running on certain trigger types.

## Multi-Trigger Workflows

Some workflows in this repository are designed to handle multiple event types (push, pull_request, issues, schedule, workflow_dispatch). These workflows use job-level conditionals to control which jobs run for each trigger type.

### Example: Documentation Automation Workflow

The `documentation.yml` workflow is triggered by:
- **push** events (commits to main)
- **pull_request** events (PRs to main)
- **schedule** events (weekly on Mondays)
- **issues** events (issues labeled with `ai-known-error`)
- **workflow_dispatch** events (manual runs)

However, not all jobs run for every trigger type:

#### AI Known Errors Intake Job

```yaml
ai-known-errors-intake:
  if: |
    (github.event_name == 'issues' && contains(github.event.issue.labels.*.name, 'ai-known-error')) ||
    (github.event_name == 'workflow_dispatch' && github.event.inputs.issue_number != '' && github.event.inputs.issue_number != null)
```

This job **only runs** when:
1. An issue is labeled with `ai-known-error`, OR
2. The workflow is manually triggered with an `issue_number` parameter

It will **skip** on:
- Push events (commits)
- Pull request events
- Scheduled runs

#### Documentation Generation Jobs

```yaml
detect-changes:
  if: github.event_name != 'issues'
```

These jobs **skip** on `issues` events because they only need to run when code or documentation changes.

## Workflow Run Examples

### Example 1: Push Event (Commit to Main)

When a commit is pushed to main, you'll see:
- ✅ `detect-changes` - **runs** (processes the commit)
- ✅ `regenerate-docs` - **runs** (regenerates documentation)
- ✅ `scan-todos` - **runs** (scans for TODOs)
- ⏭️ `ai-known-errors-intake` - **skipped** (only runs on issues events)

This is **expected and correct**.

### Example 2: Issue Labeled Event

When an issue is labeled with `ai-known-error`, you'll see:
- ✅ `ai-known-errors-intake` - **runs** (processes the labeled issue)
- ⏭️ `detect-changes` - **skipped** (no code changes to detect)
- ⏭️ `regenerate-docs` - **skipped** (no docs to regenerate)
- ⏭️ `scan-todos` - **skipped** (no TODOs to scan)

This is also **expected and correct**.

## How to Identify Issues vs. Expected Skips

### ✅ Expected Skip (Not a Problem)
- The job has an `if:` condition that doesn't match the current event type
- The workflow run succeeded overall
- Other related jobs ran successfully
- The workflow is designed to handle multiple trigger types

### ❌ Unexpected Skip (Potential Problem)
- A job that should run is skipping
- The job's `if:` condition should match the current event
- The workflow run failed or had unexpected behavior
- Required dependencies didn't run

## Common Patterns in This Repository

### 1. Event-Specific Jobs
Jobs that only run on specific event types:
```yaml
if: github.event_name == 'pull_request'  # Only on PRs
if: github.event_name == 'push'          # Only on pushes
if: github.event_name == 'issues'        # Only on issue events
```

### 2. Path-Filtered Jobs
Jobs that only run when specific files change:
```yaml
needs: detect-changes
if: needs.detect-changes.outputs.docs_changed == 'true'
```

### 3. Manual-Only Jobs
Jobs that only run on manual workflow dispatch:
```yaml
if: github.event_name == 'workflow_dispatch' && inputs.some_flag == 'true'
```

## Need Help?

If you believe a job is skipping unexpectedly:

1. **Check the job's `if:` condition** in the workflow YAML file
2. **Verify the event type** that triggered the workflow
3. **Review job dependencies** - if a parent job skipped, dependent jobs may also skip
4. **Check the workflow run summary** for any error messages or warnings

For more information, see:
- [GitHub Actions Documentation - Using Conditions](https://docs.github.com/en/actions/using-workflows/workflow-syntax-for-github-actions#jobsjob_idif)
- [Workflow README](./README.md)
- [Documentation Workflow](./documentation.yml)
