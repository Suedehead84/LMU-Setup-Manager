'use strict';
const fs=require('fs'),assert=require('node:assert/strict');let count=0;const check=(ok,name)=>{assert.ok(ok,name);count++;console.log('PASS '+name);};
const version=fs.readFileSync('VERSION','utf8').trim(),props=fs.readFileSync('Directory.Build.props','utf8'),html=fs.readFileSync('src/Lmu.Web/wwwroot/index.html','utf8'),policy=JSON.parse(fs.readFileSync('src/Lmu.Web/wwwroot/locale-policy.json','utf8'));
check(version==='1.0.0','stable version is exactly 1.0.0');
check(props.includes('<Version>'+version+'</Version>')&&props.includes('<InformationalVersion>'+version+'</InformationalVersion>')&&props.includes('<FileVersion>1.0.0.3</FileVersion>'),'EXE assembly metadata agrees with RC version and mapped Windows build');
check(html.includes(version)&&!html.includes('0.3.0-preview.'),'visible shell version agrees with current candidate');
check(!html.includes('release-candidate')&&!html.includes('shell.unpublishedCandidate')&&!fs.readFileSync('src/Lmu.Web/wwwroot/app.css','utf8').includes('.release-candidate'),'stable UI and CSS have no RC warning banner');
for(const code of ['de','en']){const d=JSON.parse(fs.readFileSync('src/Lmu.Web/wwwroot/locales/'+code+'.json','utf8'));check(!Object.hasOwn(d.messages,'shell.unpublishedCandidate')&&!policy.includes('shell.unpublishedCandidate'),code+' has no obsolete RC notice contract');}
const build=fs.readFileSync('scripts/build.ps1','utf8');check(build.includes('$Clean')&&build.includes('dotnet clean')&&build.includes('--no-incremental'),'release tooling supports clean nonincremental compilation');
check(build.includes('PUBLICATION-HOLD.txt')&&build.includes('stable-mit-review')&&!/git\s+push|gh\s+release|gh\s+repo\s+create/.test(build),'stable packaging preserves separate public-distribution hold and contains no GitHub write step');
check(fs.readFileSync('docs/ACCEPTANCE.md','utf8').includes('AutoStart=false')&&fs.readFileSync('docs/ACCEPTANCE.md','utf8').includes('Benutzer bestätigt'),'acceptance records user approval and keeps copy-based regression safeguards');
console.log(`All ${count} release-boundary assertions passed. No product functionality added.`);
