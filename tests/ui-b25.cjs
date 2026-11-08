'use strict';
const {messageUsed}=require('./locale-test-helper.cjs');
const assert=require('node:assert/strict'),fs=require('node:fs');let n=0;const js=fs.readFileSync('src/Lmu.Web/wwwroot/settings.js','utf8'),html=fs.readFileSync('src/Lmu.Web/wwwroot/index.html','utf8'),app=fs.readFileSync('src/Lmu.Web/wwwroot/app.js','utf8'),css=fs.readFileSync('src/Lmu.Web/wwwroot/app.css','utf8');function check(ok,name){assert.ok(ok,name);n++;console.log('PASS '+name);}
check(messageUsed(html,'LMU SETUP MANAGER')&&html.includes('<title>LMU Setup Manager</title>')&&!html.includes('LMU Fixed Setup Manager'),'visible product branding updated');
check(html.includes('href="#settings"')&&app.includes("route==='settings'"),'permanent settings navigation is wired');
check(js.includes('if(!data.managed)')&&messageUsed(js,'Windows-Tray-Anwendung'),'standalone web explains unavailable managed settings');
check(messageUsed(js,'nur am Windows-Computer')&&js.includes("api('/manager')"),'owner-only settings errors do not expose fake controls');
check(js.includes("api('/manager/preferences','POST'")&&js.includes('data.changing'),'settings use protected mutation and actual pending state');
check(js.includes('data.lanActive')&&js.includes('data.addresses.map'),'LAN labels and addresses derive from actual state');
check(messageUsed(js,'Verbindung ist nicht verschlüsselt')&&messageUsed(js,'vertrauenswürdigen Heimnetz'),'LAN trust and HTTP limitation explicit');
check(js.includes("api('/manager/invitations','POST'")&&messageUsed(js,'zehn Minuten'),'explicit per-device one-time invitation UI');
check(js.includes("api('/manager/devices/'")&&js.includes("'DELETE'"),'per-device revocation wired');
check(js.includes('navigator.clipboard.writeText')&&messageUsed(js,'manuell kopieren'),'copy action has clear failure feedback');
check(css.includes('.manager-info dd{margin:0;overflow-wrap:anywhere}')&&css.includes('@media(max-width:600px)'),'long paths and narrow settings layout protected');
check(css.includes('min-height:44px')&&css.includes('min-height:48px'),'settings controls retain touch target sizing');
check(app.includes("page==='settings'?'#settings':'#dashboard'"),'tray settings link uses allowlisted route');
console.log(`All ${n} B2.5 UI structure assertions passed. No physical browser/device layout certification.`);
