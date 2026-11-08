'use strict';
const fs=require('node:fs'),path=require('node:path'),zlib=require('node:zlib');
const root=path.resolve(__dirname,'..');
const licenseNames=fs.readdirSync(path.join(root,'docs/licenses')).filter(n=>n.endsWith('.txt')).sort();
const publicNames=['docs/PUBLICATION_REVIEW.md','README.md','README.de.md','RELEASE_NOTES.md','TRANSLATING.md','ROADMAP.md','THIRD_PARTY_NOTICES.md','LICENSE','CONTRIBUTING.md','docs/OPEN_SOURCE_RELEASE.md','docs/FUNDING.md','docs/SCREENSHOTS.md','docs/BUILDING.md','docs/LICENSE_OPTIONS.md','.github/FUNDING.yml','.github/ISSUE_TEMPLATE/feature_request.yml','.github/pull_request_template.md','VERSION','QR_DEPENDENCIES.json','lmu-pitwall-LICENSE.txt','docs/SECURITY.md','docs/SECURITY.de.md','docs/COMPATIBILITY.md','docs/INSTALLATION.md','docs/INSTALLATION.de.md','docs/ACCEPTANCE.md','docs/RELEASE_STATUS.md','docs/SUPPORT.md','.github/ISSUE_TEMPLATE/bug_report.yml',...licenseNames.map(n=>'licenses/'+n)].sort();
const runtimeNames=['LMU Setup Manager.exe','Lmu.Web.exe','aspnetcorev2_inprocess.dll','e_sqlite3.dll','Lmu.Diagnostics.runtimeconfig.json','Lmu.Web.runtimeconfig.json','Lmu.Web.staticwebassets.endpoints.json','web.config','tools/diagnostics/Lmu.Diagnostics.exe','tools/library/Lmu.Library.Tool.exe','tools/library/e_sqlite3.dll','tools/library/Lmu.Diagnostics.runtimeconfig.json'];
function walk(dir){return fs.readdirSync(dir,{withFileTypes:true}).flatMap(e=>{const p=path.join(dir,e.name);if(fs.lstatSync(p).isSymbolicLink())throw Error('Symlink in publication tree');return e.isDirectory()?walk(p):[p];});}
function sourceWeb(){const base=path.join(root,'src/Lmu.Web/wwwroot');return walk(base).map(p=>path.relative(base,p).replaceAll('\\','/'));}
function textAudit(name,bytes){
 let data=bytes;if(name.endsWith('.gz'))data=zlib.gunzipSync(data,{maxOutputLength:8*1024*1024});else if(name.endsWith('.br'))data=zlib.brotliDecompressSync(data,{maxOutputLength:8*1024*1024});
 const ext=name.replace(/\.(gz|br)$/,'');if(!/\.(md|txt|json|js|css|html|svg|config|yml|yaml)$/.test(ext)&&ext!=='VERSION')return;
 const text=new TextDecoder('utf-8',{fatal:true}).decode(data);
 const forbidden=[/7656119\d{10}/,/#access=[a-f0-9]{24,}/i,/Bearer [a-f0-9]{24,}/i,/-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----/,/gh[pousr]_[A-Za-z0-9]{20,}/,/[A-Z]:[\\/](?:Users|Benutzer)[\\/](?!Public|Default|<|%)[a-z0-9_.-]+[\\/]/i,/github\.com\/Suedehead84\/LMU-Fixed-Setup-Manager/i,/EULA_DRAFT|PRODUCT_DIRECTION|PHASE_B2_|\.research[\\/]|@@VERSION@@/];
 if(forbidden.some(re=>re.test(text)))throw Error('Confidential or unrendered text in '+name);
 for(const m of text.matchAll(/\b(?:10|172|192)\.\d{1,3}\.\d{1,3}\.\d{1,3}\b/g)){const n=m[0].split('.').map(Number);if(n.some(v=>v>255))continue;if(n[0]===10||(n[0]===172&&n[1]>=16&&n[1]<=31)||(n[0]===192&&n[1]===168))throw Error('Private LAN address in '+name);}
}
function audit(directory,kind){
 if(!['repo','payload'].includes(kind))throw Error('Unknown publication tree type');
 const base=path.resolve(directory);const allowed=new Set(publicNames);
 if(kind==='payload'){for(const n of [...runtimeNames,'README-Web.md','UNPUBLISHED.txt','PUBLICATION-HOLD.txt'])allowed.add(n);for(const n of sourceWeb())for(const suffix of ['','.gz','.br'])allowed.add('wwwroot/'+n+suffix);}
 const names=walk(base).map(p=>path.relative(base,p).replaceAll('\\','/')).sort();
 if(!fs.readFileSync(path.join(base,'LICENSE')).equals(fs.readFileSync(path.join(root,'LICENSE'))))throw Error('Project MIT license differs');
 for(const n of names){if(!allowed.has(n))throw Error('Unexpected publication file: '+n);textAudit(n,fs.readFileSync(path.join(base,n)));}
 const marker=fs.readFileSync(path.join(root,'VERSION'),'utf8').includes('-rc.')?'UNPUBLISHED.txt':'PUBLICATION-HOLD.txt';
 const required=kind==='repo'?publicNames:[...publicNames,...runtimeNames,'README-Web.md',marker,...sourceWeb().map(n=>'wwwroot/'+n)];
 for(const n of required)if(!names.includes(n))throw Error('Missing publication file: '+n);
 for(const n of licenseNames)if(!fs.readFileSync(path.join(base,'licenses',n)).equals(fs.readFileSync(path.join(root,'docs/licenses',n))))throw Error('License notice differs: '+n);
 if(kind==='payload')for(const n of sourceWeb()){
  const original=fs.readFileSync(path.join(root,'src/Lmu.Web/wwwroot',n)),target=path.join(base,'wwwroot',n);
  if(!fs.readFileSync(target).equals(original))throw Error('Runtime browser asset differs: '+n);
  for(const suffix of ['.gz','.br'])if(fs.existsSync(target+suffix)){
   const bytes=fs.readFileSync(target+suffix),decoded=suffix==='.gz'?zlib.gunzipSync(bytes,{maxOutputLength:8*1024*1024}):zlib.brotliDecompressSync(bytes,{maxOutputLength:8*1024*1024});
   if(!decoded.equals(original))throw Error('Compressed runtime browser asset differs: '+n+suffix);
  }
 }
 return{kind,files:names.length,findings:0};
}
function project(directory){
 const base=path.resolve(directory),artifacts=path.join(root,'artifacts')+path.sep;if(!base.startsWith(artifacts)||fs.existsSync(base))throw Error('Projection needs a new ignored artifacts directory');
 fs.mkdirSync(base,{recursive:true});const version=fs.readFileSync(path.join(root,'VERSION'),'utf8').trim();
 const put=(name,value)=>{fs.mkdirSync(path.dirname(path.join(base,name)),{recursive:true});fs.writeFileSync(path.join(base,name),value);};
 for(const [input,output]of [['README.en.txt','README.md'],['README.de.txt','README.de.md'],['RELEASE_NOTES.txt','RELEASE_NOTES.md'],['TRANSLATING.txt','TRANSLATING.md'],['ROADMAP.txt','ROADMAP.md']])put(output,fs.readFileSync(path.join(root,'docs/publication',input),'utf8').replaceAll('@@VERSION@@',version));
 for(const n of ['SECURITY.md','SECURITY.de.md']){let value=fs.readFileSync(path.join(root,'docs',n),'utf8').replace(/\nImplementation references:[\s\S]*$/,'\n').replace(/\nCodegrundlage:[\s\S]*$/,'\n');put('docs/'+n,value);}
 put('docs/COMPATIBILITY.md',fs.readFileSync(path.join(root,'docs/COMPATIBILITY.md')));
 for(const n of ['PUBLICATION_REVIEW.md','INSTALLATION.md','INSTALLATION.de.md','ACCEPTANCE.md','RELEASE_STATUS.md','SUPPORT.md'])put('docs/'+n,fs.readFileSync(path.join(root,'docs',n)));
 put('.github/ISSUE_TEMPLATE/bug_report.yml',fs.readFileSync(path.join(root,'docs/publication/bug_report.yml')));
 const notices=fs.readFileSync(path.join(root,'THIRD_PARTY_NOTICES.md'),'utf8'),start=notices.indexOf('B2.1 includes');if(start<0)throw Error('Notice projection anchor missing');
 put('THIRD_PARTY_NOTICES.md','# Third-party notices\n\nOriginal application code is MIT under LICENSE. Upstream components retain the notices below.\n\n'+notices.slice(start).replaceAll('docs/licenses/','licenses/').replaceAll('docs/lmu-pitwall-LICENSE.txt','lmu-pitwall-LICENSE.txt').replaceAll('docs/QR_DEPENDENCIES.json','QR_DEPENDENCIES.json').replaceAll('src/Lmu.Web/wwwroot/vendor/','wwwroot/vendor/').replaceAll('tests/vendor/jsQR-1.4.0.js','the source test suite'));
 for(const n of ['LICENSE','CONTRIBUTING.md','docs/OPEN_SOURCE_RELEASE.md','docs/FUNDING.md','docs/SCREENSHOTS.md','docs/BUILDING.md','docs/LICENSE_OPTIONS.md','.github/FUNDING.yml','.github/ISSUE_TEMPLATE/bug_report.yml','.github/ISSUE_TEMPLATE/feature_request.yml','.github/pull_request_template.md'])put(n,fs.readFileSync(path.join(root,n)));
 for(const [input,output]of [['VERSION','VERSION'],['docs/QR_DEPENDENCIES.json','QR_DEPENDENCIES.json'],['docs/lmu-pitwall-LICENSE.txt','lmu-pitwall-LICENSE.txt'],...licenseNames.map(n=>['docs/licenses/'+n,'licenses/'+n])])put(output,fs.readFileSync(path.join(root,input)));
 return audit(base,'repo');
}
module.exports={audit,project,publicNames,runtimeNames,walk};
if(require.main===module){try{const[command,directory,kind]=process.argv.slice(2);console.log(JSON.stringify(command==='project'?project(directory):command==='audit'?audit(directory,kind):(()=>{throw Error('Usage: publication-files.cjs project <new-dir> | audit <dir> repo|payload');})()));}catch(e){console.error(e.message);process.exitCode=1;}}
