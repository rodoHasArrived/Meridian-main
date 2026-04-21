# Kernel Migration Parity Blueprint

## Summary
Establish a fixture-driven C# ↔ F# parity program that blocks kernel regressions during migration by comparing score/severity/reason outputs at each subsystem boundary, while allowing auditable expected divergences for intentional improvements.

## Scope
### In scope
- Golden fixture authoring for each migration target subsystem.
- Dual-run execution against legacy C# and candidate F# paths.
- Deterministic mismatch detection for `score`, `severity`, and `reason` fields.
- Explicit expected-divergence annotations with expiration metadata.
- CI enforcement for kernel-related pull requests.
- Coverage tracking by subsystem in status documentation.

### Out of scope
- Re-implementing all existing kernel logic in this blueprint.
- Automatic fixture generation from production traffic.
- Non-kernel UI-only parity checks.

### Assumptions
- Existing C# kernels remain callable as baseline references during migration.
- Candidate F# implementations expose equivalent callable seams.
- Fixture inputs can be expressed as serialized request contracts per subsystem.

## Architecture
### Core components
1. **Parity Fixture Packs**
   - Location: `tests/Meridian.Tests/Application/FSharpParity/Fixtures/<Subsystem>/`
   - Contents per fixture:
     - `input`: canonical request payload.
     - `baseline`: expected C# output snapshot.
     - `metadata`: scenario description, tags, source commit.

2. **Dual Runtime Adapters**
   - `ILegacyKernelAdapter<TIn,TOut>` (C# path).
   - `ICandidateKernelAdapter<TIn,TOut>` (F# path).
   - Both adapters normalize outputs into a shared parity result shape.

3. **Parity Comparator**
   - Field-level checks for:
     - `score` (numeric exact/epsilon rules by subsystem),
     - `severity` (enum/string identity),
     - `reason` (normalized textual or code identity).
   - Emits structured mismatch records with fixture ID and field deltas.

4. **Expected Divergence Registry**
   - File: `tests/Meridian.Tests/Application/FSharpParity/expected-divergences.json`
   - Entry model:
     - `subsystem`, `fixtureId`, `field`, `legacyValue`, `candidateValue`,
       `justification`, `introducedBy`, `introducedOn`, `expiresOn`.
   - Comparator suppresses only exact, pre-declared mismatches.

5. **Coverage Reporter**
   - Computes per-subsystem parity coverage:
     - `covered scenarios / required scenarios`.
   - Publishes markdown for status docs and CI summaries.

## Interfaces and Models
### Parity contracts
- `KernelParityFixture<TIn,TOut>`
- `KernelParityObservedResult`
- `KernelParityMismatch`
- `KernelParityDivergenceAnnotation`
- `KernelParityCoverageRecord`

### Execution contract
- `IKernelParitySuite.RunAsync(subsystem, ct)` returns:
  - total fixtures,
  - matched fixtures,
  - divergence-approved fixtures,
  - failing mismatches.

## Data Flow
1. Load fixture pack for subsystem.
2. Deserialize input payload.
3. Execute legacy C# adapter.
4. Execute candidate F# adapter.
5. Normalize both outputs.
6. Compare `score`/`severity`/`reason`.
7. Resolve mismatches against expected-divergence registry.
8. Fail test when unresolved mismatches remain.
9. Emit coverage + mismatch summary artifacts.
10. CI gates kernel-related PRs on parity suite success.

## Edge Cases and Risks
- **Floating-point drift:** define subsystem-specific epsilon policy.
- **Reason text churn:** prefer stable reason codes where available.
- **Fixture rot:** require fixture metadata to include source commit/date.
- **Divergence debt:** enforce `expiresOn` and fail expired annotations.
- **Performance:** split parity suites per subsystem for parallel CI.

## Test Plan
### Local
- `dotnet test tests/Meridian.Tests --filter "Category=KernelParity|FullyQualifiedName~KernelParity" -c Release /p:EnableWindowsTargeting=true`
- `dotnet test tests/Meridian.FSharp.Tests --filter "Category=KernelParity|Name~KernelParity" -c Release /p:EnableWindowsTargeting=true`

### CI
- Add **Kernel Parity Suite** to `.github/workflows/pr-checks.yml`.
- Run parity jobs only when kernel-related paths change.
- Upload parity TRX artifacts for failed and successful runs.

## Rollout Plan
1. Bootstrap parity scaffold + CI job.
2. Onboard first subsystem (risk policy) with golden fixtures.
3. Add expected-divergence registry and policy checks.
4. Expand subsystem-by-subsystem until migration boundaries are covered.
5. Enforce minimum coverage threshold growth each milestone.

## Coverage Tracking
Track parity coverage in `docs/status/KERNEL_PARITY_STATUS.md` using:
- Subsystem name,
- Fixture count,
- Covered scenarios,
- Coverage percentage,
- Last verified date,
- Owner.

## Open Questions
- Should reason comparison be strict text or reason-code-first with text fallback?
- Which subsystems require tolerance-based score comparison vs strict equality?
- Do we enforce a global minimum parity coverage threshold in CI immediately, or phase it in?
