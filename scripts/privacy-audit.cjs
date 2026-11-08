'use strict';
// Read-only preflight: scan tracked, publish-candidate and reachable-history blobs, including gzipped replay fixtures.
const fs=require('fs'),cp=require('child_process'),zlib=require('zlib');
function git(args,optional=false){try{return cp.execFileSync('git',args,{encoding:'utf8',maxBuffer:64*1024*1024,stdio:['ignore','pipe','pipe']});}catch(e){if(optional)return '';throw e;}}
const path=require('node:path');const top=git(['rev-parse','--show-toplevel'],true).trim();const ownGit=top&&path.resolve(top)===process.cwd();
function walk(d){return fs.readdirSync(d,{withFileTypes:true}).flatMap(e=>{const p=path.join(d,e.name);if(e.isSymbolicLink())throw Error('Source symlink rejected');return e.isDirectory()?['.git','.tools','artifacts','bin','obj','node_modules'].includes(e.name)?[]:walk(p):[p.replaceAll('\\','/').replace(/^\.\//,'')];});}
const tracked=ownGit?git(['ls-files','-z']).split('\0').filter(Boolean):walk('.'),candidates=ownGit?git(['ls-files','--others','--exclude-standard','-z']).split('\0').filter(Boolean):[];
const findings=[];let scanned=0,history=0;
function scan(name,data){if(name.endsWith('.gz')){try{data=zlib.gunzipSync(data,{maxOutputLength:64*1024*1024});}catch{findings.push({file:name,rule:'unreadable compressed fixture'});return;}}
 const text=data.toString('utf8');for(const [rule,re] of [
 ['Steam identifier',/7656119\d{10}/g],['local personal home path',/[A-Z]:[\\/](?:Users|Benutzer)[\\/](?!Public|Default|<|%)[a-z0-9_.-]+[\\/]/gi],
 ['embedded access grant',/#access=[a-f0-9]{24,}/gi],['embedded bearer grant',/Bearer [a-f0-9]{24,}/gi],['private key',/-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----/g],['GitHub token',/gh[pousr]_[A-Za-z0-9]{20,}/g]
 ])if(re.test(text))findings.push({file:name,rule});scanned++;}
for(const name of [...new Set([...tracked,...candidates])]){if(!fs.existsSync(name))continue;if(/(^|\/)(?:artifacts|diagnostics|\.research|\.tools|\.restore-staging|bin|obj|backups)(\/|$)|\.(?:sqlite|sqlite-wal|sqlite-shm|db|jsonl|log|exe|pdb|zip)$|(^|\/)\.env($|\.)/i.test(name)){findings.push({file:name,rule:'private/runtime artifact in upload candidate'});continue;}scan(name,fs.readFileSync(name));}
const objects=ownGit?git(['rev-list','--objects','--all'],true).trim().split('\n').filter(Boolean):[];
for(const item of objects){const [oid,...name]=item.split(' ');if(git(['cat-file','-t',oid]).trim()!=='blob')continue;history++;const data=cp.execFileSync('git',['cat-file','blob',oid],{maxBuffer:64*1024*1024});scan('history:'+name.join(' '),data);}
const report={mode:ownGit?'git-source-and-history':'standalone-source-archive',trackedFiles:tracked.length,untrackedCandidates:candidates.length,reachableHistoryBlobs:history,scannedContents:scanned,findings};console.log(JSON.stringify(report,null,2));if(findings.length)process.exitCode=1;
