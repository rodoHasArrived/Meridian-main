# GitHub Actions Testing Checklist

This document provides a checklist for testing the newly created GitHub Actions workflows.

## Pre-merge Checklist

- [x] All workflow files are valid YAML
- [x] All paths reference existing files in the repository
- [x] Workflow permissions are minimal and appropriate
- [x] Documentation is complete and accurate
- [x] README badges are added
- [x] No hardcoded secrets or credentials

## Post-merge Testing Plan

### 1. CodeQL Security Analysis
- [ ] Wait for scheduled run (next Monday at 6:00 UTC) OR trigger manually
- [ ] Verify it completes without errors
- [ ] Check Security tab for any findings
- [ ] Review CodeQL analysis results

**Expected Outcome:** CodeQL completes successfully, no critical vulnerabilities

### 2. Dependency Review
- [ ] Create a test PR that adds a dependency
- [ ] Verify workflow runs on the PR
- [ ] Check for PR comment with dependency review results
- [ ] Verify it blocks vulnerable dependencies (if testing with known vulnerable package)

**Expected Outcome:** Workflow runs on PRs, provides feedback on dependencies

### 3. Docker Build and Push
- [ ] Verify workflow runs on PR (build only)
- [ ] After merge, check GitHub Container Registry for new image
- [ ] Pull the image: `docker pull ghcr.io/rodohasarrived/meridian:latest`
- [ ] Run the container and verify it works
- [ ] Check image tags are created correctly

**Expected Outcome:** Docker images build successfully and are pushed to GHCR

### 4. Benchmark Performance
- [ ] Create a PR that modifies source or benchmark code
- [ ] Verify benchmarks run successfully
- [ ] Check PR comment with benchmark results
- [ ] Download and review benchmark artifacts
- [ ] Verify JSON and HTML reports are generated

**Expected Outcome:** Benchmarks complete, artifacts uploaded, PR commented

### 5. Code Quality
- [ ] Create a PR with improperly formatted code
- [ ] Verify dotnet format check catches issues
- [ ] Fix formatting and push again
- [ ] Verify workflow passes
- [ ] Check markdown linting works on documentation changes

**Expected Outcome:** Code quality checks enforce standards

### 6. Stale Issue Management
- [ ] Wait for daily scheduled run
- [ ] Check if any stale issues/PRs are marked
- [ ] Verify labels are applied correctly
- [ ] Verify comments are posted appropriately

**Expected Outcome:** Stale items are properly labeled and commented

### 7. Label Management
- [ ] Create a new issue
- [ ] Create a PR that modifies different file types
- [ ] Verify auto-labels are applied based on file paths
- [ ] Verify size labels are applied correctly
- [ ] Check that labels update on PR synchronization

**Expected Outcome:** Issues and PRs are automatically labeled

## Monitoring

After merging, monitor the following for the first week:

### GitHub Actions Tab
- Check all workflows complete successfully
- Review any error logs
- Monitor run times to ensure performance is acceptable

### Security Tab
- Review CodeQL findings
- Check dependency alerts
- Review Dependabot suggestions

### Packages Tab
- Verify Docker images are published
- Check image sizes are reasonable
- Verify tags are created correctly

### Issues and PRs
- Verify auto-labeling is working
- Check for stale management activities
- Verify PR comments from workflows

## Troubleshooting Guide

### Workflow Fails to Start
- Check workflow triggers in the YAML file
- Verify permissions are set correctly
- Check GitHub Actions is enabled for the repository

### Build/Test Failures
- Review error logs in GitHub Actions
- Test locally with the same commands
- Check for environment-specific issues

### Docker Build Fails
- Verify Dockerfile exists at `deploy/docker/Dockerfile`
- Check Docker context is correct
- Test build locally: `docker build -f deploy/docker/Dockerfile .`

### Permission Errors
- Review workflow permissions section
- Check organization/repository security settings
- Verify GITHUB_TOKEN has required permissions

### CodeQL Failures
- Ensure .NET SDK 9.0 is available
- Check for build errors before CodeQL analysis
- Review CodeQL query packs being used

## Performance Expectations

| Workflow | Expected Duration | Resource Usage |
|----------|------------------|----------------|
| CodeQL | 5-10 minutes | Medium |
| Dependency Review | 30-60 seconds | Low |
| Docker Build | 3-5 minutes | High |
| Benchmark | 2-5 minutes | Medium |
| Code Quality | 2-3 minutes | Low |
| Stale | 30-60 seconds | Low |
| Label Management | 10-20 seconds | Low |

## Success Criteria

All workflows are considered successful if:

1. ✅ Workflows complete without errors
2. ✅ Expected artifacts are produced
3. ✅ PR comments are posted where appropriate
4. ✅ Security scanning finds no critical issues
5. ✅ Docker images are accessible and functional
6. ✅ Benchmarks produce valid results
7. ✅ Labels are applied correctly
8. ✅ No performance degradation in CI/CD pipeline

## Follow-up Actions

After successful testing:

- [ ] Update workflow documentation with any lessons learned
- [ ] Adjust thresholds/settings based on actual usage
- [ ] Consider additional workflows (if needed):
  - Integration testing
  - Load testing
  - Automated changelog generation
  - Release notes automation

## Notes

- Some workflows (CodeQL, Stale) run on a schedule and may not trigger immediately
- Docker images are only pushed on push to main or tags, not on PRs
- Benchmark comparison requires multiple runs to establish baseline

---

**Last Updated:** 2026-01-30
**Tested By:** CI system
**Status:** Production Ready
