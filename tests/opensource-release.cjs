'use strict';
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict'),cp=require('node:child_process');
const {snapshot,audit,scan}=require('../scripts/prepare-opensource-review.cjs');let n=0;
const check=(ok,name)=>{assert.ok(ok,name);n++;console.log('PASS '+name);};
for(const name of ['vehicles.real-redacted.json','tracks.real.json','gt3-fixed-clean.replay.json.gz']){let rejected=false;try{scan('tests/Fixtures/'+name,Buffer.from('{}'));}catch{rejected=true;}check(rejected,'public source rejects captured game input '+name);}
const expected=fs.readFileSync('docs/lmu-pitwall-LICENSE.txt','utf8').replace('Copyright (c) 2025 Swizzjack','Copyright (c) 2026 Suedehead84');
check(fs.readFileSync('LICENSE','utf8')===expected,'MIT standard text unchanged except verified 2026 Suedehead84 copyright');
check(fs.readFileSync('Directory.Build.props','utf8').includes('<PackageLicenseExpression>MIT</PackageLicenseExpression>'),'package metadata identifies MIT');
for(const p of ['README.md','README.de.md']){const s=fs.readFileSync(p,'utf8');check(s.includes('MIT')&&!/source remains private|Quellcode bleibt privat/i.test(s),p+' advertises open-source MIT without obsolete private-source policy');}
check(fs.readFileSync('.github/FUNDING.yml','utf8').split(/\r?\n/).every(l=>!l.trim()||l.trim().startsWith('#')),'funding configuration has no active or placeholder accounts');
check(['.github/ISSUE_TEMPLATE/bug_report.yml','.github/ISSUE_TEMPLATE/feature_request.yml','.github/pull_request_template.md','CONTRIBUTING.md'].every(p=>fs.existsSync(p)),'community issue PR and contribution templates included');
const dir='artifacts/opensource-review/test-'+Date.now();const result=snapshot(dir);check(result.files>150&&fs.existsSync(path.join(dir,'src/Lmu.LiveCapture/LiveState.cs'))&&fs.existsSync(path.join(dir,'scripts/build.ps1')),'full audited application source and reproducible build inputs included');
function reject(name,bytes,label){const p=path.join(dir,name),old=fs.existsSync(p)?fs.readFileSync(p):null;fs.mkdirSync(path.dirname(p),{recursive:true});fs.writeFileSync(p,bytes);let failed=false;try{audit(dir);}catch{failed=true;}finally{if(old)fs.writeFileSync(p,old);else fs.unlinkSync(p);}check(failed,label);}
reject('private.sqlite','SYNTHETIC','source snapshot rejects databases');
reject('docs/EULA_DRAFT.en.md','SYNTHETIC','source snapshot rejects superseded proprietary legal drafts');
reject('README.md','#access='+'a'.repeat(48),'source snapshot rejects access credentials');
reject('src/Lmu.LiveCapture/LiveState.cs','altered source','source snapshot rejects mismatch with build inputs');
fs.mkdirSync(path.join(dir,'.git'));let rejected=false;try{audit(dir);}catch{rejected=true;}fs.rmdirSync(path.join(dir,'.git'));check(rejected,'source snapshot rejects inherited Git history');
const docs=cp.spawnSync(process.execPath,['scripts/check-docs.cjs'],{cwd:path.resolve(dir),encoding:'utf8'});check(docs.status===0&&docs.stdout.includes('local Markdown links'),'standalone source archive documentation checks work without private Git metadata');
const privacy=cp.spawnSync(process.execPath,['scripts/privacy-audit.cjs'],{cwd:path.resolve(dir),encoding:'utf8'});check(privacy.status===0&&JSON.parse(privacy.stdout).mode==='standalone-source-archive','standalone source archive privacy audit stays inside snapshot');
console.log(`All ${n} open-source release assertions passed. No repository or funding account created.`);
