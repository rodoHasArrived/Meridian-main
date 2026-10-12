from pathlib import Path
import datetime as dt
import hashlib
import json
import os
import re
import subprocess
import sys

root=Path('/workspace/Meridian-main')
out=Path('/workspace/scratch/w10-seam-review-fixes/wpf')
paths=[
    'src/Meridian.Wpf/README.md',
    'src/Meridian.Wpf/ViewModels/OperationsContinuityViewModel.cs',
    'src/Meridian.Wpf/Workstation/Models/OperationsContinuityClosePresentation.cs',
    'src/Meridian.Wpf/Workstation/Models/OperationsContinuityPresentationModels.cs',
    'tests/Meridian.Wpf.Tests/ViewModels/OperationsContinuityViewModelTests.cs',
    'tests/Meridian.Wpf.Tests/ViewModels/OperationsContinuityViewModelTests.SharedBlockers.cs',
    'tests/Meridian.Wpf.Tests/ViewModels/OperationsContinuityViewModelTests.SelectionConsistency.cs',
]
def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()
def head():
    return subprocess.check_output(['git','rev-parse','HEAD'],cwd=root,text=True).strip()
def source_rows():
    return [{'path':p,'sha256':sha(root/p)} for p in paths]
command=['python','build/python/cli/buildctl.py','build','--project','tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj','--configuration','Release','--full-wpf-build','--property','IsWindows=true','--property','EnableWindowsTargeting=true','--profile','session:w10-seam-merge-wpf','--queue','--verbosity','quiet']
env=os.environ.copy()
env.update(PATH='/workspace/.dotnet:'+env.get('PATH',''),DOTNET_ROOT='/workspace/.dotnet',DOTNET_CLI_TELEMETRY_OPTOUT='1',DOTNET_NOLOGO='true')
before={'evidenceKind':'working-tree compilation of staged WPF changes; not a committed application candidate','headAtStart':head(),'startedAtUtc':dt.datetime.now(dt.timezone.utc).isoformat(),'command':command,'sourceHashes':source_rows(),'testsExecuted':0,'renderedWindowsCases':0}
(out/'worktree-before.json').write_text(json.dumps(before,indent=2)+'\n')
with (out/'build.log').open('w') as stream:
    code=subprocess.call(command,cwd=root,env=env,stdout=stream,stderr=subprocess.STDOUT)
log=(out/'build.log').read_text(errors='replace')
after=source_rows()
unchanged=before['sourceHashes']==after
methods=[]
for path in paths[-2:]:
    text=(root/path).read_text()
    for match in re.finditer(r'((?:\s*\[(?:Fact|Theory|InlineData)[^\n]*\]\s*)+)public\s+(?:async\s+)?(?:Task|void)\s+(\w+)\(',text):
        attrs,name=match.groups()
        methods.append({'name':name,'xunitCasesInSource':attrs.count('[InlineData') or 1})
profile=re.search(r'Build profile: .*?\((profile-[a-f0-9]+)\)',log)
assembly=None
baml=[]
if code==0 and profile:
    key=profile.group(1)
    assembly_path=root/'artifacts/bin'/key/'Meridian.Wpf.Tests/Release/net10.0-windows10.0.19041.0/Meridian.Wpf.Tests.dll'
    if assembly_path.is_file():
        payload=assembly_path.read_bytes()
        assembly={'path':str(assembly_path),'sha256':sha(assembly_path),'bytes':len(payload)}
        for method in methods:
            method['presentInAssemblyMetadata']=method['name'].encode() in payload
    for path in (root/'artifacts/obj'/key/'Meridian.Wpf').rglob('OperationsContinuityPage.baml'):
        baml.append({'path':str(path),'sha256':sha(path)})
compiled=bool(code==0 and assembly and unchanged and methods and all(m.get('presentInAssemblyMetadata') for m in methods))
warnings=re.findall(r'^\s*(\d+) Warning\(s\)',log,re.M)
errors=re.findall(r'^\s*(\d+) Error\(s\)',log,re.M)
record={**before,'finishedAtUtc':dt.datetime.now(dt.timezone.utc).isoformat(),'headAfterBuild':head(),'sourceHashesAfterBuild':after,'ownedFilesUnchangedDuringBuild':unchanged,'exitCode':code,'warnings':int(warnings[-1]) if warnings else None,'errors':int(errors[-1]) if errors else None,'fullWpfSourceAndXamlTargetBuildSucceeded':bool(code==0 and assembly and baml),'fullWpfTestAssemblyCompiled':compiled,'regressionCasesCompiled':sum(m['xunitCasesInSource'] for m in methods) if compiled else 0,'regressionMethods':methods,'testAssembly':assembly,'compiledPageArtifacts':baml,'logSha256':sha(out/'build.log'),'operatorDecision':'pending'}
(out/'working-tree-compilation.json').write_text(json.dumps(record,indent=2)+'\n')
print(json.dumps({k:record[k] for k in ['exitCode','warnings','errors','ownedFilesUnchangedDuringBuild','fullWpfTestAssemblyCompiled','regressionCasesCompiled','testsExecuted']}))
sys.exit(code if code else (0 if compiled else 2))
