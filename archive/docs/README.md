# Documentation Archive

**Status:** archive-index
**Owner:** core-team
**Reviewed:** 2026-11-14

Historical and superseded documentation, kept for traceability. Nothing here is active guidance,
status, or planning input. Start current work from the [documentation front door](../../docs/README.md)
and the [roadmap registry](../../docs/roadmap/README.md). Archive rules live in
[Documentation Ownership](../../docs/documentation-ownership.md).

| Bucket | Contents |
| --- | --- |
| [adr/](adr/README.md) | Historical ADRs retained because source still names their IDs through `ImplementsAdr` attributes. |
| [plans/](plans/README.md) | Finished or superseded plans, blueprints, and next-work determinations. |
| [summaries/](summaries/README.md) | Superseded status snapshots, strategy proposals, and retired-workflow notes. |

Each bucket README lists its documents with the archive date, reason, and current replacement.
Material removed by the 2026-09-11 cleanup (`982eea2d`) is available in the
[pre-cleanup archive snapshot](https://github.com/rodoHasArrived/Meridian-main/tree/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs).

## Dated reviews and measurement evidence

The following files moved on 2026-11-14 to keep long review histories and loose audit
snapshots out of active guidance. Review source commits, dates, findings and qualifications
remain intact; archival does not resolve findings. JSON and gzip evidence is byte-preserved.

| Retained material | Context and current owner |
| --- | --- |
| [Security Master extensibility review](architecture/security-master-extensibility-review.md) | Full institutional review chronology through the November 11 owner decisions. Navigation links were rebased during archival. The [remediation working plan](../../docs/engineering/plans/security-master-review-remediation-plan.md) owns follow-up proposals. |
| [Adversarial program review, August 25](product/adversarial-program-review-2026-08-25.md) | Original `e232ece1` review and dated addenda; navigation links were rebased. See [product review history](../../docs/product/review-history.md). |
| [October 5 local benchmark receipt](engineering/actions-local-benchmarks-2026-10-05.json) | Byte-preserved measurements; the [measurement narrative](../../docs/engineering/actions-rollout-measurement-2026-10-05.md) links the associated source, report and diagnostic policy snapshots. |
| [October 5 storage audit](engineering/actions-storage-audit-2026-10-05.json) | Byte-preserved bounded Actions sample; [scope and limitations](../../docs/engineering/actions-storage-audit-2026-10-05.md). |
| [October 15 workflow audit](engineering/actions-workflow-audit-2026-10-15.json) | Byte-preserved run/check evidence and compressed-input hashes; the [follow-up narrative](../../docs/engineering/actions-workflow-followup-2026-10-15.md) links all reproduction inputs. These diagnostic policies are historical inputs, not active branch-protection policy. |
| [Endpoint fixture concurrency receipt](testing/evidence/endpoint-fixture-concurrency.json) | Byte-preserved warmups, samples and identity digests; [reproduction and interpretation](../../docs/testing/endpoint-fixture-isolation.md). |

Current generated dashboards, registry outputs, and integrity-bound evidence packets remain at
their tooling-owned paths under `docs/`. Do not move individual files out of a checksummed packet.
