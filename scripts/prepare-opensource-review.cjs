'use strict';
const fs=require('node:fs'),path=require('node:path'),cp=require('node:child_process'),crypto=require('node:crypto'),zlib=require('node:zlib');
const root=path.resolve(__dirname,'..'),sha=b=>crypto.createHash('sha256').update(b).digest('hex');
const rootFiles=['.gitignore','LICENSE','README.md','README.de.md','ROADMAP.md','CHANGELOG.md','CONTRIBUTING.md','TRANSLATING.md','THIRD_PARTY_NOTICES.md','RELEASE_NOTES.md','VERSION','Directory.Build.props','NuGet.Config','global.json','Lmu.FixedSetupManager.slnx','docs/abi/14200-layout.tsv','docs/abi/14200-headers.sha256.json','docs/abi/README.md'];
const scriptNames=['build.ps1','release-path.ps1','test-release.ps1','test-manager.ps1','test-web.ps1','test-b1.ps1','privacy-audit.cjs','check-docs.cjs','check-translations.cjs','publication-files.cjs','prepare-opensource-review.cjs'];
const docNames=['PUBLICATION_REVIEW.md','INSTALLATION.md','INSTALLATION.de.md','SECURITY.md','SECURITY.de.md','COMPATIBILITY.md','ACCEPTANCE.md','RELEASE_STATUS.md','SUPPORT.md','BUILDING.md','LICENSE_OPTIONS.md','OPEN_SOURCE_RELEASE.md','FUNDING.md','SCREENSHOTS.md','QR_DEPENDENCIES.json','lmu-pitwall-LICENSE.txt'];
function walk(base,strict=false){return fs.readdirSync(base,{withFileTypes:true}).flatMap(e=>{const p=path.join(base,e.name);if(e.isSymbolicLink())throw Error('Source symlink rejected');if(e.isDirectory()&&['bin','obj','.git','.tools','artifacts','node_modules'].includes(e.name)){if(strict)throw Error('Generated/private directory rejected');return [];}return e.isDirectory()?walk(p,strict):[p];});}
function files(){return [...rootFiles,...scriptNames.map(n=>'scripts/'+n),...docNames.map(n=>'docs/'+n),...['src','tests','tools','.github','docs/licenses','docs/examples','docs/publication'].flatMap(d=>walk(path.join(root,d)).map(p=>path.relative(root,p).replaceAll('\\','/')))].filter((v,i,a)=>a.indexOf(v)===i).sort();}
function scan(name,data){
 if(/(?:\.real(?:-redacted)?\.json|\.replay\.json\.gz)$/.test(name))throw Error('Captured game fixture excluded from public source: '+name);
 if(/(^|\/)(\.git|\.tools|artifacts|diagnostics|backups|\.research|\.codex)(\/|$)|EULA_DRAFT|PRODUCT_DIRECTION|^docs\/PHASE_|\.(sqlite|db|log|jsonl|zip|exe|dll|pdb|protected|pfx|key|hpp)$/.test(name))throw Error('Private/runtime/header path: '+name);
 if(name.endsWith('.gz'))data=zlib.gunzipSync(data,{maxOutputLength:64*1024*1024});
 if(name.endsWith('.ico'))return; // Original build artwork, no executable/debug artifact.
 const text=new TextDecoder('utf-8',{fatal:true}).decode(data);
 if(/[A-Z]:[\\/]+SteamLibrary[\\/]/i.test(text))throw Error('Machine-specific installation path: '+name);
 if(/7656119\d{10}|#access=[a-f0-9]{24,}|Bearer [a-f0-9]{24,}|-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----|gh[pousr]_[A-Za-z0-9]{20,}/i.test(text)||/[A-Z]:[\\/](?:Users|Benutzer)[\\/](?!Public|Default|<|%)[a-z0-9_.-]+[\\/]/i.test(text))throw Error('Sensitive content: '+name);
}
function identity(){const records=files().map(n=>{const data=fs.readFileSync(path.join(root,n));scan(n,data);return{path:n,sha256:sha(data)};});const sourceDigest=sha(Buffer.from(records.sort((a,b)=>a.path.localeCompare(b.path,'en')).map(r=>r.path+'\0'+r.sha256+'\n').join('')));return{status:'passed',files:records.length,sourceDigest,records};}
function audit(directory){
 const base=path.resolve(directory),expected=new Set(files()),records=[];
 for(const p of walk(base,true)){const n=path.relative(base,p).replaceAll('\\','/');if(!expected.has(n))throw Error('Unexpected source file: '+n);const data=fs.readFileSync(p);scan(n,data);if(!data.equals(fs.readFileSync(path.join(root,n))))throw Error('Source differs from reviewed build input: '+n);records.push({path:n,sha256:sha(data)});}
 if(records.length!==expected.size)throw Error('Missing source/build/test inputs');
 const digest=sha(Buffer.from(records.sort((a,b)=>a.path.localeCompare(b.path,'en')).map(r=>r.path+'\0'+r.sha256+'\n').join('')));
 return{status:'passed',files:records.length,sourceDigest:digest,records};
}
function snapshot(directory){
 const base=path.resolve(directory),allowed=path.join(root,'artifacts','opensource-review')+path.sep;
 if(!base.startsWith(allowed)||fs.existsSync(base))throw Error('Snapshot requires new directory below artifacts/opensource-review');
 for(const n of files()){const data=fs.readFileSync(path.join(root,n));scan(n,data);const dest=path.join(base,n);fs.mkdirSync(path.dirname(dest),{recursive:true});fs.writeFileSync(dest,data);}
 return audit(base);
}
module.exports={files,audit,snapshot,identity,scan};
if(require.main===module){try{const[cmd,dir]=process.argv.slice(2);const r=cmd==='identity'?identity():cmd==='snapshot'?snapshot(dir):cmd==='audit'?audit(dir):(()=>{throw Error('Use snapshot|audit <directory>');})();console.log(JSON.stringify({status:r.status,files:r.files,sourceDigest:r.sourceDigest}));}catch(e){console.error(e.message);process.exitCode=1;}}
