'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const {audit,project,runtimeNames}=require('../scripts/publication-files.cjs');
const directory=path.join('artifacts','publication-audit-test-'+Date.now());let count=0;
function check(ok,name){assert.ok(ok,name);count++;console.log('PASS '+name);}
project(directory);check(audit(directory,'repo').findings===0,'explicit public docs projection passes allowlist/privacy/license checks');
function rejected(file,content,name){const target=path.join(directory,file);const old=fs.existsSync(target)?fs.readFileSync(target):null;fs.mkdirSync(path.dirname(target),{recursive:true});fs.writeFileSync(target,content);let failed=false;try{audit(directory,'repo');}catch{failed=true;}finally{if(old)fs.writeFileSync(target,old);else fs.unlinkSync(target);}check(failed,name);}
rejected('private.cs','internal source','private implementation file rejected');
rejected('docs/EULA_DRAFT.de.md','draft','unapproved EULA rejected from public tree');
rejected('library.sqlite','synthetic','database rejected from public tree');
rejected('README.md','#access='+'a'.repeat(48),'literal invitation secret rejected');
rejected('README.md',['C:','Users','Example','secret'].join('/'),'personal Windows path rejected');
rejected('README.md',[192,168,99,2].join('.'),'private LAN address rejected');
rejected('licenses/qrcode-generator-MIT.txt','changed','modified upstream license notice rejected');
const payload=directory+'-payload';fs.cpSync(directory,payload,{recursive:true});
for(const name of runtimeNames){const target=path.join(payload,name);fs.mkdirSync(path.dirname(target),{recursive:true});fs.writeFileSync(target,name.endsWith('.exe')||name.endsWith('.dll')?Buffer.from([0,1,2]):'{}');}
fs.cpSync('src/Lmu.Web/wwwroot',path.join(payload,'wwwroot'),{recursive:true});fs.copyFileSync(path.join(payload,'README.md'),path.join(payload,'README-Web.md'));fs.writeFileSync(path.join(payload,'PUBLICATION-HOLD.txt'),'SYNTHETIC stable release hold fixture only; never execute');
fs.renameSync(path.join(payload,'PUBLICATION-HOLD.txt'),path.join(payload,'UNPUBLISHED.txt'));let wrongMarker=false;try{audit(payload,'payload');}catch{wrongMarker=true;}fs.renameSync(path.join(payload,'UNPUBLISHED.txt'),path.join(payload,'PUBLICATION-HOLD.txt'));check(wrongMarker,'stable payload requires publication hold rather than a legacy RC marker');
const valid=audit(payload,'payload').findings===0;fs.writeFileSync(path.join(payload,'wwwroot/app.js.gz'),require('node:zlib').gzipSync('altered browser payload'));let compressedRejected=false;try{audit(payload,'payload');}catch{compressedRejected=true;}
check(valid&&compressedRejected&&audit(directory,'repo').findings===0,'restored projection passes while altered compressed runtime asset is rejected');
console.log(`All ${count} publication-boundary assertions passed. No real credentials or user databases used.`);
