'use strict';
function languageOptions(preference,system=false){
  return `<option value="auto" ${preference==='auto'?'selected':''}>${h(system?'language.system':'language.auto')}</option>`+LmuI18n.languages.map(l=>`<option value="${esc(l.code)}" ${preference===l.code?'selected':''}>${esc(l.complete?l.nativeName:tr('language.partial',{value0:l.nativeName,value1:Math.round(100*l.translated/l.total)}))}</option>`).join('');
}
function browserLanguageControl(id='browser-language'){
  return `<div class="language-control"><label for="${id}">${h('language.browser')}</label><select id="${id}" data-browser-language>${languageOptions(LmuI18n.preference)}</select><p class="field-note">${h('language.deviceNote')}</p></div>`;
}
function trayLanguageControl(settings){return `<label for="tray-language">${h('language.tray')}</label><select id="tray-language" name="trayLanguage">${languageOptions(settings.trayLanguage||'auto',true)}</select><p class="field-note">${h('language.trayNote')}</p>`;}
function refreshPairingLanguage(){const holder=$('#pair-language-controls');if(holder)holder.innerHTML=browserLanguageControl('pair-language');}
document.addEventListener('change',async event=>{
  if(!event.target.matches('[data-browser-language]'))return;
  const select=event.target;select.disabled=true;
  try{await LmuI18n.setLanguage(select.value);}catch(error){select.value=LmuI18n.preference;notice(error.message,true);}
  finally{select.disabled=false;}
});
window.addEventListener('lmu-language-changed',async event=>{
  refreshLocalizedConstants();refreshPairingLanguage();noticeController.close();
  state.heroStamp=null;state.liveStamp=null;state.popupRender=null;
  // A language change is not a settings submission. Preserve unsaved ordinary inputs
  // and only retain network consent for the very same still-available selection.
  const form=$('#manager-settings');const snapshot=form?[...form.elements].filter(e=>e.id||e.name).map(e=>({id:e.id,name:e.name,value:e.value,checked:e.checked,type:e.type})):[];
  if(state.csrf){
    await renderRoute();
    const next=$('#manager-settings');
    if(next&&snapshot.length){
      const oldChoice=snapshot.find(e=>e.id==='network-choice');
      const option=oldChoice?[...$('#network-choice').options].find(o=>o.value===oldChoice.value&&!o.disabled):null;
      if(option){$('#network-choice').value=option.value;$('#network-choice').dispatchEvent(new Event('change'));}
      for(const saved of snapshot){
        if(saved.id==='network-choice'||saved.id==='firewall-consent')continue;
        if(['network-trusted','http-risk','firewall-reviewed'].includes(saved.id)&&!option)continue;
        const control=saved.id?document.getElementById(saved.id):next.elements.namedItem(saved.name);
        if(!control)continue;if(saved.type==='checkbox')control.checked=saved.checked;else control.value=saved.value;
      }
      next.elements.lanEnabled.dispatchEvent(new Event('change'));
    }
    notice(tr(event.detail.persisted?'language.saved':'language.notSaved'),!event.detail.persisted);
  }else{const error=$('#pair-error');if(error)error.textContent=event.detail.persisted?'':tr('language.notSaved');}
});
