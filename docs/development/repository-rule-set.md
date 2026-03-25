# Meridian Repository Rule Set

**Owner:** Core Team  
**Scope:** Engineering — Repository-Wide  
**Review Cadence:** Quarterly (or when platform/tooling shifts)

---

## 1) Purpose

This rule set defines the non-negotiable standards for contributing to Meridian so changes stay safe, reviewable, and consistent across C#, F#, UI, infrastructure, providers, storage, and tooling.

---

## 2) Core Principles

- **Safety first:** Prefer correctness and observability over speed.
- **Small, focused changes:** Keep PR scope tight to reduce regression risk.
- **Reproducibility:** Every change should be buildable and testable from command line.
- **Traceability:** Decisions, tests, and assumptions must be visible in commit/PR history.
- **No silent behavior drift:** If behavior changes, update tests and docs in the same PR.

---

## 3) Non-Negotiable Engineering Rules

### 3.1 Async, concurrency, and cancellation

- All async public methods must accept and propagate `CancellationToken`.
- Do not block async work (`.Result`, `.Wait()`, sync-over-async).
- Do not use `Task.Run` for I/O-bound operations.
- Prefer `IAsyncEnumerable<T>` where streaming semantics are natural.
- Use pipeline/channel abstractions approved by ADRs for producer-consumer flows (see [ADR-004: Async Streaming Patterns](../adr/004-async-streaming-patterns.md) and [ADR-013: Bounded Channel Policy](../adr/013-bounded-channel-policy.md)).

### 3.2 Logging and diagnostics

- Use structured logging with named placeholders.
- Include operational context in logs (provider, symbol, timeframe, correlation IDs when available).
- Never log secrets, credentials, or API tokens.
- Errors must include enough context to triage without reproducing blindly.

### 3.3 Configuration and secrets

- No secrets in source, config files, tests, or docs examples.
- Runtime configuration should support mutation-safe patterns (`IOptionsMonitor<T>` where applicable).
- Any new config field must be documented in sample config and docs when user-facing.

### 3.4 Serialization and contracts

- Follow established JSON source-generation patterns for serialization.
- Any new serializable DTO must be registered in the relevant serializer context.
- Public/API contracts require explicit compatibility review before breaking changes.

### 3.5 Exceptions and error modeling

- Use domain-appropriate exception types (derive from project base exceptions where required).
- Throw `ArgumentException` family for invalid inputs; `InvalidOperationException` for invalid state.
- Wrap provider/network failures with context-rich exceptions for diagnostics.

### 3.6 Performance-sensitive code

- Avoid unnecessary allocations in hot paths.
- Prefer `Span<T>` / `Memory<T>` where materially beneficial.
- Benchmark before and after non-trivial performance changes.

### 3.7 Package and dependency hygiene

- Use Central Package Management rules (`Directory.Packages.props`).
- Do not pin versions directly in project `<PackageReference>` entries.
- New dependencies require a short justification in PR body (why needed, alternatives considered).

---

## 4) Repository Workflow Rules

### 4.1 Before coding

1. Restate acceptance criteria.
2. Check known AI/recent regression patterns (`docs/ai/ai-known-errors.md`).
3. Confirm target area and blast radius (which projects/tests/docs are affected).

### 4.2 During implementation

- Make minimal, reversible edits.
- Keep naming and file placement consistent with existing architecture.
- Avoid broad opportunistic refactors in feature/fix PRs.
- If you must refactor, separate refactor commit from behavioral changes.

### 4.3 Validation before PR

At minimum, run commands relevant to touched areas:

- `dotnet restore Meridian.sln /p:EnableWindowsTargeting=true`
- `dotnet build Meridian.sln -c Release --no-restore /p:EnableWindowsTargeting=true`
- `dotnet test tests/Meridian.Tests/Meridian.Tests.csproj -c Release /p:EnableWindowsTargeting=true`
- `dotnet test tests/Meridian.FSharp.Tests/Meridian.FSharp.Tests.fsproj -c Release /p:EnableWindowsTargeting=true`

If uncertain about impact, run full repository validation:

- `dotnet restore Meridian.sln /p:EnableWindowsTargeting=true`
- `dotnet build Meridian.sln -c Release --no-restore /p:EnableWindowsTargeting=true`
- `dotnet test tests/Meridian.Tests/Meridian.Tests.csproj -c Release /p:EnableWindowsTargeting=true`
- `dotnet test tests/Meridian.FSharp.Tests/Meridian.FSharp.Tests.fsproj -c Release /p:EnableWindowsTargeting=true`

### 4.4 Documentation and traceability

- Behavior changes must include doc updates in same PR.
- New workflows/features need either README, guide, or HELP entry updates.
- PR body must include: summary, risks/tradeoffs, validation commands, follow-ups.

---

## 5) Testing Rules

- Every bug fix must include at least one regression test unless impossible (document why).
- New behavior must be covered by unit/integration tests at the nearest architectural boundary.
- Avoid brittle tests tied to incidental implementation details.
- Use deterministic inputs (no hidden time/network dependency without fixtures/mocks).
- For provider changes, validate:
  - happy path mapping
  - malformed payload handling
  - retry/rate-limit behavior (where relevant)
  - cancellation behavior

---

## 6) Pull Request Rules

- PRs should be focused and reviewable in one pass.
- Title must reflect implemented behavior, not intent-only language.
- Include a short risk section:
  - affected subsystems
  - migration/compatibility concerns
  - rollback plan (if operationally meaningful)
- Link related ADR/issue/doc references when applicable.

---

## 7) Commit Rules

- Commit messages should be imperative and specific.
  - Good: `Add regression test for polygon quote timestamp parsing`
  - Avoid: `fix stuff`
- Separate unrelated concerns into separate commits.
- Never commit generated noise or local-only environment files unless intentionally versioned.

---

## 8) Prohibited Changes

- Introducing secrets or credentials into repo history.
- Skipping tests for behavior changes without explicit rationale.
- Mixing massive refactors into urgent bugfixes without segregation.
- Adding dependencies to “quick fix” design issues without architecture discussion.
- Suppressing warnings/errors broadly to force green builds.

---

## 9) Rule Exceptions

Exceptions are allowed only when documented in PR with:

1. rule being bypassed,
2. reason,
3. risk assessment,
4. compensating controls,
5. follow-up issue (if debt is introduced).

---

## 10) Definition of Done (DoD)

A change is considered done when all are true:

- Acceptance criteria are met.
- Relevant build/tests pass.
- Required docs updated.
- Security/secret handling reviewed.
- PR description includes scope, risks, validation.

---

## 11) Suggested Enforcement (Optional but Recommended)

- CI gates for build + required test suites.
- Linters/analyzers enabled with no blanket suppression.
- PR template requiring risk + validation sections.
- CODEOWNERS routing for high-risk directories (providers, storage, execution).

---

## 12) Ownership and Maintenance

- This rule set is owned by the Core Team.
- Any contributor may propose updates via PR.
- Rule updates should include rationale and rollout notes when impactful.
