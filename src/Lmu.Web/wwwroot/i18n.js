'use strict';
(function(root){
  const storageKey='lmu.ui.language';
  const tokens=text=>[...new Set([...text.matchAll(/\{([A-Za-z][A-Za-z0-9_]*)\}/g)].map(m=>m[1]))].sort();
  const unsafe=text=>/<[/!]?[A-Za-z]|javascript\s*:|[\u0000-\u0008\u000b\u000c\u000e-\u001f\ufffd]/i.test(text)||[...text].some(c=>{const n=c.codePointAt(0);return n>=0xd800&&n<=0xdfff;});
  // JSON.parse alone discards duplicate keys. Reject them before accepting a locale.
  function parseJson(text){
    const parsed=JSON.parse(text);let at=0;
    const space=()=>{while(/\s/.test(text[at]||'')&&at<text.length)at++;};
    function string(){const from=at++;while(at<text.length){if(text[at]==='\\'){at+=2;continue;}if(text[at++]==='"')return JSON.parse(text.slice(from,at));}throw new Error('Invalid JSON string');}
    function value(){space();if(text[at]==='{'){at++;space();const seen=new Set();if(text[at]==='}'){at++;return;}while(at<text.length){space();const key=string();if(seen.has(key))throw new Error('Duplicate translation key');seen.add(key);space();at++;value();space();if(text[at++]==='}')return;}}
      else if(text[at]==='['){at++;space();if(text[at]===']'){at++;return;}while(at<text.length){value();space();if(text[at++]===']')return;}}
      else if(text[at]==='"')string();else{while(at<text.length&&!/[\s,}\]]/.test(text[at]))at++;}}
    value();return parsed;
  }
  function validate(doc,baseline,required=false){
    if(!doc||Object.keys(doc).sort().join(',')!=='messages,meta'||!doc.meta||!doc.messages||Array.isArray(doc.messages))throw new Error('Invalid language document');
    const m=doc.meta;
    if(Object.keys(m).some(k=>!['code','nativeName','name','direction','status'].includes(k))||!/^[a-z]{2,3}(?:-[A-Za-z0-9]{2,8})*$/.test(m.code)||!['ltr','rtl'].includes(m.direction)||!['complete','partial'].includes(m.status))throw new Error('Invalid language metadata');
    for(const name of [m.name,m.nativeName])if(typeof name!=='string'||!name.trim()||name.length>80||unsafe(name))throw new Error('Invalid language name');
    new Intl.NumberFormat(m.code);
    if(!Object.keys(doc.messages).length)throw new Error('Empty language document');
    for(const [key,value]of Object.entries(doc.messages)){
      if(!/^[a-z][A-Za-z0-9]*(?:\.[A-Za-z0-9_]+)+$/.test(key)||typeof value!=='string'||!value.trim()||unsafe(value)||/[{}]/.test(value.replace(/\{[A-Za-z][A-Za-z0-9_]*\}/g,'')))throw new Error('Invalid translation');
      if(baseline&&(!Object.hasOwn(baseline,key)||tokens(value).join(',')!==tokens(baseline[key]).join(',')))throw new Error('Unknown key or inconsistent placeholders');
      if(key==='common.restorePhrase'&&value!==(m.code==='de'?'BIBLIOTHEK ERSETZEN':'REPLACE LIBRARY'))throw new Error('Do not translate the restore confirmation token');
    }
    if(baseline&&(required||m.code==='de'||m.code==='en')&&Object.keys(doc.messages).length!==Object.keys(baseline).length)throw new Error('Incomplete required language');
    return doc;
  }
  const escape=value=>String(value??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  class LanguageEngine{
    constructor(english,languages,securityKeys=[]){validate(english);if(english.meta.code!=='en'||english.meta.status!=='complete')throw new Error('Invalid English fallback');this.english=english;this.documents=new Map([['en',english]]);this.completion=new Map([['en',true]]);this.languages=languages;this.securityKeys=new Set(securityKeys);this.code='en';this.preference='auto';}
    resolve(code){if(typeof code==='string'){const exact=this.languages.find(l=>l.code.toLowerCase()===code.toLowerCase());if(exact)return exact.code;const basic=this.languages.find(l=>l.code===code.split('-')[0].toLowerCase());if(basic)return basic.code;}return null;}
    detect(preference,browserLanguages=[]){return preference&&preference!=='auto'?(this.resolve(preference)||'en'):browserLanguages.map(c=>this.resolve(c)).find(Boolean)||'en';}
    add(doc){validate(doc,this.english.messages,doc.meta.code==='de'||doc.meta.code==='en');this.documents.set(doc.meta.code,doc);this.completion.set(doc.meta.code,doc.meta.status==='complete'&&Object.keys(doc.messages).length===Object.keys(this.english.messages).length);}
    activate(code){this.code=this.documents.has(code)?code:'en';}
    t(key,args={}){
      const doc=this.documents.get(this.code),complete=this.completion.get(this.code);
      const message=(!complete&&this.securityKeys.has(key)?null:doc.messages[key])??this.english.messages[key];
      if(message===undefined)throw new Error('Unknown translation key: '+key);
      return message.replace(/\{([A-Za-z][A-Za-z0-9_]*)\}/g,(_,name)=>{if(!Object.hasOwn(args,name))throw new Error('Missing translation argument: '+name);return typeof args[name]==='number'?this.number(args[name]):String(args[name]??'');});
    }
    h(key,args){return escape(this.t(key,args));}
    number(value,options={}){return new Intl.NumberFormat(this.code,{maximumFractionDigits:4,useGrouping:false,...options}).format(value);}
    percent(value){return new Intl.NumberFormat(this.code,{style:'percent',maximumFractionDigits:4}).format(value/100);}
    date(value){return new Intl.DateTimeFormat(this.code,{dateStyle:'medium',timeStyle:'short'}).format(new Date(value));}
  }
  const exported={LanguageEngine,validate,parseJson,tokens,escape,storageKey};
  if(typeof module==='object'&&module.exports)module.exports=exported;
  if(!root.document)return;
  let engine;
  const load=async path=>{const r=await fetch(path,{cache:'no-store',credentials:'same-origin'});if(!r.ok)throw new Error('Language resource unavailable');return parseJson(await r.text());};
  async function addLanguage(code){const doc=await load('/api/languages/'+encodeURIComponent(code));if(doc.meta.code!==code)throw new Error('Language identity mismatch');engine.add(doc);}
  function applyStatic(scope=document){for(const el of scope.querySelectorAll('[data-i18n]'))el.textContent=engine.t(el.dataset.i18n);for(const attr of ['aria-label','title','alt'])for(const el of scope.querySelectorAll('[data-i18n-'+attr+']'))el.setAttribute(attr,engine.t(el.getAttribute('data-i18n-'+attr)));document.documentElement.lang=engine.code;document.documentElement.dir=engine.documents.get(engine.code).meta.direction;}
  const api={...exported,get code(){return engine?.code||'en';},get preference(){return engine?.preference||'auto';},get languages(){return engine?.languages||[];},t:(k,a)=>engine.t(k,a),h:(k,a)=>engine.h(k,a),number:(v,o)=>engine.number(v,o),date:v=>engine.date(v),percent:v=>engine.percent(v),applyStatic};
  api.setLanguage=async preference=>{
    if(preference!=='auto'&&!engine.resolve(preference))preference='en';
    const code=engine.detect(preference,navigator.languages||[navigator.language]);
    if(!engine.documents.has(code)){try{await addLanguage(code);}catch{throw new Error(engine.t('language.loadFailed'));}}
    engine.preference=preference;engine.activate(code);let persisted=true;
    try{if(preference==='auto')localStorage.removeItem(storageKey);else localStorage.setItem(storageKey,code);}catch{persisted=false;}
    applyStatic();root.dispatchEvent(new CustomEvent('lmu-language-changed',{detail:{persisted}}));return persisted;
  };
  api.ready=(async()=>{
    const [registry,english]=await Promise.all([load('/api/languages'),load('/api/languages/en')]);
    engine=new LanguageEngine(english,registry.languages,registry.securityKeys);
    let preference='auto';try{preference=localStorage.getItem(storageKey)||'auto';}catch{}
    let code=engine.detect(preference,navigator.languages||[navigator.language]);
    if(code!=='en'){try{await addLanguage(code);}catch{code='en';if(preference!=='auto')preference='en';}}
    if(preference!=='auto'&&!engine.resolve(preference))preference='en';
    engine.preference=preference;engine.activate(code);applyStatic();return api;
  })();
  root.LmuI18n=api;
})(typeof window==='undefined'?globalThis:window);
function tr(key,args){return LmuI18n.t(key,args);}
function h(key,args){return LmuI18n.h(key,args);}
