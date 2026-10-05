# ADR-XXX: [Title]

**Status:** Proposed | Accepted | Deprecated | Superseded
**Date:** YYYY-MM-DD
**Owner:** [Responsible team]
**Reviewed:** YYYY-MM-DD
**Deciders:** [List of people involved]
**Supersedes:** [ADR-XXX if applicable]
**Superseded by:** [ADR-XXX if applicable]

## Context

[Describe the context and problem that led to this decision. What forces are at play? What constraints exist?]

## Decision

[Clearly state the decision that was made. Be specific and unambiguous.]

## Implementation Links

<!-- Replace placeholders with existing repository-relative paths. Identify planned components
separately rather than presenting missing paths as implemented evidence. The ADR link verifier
checks path existence; behavioral compliance needs the focused checks described below. -->

| Component | Location | Purpose |
|-----------|----------|---------|
| Interface | `src/Path/To/IInterface.cs` | Contract definition |
| Implementation | `src/Path/To/Implementation.cs` | Concrete implementation |
| Tests | `tests/Path/To/Tests.cs` | Verification tests |

[State which parts are implemented, which remain proposed, and where current validation or release
evidence is recorded. Decision acceptance alone does not certify a release.]

## Rationale

[Explain why this decision was chosen over alternatives. What trade-offs were considered?]

## Alternatives Considered

### Alternative 1: [Name]

**Pros:**
- [Pro 1]
- [Pro 2]

**Cons:**
- [Con 1]
- [Con 2]

**Why rejected:** [Reason]

### Alternative 2: [Name]

[Repeat pattern]

## Consequences

### Positive

- [Benefit 1]
- [Benefit 2]

### Negative

- [Drawback 1]
- [Drawback 2]

### Neutral

- [Side effect 1]

## Compliance

### Code Contracts

The following contracts must be satisfied by implementations:

```csharp
// [Contract Definition]
// Example: All implementations must satisfy this interface
public interface IExample
{
    // Contract methods
}
```

### Runtime Verification

<!-- Name actual enforcement and focused tests. An annotation provides traceability, not proof
that the implementation satisfies the decision. -->
- `[ImplementsAdr("ADR-XXX")]` — traceability on implementing classes where applicable.
- [Focused tests or runtime guards that enforce this decision, including failure cases.]
- Verify paths in the Implementation Links table from the repository root:

```bash
dotnet run --project build/dotnet/DocGenerator/DocGenerator.csproj -- verify-adrs --adr-dir docs/adr --src-dir src
```

This command verifies referenced files and directories exist; it does not validate behavior.

## References

- [Link to related documentation]
- [Link to discussion/RFC]
- [Link to external resources]

---

*Last Updated: YYYY-MM-DD*
