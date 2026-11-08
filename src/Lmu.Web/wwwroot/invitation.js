'use strict';
// Short-lived invitation UI only. No storage, network service or token logging.
(function(root){
 const tr=(key,args)=>root.LmuI18n.t(key,args);
 function qrSvg(url,factory=root.qrcode){
  const parsed=new URL(url);
  if(parsed.protocol!=='http:'||parsed.username||parsed.password||parsed.pathname!=='/'||parsed.search||!/^#access=[A-F0-9]{48}$/.test(parsed.hash))throw new Error('Invalid invitation URL');
  const qr=factory(0,'M');qr.addData(url,'Byte');qr.make();
  return qr.createSvgTag({cellSize:6,margin:24,scalable:true,alt:tr("invitation.singleUseDeviceInvitation"),title:tr("invitation.qrCodeForDeviceConnection")});
 }
 function mount(container,invite,options){
  let stopped=false,polling=false,timer;const now=options.now||Date.now;
  const deadline=Math.min(Date.parse(invite.expiresAt),now()+600000);
  const status=document.createElement('p');status.setAttribute('role','status');
  const secret=document.createElement('div');secret.className='invitation-secret';
  const stop=message=>{stopped=true;clearInterval(timer);secret.replaceChildren();status.textContent=message;window.removeEventListener('pagehide',leave);};
  const leave=()=>stop(tr("invitation.invitationNoLongerDisplayedHereCreateANew"));
  container.replaceChildren(status,secret);
  if(!Number.isFinite(deadline)||!invite.urls?.length){stop(tr("invitation.couldNotDisplayTheInvitationPleaseCreateA"));return stop;}
  for(const url of invite.urls){
   const graphic=document.createElement('div');graphic.className='invitation-qr';
   try{graphic.innerHTML=qrSvg(url,options.qrFactory);}catch{graphic.className='';graphic.textContent=tr("invitation.couldNotCreateTheQrCodeCopyThe");}
   const instruction=document.createElement('p');instruction.textContent=tr("invitation.scanTheQrCodeWithASmartphoneOr");
   const label=document.createElement('label');label.textContent=tr("invitation.singleUseInvitationLink");
   const input=document.createElement('input');input.readOnly=true;input.value=url;input.autocomplete='off';input.setAttribute('aria-label',tr("invitation.singleUseInvitationLink"));label.append(input);
   const copy=document.createElement('button');copy.type='button';copy.textContent=tr("invitation.copyInvitationLink");copy.dataset.copyAddress=url;
   secret.append(graphic,instruction,label,copy);
  }
  async function tick(){
   if(stopped)return;
   if(!container.isConnected){stop('');return;}
   const seconds=Math.max(0,Math.ceil((deadline-now())/1000));
   if(!seconds){stop(tr("invitation.invitationExpiredPleaseCreateANewOne"));return;}
   status.textContent=tr("invitation.validForAnotherMinutesOneUseShareOnly",{value0:(Math.floor(seconds/60)),value1:(String(seconds%60).padStart(2,'0'))});
   if(polling)return;polling=true;
   try{
    const result=await options.status(invite.id);
    if(stopped||!container.isConnected)return;
    if(result.status==='used'){stop(tr("invitation.invitationAlreadyRedeemedTheDeviceIsAuthorized"));options.onUsed?.();}
    else if(result.status!=='active')stop(result.status==='expired'?tr("invitation.invitationExpiredPleaseCreateANewOne"):tr("invitation.invitationNoLongerAvailableCheckTheNetworkStatus"));
   }catch{if(!stopped)stop(tr("invitation.invitationStatusCannotBeCheckedQrCodeAnd"));}
   finally{polling=false;}
  }
  window.addEventListener('pagehide',leave);timer=setInterval(tick,2000);tick();return stop;
 }
 root.LmuInvitation={qrSvg,mount};
 if(typeof module==='object'&&module.exports)module.exports=root.LmuInvitation;
})(typeof window==='undefined'?globalThis:window);
