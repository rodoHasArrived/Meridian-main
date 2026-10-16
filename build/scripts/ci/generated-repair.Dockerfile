# Verified linux/amd64 Node 24 bookworm manifest, 2026-10-15.
FROM node:24-bookworm-slim@sha256:ec857ea6bb60cb3d0fa4d97495590c5fe7d51be8572c307b55957eeb06569cf7
RUN apt-get update \
    && apt-get install -y --no-install-recommends git python3 python3-venv ca-certificates \
    && rm -rf /var/lib/apt/lists/*
# Runtime sets an unprivileged UID, read-only rootfs, fresh writable work/output
# mounts, and an explicit environment. No runner credentials or socket enter it.
