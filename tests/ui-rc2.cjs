'use strict';
const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
const source=fs.readFileSync('src/Lmu.Web/wwwroot/app.js','utf8');let count=0;
function check(ok,name){assert.ok(ok,name);count++;console.log('PASS '+name);}
async function run(){
  const panel={innerHTML:'',classList:{toggle(){}}},state={};let result;
  const context={state,Date,JSON,document:{},api:async()=>result,$:id=>id==='#live-panel'?panel:null,
    renderSetupPresentation(){},tr:key=>key,h:key=>key,esc:v=>String(v??''),number:v=>String(v),sessionLabel:v=>v,
    LmuI18n:{percent:v=>v+'%'},clearLiveDisplay:message=>{state.live=null;panel.innerHTML='CLEARED '+message;}};
  vm.createContext(context);
  const readings=source.match(/^function readings\(v\).*$/m)[0];
  const refresh=source.slice(source.indexOf('async function refreshLive()'),source.indexOf("\ndocument.addEventListener('change',e=>{if(e.target.matches('[data-setup-select]'))"));
  vm.runInContext('let liveLoading=false;'+readings+'\n'+refresh,context);
  const values={tc:7,tcCut:8,tcSlip:7,abs:6,brakeBiasFrontPercent:51.25};
  result={status:{fresh:true,canCapture:true,values,message:'fresh',key:{}},matchingSetups:[]};
  await context.refreshLive();
  check(panel.innerHTML.includes('web.currentLiveReadings')&&!panel.innerHTML.includes('disabled'),'complete current values enable live save with empty matching library');
  result={status:{...result.status,values:{...values,tcCut:null},canCapture:false,message:'TC Cut is missing'},matchingSetups:[]};
  await context.refreshLive();
  check(panel.innerHTML.includes('disabled')&&panel.innerHTML.includes('TC Cut is missing')&&panel.innerHTML.includes('—'),'partial snapshot blocks save and renders missing field without zero default');
  result={status:{...result.status,fresh:false,message:'stale',values},matchingSetups:[]};
  await context.refreshLive();
  check(panel.innerHTML.includes('web.staleLiveReadings')&&!panel.innerHTML.includes('<strong>7</strong>')&&!panel.innerHTML.includes('51.25%'),'stale payload is labelled and never rendered as last-known live values');
  result={status:{fresh:true,canCapture:true,values:{tc:0,tcCut:0,tcSlip:0,abs:0,brakeBiasFrontPercent:0},message:'reconnected'},matchingSetups:[]};
  await context.refreshLive();
  check(!panel.innerHTML.includes('disabled')&&!panel.innerHTML.includes('—')&&panel.innerHTML.includes('<strong>0</strong>'),'reconnected complete zero values render and enable save');
  result={status:{fresh:false,canCapture:false,values:{},message:'vehicle changed'},matchingSetups:[]};
  await context.refreshLive();
  check(panel.innerHTML.includes('disabled')&&panel.innerHTML.includes('vehicle changed')&&!panel.innerHTML.includes('<strong>0</strong>'),'changed native context removes prior electronic values');
  check(source.includes("cache:'no-store'"),'API reads explicitly bypass browser caches after deletion or refresh');
  for(const language of ['de','en']){const d=JSON.parse(fs.readFileSync('src/Lmu.Web/wwwroot/locales/'+language+'.json','utf8')).messages;
    check(d['live.telemetryIdentityNotYetConfirmed']&&d['web.staleLiveReadings']&&d['web.currentLiveReadings'],'precise identity and current/stale labels exist in '+language);}
  console.log(`All ${count} RC2 live-display assertions passed. VM rendering and API tests do not claim a physical browser drive.`);
}
run().catch(e=>{console.error(e);process.exitCode=1;});
