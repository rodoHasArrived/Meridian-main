# ADR-019: Production Support Matrix and Typed Deployment Posture

**Status:** Proposed (awaiting core-team sign-off; signing this ADR is the `PRD-000` "signed support matrix" artifact)
**Date:** 2026-07-12
**Deciders:** core-team
**Supersedes:** —
**Superseded by:** —

## Context

`PRD-000` in the [production-readiness tracker](../product/implementation-todo-list.md) blocks
every supported production release until Meridian declares exactly one supported production
envelope and makes the final dependency graph valid for it. Today no single declaration exists,
and the enforcement policy is split three ways:

- `ProductionServiceRegistrationPolicy` decides "production" from environment variables
  (`ASPNETCORE_ENVIRONMENT`, `MERIDIAN_MODE`, …) and validates the service collection **once**,
  mid-composition, inside `AddMarketDataServices` — every registration made after that call
  (workstation services, execution wiring, host-local stores) bypasses validation entirely.
- `ApiHostOptions.DeploymentMode` decides "production" from configuration
  (`ApiHost:DeploymentMode` / `MERIDIAN_API_DEPLOYMENT_MODE`), silently falls back to
  `LocalWorkstation` on unrecognized values, and is never consulted by the registration policy.
- `StorageFeatureRegistration.EnsureGovernancePersistenceProfile` enforces a third,
  governance-only rule from its own environment-variable read.

A host can therefore run as `ProductionApi` while the registration policy believes it is in
development, and in-memory/null/no-op bindings registered after the composition root — or through
factory delegates the descriptor scan cannot see — reach a nominally production process silently.

Deployment surfaces meanwhile multiply without a support claim: a desktop installer lane,
`deploy/docker/`, `deploy/k8s/`, and `deploy/systemd/` all exist, while `PRD-001` (sessions,
tenancy), `PRD-013` (container startup), and `PRD-014` (artifact certification) document that no
remote multi-node topology is close to certifiable.

## Decision

### 1. Support matrix (v1 production envelope)

| Dimension | Supported in the v1 production envelope | Explicitly experimental (fail closed / not production) |
|-----------|------------------------------------------|--------------------------------------------------------|
| Topology | Single-operator, single-company, single-node **local workstation**: one Meridian host process per operator machine | Remote/browser-hosted `ProductionApi`, multi-node, shared multi-tenant hosting |
| OS / runtime | Windows 11 x64, .NET 10, installed via the desktop-installer lane | Linux container (`deploy/docker/`, `deploy/k8s/`), `deploy/systemd/` service |
| UI | Browser workstation served over loopback by the local host, plus the WPF desktop shell (co-equal lanes over shared contracts) | Any remotely served workstation origin |
| Auth / tenancy | `AuthenticationMode.Required` (packaged/customer builds already default to it); one company per isolated deployment | `Optional` auth outside dev/test; cross-tenant shared deployments (until `PRD-001` tenant partitioning completes) |
| Storage | Local WAL / atomic-file stores under the resolved `DataRoot`; operator-provisioned **local** PostgreSQL for the domains that require it (fund accounts, fund structure, security master, direct lending) | Remote/managed database topologies; any in-memory, null, or no-op store in a production posture |
| Providers | Approved HTTPS provider connections through the existing connection registry | SFTP (`PRD-104`), QuantLab in-process script execution (`PRD-012`) |

Everything in the right-hand column stays in the repository as labeled experimental material and
must be rejected — not silently degraded — when a production posture selects it. `PRD-013`
chooses the installer-based publish smoke as its certification path; container/systemd work is
deferred until the envelope reopens (recorded scope reduction per the tracker's completion rules).

Under this decision the `PRD-000` completion evidence item "a `ProductionApi` integration test
that starts successfully" is re-scoped to: the **supported local-workstation posture composes and
starts**, and every experimental posture **fails closed with a diagnostic naming the prohibited
bindings**. Sign-off on this ADR ratifies that amendment.

### 2. One typed deployment posture, one policy, validated on the final graph

- `MeridianDeploymentPosture` (in `Meridian.Application.Composition`) is the single typed posture:
  `Unspecified`, `LocalWorkstation`, `ProductionApi`, `Worker`, `Migration`. Hosts declare it on
  the service collection **before** composing features; `ApiHostOptions` maps its deployment mode
  onto it and **throws on unrecognized `DeploymentMode` values** instead of falling back.
- `ProductionServiceRegistrationPolicy` becomes the only production decision: a posture of
  `ProductionApi`, or a production/live environment variable, or
  `MERIDIAN_API_DEPLOYMENT_MODE=ProductionApi`, all resolve to the same answer everywhere.
- A **final-graph guard** (`ProductionRegistrationGuardService`) is inserted as the first
  `IHostedService` by the composition root. At host start — after every registration has landed —
  it re-validates the complete collection in production postures: a static pass over descriptor
  implementation types and instances, plus eager resolution of singleton factory descriptors so
  factory-hidden implementations are checked by their **actual runtime type**. Any violation
  aborts startup with the full list of prohibited bindings.
- Prohibited-by-default implementation names in production postures: `InMemory*`, `Null*`,
  `NoOp*`, `Fake*`, `Stub*`, `Sample*`, plus anything carrying
  `[NonProductionOnlyImplementation]` or `INonProductionOnlyService`. A type that is genuinely
  safe for a production role opts out explicitly with `[ProductionSafeImplementation("reason")]`
  (matched by attribute name so lower layers can declare their own copy without an upward
  reference). Marker-based prohibitions cannot be overridden.

## Implementation Links

| Component | Location | Purpose |
|-----------|----------|---------|
| Posture | `src/Meridian.Application/Composition/MeridianDeploymentPosture.cs` | Typed deployment posture + declaration extension |
| Policy | `src/Meridian.Application/Composition/ProductionServiceRegistrationPolicy.cs` | Unified production resolution + prohibited-implementation matcher |
| Guard | `src/Meridian.Application/Composition/ProductionRegistrationGuardService.cs` | Final-graph startup validation |
| Host mapping | `src/Meridian/ApiHostOptions.cs`, `src/Meridian/UiServer.cs` | Strict mode parsing, posture declaration before composition |
| Tests | `tests/Meridian.Tests/Application/Composition/` | Startup-rejection and posture-unification tests |

## Rationale

The local workstation is the only topology whose prerequisites largely exist today: the desktop
installer and WPF lane are active, desktop persistence is externalized under `%LocalAppData%`,
auth already defaults to `Required` for packaged builds, and single-company isolation sidesteps
the unfinished tenancy partitioning. Certifying it first yields a real, supportable release while
`PRD-001`/`PRD-013` mature the hosted envelope. Enforcing the envelope in the dependency graph —
rather than in documentation — is what makes the declaration binding: an experimental binding
cannot reach a production posture without failing startup loudly.

## Alternatives Considered

### Alternative 1: Certify the hosted `ProductionApi` container topology first

**Pros:** one shared deployment to operate; matches the browser-first onboarding wedge.
**Cons:** blocked behind durable/revocable multi-node sessions, tenant partitioning, container
startup alignment, and supply-chain certification (`PRD-001`, `PRD-013`, `PRD-014`) — the longest
possible path to any supported release.
**Why rejected:** sequencing; it becomes the v2 envelope once its blockers close.

### Alternative 2: Keep environment-variable-only production detection

**Pros:** no host changes.
**Cons:** perpetuates the split-policy defect named in `PRD-000`; a `ProductionApi` host in a
default environment stays unvalidated.
**Why rejected:** it is the defect, not a design option.

## Consequences

### Positive

- One binding answer to "is this production?" and one enforcement seam for the whole graph,
  including late and factory-hidden registrations.
- Production postures fail closed **today**, with a diagnostic enumerating exactly which real
  bindings remain to be built — turning the remaining `PRD-*` work into a machine-generated list.
- Unrecognized deployment-mode strings can no longer silently demote a production host to
  a development posture.

### Negative

- No production posture can start until real bindings replace the prohibited ones (intended:
  production certification is blocked anyway; the guard makes that state explicit).
- Eager singleton resolution at production startup surfaces construction failures at boot
  (intended fail-fast, but it changes startup timing for production postures).

### Neutral

- Development and test composition behavior is unchanged; the guard no-ops outside production
  postures.
- `deploy/docker/`, `deploy/k8s/`, and `deploy/systemd/` remain in-tree as experimental material
  pending `PRD-013` disposition.

## Compliance

### Code Contracts

```csharp
// Hosts declare posture before composing features:
services.DeclareMeridianDeploymentPosture(MeridianDeploymentPosture.LocalWorkstation);

// Non-production implementations are marked, or match a prohibited name prefix:
[NonProductionOnlyImplementation]            // never valid in a production posture
[ProductionSafeImplementation("reason")]     // explicit, auditable opt-out for name matches
```

### Runtime Verification

- `ProductionRegistrationGuardService` runs first at host start and throws on violations in
  production postures.
- `ProductionServiceRegistrationPolicy.Validate` still runs inline at the composition root for
  non-hosted composition paths.

## References

- [Production-readiness tracker — PRD-000](../product/implementation-todo-list.md)
- [Program state registry](../roadmap/data/program-state.yml)
- [Layer boundaries](../architecture/layer-boundaries.md)
- [ADR-017: Modular operational monolith](017-modular-operational-monolith.md)

---

*Last Updated: 2026-07-12*
