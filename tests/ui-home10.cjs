'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs');const ui=fs.readFileSync('src/Lmu.Web/wwwroot/settings.js','utf8'),css=fs.readFileSync('src/Lmu.Web/wwwroot/app.css','utf8'),policy=JSON.parse(fs.readFileSync('src/Lmu.Web/wwwroot/locale-policy.json','utf8'));let count=0;
function check(ok,name){assert.ok(ok,name);count++;console.log('PASS '+name);}
new(require('node:vm').Script)(ui);check(true,'network settings JavaScript parses before UI contract checks');
check((ui.match(/role="switch"/g)||[]).length===1&&ui.includes("s.lanEnabled?'checked':''"),'single main switch reflects persisted user intent');
check((ui.match(/id="home-consent" type="checkbox" required/g)||[]).length===1,'exactly one mandatory security checkbox in network setup');
check(ui.indexOf('id="home-risk"')<ui.indexOf('id="home-consent"')&&!ui.slice(ui.indexOf('<form id="home-setup"'),ui.indexOf('<details class="home-details"')).includes('<details'),'security notice is visible before consent, outside optional details');
check(ui.includes('id="home-cancel"')&&ui.includes('toggle.focus()'),'cancel returns keyboard focus to main switch');
check(ui.includes('role="status" aria-live="polite"')&&ui.includes('aria-describedby="home-status home-http"')&&ui.includes('aria-describedby="home-risk"'),'switch and consent expose accessible names descriptions and live status');
check(ui.includes('showModal(dialogHead(tr("settings.homeRemove"))')&&ui.includes('h("settings.homeRemoveConfirm")')&&ui.includes('removeApproval:true'),'approval removal has translated separate in-app confirmation');
check(ui.includes("api('/manager/preferences','POST'")&&!ui.includes("f.has('lanEnabled')"),'general preferences cannot derive network intent from current runtime status');
check(ui.includes('eligible.length===1')&&ui.includes('choice.onchange')&&ui.includes('confirmed:'),'visible unique connection still needs consent; selection changes reset it');
check(css.includes('repeat(2,minmax(0,1fr))')&&css.includes('nav a{min-width:0;white-space:normal}')&&css.includes('overflow-wrap:break-word'),'narrow navigation wraps labels rather than clipping');
check(css.includes('.home-switch input:focus-visible')&&css.includes('prefers-reduced-motion')&&css.includes('min-height:2.75rem'),'network controls retain keyboard focus touch sizing and reduced motion');
for(const code of ['de','en']){const doc=JSON.parse(fs.readFileSync(`src/Lmu.Web/wwwroot/locales/${code}.json`,'utf8'));const keys=[...ui.matchAll(/["'](settings\.home[A-Za-z]+)["']/g)].map(m=>m[1]);check(keys.every(k=>typeof doc.messages[k]==='string'&&doc.messages[k].length>0),code+' has every new network state and action');check(['settings.homeConsent','settings.homeHttpConsent','settings.homeRemoveConfirm'].every(k=>policy.includes(k)),code+' network consent/removal belongs to complete safety fallback bundle');check(!/revokes all|widerruft alle/.test(doc.messages['settings.invitationsAreValidForTenMinutesAndOne']),code+' invitation help no longer claims off deletes device grants');}
console.log(`All ${count} preview.10 UI contract assertions passed. Actual viewport/keyboard checks are recorded separately.`);
