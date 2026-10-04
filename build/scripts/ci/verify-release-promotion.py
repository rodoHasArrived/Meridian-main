#!/usr/bin/env python3
"""Stage only packages and evidence whose commit, attempt and certified digests agree."""
from __future__ import annotations
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil

FAMILIES = {('desktop-installer', 'win-x64'), ('desktop-installer', 'win-arm64'), ('consumer-setup', 'win-x64')}
GATES = {'eligibility', 'ci', 'secrets', 'codeql', 'production', 'smoke', 'release-preflight',
         'build-msix', 'build-consumer-setup', 'certify-installed-desktop'}


def digest(file):
    with file.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def verify(root: Path, receipts: Path, smoke: Path, commit: str, run_id: str, attempt: str, needs: dict):
    if any(needs.get(gate, {}).get('result') != 'success' for gate in GATES):
        raise ValueError('Every release dependency must succeed; missing, skipped or failed gates cannot publish.')
    manifests = list(root.rglob('*-release-evidence.json'))
    found = set()
    assets = {}
    certified = []
    for file in manifests:
        manifest = json.loads(file.read_text(encoding='utf-8'))
        family = (manifest['project'], manifest['runtime'])
        if family not in FAMILIES or family in found:
            raise ValueError(f'Unexpected or duplicate package family: {family}')
        found.add(family)
        for field, expected in [('commitSha', commit), ('workflowRunId', run_id), ('workflowRunAttempt', attempt)]:
            if str(manifest.get(field)) != expected:
                raise ValueError(f'{file.name}: mismatched {field}')
        tracked = {Path(row['path']).name: row for row in manifest['files']
                   if Path(row['path']).parent.as_posix() == manifest['artifactRoot']
                   and row['path'].lower().endswith(('.msix', '.msixbundle', '.appinstaller', '.exe', '.spdx.json'))}
        for asset in file.parent.iterdir():
            if not asset.is_file() or not (asset.name in tracked or asset == file or asset.name.endswith('-SHA256SUMS')):
                continue
            if asset.name in assets:
                raise ValueError(f'Duplicate release asset name: {asset.name}')
            actual = digest(asset)
            if asset.name in tracked and actual != tracked[asset.name]['sha256']:
                raise ValueError(f'Package or SBOM digest changed: {asset.name}')
            assets[asset.name] = {'path': asset, 'sha256': actual}
        if any(name not in assets for name in tracked):
            raise ValueError(f'Missing packaged evidence in {file.name}')
        checksum = file.parent / f'{family[0]}-{family[1]}-SHA256SUMS'
        checksums = {}
        for line in checksum.read_text(encoding='utf-8-sig').splitlines():
            sha, name = line.split('  ', 1)
            if name in checksums or name not in assets or sha != assets[name]['sha256']:
                raise ValueError('Checksum evidence contains duplicates, unknown assets or changed digests.')
            checksums[name] = sha
        if set(checksums) != set(tracked) | {file.name}:
            raise ValueError('Checksum evidence does not cover every published family asset.')
        if family[0] == 'desktop-installer':
            runtime = family[1]
            receipt = json.loads((receipts / f'desktop-installer-{runtime}-lifecycle.json').read_text(encoding='utf-8'))
            if receipt.get('status') != 'passed' or receipt.get('sourceCommit') != commit:
                raise ValueError(f'Failed or mismatched lifecycle certification: {runtime}')
            if receipt.get('mode') not in ('first-release', 'n-1-update') or receipt.get('publisherTrust') != 'pre-trusted-certificate-chain':
                raise ValueError('Lifecycle evidence must use a production publisher and an explicit certification mode.')
            if str(receipt.get('workflowRunId')) != run_id or str(receipt.get('workflowRunAttempt')) != attempt:
                raise ValueError('Lifecycle evidence belongs to another run attempt.')
            if receipt.get('architecture', '').lower() != runtime.removeprefix('win-'):
                raise ValueError('Lifecycle architecture does not match package runtime.')
            current = receipt['currentPackage']
            if current['name'] not in tracked or assets[current['name']]['sha256'] != current['sha256']:
                raise ValueError('Published package differs from the exact package that passed lifecycle certification.')
            required = {'trust-publisher', 'install-current', 'launch-current', 'repair-current', 'launch-after-repair', 'uninstall'}
            passed = {step['name'] for step in receipt['steps'] if step['status'] == 'passed'}
            # In N-1 mode installing current is named update-current by the existing harness.
            if receipt.get('mode') == 'n-1-update':
                required.discard('install-current')
                required.update({'install-prior', 'launch-prior', 'update-current', 'rollback-prior', 'launch-after-rollback'})
            if not required <= passed:
                raise ValueError('Lifecycle receipt omits required successful steps.')
            certified.append(receipt)
    if found != FAMILIES:
        raise ValueError('Missing required release package families/runtimes.')
    if 'Meridian-Setup.exe' not in assets:
        raise ValueError('Missing consumer setup package.')
    startup = json.loads(smoke.read_text(encoding='utf-8'))
    if (startup.get('commitSha') != commit or str(startup.get('workflowRunId')) != run_id
            or str(startup.get('workflowRunAttempt')) != attempt
            or startup.get('project') != 'web-workstation' or startup.get('runtime') != 'win-x64'
            or 'web-workstation-installed-startup' not in startup.get('validationLanes', [])):
        raise ValueError('Required web-workstation/win-x64 installed-startup evidence is absent or mismatched.')
    return assets, certified, startup


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, required=True)
    parser.add_argument('--receipts', type=Path, required=True)
    parser.add_argument('--smoke', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    needs = json.loads(os.environ['RELEASE_NEEDS'])
    assets, receipts, startup = verify(args.root, args.receipts, args.smoke, os.environ['GITHUB_SHA'],
                              os.environ['GITHUB_RUN_ID'], os.environ['GITHUB_RUN_ATTEMPT'], needs)
    args.output.mkdir(parents=True, exist_ok=True)
    if list(args.output.iterdir()):
        raise ValueError('Publication directory must be fresh.')
    for name, asset in assets.items():
        shutil.copyfile(asset['path'], args.output / name)
        if digest(args.output / name) != asset['sha256']:
            raise ValueError('Digest changed while staging publication.')
    evidence = dict(schemaVersion=1, commitSha=os.environ['GITHUB_SHA'], runId=os.environ['GITHUB_RUN_ID'],
                    runAttempt=os.environ['GITHUB_RUN_ATTEMPT'],
                    runUrl=f"https://github.com/{os.environ['GITHUB_REPOSITORY']}/actions/runs/{os.environ['GITHUB_RUN_ID']}/attempts/{os.environ['GITHUB_RUN_ATTEMPT']}",
                    validations=needs, lifecycle=receipts, installedStartup=startup,
                    assets={name: asset['sha256'] for name, asset in assets.items()})
    (args.output / 'release-gate-evidence.json').write_text(json.dumps(evidence, indent=2) + '\n', encoding='utf-8')
    print(f'Verified {len(assets)} unique release assets at the exact certified commit.')


if __name__ == '__main__':
    main()
