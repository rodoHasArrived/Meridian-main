# Credential workstation cutover — 2026-10-28

**Status:** local implementation; Windows execution pending  
**Owner:** core-team  
**Source baseline:** `a5b2589f0395d054bec0b59672a990419ef237ed`

Initial desktop setup and Backfill now route their credential actions to the existing
`CredentialManagement` screen. That screen requires explicit selection of a retained owned connection;
its canonical authenticated API client carries the connection ID on save, removal and verification.
The old setup user-environment load/save methods, secret inputs and Backfill environment Set/Clear
methods have been removed. Existing environment values are untouched; they are not automatically
assigned to any tenant or account. Configuration-only setup does not establish credentials,
verification or runtime activation.

Credential management now retains the complete provider-family/connection/tenant/account/environment
identity and reloads the owned inventory before every mutation or verification. Changed or missing
ownership clears the selection and secret editors and sends no credential mutation. Selection changes
clear prior secret editors; delayed preflight or mutation responses cannot apply to another selection.
A status row for another provider cannot supply the selected connection's editor schema. Display-label
changes do not alter credential ownership.

New Windows tests cover changed tenant, account, environment, connection ID, provider and removed
connection refusal before save/delete/test, a foreign-provider status schema, and setup navigation
without saving configuration or starting a backend. Existing asynchronous persistence and verification
fixtures remain in place. These tests require Windows execution; a Linux stub is not evidence.

Observed validation:

- Browser credential/settings support: `npm --prefix src/Meridian.Ui/dashboard run test:vitest --
  src/screens/settings-owned-provider-credentials.test.tsx src/lib/provider-credential-scope.test.ts
  src/screens/settings-screen.test.tsx` passed **85 tests in 3 files**. Responses are mocked.
- Both changed XAML documents parsed as XML; `git diff --check` passed at this point.
- Full Windows application/XAML and complete test-source cross-compilation passed with **0 errors,
  275 warnings** in 4m46s. Command: `PATH=/workspace/.dotnet:$PATH python
  build/python/cli/buildctl.py build --project tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj
  --configuration Release --queue --full-wpf-build --property IsWindows=true
  --property EnableWindowsTargeting=true --verbosity minimal`. Log:
  `/tmp/credential-ui-wpf-build.log`. This explicitly compiled the Windows sources on Linux;
  it did not execute Windows tests or render the workstation.
- Live installed Windows/operator acceptance: unavailable in this Linux workspace.

The browser's scoped Settings editor and desktop add-provider owned-connection wizard were already
implemented at this baseline. They were inspected and retained. No provider-wide fallback,
account auto-selection, connection creation or runtime credential authority was added by this lane.

The inventory preflight is a client-side consistency check, not an atomic expected-scope fence.
Credential requests currently carry `connectionId`; deleting and recreating that ID between the
preflight and server mutation requires separate server-side concurrency protection. This lane does
not claim closure of that race or the broader runtime/legacy ownership migration P0.
