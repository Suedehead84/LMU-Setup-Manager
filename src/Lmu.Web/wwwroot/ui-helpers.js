'use strict';
// Small pure helpers shared with dependency-free Node regression tests.
(function(root){
  function notificationTimer(show,fade,hide,timers={setTimeout:(fn,ms)=>root.setTimeout(fn,ms),clearTimeout:id=>root.clearTimeout(id)}) {
    let waiting,finishing;
    const clear=()=>{timers.clearTimeout(waiting);timers.clearTimeout(finishing);};
    const close=()=>{clear();fade();finishing=timers.setTimeout(hide,180);};
    return {show(message,error=false){clear();show(message,error);if(!error)waiting=timers.setTimeout(close,5000);},close};
  }
  const selectionAfterRefresh=(selected,choices)=>new Set([...selected].filter(id=>choices.some(c=>c.id===id)));
  const visibleChoices=(choices,query,vehicleClass,track)=>{const q=query.toLocaleLowerCase('de');return choices.filter(c=>(!vehicleClass||c.class===vehicleClass)&&(!track||c.trackId===track)&&[c.name,c.class,c.trackName,c.detail,...(c.candidates||[]).map(x=>x.name)].some(v=>v?.toLocaleLowerCase('de').includes(q)));};
  function setupSignal(){let last=null,current=null,open=false,selected=null,explicit=false,instance=null;return {
    update(key,matches,lifecycle){if(lifecycle?.ended){last=null;current=null;open=false;selected=null;explicit=false;instance=null;}if(lifecycle?.instanceId&&lifecycle.instanceId!==instance){instance=lifecycle.instanceId;last=null;selected=null;explicit=false;}const context=key?JSON.stringify([key.vehicleId,key.trackId,key.layoutId]):null;const signature=context&&matches.length?JSON.stringify([instance,context,[...matches].sort((a,b)=>a.id.localeCompare(b.id)).map(s=>[s.id,s.versionId])]):null;
      if(signature&&signature!==last){if(current!==context){selected=null;explicit=false;}last=signature;open=true;}current=context;
      if(!signature)open=false;if(matches.length>1&&!explicit)selected=null;if(!matches.some(s=>s.id===selected)){selected=matches.length===1?matches[0].id:null;explicit=false;}
      return {open:!!signature&&open,selected,signature,context};},
    close(){open=false;},select(id){selected=id;explicit=!!id;},reopen(){if(current)open=true;}
  };}
  function compareValue(stored,live,fresh){return !fresh||live==null?'unavailable':Math.abs(stored-live)<.00005?'equal':'different';}
  root.LmuUi={notificationTimer,selectionAfterRefresh,visibleChoices,setupSignal,compareValue};
  if(typeof module!=='undefined')module.exports=root.LmuUi;
})(typeof globalThis!=='undefined'?globalThis:this);
