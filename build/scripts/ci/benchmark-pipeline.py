#!/usr/bin/env python3
"""Run the bounded PRD-112 pipeline budget lane and retain commit-bound evidence."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import platform
import signal
import subprocess
import sys
import time
import uuid


ROOT = Path(__file__).resolve().parents[3]
PROFILE = ROOT / 'build/config/testing/pipeline-benchmark-profile.json'
PROJECT = 'benchmarks/Meridian.Benchmarks/Meridian.Benchmarks.csproj'
ASSEMBLY = 'benchmarks/Meridian.Benchmarks/bin/Release/net10.0/Meridian.Benchmarks.dll'


def now():
    return datetime.now(timezone.utc).isoformat()


def write_json(path, value):
    path.write_text(json.dumps(value, indent=2, allow_nan=False) + '\n', encoding='utf-8')


def provenance(local):
    commit = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()
    dirty = bool(subprocess.check_output(
        ['git', 'status', '--porcelain'], cwd=ROOT, text=True).strip())
    if local:
        return dict(commitSha=commit, worktreeDirty=dirty, executionEnvironment='local',
                    runId=f'local-{uuid.uuid4().hex}', runAttempt='1')
    required = ('GITHUB_SHA', 'GITHUB_RUN_ID', 'GITHUB_RUN_ATTEMPT')
    if any(not os.environ.get(key) for key in required):
        raise ValueError('Actions metadata is required; use --local for local evidence.')
    if os.environ['GITHUB_SHA'] != commit or dirty:
        raise ValueError('Hosted evidence requires a clean checkout of GITHUB_SHA.')
    if not all(os.environ[key].isdigit() for key in required[1:]):
        raise ValueError('Actions run ID and attempt must be numeric.')
    return dict(commitSha=commit, worktreeDirty=False, executionEnvironment='github-actions',
                runId=os.environ['GITHUB_RUN_ID'], runAttempt=os.environ['GITHUB_RUN_ATTEMPT'])


def hardware():
    # Only known, non-secret machine/runner properties are recorded.
    cpu = Path('/proc/cpuinfo').read_text(encoding='utf-8')
    memory = Path('/proc/meminfo').read_text(encoding='utf-8')
    models = sorted({line.split(':', 1)[1].strip() for line in cpu.splitlines()
                     if line.startswith('model name')})
    if not models:
        raise ValueError('CPU model is unavailable; cannot record a hardware profile.')
    result = dict(os=platform.system(), architecture=platform.machine(),
                  kernel=platform.release(), cpuModels=models, logicalCpuCount=os.cpu_count(),
                  cpuAffinity=sorted(os.sched_getaffinity(0)),
                  memoryTotal=next(line for line in memory.splitlines() if line.startswith('MemTotal:')),
                  osRelease=Path('/etc/os-release').read_text(encoding='utf-8'),
                  runner={key: os.environ.get(key) for key in
                          ('RUNNER_NAME', 'RUNNER_OS', 'RUNNER_ARCH', 'ImageOS', 'ImageVersion')})
    result['cgroup'] = {name: path.read_text().strip() for name in ('cpu.max', 'memory.max')
                        if (path := Path('/sys/fs/cgroup') / name).is_file()}
    return result


def run_command(command, log_path, timeout, env, cwd=ROOT):
    result = dict(command=command, workingDirectory=str(cwd), startedAt=now(),
                  exitCode=None, timedOut=False, error=None)
    started = time.monotonic()
    with log_path.open('w', encoding='utf-8') as log:
        try:
            # Kill the entire process group on timeout, including BDN-generated children.
            process = subprocess.Popen(command, cwd=cwd, env=env, stdout=log,
                                       stderr=subprocess.STDOUT, start_new_session=True)
            try:
                result['exitCode'] = process.wait(timeout=timeout)
            except subprocess.TimeoutExpired:
                os.killpg(process.pid, signal.SIGKILL)
                process.wait()
                result.update(exitCode=124, timedOut=True, error=f'Timed out after {timeout}s')
        except OSError as exc:
            result.update(exitCode=127, error=str(exc))
        if result['error']:
            log.write(result['error'] + '\n')
    result.update(seconds=time.monotonic() - started, completedAt=now())
    return result


def check_full_measurements(directory, profile):
    """Reject partial/mixed reports before the validator's best-match selection can mask them."""
    budgets = json.loads((directory / 'bdn/perf-budgets.json').read_text())
    expected = set(profile['requiredStages'])
    actual = [b['stage_name'] for b in budgets if not b.get('requires_simd', False)]
    if len(actual) != len(expected) or set(actual) != expected:
        raise ValueError('Exported non-SIMD budgets differ from the recorded profile.')
    rows = []
    for path in (directory / 'bdn/results').glob('*-report-full.json'):
        report = json.loads(path.read_text())
        if not report.get('HostEnvironmentInfo'):
            raise ValueError(f'{path.name} has no BenchmarkDotNet host/runtime information.')
        rows.extend(report['Benchmarks'])
    names = [row.get('FullName') for row in rows]
    required = {profile['benchmarkClass'] + '.' + stage for stage in expected}
    if len(names) != len(required) or set(names) != required:
        raise ValueError('Expected exactly one full measurement for each of the eight pipeline stages.')
    if any(not row.get('Measurements') for row in rows):
        raise ValueError('Full BenchmarkDotNet reports must retain raw Measurements.')


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output-root', type=Path, default=ROOT / 'artifacts/pipeline-benchmark')
    parser.add_argument('--local', action='store_true', help='Mark local/dirty evidence explicitly.')
    args = parser.parse_args(argv)
    try:
        identity = provenance(args.local)
        profile = json.loads(PROFILE.read_text(encoding='utf-8'))
        directory = (args.output_root / identity['commitSha'] /
                     f'{identity["runId"]}-{identity["runAttempt"]}').resolve()
        # Never reuse measurements from another invocation, including an interrupted run.
        directory.mkdir(parents=True, exist_ok=False)
    except (OSError, ValueError, subprocess.CalledProcessError) as exc:
        parser.error(str(exc))

    write_json(directory / 'profile.json', profile)
    manifest = dict(schemaVersion=1, **identity, startedAt=now(), conclusion='running',
                    profileId=profile['id'], profileSha256=hashlib.sha256(PROFILE.read_bytes()).hexdigest(),
                    runnerLabel=f'local-{platform.system()}-{platform.machine()}' if args.local
                    else profile['runnerLabel'], commands={}, errors=[])
    env = os.environ.copy()
    env.update(profile['environment'])
    env['MERIDIAN_BENCH_HISTORY_ROOT'] = str(directory / 'history')
    manifest['runtimeEnvironment'] = profile['environment']

    def persist():
        write_json(directory / 'run.json', manifest)

    def command(name, argv, timeout, log=None, cwd=ROOT):
        result = run_command(argv, directory / (log or f'{name}.log'), timeout, env, cwd=cwd)
        manifest['commands'][name] = result
        persist()
        print(f'{name}: exit {result["exitCode"]}; evidence: {directory}', flush=True)
        return result['exitCode'] == 0

    persist()
    try:
        if platform.system() != profile['os'] or platform.machine() != profile['architecture']:
            raise ValueError('This profile requires Linux x86_64.')
        manifest['hardware'] = hardware()
        persist()
        if not command('sdk', ['dotnet', '--version'], 30):
            raise ValueError('Unable to identify the .NET SDK.')
        if (directory / 'sdk.log').read_text().strip() != profile['sdkVersion']:
            raise ValueError(f'This profile requires SDK {profile["sdkVersion"]}.')
        if not command('runtime', ['dotnet', '--list-runtimes'], 30):
            raise ValueError('Unable to identify installed runtimes.')
        runtime = f'Microsoft.NETCore.App {profile["runtimeVersion"]} '
        if not any(line.startswith(runtime) for line in (directory / 'runtime.log').read_text().splitlines()):
            raise ValueError(f'This profile requires runtime {profile["runtimeVersion"]}.')
        if not command('dotnetInfo', ['dotnet', '--info'], 30, 'dotnet-info.log'):
            raise ValueError('Unable to record .NET runtime information.')
        if not command('restore', ['dotnet', 'restore', PROJECT, '-p:EnableWindowsTargeting=true'],
                       profile['buildTimeoutSeconds']):
            raise ValueError('Benchmark restore failed.')
        if not command('build', ['dotnet', 'build', PROJECT, '-c', profile['configuration'],
                                 '--no-restore', '-p:EnableWindowsTargeting=true'],
                       profile['buildTimeoutSeconds']):
            raise ValueError('Benchmark build failed.')
        command('benchmark', ['dotnet', '--fx-version', profile['runtimeVersion'], str(ROOT / ASSEMBLY),
                              '--filter', profile['benchmarkClass'] + '.*',
                              '--job', profile['job'], '--launchCount', str(profile['launchCount']),
                              '--warmupCount', str(profile['warmupCount']),
                              '--iterationCount', str(profile['iterationCount']),
                              '--iterationTime', str(profile['iterationTimeMs']),
                              '--memory', '--exporters', 'fulljson', '--export-budgets',
                              '--artifacts', str(directory / 'bdn')], profile['benchmarkTimeoutSeconds'],
                cwd=ROOT / Path(PROJECT).parent)
    except (OSError, ValueError, KeyError) as exc:
        manifest['errors'].append(str(exc))

    # Always invoke the real validator, including after failed builds or empty BDN runs.
    command('validator', [sys.executable, str(ROOT / 'build/scripts/validate_budget.py'),
                          '--bdn-results', str(directory / 'bdn/results'),
                          '--budget-json', str(directory / 'bdn/perf-budgets.json'),
                          '--fail-on-violation', '--json-output', str(directory / 'budget-evidence.json')],
            60, 'budget-validation.log')
    try:
        check_full_measurements(directory, profile)
        evidence = json.loads((directory / 'budget-evidence.json').read_text())
        if evidence['measured_count'] != len(profile['requiredStages']):
            raise ValueError('Validator did not measure every required stage.')
        manifest['measurements'] = [dict(stage, operationsPerSecond=1e9 / stage['actual_mean_nanos']
                                        if stage['actual_mean_nanos'] else None)
                                    for stage in evidence['stages'] if not stage['requires_simd']]
    except (OSError, ValueError, KeyError, TypeError) as exc:
        manifest['errors'].append(f'Incomplete benchmark evidence: {exc}')

    failed = (manifest['errors'] or 'benchmark' not in manifest['commands']
              or any(item['exitCode'] != 0 for item in manifest['commands'].values()))
    manifest.update(conclusion='failure' if failed else 'success', completedAt=now())
    manifest['artifactSha256'] = {
        str(path.relative_to(directory)): hashlib.sha256(path.read_bytes()).hexdigest()
        for path in sorted(directory.rglob('*')) if path.is_file() and path.name != 'run.json'
    }
    persist()
    print(f'Pipeline benchmark {manifest["conclusion"]}: {directory / "run.json"}', flush=True)
    for error in manifest['errors']:
        print(error, file=sys.stderr)
    return int(bool(failed))


if __name__ == '__main__':
    raise SystemExit(main())
