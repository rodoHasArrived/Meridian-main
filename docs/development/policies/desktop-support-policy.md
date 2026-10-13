# Desktop Support Policy

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-10-05

## Scope

This policy defines contribution and validation expectations for desktop surfaces:

- `src/Meridian.Wpf` (**primary desktop surface**)
- `src/Meridian.Ui.Services` (shared services used by the desktop client)
- `src/Meridian.Ui.Shared` and shared workstation contracts consumed by both UI lanes

## Support Level

### WPF (Primary)

WPF is the sole desktop implementation and is co-equal with the browser workstation. Desktop
workflow improvements target WPF; shared business behavior and read models must serve both clients.

Expected for WPF-affecting changes:
- Build validation of WPF project
- Desktop-focused service tests
- Documentation updates when behavior or workflow changes

## Required checks by change type

### WPF-only change

On Windows, from the repository root with PowerShell 7 and the SDK selected by `global.json`:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/dev/validate-wpf-dev.ps1 -Restore
```

The default filter covers the desktop workflow slice. Select additional tests for the affected
feature, as described in the [desktop testing guide](../desktop-testing-guide.md). Non-Windows stub
builds do not validate WPF sources or rendering. GNU Make aliases are optional convenience wrappers.

### Shared desktop services change (`Ui.Services` or shared contracts)

Run the affected shared-service tests and Windows desktop checks, plus browser tests/build when its
contracts or behavior change. Use [Engineering](../../engineering/README.md#buildtestrun) for the
current commands and the full PR gate; WPF-only validation is insufficient for a shared API change.

## Ownership and maintenance expectations

- Desktop investment should prioritize WPF path quality and iteration speed.
- Avoid introducing new coupling from shared services into platform-specific UI layers.

---

## Related Documentation

- **Desktop Development:**
  - [Desktop Testing Guide](../desktop-testing-guide.md) - Testing procedures and requirements
  - [WPF Implementation Notes](../wpf-implementation-notes.md) - WPF architecture details
  - [Desktop Platform Improvements archive](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/assessments/desktop-platform-improvements-implementation-guide.md) - Historical improvement assessment; use current engineering/operator docs for active guidance

- **Architecture and Quality:**
  - [Desktop Architecture Layers](../../architecture/desktop-layers.md) - Layer boundaries
  - [UI Fixture Mode Guide](../ui-fixture-mode-guide.md) - Offline development
  - [Repository Organization Guide](../repository-organization-guide.md) - Code structure

- **Workflows:**
  - [Windows Desktop Build Workflow](https://github.com/rodoHasArrived/Meridian/blob/main/.github/workflows/windows-desktop-build.yml) - CI configuration
  - [GitHub Actions Summary](../github-actions-summary.md) - CI/CD overview
