import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
test('KhoSpace bounds the actual session Google icon without importing the retired theme',()=>{
 const css=fs.readFileSync('frontend/src/khospace.css','utf8');
 const mark=css.match(/\.khospace \.google-mark\s*\{([^}]+)\}/)?.[1]??'';
 for(const rule of ['width:20px','height:20px','flex-shrink:0'])assert.ok(mark.includes(rule),rule);
 assert.match(css,/\.khospace \.google-button\{[^}]*background:white/);
 const session=fs.readFileSync('frontend/src/features/auth/SessionPanel.tsx','utf8');
 assert.match(session,/className="google-button"/);assert.match(session,/className="google-mark"/);
 assert.doesNotMatch(fs.readFileSync('frontend/src/main.tsx','utf8'),/import '\.\/refinement.css'/);
});


test('KhoSpace secondary hover overrides the primary hover and legacy text color together',()=>{
 const css=fs.readFileSync('frontend/src/khospace.css','utf8');
 const rule=css.match(/\.khospace \.secondary-button:hover:not\(:disabled\),\.khospace \.ks-ghost:hover\{([^}]+)\}/)?.[1]??'';
 assert.ok(rule.includes('background:#edf3fa'),'secondary hover must outrank primary hover background');
 assert.ok(rule.includes('color:var(--ink)'),'secondary hover must override the legacy teal text');
});


test('KhoSpace auth controls keep explicit readable colors and selected-tab emphasis',()=>{
 const css=fs.readFileSync('frontend/src/khospace.css','utf8');
 const controls=css.match(/\.khospace \.password-toggle,\.khospace \.text-button\{([^}]+)\}/)?.[1]??'';
 assert.ok(controls.includes('color:var(--primary)'),'text controls must not inherit white primary-button text');
 const tabs=css.match(/\.khospace \.auth-tabs button\{([^}]+)\}/)?.[1]??'';
 assert.ok(tabs.includes('background:transparent')&&tabs.includes('color:var(--muted)'));
 const selected=css.match(/\.khospace \.auth-tabs button\[aria-selected="true"\]\{([^}]+)\}/)?.[1]??'';
 assert.ok(selected.includes('background:var(--primary)')&&selected.includes('color:white'));
});


test('mobile work navigation keeps each horizontal scroll item uncompressed and touch-sized',()=>{
 const css=fs.readFileSync('frontend/src/khospace.css','utf8');
 const rule=css.match(/\.khospace \.ks-workspace-nav a\{([^}]+)\}/)?.[1]??'';
 assert.ok(rule.includes('flex-shrink:0'),'nowrap work links must not shrink into overlapping neighbours');
 assert.ok(rule.includes('min-height:44px'),'work links must retain44px touch targets');
});
