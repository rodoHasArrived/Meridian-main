# Desktop Support Policy

## Scope

This policy defines contribution and validation expectations for desktop surfaces:

- `src/Meridian.Wpf` (**primary desktop surface**)
- `src/Meridian.Ui.Services` (shared services used by the desktop client)

## Support Level

### WPF (Primary)

WPF is the sole desktop implementation. All new features and developer workflow improvements target this platform.

Expected for WPF-affecting changes:
- Build validation of WPF project
- Desktop-focused service tests
- Documentation updates when behavior or workflow changes

## Required checks by change type

### WPF-only change
- `make desktop-build`
- `make desktop-test`

### Shared desktop services change (`Ui.Services` or shared contracts)
- `make desktop-build`
- `make desktop-test`

## Ownership and maintenance expectations

- Desktop investment should prioritize WPF path quality and iteration speed.
- Avoid introducing new coupling from shared services into platform-specific UI layers.

---

## Related Documentation

- **Desktop Development:**
  - [Desktop Testing Guide](../desktop-testing-guide.md) - Testing procedures and requirements
  - [WPF Implementation Notes](../wpf-implementation-notes.md) - WPF architecture details
  - [Desktop Platform Improvements](../../evaluations/desktop-platform-improvements-implementation-guide.md) - Improvement roadmap

- **Architecture and Quality:**
  - [Desktop Architecture Layers](../../architecture/desktop-layers.md) - Layer boundaries
  - [UI Fixture Mode Guide](../ui-fixture-mode-guide.md) - Offline development
  - [Repository Organization Guide](../repository-organization-guide.md) - Code structure

- **Workflows:**
  - [Desktop Builds Workflow](https://github.com/rodoHasArrived/Meridian/blob/main/.github/workflows/desktop-builds.yml) - CI configuration
  - [GitHub Actions Summary](../github-actions-summary.md) - CI/CD overview
