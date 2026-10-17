#!/usr/bin/env python3
"""Stage only packages and evidence whose commit, attempt and certified digests agree."""
from __future__ import annotations
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil

FAMILIES = {('desktop-installer', 'win-x64'), ('desktop-installer', 'win-arm64'), ('consumer-setup', 'win-x64')}
GATES = {'eligibility', 'ci', 'secrets', 'codeql', 'production', 'smoke', 'release-preflight',
         'build-msix', 'build-consumer-setup', 'certify-installed-desktop', 'certify-installed-consumer'}

CONSUMER_REQUIRED_STEPS = {
    'verify-signature', 'verify-payload', 'clean-runner', 'install-current', 'launch-current',
    'bundled-database-ready', 'repair-current', 'launch-after-repair', 'restart-current',
    'launch-after-restart', 'uninstall', 'preserve-data',
}
CONSUMER_PREDECESSOR_STEPS = {
    'install-prior', 'launch-prior', 'update-current', 'rollback-prior', 'launch-after-rollback',
}
CONSUMER_RESTORE_STEPS = {'restore-current', 'launch-after-restore'}


def digest(file):
    with file.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def verify_consumer_predecessor(receipt: dict, commit: str, run_id: str, attempt: str):
    predecessor = receipt.get('predecessor')
    if (not isinstance(predecessor, dict) or type(predecessor.get('schemaVersion')) is not int
            or predecessor['schemaVersion'] != 1):
        raise ValueError('Consumer lifecycle receipt requires a bound predecessor resolution receipt.')
    identity = {'project': 'consumer-setup', 'runtime': 'win-x64', 'sourceCommit': commit}
    if (any(predecessor.get(field) != expected for field, expected in identity.items())
            or str(predecessor.get('workflowRunId')) != run_id
            or str(predecessor.get('workflowRunAttempt')) != attempt):
        raise ValueError('Consumer predecessor resolution belongs to another family, commit or run attempt.')
    for field in ('eligibleReleaseCount', 'consumerReleaseCount'):
        if type(predecessor.get(field)) is not int or predecessor[field] < 0:
            raise ValueError('Consumer predecessor resolution requires nonnegative release counts.')
    if predecessor['consumerReleaseCount'] > predecessor['eligibleReleaseCount']:
        raise ValueError('Consumer predecessor release counts are inconsistent.')
    first_release = receipt['mode'] == 'first-release'
    if predecessor.get('firstRelease') is not first_release:
        raise ValueError('Consumer lifecycle mode does not match resolved predecessor availability.')
    if first_release:
        if (predecessor['consumerReleaseCount'] != 0 or receipt.get('priorPackage') is not None
                or receipt.get('priorReleaseTag') is not None or predecessor.get('priorPackage') is not None
                or predecessor.get('priorReleaseTag') is not None
                or not isinstance(predecessor.get('reason'), str) or not predecessor['reason'].strip()):
            raise ValueError('Consumer first-release exception requires explicit evidence that no predecessor exists.')
    else:
        prior, resolved = receipt.get('priorPackage'), predecessor.get('priorPackage')
        tag = receipt.get('priorReleaseTag')
        if (predecessor['consumerReleaseCount'] == 0 or not isinstance(tag, str) or not tag.startswith('v')
                or len(tag) <= 1 or tag != predecessor.get('priorReleaseTag')
                or not isinstance(prior, dict) or not isinstance(resolved, dict)
                or prior.get('name') != 'Meridian-Setup.exe' or resolved.get('name') != 'Meridian-Setup.exe'
                or not isinstance(prior.get('sha256'), str) or not re.fullmatch('[0-9a-f]{64}', prior['sha256'])
                or prior['sha256'] != resolved.get('sha256')):
            raise ValueError('Consumer N-1 certification requires the exact resolved predecessor tag and package digest.')


def verify_consumer_receipt(receipts: Path, tracked: dict, assets: dict,
                            commit: str, run_id: str, attempt: str):
    file = receipts / 'consumer-setup-win-x64-lifecycle.json'
    try:
        receipt = json.loads(file.read_text(encoding='utf-8'))
    except (OSError, json.JSONDecodeError) as error:
        raise ValueError('Missing or invalid consumer lifecycle certification receipt.') from error
    if not isinstance(receipt, dict) or type(receipt.get('schemaVersion')) is not int or receipt['schemaVersion'] != 1:
        raise ValueError('Consumer lifecycle evidence has an unsupported receipt schema.')
    identity = {'project': 'consumer-setup', 'runtime': 'win-x64', 'architecture': 'x64',
                'sourceCommit': commit, 'status': 'passed',
                'publisherTrust': 'pre-trusted-certificate-chain'}
    if any(receipt.get(field) != expected for field, expected in identity.items()):
        raise ValueError('Failed or mismatched consumer lifecycle certification identity.')
    if str(receipt.get('workflowRunId')) != run_id or str(receipt.get('workflowRunAttempt')) != attempt:
        raise ValueError('Consumer lifecycle evidence belongs to another run attempt.')
    mode = receipt.get('mode')
    if mode not in ('first-release', 'n-1-update'):
        raise ValueError('Consumer lifecycle evidence requires an explicit certification mode.')
    verify_consumer_predecessor(receipt, commit, run_id, attempt)
    current = receipt.get('currentPackage')
    package_name = 'Meridian-Setup.exe'
    if (not isinstance(current, dict) or current.get('name') != package_name or package_name not in tracked
            or assets[package_name]['sha256'] != current.get('sha256')):
        raise ValueError('Published consumer EXE differs from the exact package that passed lifecycle certification.')
    rows = receipt.get('steps')
    if not isinstance(rows, list):
        raise ValueError('Consumer lifecycle receipt omits its step evidence.')
    steps = {}
    for row in rows:
        if not isinstance(row, dict) or not isinstance(row.get('name'), str) or not row['name'] or row['name'] in steps:
            raise ValueError('Consumer lifecycle step names must be present and unique.')
        name, status = row['name'], row.get('status')
        if status != 'passed':
            if (mode != 'first-release' or name not in CONSUMER_PREDECESSOR_STEPS or status != 'not-applicable'
                    or not isinstance(row.get('detail'), str) or not row['detail'].strip()):
                raise ValueError('Consumer lifecycle steps must pass or record an explicit first-release exception.')
        steps[name] = status
    if any(steps.get(name) != 'passed' for name in CONSUMER_REQUIRED_STEPS):
        raise ValueError('Consumer lifecycle receipt omits required successful steps.')
    if mode == 'n-1-update' and any(steps.get(name) != 'passed' for name in CONSUMER_RESTORE_STEPS):
        raise ValueError('Consumer N-1 receipt must restore and launch the certified current EXE before uninstall.')
    prior_status = 'not-applicable' if mode == 'first-release' else 'passed'
    if any(steps.get(name) != prior_status for name in CONSUMER_PREDECESSOR_STEPS):
        raise ValueError('Consumer lifecycle receipt omits required predecessor evidence or first-release exceptions.')
    return receipt


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
        else:
            certified.append(verify_consumer_receipt(receipts, tracked, assets, commit, run_id, attempt))
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
