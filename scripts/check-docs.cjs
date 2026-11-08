'use strict';
const fs=require('node:fs'),path=require('node:path'),cp=require('node:child_process');
const directory=process.argv[2]==='--directory'?process.argv[3]:null;
if(process.argv.length>2&&!directory)throw Error('Usage: check-docs.cjs [--directory <extracted-package>]');
if(directory)process.chdir(directory);
const walk=dir=>fs.readdirSync(dir,{withFileTypes:true}).flatMap(e=>e.isDirectory()?walk(path.join(dir,e.name)):e.name.endsWith('.md')?[path.join(dir,e.name)]:[]);
let ownGit=false;try{ownGit=path.resolve(cp.execFileSync('git',['rev-parse','--show-toplevel'],{encoding:'utf8',stdio:['ignore','pipe','ignore']}).trim())===process.cwd();}catch{}
const archiveWalk=d=>fs.readdirSync(d,{withFileTypes:true}).flatMap(e=>e.isDirectory()?['.git','.tools','artifacts','bin','obj','node_modules'].includes(e.name)?[]:archiveWalk(path.join(d,e.name)):e.name.endsWith('.md')?[path.join(d,e.name)]:[]);
const files=directory?walk('.'):!ownGit?archiveWalk('.'):[...new Set(['ls-files','ls-files --others --exclude-standard'].flatMap(command=>cp.execFileSync('git',command.split(' ').concat('-z'),{encoding:'utf8'}).split('\0').filter(p=>p.endsWith('.md'))))].filter(p=>fs.existsSync(p));
const plain=s=>s.replace(/```[^\n]*\n[\s\S]*?```/g,'').replace(/`[^`]*`/g,'');
const slug=s=>s.toLowerCase().replace(/<[^>]*>/g,'').replace(/[^\p{L}\p{N}_\s-]/gu,'').trim().replace(/\s/g,'-');
let links=0;const errors=[];
for(const file of files){const source=plain(fs.readFileSync(file,'utf8'));for(const m of source.matchAll(/!?\[[^\]\n]*\]\((<[^>]+>|[^\s)]+)(?:\s+"[^"]*")?\)/g)){
 const target=m[1].replace(/^<|>$/g,'');if(/^[a-z][a-z0-9+.-]*:/i.test(target))continue;links++;
 let decoded;try{decoded=decodeURIComponent(target);}catch{errors.push(file+': invalid URL encoding');continue;}
 const [relative,anchor]=decoded.split('#');const resolved=relative?path.resolve(path.dirname(file),relative):path.resolve(file);
 if(!resolved.startsWith(path.resolve('.')+path.sep)&&resolved!==path.resolve('.')){errors.push(file+': link leaves project '+target);continue;}
 if(!fs.existsSync(resolved)){errors.push(file+': missing '+target);continue;}
 if(relative){let current=path.resolve('.');for(const part of path.relative(current,resolved).split(path.sep)){if(!fs.readdirSync(current).includes(part)){errors.push(file+': incorrect path case '+target);break;}current=path.join(current,part);}}
 if(anchor&&resolved.endsWith('.md')){const headers=[...plain(fs.readFileSync(resolved,'utf8')).matchAll(/^#{1,6}\s+(.+)$/gm)].map(x=>slug(x[1]));if(!headers.includes(anchor))errors.push(file+': missing heading '+target);}
}}
if(errors.length){console.error(errors.join('\n'));process.exitCode=1;}else console.log(`PASS ${links} local Markdown links across ${files.length} documents (existence, path case, headings). External URLs require separate review.`);
