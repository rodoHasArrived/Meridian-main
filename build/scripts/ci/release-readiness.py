#!/usr/bin/env python3
"""Report release blockers without granting publication or replacing promotion verification."""
from __future__ import annotations

import argparse
from datetime import datetime, timedelta, timezone
import importlib.util
import json
import os
from pathlib import Path
import re
import subprocess


SCRIPT_DIR = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('release_promotion', SCRIPT_DIR / 'verify-release-promotion.py')
promotion = importlib.util.module_from_spec(spec)
spec.loader.exec_module(promotion)
GATES = promotion.GATES | {'release'}
RUNNER_JOBS = {
    'windows-latest / native x64 lifecycle': 'clean install lifecycle (win-x64)',
    'windows-11-arm / native ARM64 lifecycle': 'clean install lifecycle (win-arm64)',
    'windows-2025 / consumer packaging': 'package-consumer-setup',
}
RUNNER_LABELS = {
    'windows-latest / native x64 lifecycle': 'windows-latest',
    'windows-11-arm / native ARM64 lifecycle': 'windows-11-arm',
    'windows-2025 / consumer packaging': 'windows-2025',
}


def timestamp(value: str) -> datetime:
    if not isinstance(value, str):
        raise ValueError('Missing timestamp.')
    result = datetime.fromisoformat(value.replace('Z', '+00:00'))
    if result.tzinfo is None:
        raise ValueError('Timestamps must contain a timezone.')
    return result.astimezone(timezone.utc)


def identity(commit: str, run_id: str, attempt: str) -> dict:
    if not re.fullmatch('[0-9a-f]{40}', commit) or not re.fullmatch('[1-9][0-9]*', run_id) or not re.fullmatch('[1-9][0-9]*', attempt):
        raise ValueError('Expected a full commit SHA and positive workflow run/attempt IDs.')
    return dict(commitSha=commit, runId=run_id, runAttempt=attempt)


def bound(payload: dict, expected: dict) -> bool:
    return isinstance(payload, dict) and all(str(payload.get(key)) == value for key, value in expected.items())


def row(name: str, status: str, detail: str, action: str = '') -> dict:
    return dict(name=name, status=status, detail=detail, nextAction=action)


def certificate_status(receipt: dict | None, expected: dict, now: datetime, warning_days: int = 30) -> dict:
    action = 'Review signing secrets and certificate in the protected desktop-release-signing environment; rerun readiness.'
    if receipt is None:
        return row('Signing certificate', 'unknown', 'No signing receipt for this run attempt.', action)
    if not bound(receipt, expected) or type(receipt.get('schemaVersion')) is not int or receipt['schemaVersion'] != 1:
        return row('Signing certificate', 'blocked', 'Signing receipt identity or schema does not match.', action)
    if receipt.get('status') != 'passed' or receipt.get('hasPrivateKey') is not True or receipt.get('publisherMatches') is not True:
        return row('Signing certificate', 'blocked', 'Signing prerequisites failed; private key and publisher must validate.', action)
    try:
        start, end, observed = (timestamp(receipt.get(field)) for field in ('notBefore', 'notAfter', 'observedAt'))
    except (ValueError, TypeError):
        return row('Signing certificate', 'blocked', 'Certificate validity metadata is missing or invalid.', action)
    if observed > now + timedelta(minutes=5) or now - observed > timedelta(days=1):
        return row('Signing certificate', 'unknown', 'Certificate observation is stale or in the future.', action)
    if start > now or end <= now or start >= end:
        return row('Signing certificate', 'blocked', 'Certificate is expired or outside its validity window.', action)
    days = (end - now).total_seconds() / 86400
    if days <= warning_days:
        return row('Signing certificate', 'warning', f'Certificate expires in {days:.1f} days ({end.isoformat()}).',
                   'Renew the certificate, preserve the approved publisher, and complete a signed rehearsal before expiry.')
    return row('Signing certificate', 'ready', f'Certificate valid for {days:.1f} more days ({end.isoformat()}).')


def promotion_status(receipt: dict | None, expected: dict) -> dict:
    action = 'Inspect verify-and-publish-release-evidence and its upstream jobs; rerun the complete coordinator at this commit.'
    if receipt is None:
        return row('Certified package evidence', 'blocked', 'release-gate-evidence.json is missing.', action)
    if not bound(receipt, expected) or type(receipt.get('schemaVersion')) is not int or receipt['schemaVersion'] != 1:
        return row('Certified package evidence', 'blocked', 'Promotion receipt identity or schema does not match.', action)
    validations = receipt.get('validations', {})
    if not isinstance(validations, dict) or any(not isinstance(validations.get(gate), dict) or validations[gate].get('result') != 'success' for gate in promotion.GATES):
        return row('Certified package evidence', 'blocked', 'Promotion receipt lacks successful dependency evidence.', action)
    lifecycle = receipt.get('lifecycle')
    families = set()
    assets = receipt.get('assets')
    if not isinstance(assets, dict) or not assets or any(not isinstance(value, str) or not re.fullmatch('[0-9a-f]{64}', value) for value in assets.values()):
        return row('Certified package evidence', 'blocked', 'Promotion receipt lacks valid asset digests.', action)
    if isinstance(lifecycle, list):
        for item in lifecycle:
            if not isinstance(item, dict):
                continue
            package = item.get('currentPackage', {})
            if (item.get('status') != 'passed' or item.get('sourceCommit') != expected['commitSha']
                    or str(item.get('workflowRunId')) != expected['runId']
                    or str(item.get('workflowRunAttempt')) != expected['runAttempt']
                    or not isinstance(package, dict) or assets.get(package.get('name')) != package.get('sha256')
                    or not package.get('sha256')):
                return row('Certified package evidence', 'blocked', 'Lifecycle identity, result or package digest does not match.', action)
            if package.get('name') == 'Meridian-Setup.exe' and item.get('project') == 'consumer-setup':
                families.add(('consumer-setup', item.get('runtime')))
            elif str(package.get('name', '')).endswith('.msix'):
                families.add(('desktop-installer', 'win-' + str(item.get('architecture', '')).lower()))
    startup = receipt.get('installedStartup', {})
    if (families != promotion.FAMILIES or not isinstance(lifecycle, list) or len(lifecycle) != 3
            or not isinstance(startup, dict) or startup.get('commitSha') != expected['commitSha']
            or str(startup.get('workflowRunId')) != expected['runId']
            or str(startup.get('workflowRunAttempt')) != expected['runAttempt']
            or startup.get('project') != 'web-workstation' or startup.get('runtime') != 'win-x64'
            or not isinstance(startup.get('validationLanes'), list)
            or 'web-workstation-installed-startup' not in startup['validationLanes']):
        return row('Certified package evidence', 'blocked', 'Required lifecycle or installed-startup evidence is incomplete.', action)
    return row('Certified package evidence', 'ready', 'Promotion verifier retained matching commit, attempt, lifecycle and package digest evidence.')


def runner_status(snapshot: dict | None, expected: dict, now: datetime) -> list[dict]:
    action = 'Run a full signed rehearsal; inspect Actions runner capacity and label access if allocation remains queued.'
    if snapshot is None or not bound(snapshot, expected) or type(snapshot.get('schemaVersion')) is not int or snapshot['schemaVersion'] != 1:
        return [row(name, 'unknown', 'No bound runner observation snapshot.', action) for name in RUNNER_JOBS]
    observations = snapshot.get('observations', {})
    result = []
    for name in RUNNER_JOBS:
        item = observations.get(name) if isinstance(observations, dict) else None
        try:
            observed = timestamp(item.get('completedAt')) if isinstance(item, dict) else None
        except (ValueError, TypeError):
            observed = None
        if (observed is None or observed > now or now - observed > timedelta(days=14)
                or item.get('conclusion') != 'success' or not item.get('runnerName')
                or not re.fullmatch('[0-9a-f]{40}', str(item.get('commitSha', '')))
                or not re.fullmatch('[1-9][0-9]*', str(item.get('runId', '')))
                or not re.fullmatch('[1-9][0-9]*', str(item.get('runAttempt', '')))
                or not isinstance(item.get('labels'), list) or RUNNER_LABELS[name] not in item['labels']):
            result.append(row(name, 'unknown', 'No successful assigned coordinator job observed in the last 14 days.', action))
        else:
            result.append(row(name, 'observed', f"Successful assigned job at {observed.isoformat()}, run {item['runId']} attempt {item['runAttempt']} ({item['commitSha']}). Historical observation; current capacity is not guaranteed."))
    if snapshot.get('collectionError'):
        result.append(row('Runner evidence collection', 'unknown', 'Actions history could not be completely queried.',
                          'Check Actions read access and retry the readiness workflow.'))
    return result


def report(mode: str, needs: dict, expected: dict, signing: dict | None, gate: dict | None,
           runners: dict | None, now: datetime, warning_days: int = 30) -> dict:
    rows = []
    if mode == 'coordinator':
        for name in sorted(GATES):
            entry = needs.get(name, {})
            result = entry.get('result', 'missing') if isinstance(entry, dict) else 'missing'
            rows.append(row(name, 'ready' if result == 'success' else 'blocked', f'Job result: {result}.',
                            '' if result == 'success' else f'Inspect {name} and upstream failures; rerun the complete coordinator on the same commit.'))
            if name == 'eligibility' and result != 'success':
                rows[-1]['nextAction'] = ('Review signing prerequisites, source identity and increasing MSIX version; '
                                         'for scheduled rehearsals configure MDC_RELEASE_REHEARSAL_VERSION above published identities.')
        rows.append(promotion_status(gate, expected))
    else:
        rows.extend(runner_status(runners, expected, now))
    rows.append(certificate_status(signing, expected, now, warning_days))
    statuses = {item['status'] for item in rows}
    status = next((value for value in ('blocked', 'unknown', 'warning') if value in statuses), 'ready')
    return dict(schemaVersion=1, **expected, mode=mode, status=status, observedAt=now.isoformat(), checks=rows,
                publicationAuthorized=False)


def markdown(payload: dict) -> str:
    def escape(value: str) -> str:
        return str(value).replace('&', '&amp;').replace('<', '&lt;').replace('>', '&gt;').replace('|', '\\|').replace('\n', ' ')
    lines = [f"## Release readiness: {payload['status']}", '',
             f"Commit `{payload['commitSha']}` · run `{payload['runId']}` · attempt `{payload['runAttempt']}`", '',
             'This diagnostic does not authorize publication. The existing promotion verifier remains authoritative.', '',
             '| Check | Status | Evidence | Next action |', '| --- | --- | --- | --- |']
    lines.extend('| ' + ' | '.join(escape(item[key]) for key in ('name', 'status', 'detail', 'nextAction')) + ' |' for item in payload['checks'])
    return '\n'.join(lines) + '\n'


def gh_json(endpoint: str):
    # Bound network failures; a missing response is unknown, never proof of no runners.
    result = subprocess.run(['gh', 'api', endpoint], check=True, capture_output=True, text=True, timeout=60)
    return json.loads(result.stdout)


def collect_runners(repository: str, expected: dict, now: datetime) -> dict:
    if not re.fullmatch('[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+', repository):
        raise ValueError('Expected owner/repository.')
    observations = {}
    snapshot = dict(schemaVersion=1, **expected, observedAt=now.isoformat(), observations=observations,
                    lookbackDays=14, collectionError=False)
    cutoff = now - timedelta(days=14)
    try:
        # Pagination is bounded by the date window, not an arbitrary first-page sample.
        page = 1
        while True:
            response = gh_json(f'repos/{repository}/actions/workflows/desktop-installer-packaging.yml/runs?per_page=100&page={page}&created=%3E%3D{cutoff.strftime("%Y-%m-%dT%H:%M:%SZ")}')
            runs = response['workflow_runs']
            if not isinstance(runs, list):
                raise ValueError('Invalid run response.')
            for run in runs:
                if run.get('head_repository', {}).get('full_name') != repository:
                    continue
                run_id, attempt = str(run['id']), str(run['run_attempt'])
                identity(run['head_sha'], run_id, attempt)
                jobs_page = 1
                while True:
                    jobs = gh_json(f'repos/{repository}/actions/runs/{run_id}/attempts/{attempt}/jobs?per_page=100&page={jobs_page}')['jobs']
                    for job in jobs:
                        for name, expected_job in RUNNER_JOBS.items():
                            if job.get('name') != expected_job or job.get('conclusion') != 'success' or not job.get('runner_name') or not job.get('started_at'):
                                continue
                            completed = timestamp(job['completed_at'])
                            if completed < cutoff or completed > now or RUNNER_LABELS[name] not in job.get('labels', []):
                                continue
                            if name in observations and timestamp(observations[name]['completedAt']) >= completed:
                                continue
                            observations[name] = dict(commitSha=run['head_sha'], runId=run_id, runAttempt=attempt,
                                                      jobId=job['id'], conclusion='success', runnerName=job['runner_name'],
                                                      labels=job['labels'], completedAt=job['completed_at'])
                    if len(jobs) < 100:
                        break
                    jobs_page += 1
                # There is a finite search cap in Actions' run API; do not claim completeness beyond it.
            if len(runs) < 100:
                break
            if page >= 10:
                snapshot['collectionError'] = True
                break
            page += 1
    except (subprocess.SubprocessError, OSError, ValueError, KeyError, TypeError):
        snapshot['collectionError'] = True
    return snapshot


def read_receipt(directory: Path, filename: str) -> dict | None:
    files = list(directory.rglob(filename)) if directory.exists() else []
    if not files:
        return None
    if len(files) != 1:
        return {}  # Duplicate evidence is invalid, not an arbitrary first match.
    try:
        result = json.loads(files[0].read_text(encoding='utf-8-sig'))
        return result if isinstance(result, dict) else {}
    except (OSError, ValueError):
        return {}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command', choices=('collect-runners', 'summary'))
    parser.add_argument('--mode', choices=('coordinator', 'preflight'), default='preflight')
    parser.add_argument('--evidence-dir', type=Path, default=Path('artifacts/readiness-input'))
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--warning-days', type=int, default=30)
    args = parser.parse_args()
    if not 1 <= args.warning_days <= 365:
        parser.error('--warning-days must be between 1 and 365.')
    expected = identity(os.environ['GITHUB_SHA'], os.environ['GITHUB_RUN_ID'], os.environ['GITHUB_RUN_ATTEMPT'])
    now = datetime.now(timezone.utc)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    if args.command == 'collect-runners':
        payload = collect_runners(os.environ['GITHUB_REPOSITORY'], expected, now)
    else:
        needs = json.loads(os.environ.get('RELEASE_NEEDS', '{}'))
        if not isinstance(needs, dict):
            raise ValueError('Release dependencies must be an object.')
        payload = report(args.mode, needs, expected, read_receipt(args.evidence_dir, 'signing.json'),
                         read_receipt(args.evidence_dir, 'release-gate-evidence.json'),
                         read_receipt(args.evidence_dir, 'runners.json'), now, args.warning_days)
        rendered = markdown(payload)
        args.output.with_suffix('.md').write_text(rendered, encoding='utf-8')
        if os.environ.get('GITHUB_STEP_SUMMARY'):
            with open(os.environ['GITHUB_STEP_SUMMARY'], 'a', encoding='utf-8') as output:
                output.write(rendered)
        print(f"Release readiness: {payload['status']}; full report: {args.output}")
    args.output.write_text(json.dumps(payload, indent=2) + '\n', encoding='utf-8')
    return int(args.command == 'summary' and payload['status'] in ('blocked', 'unknown'))


if __name__ == '__main__':
    raise SystemExit(main())
