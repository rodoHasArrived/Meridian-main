# ADR-003: Microservices Decomposition

**Status:** Rejected
**Date:** 2024-07-15
**Deciders:** Core Team

## Context

As the Meridian grew in complexity with multiple data providers, storage tiers, and processing pipelines, the team evaluated whether to decompose the monolithic architecture into microservices.

The proposed decomposition would create separate services for:
1. Data ingestion (per provider)
2. Data processing/normalization
3. Storage management
4. API gateway
5. Monitoring and alerting

## Decision

**Reject microservices decomposition.** Maintain the monolithic architecture with clear internal module boundaries.

## Rationale

After careful analysis, microservices decomposition was deemed inappropriate for this project due to:

### 1. Operational Complexity
- Single-process deployment is significantly simpler to operate
- No need for service mesh, container orchestration, or distributed tracing
- Easier debugging and troubleshooting with a single process
- Lower infrastructure costs (no Kubernetes/Docker overhead required)

### 2. Latency Requirements
- Market data requires sub-millisecond processing latency
- Inter-service communication adds network overhead
- In-process event passing via `System.Threading.Channels` is faster than any RPC mechanism

### 3. Team Size
- Small team (1-3 developers) cannot efficiently maintain multiple services
- Monolith with clear module boundaries provides sufficient separation of concerns
- Single codebase is easier to understand, modify, and test

### 4. Data Locality
- All components need access to the same market data streams
- Duplicating data across services would increase memory usage
- Central event pipeline (`EventPipeline`) provides efficient data distribution

### 5. Deployment Simplicity
- Single executable simplifies deployment to any environment
- No service discovery, load balancing, or circuit breakers between services
- Easier to support diverse deployment targets (Windows, Linux, Docker, bare metal)

## Alternatives Considered

### Alternative 1: Full Microservices

Decompose into 5+ independent services with REST/gRPC communication.

**Pros:**
- Independent scaling per component
- Technology flexibility per service
- Fault isolation

**Cons:**
- Massive operational overhead
- Network latency between services
- Complex debugging across service boundaries
- Overkill for current scale

**Why rejected:** Complexity far outweighs benefits at current scale.

### Alternative 2: Modular Monolith (Chosen)

Keep single deployable unit but enforce module boundaries through namespaces and interfaces.

**Pros:**
- Simple deployment and operations
- Fast in-process communication
- Easy debugging
- Can evolve to microservices later if needed

**Cons:**
- Must be disciplined about module boundaries
- Cannot scale components independently

**Why chosen:** Best fit for team size, latency requirements, and operational simplicity.

### Alternative 3: Hybrid Approach

Core processing as monolith, with satellite microservices for non-critical functions.

**Pros:**
- Core path remains fast
- Flexibility for non-critical features

**Cons:**
- Still introduces distributed systems complexity
- Inconsistent architecture

**Why rejected:** Introduces complexity without clear benefit.

## Consequences

### Positive

- Simple deployment (single executable)
- Fast in-process communication
- Easy debugging and profiling
- Lower operational costs
- Faster development velocity

### Negative

- Cannot scale components independently
- Must maintain discipline around module boundaries
- Large codebase in single repository

### Neutral

- Future growth may require revisiting this decision
- Clear interfaces allow future decomposition if needed

## Implementation

The monolithic architecture uses these patterns to maintain modularity:

| Pattern | Implementation | Purpose |
|---------|---------------|---------|
| Module Boundaries | Namespaces (`Domain/`, `Infrastructure/`, `Application/`) | Logical separation |
| Dependency Injection | `Microsoft.Extensions.DependencyInjection` | Loose coupling |
| Interface Contracts | `IMarketDataClient`, `IHistoricalDataProvider` | Abstraction layers |
| Event Pipeline | `System.Threading.Channels` | Decoupled data flow |
| Configuration | `IOptions<T>` pattern | Runtime flexibility |

## Review Triggers

This decision should be revisited if:
- Team grows beyond 5 developers
- Latency requirements relax significantly
- Specific components need independent scaling
- Multi-region deployment becomes necessary

## References

- [ADR-001: Provider Abstraction](001-provider-abstraction.md) - Interface boundaries
- [ADR-002: Tiered Storage](002-tiered-storage-architecture.md) - Storage modularity
- [ADR-004: Async Streaming](004-async-streaming-patterns.md) - In-process streaming
- [Martin Fowler on Monolith First](https://martinfowler.com/bliki/MonolithFirst.html)

---

*Last Updated: 2026-01-28*
