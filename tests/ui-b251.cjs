'use strict';
const {messageUsed}=require('./locale-test-helper.cjs');
const assert=require('node:assert/strict'),fs=require('node:fs');let n=0;const ui=fs.readFileSync('src/Lmu.Web/wwwroot/settings.js','utf8'),controller=fs.readFileSync('src/Lmu.Manager/ManagerController.cs','utf8'),policy=fs.readFileSync('src/Lmu.Manager/NetworkAuthorization.cs','utf8'),native=fs.readFileSync('src/Lmu.Manager/PrivateNetworks.cs','utf8');function check(ok,name){assert.ok(ok,name);n++;console.log('PASS '+name);}
check(ui.includes('eligible.length===1')&&ui.includes('eligible.map')&&!ui.includes('net.choices[0]'),'only one eligible connection may be preselected; multiple connections remain explicit');
check(ui.includes('id="home-consent"')&&!ui.includes('network-trusted')&&!ui.includes('http-risk')&&!ui.includes('firewall-reviewed'),'one informed setup consent replaces three separate acknowledgements');
check(messageUsed(ui,'Zugangscodes'),'visible HTTP warning includes credentials');
check(messageUsed(ui,'mitlesen oder verändern'),'visible HTTP warning covers observation and modification');
check(messageUsed(ui,'HTTPS ist nicht eingerichtet')&&messageUsed(ui,'nicht verschlüsselt'),'no misleading encrypted-connection claim');
check(ui.includes("api('/manager/firewall-help','POST'")&&ui.includes('consent:true')&&!ui.includes('firewall-consent'),'explicit help button requests guidance; no extra mandatory checkbox or execution');
check(ui.includes('net.lastRemoteContact')&&messageUsed(ui,'noch nicht nachgewiesen'),'binding and device reachability distinguished');
check(ui.includes('choice.onchange')&&ui.includes("$('#home-consent').checked=false"),'changed selected connection clears initial consent');
check(messageUsed(ui,'für den neuen Port')&&policy.includes('approval.Port!=settings.Port'),'port changes do not reuse approval silently');
check(controller.includes('NetworkAuthorization.Select')&&controller.includes('Active(current)'),'runtime network proof enforced for grants and remote access');
check(policy.includes('-LocalAddress ')&&policy.includes('-InterfaceAlias ')&&policy.includes('-RemoteAddress ')&&policy.includes('-EdgeTraversalPolicy Block'),'firewall suggestion remains jointly scoped');
check(!native.includes('SetCategory(')&&!controller.includes('Process.Start')&&!policy.includes('Process.Start'),'network logic does not change Windows profile or execute firewall commands');
console.log(`All ${n} B2.5.1/preview.10 UI/policy structure assertions passed. Explicit product changes replace the old multi-checkbox expectations.`);
