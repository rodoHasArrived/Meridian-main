# GitHub Actions Testing Checklist

**Status:** Active
**Reviewed:** 2026-05-18

Use this checklist when changing `.github/workflows/`, workflow-related scripts, or
the local commands mirrored by CI.

## Pre-Merge Checks

- Workflow YAML parses locally.
- `python build/scripts/ci/check-workflow-hygiene.py` passes.
- Referenced solution, project, script, and dashboard paths exist.
- New workflow steps use repository-relative paths.
- Token permissions stay at `contents: read` unless a write is explicitly required.
- Publish workflows upload artifacts only; they do not create public releases or deploy externally.

## Local Validation

```powershell
python build/scripts/ci/check-workflow-hygiene.py
python - <<'PY'
import pathlib, yaml
for path in pathlib.Path(".github/workflows").glob("*.yml"):
    yaml.safe_load(path.read_text())
PY
```

For build/test command parity, run the command set from
[`github-actions-summary.md`](github-actions-summary.md).

## Post-Merge Smoke

- Confirm `CI` runs on the next pull request and on pushes to `main`.
- Confirm `Windows Desktop Build` reaches the WPF publish smoke step.
- Manually run `Publish Smoke` for `collector` and verify the uploaded artifact contains `Meridian.exe`.
- Manually run `Maintenance` after workflow edits and confirm the hygiene script and `actionlint` pass.

## Expected Artifacts

| Workflow | Artifact |
| --- | --- |
| CI | .NET TRX results only on failure |
| Windows Desktop Build | WPF TRX results only on failure |
| Publish Smoke | `artifacts/publish/publish-smoke/` upload |
| Maintenance | None |

Generated artifacts remain ignored by Git and should not be committed.
