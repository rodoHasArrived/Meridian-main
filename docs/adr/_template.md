# ADR-XXX: [Title]

**Status:** Proposed | Accepted | Deprecated | Superseded
**Date:** YYYY-MM-DD
**Deciders:** [List of people involved]
**Supersedes:** [ADR-XXX if applicable]
**Superseded by:** [ADR-XXX if applicable]

## Context

[Describe the context and problem that led to this decision. What forces are at play? What constraints exist?]

## Decision

[Clearly state the decision that was made. Be specific and unambiguous.]

## Implementation Links

<!-- These links are verified by the build process to ensure code matches documentation -->

| Component | Location | Purpose |
|-----------|----------|---------|
| Interface | `src/Path/To/IInterface.cs` | Contract definition |
| Implementation | `src/Path/To/Implementation.cs` | Concrete implementation |
| Tests | `tests/Path/To/Tests.cs` | Verification tests |

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

<!-- Specify verification attributes that enforce this decision -->
- `[ImplementsAdr("ADR-XXX")]` - Applied to implementing classes
- Build-time verification via `make verify-adrs`

## References

- [Link to related documentation]
- [Link to discussion/RFC]
- [Link to external resources]

---

*Last Updated: YYYY-MM-DD*
