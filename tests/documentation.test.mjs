import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';

const root=fileURLToPath(new URL('../',import.meta.url));
const read=name=>fs.readFileSync(path.join(root,name),'utf8');
const rows=(text,prefix)=>[...text.matchAll(new RegExp('^\\| ('+prefix+'-\\d{3}) \\|','gm'))].map(m=>m[1]);

test('all functional and non-functional requirements have one traceability row',()=>{
 const srs=read('docs/requirements/SRS.md'),trace=read('docs/testing/traceability.md');
 for(const [prefix,count] of [['FR',48],['NFR',16]]){
  const source=rows(srs,prefix),mapped=rows(trace,prefix);
  assert.equal(source.length,count);assert.equal(new Set(source).size,count);
  assert.equal(mapped.length,count);assert.equal(new Set(mapped).size,count);
  assert.deepEqual([...source].sort(),[...mapped].sort());
 }
 const plan=read('docs/superpowers/plans/warehouse-rental/plan.md');
 for(const line of trace.split('\n').filter(x=>/^\| FR-/.test(x))){
  const testcase=line.match(/TC-\d{2}/)?.[0],wp=line.match(/WP\d{2}/)?.[0];
  assert.ok(testcase&&read('docs/testing/test-strategy.md').includes('| '+testcase+' |'),line);
  assert.ok(wp&&plan.includes('## '+wp+' '),line);
 }
});

test('all local Markdown file links remain within the consumer and resolve',()=>{
 const files=fs.readdirSync(root,{recursive:true,withFileTypes:true})
  .filter(e=>e.isFile()&&e.name.endsWith('.md')&&!path.relative(root,e.parentPath).split(path.sep).some(p=>p.startsWith('.')||['node_modules','bin','obj','dist','coverage','TestResults'].includes(p)))
  .map(e=>path.join(e.parentPath,e.name));
 for(const file of files)for(const match of fs.readFileSync(file,'utf8').matchAll(/\]\(([^\s)]+)\)/g)){
  const target=match[1];if(/^(?:https?:|#)/.test(target))continue;
  const resolved=path.resolve(path.dirname(file),decodeURIComponent(target.split('#')[0]));
  const relative=path.relative(root,resolved);
  assert.ok(!relative.startsWith('..')&&!path.isAbsolute(relative),file+': '+target);
  assert.ok(fs.statSync(resolved).isFile(),file+': '+target);
 }
});

test('release priorities match traceability and every work package declares interfaces and tests',()=>{
 const priorities=text=>new Map([...text.matchAll(/^\| (FR-\d{3}) \| (P[01]) \|/gm)].map(m=>[m[1],m[2]]));
 assert.deepEqual(priorities(read('docs/requirements/SRS.md')),priorities(read('docs/testing/traceability.md')));
 const packages=read('docs/superpowers/plans/warehouse-rental/plan.md').split(/^## WP\d{2} /m).slice(1);
 assert.equal(packages.length,16);
 for(const work of packages){assert.ok(work.includes('**Interfaces:**'));assert.ok(work.includes('**Tests:**'));assert.ok(work.includes('**Required assertions:**'));}
});

test('consumer profile points to its single approved implementation checkpoint',()=>{
 const profile=JSON.parse(read('docs/jarvis/project-profile.json'));
 assert.equal(profile.projectId,'warehouse-rent');assert.equal(profile.stateFile,'docs/project-checkpoint.md');
 assert.match(read(profile.stateFile),/P0_IMPLEMENTATION \/ IN_PROGRESS/);
 assert.match(read('docs/superpowers/specs/2026-10-27-warehouse-rental-design.md'),/APPROVED FOR P0 IMPLEMENTATION — UI A/);
 assert.equal(profile.url,null);
});
