#!/usr/bin/env python3
"""Validate source identity and monotonically increasing MSIX versions before builds."""
from __future__ import annotations
import argparse
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile
import xml.etree.ElementTree as ET
import zipfile


def package_version(version: str) -> tuple[int, ...]:
    match = re.fullmatch(r'(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?', version)
    if not match:
        raise ValueError(f'Invalid release version: {version}')
    result = tuple(map(int, match.groups())) + (0,)
    if any(part > 65535 for part in result):
        raise ValueError('MSIX version components must fit in 16 bits.')
    return result


def require_increasing(candidate: tuple[int, ...], previous: list[tuple[int, ...]]):
    if any(candidate <= version for version in previous):
        raise ValueError('MSIX version must strictly increase. RC and stable tags with the same major.minor.patch both map to revision 0 and collide; choose a higher package version.')


def msix_version(file: Path) -> str:
    with zipfile.ZipFile(file) as archive:
        root = ET.fromstring(archive.read('AppxManifest.xml'))
    identity = next(node for node in root if node.tag.rsplit('}', 1)[-1] == 'Identity')
    version = identity.attrib['Version']
    if not re.fullmatch(r'\d+\.\d+\.\d+\.\d+', version):
        raise ValueError(f'Invalid package identity version: {version}')
    return version


def gh_json(endpoint: str):
    return json.loads(subprocess.check_output(['gh', 'api', endpoint], text=True))


def published_versions(repository: str) -> list[tuple[int, ...]]:
    previous = []
    page = 1
    while releases := gh_json(f'repos/{repository}/releases?per_page=100&page={page}'):
        for release in releases:
            # Evaluation uses a separate self-signed identity/channel. Production RCs count.
            if release['draft'] or not release['tag_name'].startswith('v'):
                continue
            previous.append(package_version(release['tag_name'][1:]))
            for asset in release['assets']:
                if not asset['name'].endswith('.msix'):
                    continue
                # Read actual package identity even for legacy releases that stamped a constant
                # manifest version. Tag names alone cannot prove the installed version.
                with tempfile.TemporaryDirectory(prefix='meridian-version-') as temp:
                    file = Path(temp) / 'prior.msix'
                    with file.open('wb') as output:
                        subprocess.run(['gh', 'api', f"repos/{repository}/releases/assets/{asset['id']}",
                                        '-H', 'Accept: application/octet-stream'], stdout=output, check=True)
                    previous.append(tuple(map(int, msix_version(file).split('.'))))
        page += 1
    return previous


def verify_source(expected: str, ref: str, publishing: bool):
    actual = subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True).strip()
    if actual != expected:
        raise ValueError('Checkout commit does not match the coordinator commit.')
    if publishing:
        if not ref.startswith('refs/tags/v'):
            raise ValueError('Publication requires a v* tag push.')
        tagged = subprocess.check_output(['git', 'rev-parse', ref + '^{commit}'], text=True).strip()
        if tagged != expected:
            raise ValueError('Tag and checked-out commit differ.')
        subprocess.run(['git', 'merge-base', '--is-ancestor', expected, 'origin/main'], check=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--rehearsal-version', default='1.0.0-rehearsal')
    args = parser.parse_args()
    publishing = os.environ['GITHUB_EVENT_NAME'] == 'push'
    ref = os.environ['GITHUB_REF']
    verify_source(os.environ['GITHUB_SHA'], ref, publishing)
    version = ref.removeprefix('refs/tags/v') if publishing else args.rehearsal_version
    candidate = package_version(version)
    require_increasing(candidate, published_versions(os.environ['GITHUB_REPOSITORY']))
    with open(os.environ['GITHUB_OUTPUT'], 'a', encoding='utf-8') as output:
        output.write(f"package_version={'.'.join(map(str, candidate))}\nversion={version}\ncommit={os.environ['GITHUB_SHA']}\n")
    print(f'Eligible commit {os.environ["GITHUB_SHA"]}; MSIX version {candidate}; publication={publishing}')


if __name__ == '__main__':
    main()
