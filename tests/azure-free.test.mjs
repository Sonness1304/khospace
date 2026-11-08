import test from 'node:test';
import assert from 'node:assert/strict';
import {spawnSync} from 'node:child_process';
const script='ops/azure-free.ps1';
function run(extra=[]){const defaults=['-Subscription','11111111-1111-1111-1111-111111111111','-ResourceGroup','khospace-free','-Server','khospace-free-sql','-Database','WarehouseRent_Cloud_Demo'];for(let i=0;i<extra.length;i+=2){defaults[defaults.indexOf(extra[i])+1]=extra[i+1];}return spawnSync('pwsh',['-NoProfile','-File',script,'-Action','Plan',...defaults],{encoding:'utf8'});}
test('offline SQL plan uses a named subscription and free-only AutoPause flags',()=>{
 const result=run();assert.equal(result.status,0,result.stderr);const plan=JSON.parse(result.stdout);
 assert.equal(plan.action,'Plan');assert.equal(plan.cloudExecuted,false);
 const args=plan.createArguments;
 for(const pair of [['--subscription','11111111-1111-1111-1111-111111111111'],['--use-free-limit','true'],['--free-limit-exhaustion-behavior','AutoPause'],['--edition','GeneralPurpose'],['--compute-model','Serverless']])assert.equal(args[args.indexOf(pair[0])+1],pair[1]);
 assert.equal(args.includes('BillOverUsage'),false);assert.equal(args.includes('--admin-password'),false);
});
test('SQL plan rejects an existing non-cloud catalog name',()=>{const r=run(['-Database','master']);assert.notEqual(r.status,0);});
test('SQL plan rejects unpinned subscription',()=>{const r=run(['-Subscription','not-a-guid']);assert.notEqual(r.status,0);});
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
function mockFixture(mode){
 const root=fs.mkdtempSync(path.join(os.tmpdir(),'khospace-azure-fixture-')),mock=path.join(root,'az.ps1'),log=path.join(root,'calls.jsonl');
 fs.writeFileSync(mock,`$ErrorActionPreference='Stop'
 ConvertTo-Json -InputObject @($args) -Compress | Add-Content -LiteralPath $env:FAKE_AZ_LOG
 $line=$args -join ' '
 $state=@{name='WarehouseRent_Cloud_Demo';useFreeLimit=$true;freeLimitExhaustionBehavior='AutoPause';sku=@{tier='GeneralPurpose';family='Gen5'};currentBackupStorageRedundancy='Local'}
 if($env:FAKE_AZ_MODE -in @('paid','corrupt-new')){$state.useFreeLimit=$false}
 if($line -match '^account show'){@{id='11111111-1111-1111-1111-111111111111';state='Enabled'}|ConvertTo-Json;exit 0}
 if($line -match '^sql server show'){@{name='khospace-free-sql';minimalTlsVersion='1.2'}|ConvertTo-Json;exit 0}
 if($line -match '^sql db list') {if($env:FAKE_AZ_MODE -in @('new','corrupt-new','unsupported')){'[]'}else{ConvertTo-Json -InputObject @($state) -Depth 6};exit 0}
 if($line -match '^sql db create --help'){if($env:FAKE_AZ_MODE -eq 'unsupported'){'old-cli'}else{'--use-free-limit --free-limit-exhaustion-behavior'};exit 0}
 if($line -match '^sql db (create|show)'){$state|ConvertTo-Json -Depth 6;exit 0}
 exit 7
 `);
 const args=['-NoProfile','-File',script,'-Action','Create','-Subscription','11111111-1111-1111-1111-111111111111','-ResourceGroup','khospace-free','-Server','khospace-free-sql','-Database','WarehouseRent_Cloud_Demo','-AzureCli',mock];
 const result=spawnSync('pwsh',args,{encoding:'utf8',env:{...process.env,FAKE_AZ_LOG:log,FAKE_AZ_MODE:mode}});
 const calls=fs.existsSync(log)?fs.readFileSync(log,'utf8').trim().split(/\r?\n/).map(s=>JSON.parse(s)):[];
 return {result,calls};
}
test('existing paid database is refused without any create/update/delete call',()=>{
 const {result,calls}=mockFixture('paid');assert.notEqual(result.status,0);assert.match(result.stderr,/free-only/);assert.equal(calls.some(args=>args.some(x=>['create','update','delete'].includes(x))),false);
});
test('existing free database is read back without mutation',()=>{
 const {result,calls}=mockFixture('free');assert.equal(result.status,0,result.stderr);const receipt=JSON.parse(result.stdout);assert.equal(receipt.created,false);assert.equal(receipt.freeVerified,true);assert.equal(calls.some(args=>args.includes('create')),false);
});
test('new SQL database creation passes only free capped arguments and checks readback',()=>{
 const {result,calls}=mockFixture('new');assert.equal(result.status,0,result.stderr);const receipt=JSON.parse(result.stdout);assert.equal(receipt.created,true);assert.equal(receipt.freeVerified,true);
 const create=calls.find(args=>args[0]==='sql'&&args[1]==='db'&&args[2]==='create'&&!args.includes('--help'));assert.ok(create);assert.equal(create[create.indexOf('--free-limit-exhaustion-behavior')+1],'AutoPause');assert.equal(create[create.indexOf('--use-free-limit')+1],'true');assert.equal(calls.some(args=>args[0]==='sql'&&args[1]==='db'&&args[2]==='show'),true);
});

test('missing CLI free flags fail before database creation, no paid fallback',()=>{
 const {result,calls}=mockFixture('unsupported');assert.notEqual(result.status,0);assert.match(result.stderr,/does not support free-only/);assert.equal(calls.some(args=>args[2]==='create'&&!args.includes('--help')),false);
});
test('unsafe creation readback stops without retry/update/delete',()=>{
 const {result,calls}=mockFixture('corrupt-new');assert.notEqual(result.status,0);assert.match(result.stderr,/free-only settings/);assert.equal(calls.filter(args=>args[2]==='create'&&!args.includes('--help')).length,1);assert.equal(calls.some(args=>args.includes('update')||args.includes('delete')),false);
});
