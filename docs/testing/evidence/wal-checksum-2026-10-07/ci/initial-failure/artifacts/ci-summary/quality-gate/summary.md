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
| Restore .NET solution | passed | 0 | 12 |
| Verify .NET formatting | passed | 0 | 47 |
| Validate warning suppression inventory | passed | 0 | 0 |
| Enforce ApiClientService caller ratchet | passed | 0 | 0 |
| Enforce no-new-god-file ratchet | passed | 0 | 0 |
| Enforce consolidated-helper duplication ratchet | passed | 0 | 8 |
| Enforce inline SHA-256 hashing ratchet | passed | 0 | 2 |
| Enforce posture-environment test serialization | passed | 0 | 3 |
| Enforce server-derived ActionOrigin at endpoints | passed | 0 | 0 |
| Enforce declared file-store concurrency postures | passed | 0 | 2 |
| Enforce ledger-book-native accounting scope | passed | 0 | 0 |
| Enforce ledger dimension coverage across surfaces | passed | 0 | 0 |
| Build web workstation .NET lane | passed | 0 | 181 |
| Run .NET non-integration test projects | failed | 1 | 1682 |

#### .NET test summary

- Total .NET slices: 19
- Passed: 18
- Failed: 1
- Test counts: {"passed": 19338, "failed": 4, "skipped": 5, "other": 0}

#### Failing .NET slices

- core-remainder (tests/Meridian.Tests/Meridian.Tests.csproj) exited 1

#### First useful failure lines

- `/workspace/Meridian-main/src/Meridian.Storage/Archival/AtomicFileWriter.cs(609,13): warning CA1416: This call site is reachable on all platforms. 'FileStreamOptions.UnixCreateMode.set' is unsupported on: 'windows'. (h...`
- `/workspace/Meridian-main/src/Meridian.Application/Integrations/ProviderIntegrationSchemaDriftService.cs(40,44): warning CS8604: Possible null reference argument for parameter 'request' in 'Task<ProviderIntegrationSche...`
- `/workspace/Meridian-main/src/Meridian.Storage/Archival/AtomicFileWriter.cs(609,13): warning CA1416: This call site is reachable on all platforms. 'FileStreamOptions.UnixCreateMode.set' is unsupported on: 'windows'. (h...`
- `/workspace/Meridian-main/src/Meridian.Application/Integrations/ProviderIntegrationSchemaDriftService.cs(40,44): warning CS8604: Possible null reference argument for parameter 'request' in 'Task<ProviderIntegrationSche...`

#### Artifact roots

- `artifacts/ci-summary/`
- `artifacts/build-logs/` when a build lane ran
- `artifacts/test-results/` when a test lane ran
