import test from 'node:test';
import assert from 'node:assert/strict';
import {createSupabaseFreePlan} from '../ops/supabase-free-plan.mjs';
const org={id:'fixtureorg',name:'Fixture Organization',plan:'free'};
const projects=[{id:'protected-fixture',name:'Ielts-Learning',organization_id:org.id}];
const fixture=()=>({organization:org,selectedOrganizationId:org.id,cost:{amount:0,recurrence:'monthly',type:'project'},projects,name:'khospace',region:'ap-southeast-1'});
test('free Supabase plan targets a new isolated project without Azure or IELTS data reuse',()=>{
 const p=createSupabaseFreePlan(fixture());assert.equal(p.operation,'create-new-project');assert.equal(p.expectedCostUsd,0);assert.equal(p.organizationId,org.id);assert.equal(p.cloudExecuted,false);assert.equal(p.provider,'supabase');assert.equal(p.databaseEngine,'postgresql');assert.equal(p.usesExistingIeltsDatabase,false);
});
test('organization must be explicitly selected, never inferred from a singleton list',()=>{const f=fixture();delete f.selectedOrganizationId;assert.throws(()=>createSupabaseFreePlan(f),/explicit/i);});
for(const amount of [0.01,1,25,null,'0',-1])test(`nonzero or invalid quote ${amount} is rejected`,()=>{const f=fixture();f.cost.amount=amount;assert.throws(()=>createSupabaseFreePlan(f),/cost/i);});
test('paid organization cannot silently use a nominal zero quote',()=>{const f=fixture();f.organization={...org,plan:'pro'};assert.throws(()=>createSupabaseFreePlan(f),/free/i);});
test('existing IELTS project is never selected, overwritten, paused or deleted',()=>{const f=fixture();f.name='Ielts-Learning';assert.throws(()=>createSupabaseFreePlan(f),/existing/i);});
test('unknown existing KhoSpace project requires readback rather than retrying creation',()=>{const f=fixture();f.projects=[...projects,{id:'existing',name:'khospace',organization_id:org.id}];assert.throws(()=>createSupabaseFreePlan(f),/existing/i);});
test('wrong organization quote/context is refused',()=>{const f=fixture();f.selectedOrganizationId='different';assert.throws(()=>createSupabaseFreePlan(f),/organization/i);});
