# Deployment Manifests — Experimental

**Status:** experimental; not part of the supported production envelope
**Governing decision:** [ADR-019: Production Support Matrix and Typed Deployment Posture](../docs/adr/019-production-support-matrix-and-deployment-posture.md)
**Disposition owner:** `PRD-013` in the [production-readiness tracker](../docs/product/implementation-todo-list.md)

The v1 supported production deployment is the **single-operator, single-company local
workstation** installed through the desktop-installer lane: one Meridian host process serving the
browser workstation over loopback plus the WPF desktop shell. Nothing in this directory is part of
that envelope. The runtime enforces this: production postures validate the final dependency graph
at startup and fail closed on prohibited bindings, and remote `ProductionApi` hosting remains
gated until its release blockers close.

| Directory | Contents | Posture |
| --- | --- | --- |
| `docker/` | Dockerfile and compose stack | Experimental — container startup, secrets, health, and rollback alignment are open under `PRD-013` |
| `k8s/` | Kubernetes manifests (kustomize) | Experimental — same `PRD-013` scope; not startable in a production posture today |
| `systemd/` | Linux service unit | Experimental — same `PRD-013` scope |
| `monitoring/` | Prometheus, Grafana, and alert-rule assets | Operational tooling usable against any envelope; executable objectives and alert wiring are owned by `PRD-111` |

These manifests stay in-tree as working material for the future hosted envelope (the v2 direction
named in ADR-019). Do not present them as a supported deployment path in documentation or release
guidance; when `PRD-013` chooses the certified deployment, unsupported manifests are archived or
promoted through that row's acceptance evidence.
