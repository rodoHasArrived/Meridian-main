# High Availability Guide

This document describes strategies for running Meridian in a resilient configuration that minimizes data loss and downtime.

---

## Architecture Overview

Meridian is a monolithic application by design (see [ADR-003](../adr/003-microservices-decomposition.md)). High availability is achieved through provider failover, WAL-based durability, and process-level redundancy rather than microservice decomposition.

```
┌──────────────────────────────────────────────┐
│                Load Balancer                 │
│           (health probe: /healthz)           │
└────────────┬─────────────────┬───────────────┘
             │                 │
     ┌───────▼───────┐ ┌──────▼────────┐
     │  Instance A   │ │  Instance B   │
     │  (primary)    │ │  (standby)    │
     │               │ │               │
     │  Alpaca WS ─┐ │ │  Polygon WS ┐│
     │  Polygon WS ┘ │ │  Alpaca WS  ┘│
     │       │        │ │       │      │
     │   EventPipeline│ │  EventPipeline│
     │       │        │ │       │      │
     │   WAL → Storage│ │  WAL → Storage│
     └───────┬────────┘ └──────┬───────┘
             │                 │
     ┌───────▼─────────────────▼───────┐
     │       Shared Storage (NFS/S3)   │
     └─────────────────────────────────┘
```

---

## Provider Failover

The `FailoverAwareMarketDataClient` automatically switches between streaming providers when the primary becomes unavailable.

### Configuration

Failover rules are configured via `/api/failover/rules` or `appsettings.json`:

```json
{
  "Failover": {
    "Enabled": true,
    "HealthCheckIntervalSeconds": 30,
    "Rules": [
      {
        "Id": "primary-to-secondary",
        "PrimaryProvider": "alpaca",
        "SecondaryProvider": "polygon",
        "TriggerConditions": {
          "ConsecutiveFailures": 3,
          "LatencyThresholdMs": 5000,
          "DisconnectDurationSeconds": 60
        },
        "AutoRevert": true,
        "RevertAfterSeconds": 300
      }
    ]
  }
}
```

### Failover Triggers

| Condition | Default Threshold | Notes |
|-----------|-------------------|-------|
| Consecutive connection failures | 3 | WebSocket disconnect count |
| Latency exceeds threshold | 5,000 ms | End-to-end message latency |
| Disconnect duration | 60 seconds | Time since last successful message |

### Monitoring

- `/api/failover/health` — Current failover state and provider health
- `/api/providers/status` — All provider connection states
- `/api/connections` — Detailed connection health metrics

---

## Write-Ahead Log (WAL) Durability

The WAL ensures no data loss during crashes or restarts. Events are written to the WAL before being committed to storage.

### Recovery Process

1. On startup, the application checks for uncommitted WAL entries.
2. Uncommitted entries are replayed through the pipeline.
3. Once confirmed written to storage, WAL entries are truncated.
4. Old WAL files are optionally archived as gzip.

### Configuration for Maximum Durability

```json
{
  "Storage": {
    "Wal": {
      "SyncMode": "EveryWrite",
      "MaxFlushDelay": "00:00:00.100",
      "ArchiveAfterTruncate": true
    }
  }
}
```

For the tradeoff between durability and performance, see [Performance Tuning Guide](performance-tuning.md#write-ahead-log-wal).

---

## Process-Level Redundancy

### Active-Standby

Run two instances with the same configuration. Use health probes to detect the primary's failure.

1. **Primary instance**: Actively collecting data, serving the web dashboard.
2. **Standby instance**: Running in `--dry-run` mode or with subscriptions paused.
3. **Health monitor**: External watchdog (systemd, Kubernetes, or a simple script) that promotes standby when primary fails.

#### systemd Configuration

The provided systemd service file (`deploy/systemd/meridian.service`) includes:
- `Restart=always` — Automatic restart on crash
- `RestartSec=10` — 10-second delay before restart
- `WatchdogSec=120` — systemd kills the process if it stops responding within 2 minutes

#### Kubernetes Deployment

Use the built-in health probes:

```yaml
livenessProbe:
  httpGet:
    path: /livez
    port: 8080
  initialDelaySeconds: 15
  periodSeconds: 10

readinessProbe:
  httpGet:
    path: /readyz
    port: 8080
  initialDelaySeconds: 5
  periodSeconds: 5

startupProbe:
  httpGet:
    path: /healthz
    port: 8080
  failureThreshold: 30
  periodSeconds: 10
```

### Active-Active (Advanced)

For setups requiring zero downtime, run two instances collecting from different providers:

- **Instance A**: Alpaca (primary), Polygon (failover)
- **Instance B**: Polygon (primary), Alpaca (failover)

Both write to the same shared storage. The `PortableDataPackager` and `StorageCatalogService` handle deduplication when merging data from multiple sources.

---

## Shared Storage

### Local Disk

For single-instance deployments, local disk with the WAL provides adequate durability. Use a RAID-1 or RAID-10 configuration for disk fault tolerance.

### Network File System (NFS)

For multi-instance deployments, mount shared storage via NFS or a cloud file system (EFS, Azure Files). Ensure:

- Write ordering is preserved (NFS v4+ with close-to-open consistency).
- The WAL directory is on local fast storage, not NFS (WAL requires low-latency fsync).
- Data directories can be on NFS for shared access.

### Object Storage (Future)

Phase 6 of the roadmap includes S3/Azure Blob/GCS sink support. When implemented, this will provide native cloud-durable storage without NFS.

---

## Monitoring and Alerting

### Prometheus Alert Rules

The deployment includes pre-configured alert rules in `deploy/monitoring/alert-rules.yml`:

| Alert | Condition | Severity |
|-------|-----------|----------|
| `MeridianDown` | Instance unreachable for 1 minute | critical |
| `MeridianUnhealthy` | Health check failing for 2 minutes | critical |
| `MeridianHighDropRate` | Drop rate > threshold for 5 minutes | warning |
| `MeridianPipelineBackpressure` | Pipeline utilization > 80% for 2 minutes | warning |
| `MeridianProviderDisconnected` | Provider disconnected for 2 minutes | warning |
| `MeridianHighProviderLatency` | Latency > threshold for 5 minutes | warning |
| `MeridianStorageWriteErrors` | Write errors detected for 5 minutes | critical |
| `MeridianLowDataQuality` | Quality score below threshold for 15 minutes | warning |

### Health Endpoints

| Endpoint | Purpose | Use For |
|----------|---------|---------|
| `/healthz` | Full health check | Startup probe |
| `/readyz` | Readiness check | Readiness probe |
| `/livez` | Liveness check | Liveness probe |
| `/api/health` | Detailed health | Dashboard monitoring |
| `/api/status` | Full system status | Operational monitoring |

---

## Graceful Shutdown

The `GracefulShutdownService` coordinates orderly shutdown:

1. Stop accepting new subscriptions.
2. Flush the EventPipeline (up to 30-second timeout).
3. Commit and truncate the WAL.
4. Close provider connections.
5. Flush and close storage sinks.

Send `SIGTERM` (or `Ctrl+C`) to trigger graceful shutdown. Avoid `SIGKILL` — it bypasses the flush and the WAL will need recovery on next startup.

---

## Disaster Recovery Checklist

1. **WAL files intact**: Check `_wal/` directory for uncommitted entries. The application replays these automatically on startup.
2. **Storage integrity**: Run `--validate-package` on data files, or use `/api/storage/quality/*` endpoints.
3. **Configuration backup**: Store `appsettings.json` in version control (without secrets).
4. **Credential recovery**: All credentials are in environment variables or vault — no data files to lose.
5. **Backfill gaps**: After extended downtime, run `--backfill` with the gap period to fill missing data.

---

*Last Updated: 2026-02-10*
