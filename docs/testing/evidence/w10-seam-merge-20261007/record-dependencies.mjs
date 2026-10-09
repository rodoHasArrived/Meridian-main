import fs from 'node:fs/promises';
import path from 'node:path';
import crypto from 'node:crypto';
import { createRequire } from 'node:module';
import { execFileSync, spawnSync } from 'node:child_process';
const repo='/workspace/Meridian-main',dashboard=path.join(repo,'src/Meridian.Ui/dashboard'),out='/workspace/scratch/w10-seam-merge/browser-final';
const require=createRequire(path.join(dashboard,'package.json'));
const hash=bytes=>crypto.createHash('sha256').update(bytes).digest('hex');
const files=['package.json','package-lock.json','src/Meridian.Ui/dashboard/package.json','src/Meridian.Ui/dashboard/package-lock.json','src/Meridian.Ui/dashboard/node_modules/.package-lock.json','src/Meridian.Ui/dashboard/vite.config.ts','src/Meridian.Ui/dashboard/postcss.config.cjs','src/Meridian.Ui/dashboard/tailwind.config.ts'];
const hashes=[];
for(const file of files){try{hashes.push({file,sha256:hash(await fs.readFile(path.join(repo,file)))});}catch(e){hashes.push({file,unavailable:e.code});}}
const packages=['react','react-dom','react-router-dom','tailwindcss','postcss','autoprefixer','vite','vitest','playwright','typescript','lucide-react'];
const dependencies=[];
for(const name of packages){let location;try{location=require.resolve(`${name}/package.json`);}catch{let d=path.dirname(require.resolve(name));while(true){const p=path.join(d,'package.json');try{if(JSON.parse(await fs.readFile(p,'utf8')).name===name){location=p;break;}}catch{}d=path.dirname(d);}}
 const bytes=await fs.readFile(location);dependencies.push({name,version:JSON.parse(bytes).version,manifestPath:location,manifestSha256:hash(bytes)});
}
const installed=spawnSync('npm',['ls','--all','--json'],{cwd:dashboard,encoding:'utf8',maxBuffer:64*1024*1024});
await fs.writeFile(path.join(out,'npm-ls-all.json'),installed.stdout);await fs.writeFile(path.join(out,'npm-ls-all.stderr.txt'),installed.stderr);
const result={atUtc:new Date().toISOString(),head:execFileSync('git',['-C',repo,'rev-parse','HEAD'],{encoding:'utf8'}).trim(),nodeVersion:process.version,npmVersion:execFileSync('npm',['--version'],{encoding:'utf8'}).trim(),files:hashes,dependencies,npmLs:{exitStatus:installed.status,stdoutSha256:hash(installed.stdout),stderrSha256:hash(installed.stderr)},captureHarnessSha256:hash(await fs.readFile(path.join(out,'capture.mjs')))};
await fs.writeFile(path.join(out,'dependencies.json'),JSON.stringify(result,null,2));console.log(JSON.stringify({head:result.head,node:result.nodeVersion,npm:result.npmVersion,dependencies:dependencies.map(({name,version})=>({name,version})),npmLsExit:installed.status},null,2));
