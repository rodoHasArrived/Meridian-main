# GitHub Actions Artifact Version Downgrade

## Summary

This document explains the downgrade of GitHub Actions artifact upload/download actions from v6/v7 to v4 across all workflows.

## Problem

The workflow runs were failing with the following artifacts:
- `actions/upload-artifact@v6.0.0`
- `actions/download-artifact@v7.0.0`

**Reference:** [GitHub Actions Run #21701929016](https://github.com/rodoHasArrived/Meridian/actions/runs/21701929016/job/62584107815#step:5:1)

## Root Cause

Version 6.0.0 of `upload-artifact` and version 7.0.0 of `download-artifact` have the following requirements:

1. **Node.js 24** - Required runtime environment
2. **GitHub Actions Runner 2.327.1+** - Minimum runner version

These requirements may not be met on:
- GitHub-hosted runners (gradual rollout)
- Self-hosted runners (require manual updates)
- Older runner environments

Additionally, v6+ has known issues:
- Proxy compatibility problems (issue #747)
- Hidden file handling changes
- Edge cases with artifact lifecycle management
- Immutable artifacts (cannot overwrite within same workflow)

## Solution

**Standardize on v4** for both upload and download artifact actions:
- `actions/upload-artifact@v4`
- `actions/download-artifact@v4`

### Why v4?

1. **Stable and tested** - v4 has been in production use since December 2023
2. **Wide compatibility** - Works on all GitHub-hosted runners and most self-hosted runners
3. **Well-documented** - Extensive documentation and community support
4. **Feature-complete** - Includes all necessary features for our use cases
5. **No breaking changes** - Smooth migration path

### Performance vs Stability Trade-off

While v6+ offers some performance improvements (up to 80% faster downloads), v4 provides:
- Better stability
- Wider compatibility
- Fewer edge-case bugs
- Proven production track record

For our CI/CD pipelines, **stability and reliability are more important than marginal performance gains**.

## Changes Made

### Affected Workflows

The following 11 workflows were updated:

1. `.github/workflows/benchmark.yml`
2. `.github/workflows/build-observability.yml`
3. `.github/workflows/code-quality.yml`
4. `.github/workflows/desktop-app.yml`
5. `.github/workflows/dotnet-desktop.yml`
6. `.github/workflows/nightly.yml`
7. `.github/workflows/pr-checks.yml`
8. `.github/workflows/reusable-dotnet-build.yml`
9. `.github/workflows/scheduled-maintenance.yml`
10. `.github/workflows/test-matrix.yml`
11. `.github/workflows/wpf-desktop.yml`

### Statistics

- **Upload artifact changes:** 20 occurrences (v6.0.0 → v4)
- **Download artifact changes:** 4 occurrences (v7.0.0 → v4)
- **Total changes:** 24 action version updates

## Verification

All workflow YAML files were validated for syntax correctness:

```bash
# Validate all workflows
for f in .github/workflows/*.yml; do
  python3 -c "import yaml; yaml.safe_load(open('$f'))"
done
```

Result: ✅ All 25 workflow files are valid YAML

## Future Considerations

### When to Upgrade to v6+

Consider upgrading when:

1. **Runner requirements are met** - All runners (including self-hosted) are updated to 2.327.1+
2. **Node.js 24 is available** - Guaranteed availability on all execution environments
3. **Known bugs are fixed** - v6+ issues are resolved upstream
4. **Breaking changes are acceptable** - Team is ready to handle any migration issues

### Monitoring

Monitor the following:
- [actions/upload-artifact releases](https://github.com/actions/upload-artifact/releases)
- [actions/download-artifact releases](https://github.com/actions/download-artifact/releases)
- [GitHub Actions changelog](https://github.blog/changelog)

## References

- [GitHub Actions Artifact v4 Announcement](https://github.blog/changelog/2023-12-14-github-actions-artifacts-v4-is-now-generally-available/)
- [upload-artifact v6.0.0 Release Notes](https://github.com/actions/upload-artifact/releases/tag/v6.0.0)
- [download-artifact v7.0.0 Release Notes](https://github.com/actions/download-artifact/releases/tag/v7.0.0)
- [Actions Runner Releases](https://github.com/actions/runner/releases)

## Related ADRs

This change aligns with:
- **ADR-010**: Use of stable, well-tested dependencies in CI/CD
- Best practices for GitHub Actions workflow reliability

---

*Last Updated: 2026-02-05*
*Author: Copilot Workspace Agent*
