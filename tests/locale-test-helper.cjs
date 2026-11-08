'use strict';
const fs=require('node:fs');
const de=JSON.parse(fs.readFileSync('src/Lmu.Web/wwwroot/locales/de.json','utf8'));
const en=JSON.parse(fs.readFileSync('src/Lmu.Web/wwwroot/locales/en.json','utf8'));
// Legacy UI checks now verify an actual resource reference plus its German text.
// This is deliberately not a concatenation of all resources into the source under test.
function messageUsed(source,text){return Object.entries(de.messages).some(([key,value])=>value.includes(text)&&(source.includes('"'+key+'"')||source.includes("'"+key+"'")));}
function germanEngine(){const {LanguageEngine}=require('../src/Lmu.Web/wwwroot/i18n.js');const engine=new LanguageEngine(en,[{code:'en'},{code:'de'}],JSON.parse(fs.readFileSync('src/Lmu.Web/wwwroot/locale-policy.json','utf8')));engine.add(de);engine.activate('de');return engine;}
module.exports={messageUsed,germanEngine,de,en};
