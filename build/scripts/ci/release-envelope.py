#!/usr/bin/env python3
"""Reviewed, version-bounded release support policy; no dispatch scope override."""
from __future__ import annotations

import re


FAMILIES = {('desktop-installer', 'win-x64'), ('desktop-installer', 'win-arm64'),
            ('consumer-setup', 'win-x64')}


def required_families(version: str | None = None) -> set[tuple[str, str]]:
    # Receipts produced before version-scoped envelopes retain their original dual-arch
    # requirements. A missing version can never turn ARM64 certification off.
    if version is None:
        return set(FAMILIES)
    match = re.fullmatch(r'(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?', version)
    if not match:
        raise ValueError('Release support scope requires a valid release version.')
    if tuple(map(int, match.groups())) == (0, 1, 0):
        return {('desktop-installer', 'win-x64'), ('consumer-setup', 'win-x64')}
    return set(FAMILIES)


def support_scope(version: str | None = None) -> str:
    return ('windows-x64-v0.1.0' if len(required_families(version)) == 2
            else 'windows-x64-arm64')


def desktop_matrix(version: str) -> list[dict[str, str]]:
    return [dict(runtime=runtime, architecture='x64' if runtime == 'win-x64' else 'ARM64',
                 runner='windows-latest' if runtime == 'win-x64' else 'windows-11-arm')
            for project, runtime in sorted(required_families(version)) if project == 'desktop-installer']
