import {pathToFileURL} from 'node:url';
import path from 'node:path';
export function createSupabaseFreePlan({organization,selectedOrganizationId,cost,projects,name,region}){
 if(typeof selectedOrganizationId!=='string'||!selectedOrganizationId)throw Error('An explicit organization selection is required');
 if(!organization||organization.id!==selectedOrganizationId)throw Error('Selected organization does not match inspected organization');
 if(organization.plan!=='free')throw Error('Only a confirmed Free organization is allowed');
 if(!cost||typeof cost.amount!=='number'||!Number.isFinite(cost.amount)||cost.amount!==0||cost.recurrence!=='monthly'||cost.type!=='project')throw Error('An exact zero-dollar monthly project cost quote is required');
 if(!/^[a-z][a-z0-9-]{2,39}$/.test(name??'')){
  if((projects??[]).some(p=>p.name===name))throw Error('An existing project cannot be overwritten or reused');
  throw Error('Invalid dedicated project name');
 }
 if(!['ap-southeast-1','eu-west-1'].includes(region))throw Error('A reviewed project region is required');
 if(!Array.isArray(projects))throw Error('Existing project inventory is required before create');
 if(projects.some(p=>typeof p.name!=='string'||!p.id||!p.organization_id))throw Error('Invalid existing project inventory');
 if(projects.some(p=>p.organization_id===selectedOrganizationId&&p.name.toLowerCase()===name.toLowerCase()))throw Error('An existing project requires readback, not duplicate creation or reuse');
 return{operation:'create-new-project',provider:'supabase',databaseEngine:'postgresql',organizationId:selectedOrganizationId,projectName:name,region,expectedCostUsd:0,cloudExecuted:false,usesExistingIeltsDatabase:false,requiresCurrentProviderQuote:true,paidAddonsAllowed:false,productionActivationAllowed:false,reason:'PostgreSQL runtime/guard parity and actual cloud acceptance must finish before activation'};
}
if(process.argv[1]&&import.meta.url===pathToFileURL(path.resolve(process.argv[1])).href){
 console.log(JSON.stringify({status:'INPUT_REQUIRED',cloudExecuted:false,required:['explicit organization selection','fresh organization plan/project inventory','exact zero-dollar project quote'],message:'Use the authenticated native Supabase tools; never paste tokens or connection secrets into this command.'},null,2));
}
