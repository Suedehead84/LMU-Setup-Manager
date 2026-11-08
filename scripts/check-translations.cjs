'use strict';
const fs=require('node:fs'),path=require('node:path');
const {parseJson,validate,tokens}=require('../src/Lmu.Web/wwwroot/i18n.js');
const localeDir='src/Lmu.Web/wwwroot/locales';
function files(dir){return fs.readdirSync(dir,{withFileTypes:true}).flatMap(e=>e.isDirectory()?['bin','obj','vendor'].includes(e.name)?[]:files(path.join(dir,e.name)):[path.join(dir,e.name)]);}
function audit(directory=localeDir,sourceCheck=true){
 const entries=fs.readdirSync(directory).filter(n=>n.endsWith('.json')).sort();
 const read=file=>new TextDecoder('utf-8',{fatal:true}).decode(fs.readFileSync(file));
 const english=validate(parseJson(read(path.join(directory,'en.json'))));const locales=[];
 for(const name of entries){const doc=validate(parseJson(read(path.join(directory,name))),english.messages,name==='de.json'||name==='en.json');if(doc.meta.code!==name.slice(0,-5))throw new Error(name+': filename and language code differ');locales.push({code:doc.meta.code,keys:Object.keys(doc.messages).length,complete:doc.meta.status==='complete'&&Object.keys(doc.messages).length===Object.keys(english.messages).length});}
 const policy=parseJson(fs.readFileSync('src/Lmu.Web/wwwroot/locale-policy.json','utf8'));
 if(!Array.isArray(policy)||new Set(policy).size!==policy.length||policy.some(k=>!Object.hasOwn(english.messages,k)))throw new Error('Unknown or duplicate protected translation key');
 const wire=parseJson(fs.readFileSync('src/Lmu.Localization/CanonicalMessages.json','utf8'));
 for(const[key,value]of Object.entries(wire))if(!Object.hasOwn(english.messages,key)||typeof value!=='string'||!value.trim()||tokens(value).join(',')!==tokens(english.messages[key]).join(','))throw new Error('Invalid canonical message contract: '+key);
 if(sourceCheck){
   const sourceFiles=[...files('src'),...files('tools')].filter(p=>/\.(cs|js|html)$/.test(p));
   const sources=sourceFiles.map(p=>fs.readFileSync(p,'utf8')).join('\n');
   const used=new Set([...sources.matchAll(/["']((?:web|settings|invitation|shell|language|common|manager|library|api|live|diagnostics)\.[A-Za-z0-9_]+)["']/g)].map(m=>m[1]));
   const calls=[...sources.matchAll(/(?:UiText\.(?:C|T)|(?<![\w.])(?:t|tr|h))\(\s*["']([^"']+)["']/g)].map(m=>m[1]);
   const bindings=[...sources.matchAll(/data-i18n(?:-[a-z-]+)?="([^"]+)"/g)].map(m=>m[1]);
   for(const key of [...calls,...bindings])if(!Object.hasOwn(english.messages,key))throw new Error('Referenced translation key missing: '+key);
   const orphan=Object.keys(english.messages).filter(k=>!used.has(k));if(orphan.length)throw new Error('Unused translation keys: '+orphan.join(', '));
 }
 return{locales,keys:Object.keys(english.messages).length,protectedKeys:policy.length};
}
module.exports={audit};
if(require.main===module){try{console.log(JSON.stringify(audit(),null,2));console.log('PASS translation structure, duplicate keys, coverage, placeholders, content, source references and safety policy');}catch(error){console.error(error.message);process.exitCode=1;}}
