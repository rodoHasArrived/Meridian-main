### Meridian CI lane: `quality-gate`

- Result: `failed`
- Exit code: `1`
- Run attempt: `local`; commit: `local`
- Dependency cache hit: `not reported`
- Reproduce: `bash scripts/ci.sh --lane quality-gate`
- Queue time: available after completion through `ci-metrics.py`; excluded from step durations.

| Step | Status | Exit code | Duration (s) |
| --- | --- | ---: | ---: |
| Verify .NET SDK | passed | 0 | 0 |
| Verify Python | passed | 0 | 0 |
| Restore .NET solution | passed | 0 | 7 |
| Verify .NET formatting | passed | 0 | 44 |
| Validate warning suppression inventory | passed | 0 | 0 |
| Enforce ApiClientService caller ratchet | passed | 0 | 1 |
| Enforce no-new-god-file ratchet | passed | 0 | 0 |
| Enforce consolidated-helper duplication ratchet | passed | 0 | 8 |
| Enforce inline SHA-256 hashing ratchet | passed | 0 | 2 |
| Enforce posture-environment test serialization | passed | 0 | 2 |
| Enforce server-derived ActionOrigin at endpoints | passed | 0 | 1 |
| Enforce declared file-store concurrency postures | passed | 0 | 1 |
| Enforce ledger-book-native accounting scope | passed | 0 | 1 |
| Enforce ledger dimension coverage across surfaces | passed | 0 | 0 |
| Build web workstation .NET lane | passed | 0 | 190 |
| Run .NET non-integration test projects | passed | 0 | 1494 |
| Validate browser evidence reader | passed | 0 | 0 |
| Verify Node.js | passed | 0 | 0 |
| Verify npm | passed | 0 | 0 |
| Verify Python | passed | 0 | 0 |
| Install dashboard dependencies from lockfile | passed | 0 | 8 |
| Generated UI contract drift gate | passed | 0 | 0 |
| Lint dashboard source | passed | 0 | 34 |
| Enforce strictNullChecks on dashboard source | passed | 0 | 57 |
| Run dashboard tests | passed | 0 | 377 |
| Build dashboard bundle | passed | 0 | 50 |
| Workstation bundle freshness gate | passed | 0 | 0 |
| Verify Python | passed | 0 | 0 |
| Validate dashboard type barrel | passed | 0 | 1 |
| Validate C#/TypeScript contract parity | passed | 0 | 0 |
| Validate observability contract | passed | 0 | 4 |
| Validate sample config data sources | passed | 0 | 0 |
| Validate monitoring deployment | passed | 0 | 0 |
| Validate status docs delivery claims | passed | 0 | 0 |
| Validate status doc staleness | passed | 0 | 0 |
| Check docs automation dependencies | passed | 0 | 1 |
| Validate Claude agent definitions | passed | 0 | 0 |
| Validate provider-validation script tests | passed | 0 | 14 |
| Validate TODO registry contract | passed | 0 | 6 |
| Validate AI contract drift | passed | 0 | 0 |
| Validate AI navigation freshness | passed | 0 | 0 |
| Validate lane vocabulary | passed | 0 | 0 |
| Validate AI handoff checklist schema | passed | 0 | 2 |
| Reject whole-repo generated documentation drift | passed | 0 | 4 |
| Regenerate the workflows overview | passed | 0 | 0 |
| Verify whole-repo generated documentation is committed | failed | 1 | 0 |

#### .NET test summary

- Total .NET slices: 19
- Passed: 19
- Failed: 0
- Test counts: {"passed": 19646, "failed": 0, "skipped": 5, "other": 0}

#### First useful failure lines

- `Error: Uncaught [Error: child exploded]`
- `at reportException (/workspace/Meridian-main/src/Meridian.Ui/dashboard/node_modules/jsdom/lib/jsdom/living/helpers/runtime-script-errors.js:66:24)`
- `at performUnitOfWork (/workspace/Meridian-main/src/Meridian.Ui/dashboard/node_modules/react-dom/cjs/react-dom.development.js:26599:12) Error: child exploded`
- `Error: Uncaught [Error: child exploded]`
- `at reportException (/workspace/Meridian-main/src/Meridian.Ui/dashboard/node_modules/jsdom/lib/jsdom/living/helpers/runtime-script-errors.js:66:24)`
- `at performUnitOfWork (/workspace/Meridian-main/src/Meridian.Ui/dashboard/node_modules/react-dom/cjs/react-dom.development.js:26599:12) Error: child exploded`
- `Error: Uncaught [Error: panel exploded]`
- `at reportException (/workspace/Meridian-main/src/Meridian.Ui/dashboard/node_modules/jsdom/lib/jsdom/living/helpers/runtime-script-errors.js:66:24)`

#### Artifact roots

- `artifacts/ci-summary/`
- `artifacts/build-logs/` when a build lane ran
- `artifacts/test-results/` when a test lane ran
