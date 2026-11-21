# W12-ICAGREE-001 status-detail migration snapshot

Captured from `docs/roadmap/data/roadmap-items.yml` at `3503ae000e` on 2026-11-14.
This retains the original implementation detail, reservations and historical claims verbatim.
Read the current registry for status and decisions; this snapshot does not grant new acceptance.
Detailed boundaries remain applicable until a later scoped change replaces them.

```yaml
    current_summary: >-
      User-directed P1 implementation reuses the native legal registry and links immutable
      tenant-owned accounting profiles to exact approved legal IDs, integer versions and payload
      hashes. Profiles own templates, party books, stable account mappings and bounded validity;
      their owner approval does not replace independent legal approval or create a novation.
      Separate facilities may coexist; one profile's approved validity windows cannot conflict.
      Guarantees have no posting treatment.
      Approved legacy-attribution sidecars pin exact source line digests without rewriting posted
      journals. A posted-balance schedule enriches actual journals with agreement/account/party
      evidence for close and NetSuite mapping controls. Current consumer and migration verification
      must be evidenced before acceptance; source and added tests alone do not prove a passed check.
```
