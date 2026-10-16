# GitHub Actions Testing Checklist

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-10-05

Use this checklist when changing `.github/workflows/`, workflow-related scripts, or
the local commands mirrored by CI.

## Pre-Merge Checks

- Workflow YAML parses locally.
- `python build/scripts/ci/check-workflow-hygiene.py` passes.
- Referenced solution, project, script, and dashboard paths exist.
- New workflow steps use repository-relative paths.
- Token permissions stay at `contents: read` unless a write is explicitly required.
- Verify artifact, release, and deployment behavior against the workflow being changed.
- Workflow governance changes require explicit human review under the root `AGENTS.md` policy.

## Local Validation

From the repository root in PowerShell or Bash, with Python and the documentation YAML dependency:

```powershell
python -m pip install --requirement build/scripts/docs/requirements.txt
python build/scripts/ci/check-workflow-hygiene.py
python -c "import pathlib, yaml; [yaml.safe_load(p.read_text(encoding='utf-8')) for p in pathlib.Path('.github/workflows').glob('*.yml')]"
bash scripts/ci.sh --lane verify-workflows
```

Expected result: each command exits zero. YAML parsing checks syntax; the workflow lane adds the
repository's workflow checks. Fix the reported file/step before rerunning; do not suppress a failed
check. Use [Engineering](../engineering/README.md#buildtestrun) for additional affected build/test
lanes, then run the full `bash scripts/ci.sh` gate before submitting the change.

## Post-Merge Smoke

- Confirm required `Meridian CI` checks run for the applicable pull request, push, or merge-group event.
- For WPF changes, inspect the `Windows Desktop Build` validation bundle; its publish step runs only
  when the workflow's smoke-publish condition is met.
- For publish changes, run the applicable `Publish Smoke` project/runtime and inspect its artifact
  and release-evidence manifest using the current [workflow guide](../../.github/workflows/README.md).
- Inspect failed jobs and their evidence before considering the change verified.

## Expected Artifacts

| Workflow | Artifact |
| --- | --- |
| Meridian CI | Lane-specific build logs, test results, and summaries; uploads run even after failure when files exist |
| Windows Desktop Build | `artifacts/wpf-validation/windows-desktop-build/` and conditional desktop smoke output |
| Publish Smoke | `artifacts/publish/publish-smoke/`, including failure evidence when available |

Workflow artifacts and local build outputs are distinct from tracked generated documentation and
the tracked browser bundle. Follow [generated-content ownership](../documentation-ownership.md)
when deciding what belongs in the commit; artifact names and retention live in the workflow guide.
