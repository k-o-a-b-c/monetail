"use strict";
const test=require('node:test'), assert=require('node:assert/strict'), vm=require('node:vm'), fs=require('node:fs'), path=require('node:path');
const pages=path.resolve(__dirname,'../../src/Monetail.Api/Pages');
const account='11111111-1111-4111-8111-111111111111', ledger='22222222-2222-4222-8222-222222222222', token='S'.repeat(43);
const flush=()=>new Promise(setImmediate);
async function page({storage=new Map(),hash='#token='+token,pending=false,preview='valid',fail=false,logged=true,broken=false,previewFail=false,previewCode=null}={}) {
 const elements=new Map(),calls=[],moves=[];let who=account,checkStatus=200;
 if(pending)storage.set('monetail.ledgerInvitation',JSON.stringify({token,pending:{accountId:account,ledgerId:ledger}}));
 const el=id=>{if(!elements.has(id))elements.set(id,{hidden:false,textContent:'',disabled:false,handlers:{},addEventListener(n,f){this.handlers[n]=f;}});return elements.get(id);};
 const location={hash,search:'',origin:'https://monetail.example.test',assign(v){moves.push(v);}};
 const window={};Object.defineProperty(window,'localStorage',{get(){throw Error('forbidden');}});
 const sessionStorage={getItem(k){if(broken)throw Error();return storage.get(k)||null;},setItem(k,v){if(broken)throw Error();storage.set(k,v);},removeItem(k){if(broken)throw Error();storage.delete(k);}};
 const reply=(status,data)=>({status,ok:status>=200&&status<300,json:async()=>data});
 const ctx=vm.createContext({window,document:{getElementById:el,querySelectorAll:()=>[]},location,history:{replaceState(a,b,url){assert.equal(url,'/ledger-invitation');location.hash='';}},sessionStorage,URL,URLSearchParams,Uint8Array,atob,btoa,
 async fetch(url,options={}){assert.equal(location.hash,'');assert.ok(!url.includes(token));calls.push({url,options});
 if(url==='/auth/me')return reply(logged?200:401,{authUserId:who});
 if(url==='/auth/csrf')return reply(200,{token:'csrf'});
 if(url.endsWith('/preview') && previewFail)throw Error('offline');
 if(url.endsWith('/preview'))return reply(typeof preview==='number'?preview:200,{code:previewCode,status:preview,ledgerId:ledger,ledgerName:'<家計簿>'});
 if(url.endsWith('/accept')){if(fail)throw Error('lost');return reply(200,{status:'joined',ledger:{ledgerId:ledger}});}
 return reply(checkStatus,{ledgerId:ledger});}});
 for(const file of ['PasskeyClient.js','LedgerInvitation.js'])vm.runInContext(fs.readFileSync(path.join(pages,file),'utf8'),ctx);
 await flush();return {el,calls,moves,storage,click:async id=>{await el(id).handlers.click();await flush();},account(v){who=v;},check(v){checkStatus=v;}};
}
test('参加は明示操作だけ、完了は同じ画面に継続表示しトークンを消す',async()=>{const p=await page();assert.equal(p.el('ledger-name').textContent,'<家計簿>');assert.equal(p.calls.filter(c=>c.url.endsWith('/accept')).length,0);await p.click('accept');assert.match(p.el('message').textContent,/Windows版/);assert.equal(p.storage.size,0);assert.equal(p.moves.length,0);});
for(const status of ['already_member',400,404,409])test('参加済み・利用不可はトークンを削除:'+status,async()=>{const p=await page({preview:status});assert.equal(p.storage.size,0);assert.equal(p.el('accept').hidden,true);});
test('未ログインは秘密を持つタブでログインへ誘導し記録を保持',async()=>{const p=await page({logged:false});assert.equal(p.el('invitation-login').hidden,false);assert.equal(p.storage.size,1);assert.equal(p.calls.length,1);});
test('応答喪失を再読込しても自動preview・acceptせず本人GETで完了確認',async()=>{const p=await page({fail:true});await p.click('accept');const q=await page({storage:p.storage,hash:''});assert.equal(q.calls.length,0);await q.click('check');assert.deepEqual(q.calls.map(c=>c.url),['/auth/me','/ledgers/'+ledger]);assert.match(q.el('message').textContent,/Windows版/);assert.equal(q.storage.size,0);});
test('404は未確認として記録を維持、再参加が使用済みでも明示確認を残す',async()=>{const p=await page({pending:true,hash:'',fail:true});p.check(404);await p.click('check');assert.match(p.el('message').textContent,/失敗したとは限りません/);assert.equal(p.el('accept').hidden,false);await p.click('accept');assert.equal(p.el('check').hidden,false);assert.equal(p.storage.size,1);});
for(const status of [401,503])test('確認失敗は結果不明のまま:'+status,async()=>{const p=await page({pending:true,hash:''});p.check(status);await p.click('check');assert.equal(p.storage.size,1);assert.equal(p.calls.length,2);});
test('別アカウントは元のGET・acceptを送らない',async()=>{const p=await page({pending:true,hash:''});p.account(ledger);await p.click('check');await p.click('accept');assert.ok(p.calls.every(c=>c.url==='/auth/me'));assert.equal(p.storage.size,1);});
test('preview後の別アカウントにも参加操作を引き継がない',async()=>{const p=await page();p.account(ledger);await p.click('accept');assert.equal(p.calls.filter(c=>c.url.endsWith('/accept')).length,0);});
test('保存不能ならAPIを呼ばずURLから秘密を除去',async()=>{const p=await page({broken:true});assert.equal(p.calls.length,0);assert.equal(p.el('accept').hidden,true);});
test('戻るは一時情報を消す',async()=>{const p=await page();await p.click('back');assert.equal(p.storage.size,0);assert.deepEqual(p.moves,['/auth/login']);});
for(const target of ['/ledger-invitation','/ledger-invitation?token=secret','https://wrong.example/ledger-invitation'])test('固定の招待戻り先だけを追加:'+target,async()=>{let assigned;const ctx=vm.createContext({window:{},location:{origin:'https://monetail.example.test',search:'?returnUrl='+encodeURIComponent(target),assign(v){assigned=v;}},URL,URLSearchParams,Uint8Array,atob,btoa});vm.runInContext(fs.readFileSync(path.join(pages,'PasskeyClient.js'),'utf8'),ctx);assert.equal(ctx.window.MonetailPasskeys.continueAuthorization(),target==='/ledger-invitation');if(assigned)assert.equal(assigned,'https://monetail.example.test/ledger-invitation');});

for(const logged of [true,false])test('通常の戻るはログイン状態別の文言で一時情報を削除:'+logged,async()=>{
 const p=await page({logged});assert.equal(p.el('back').hidden,false);
 assert.equal(p.el('back').textContent,logged?'ログイン情報に戻る':'ログイン画面に戻る');
 await p.click('back');assert.equal(p.storage.size,0);assert.deepEqual(p.moves,['/auth/login']);
});
for(const code of ['invalid_invitation','invitation_expired','invitation_used','invitation_revoked'])test('利用不可では戻るを含め操作を出さない:'+code,async()=>{
 const p=await page({preview:409,previewCode:code});
 for(const id of ['back','preview','accept','check','invitation-login'])assert.equal(p.el(id).hidden,true,id);
 await p.click('back');assert.equal(p.moves.length,0);assert.equal(p.storage.size,0);
});
test('通常の通信失敗では確認再試行と通常の戻るを維持',async()=>{
 const p=await page({previewFail:true});assert.equal(p.el('preview').hidden,false);assert.equal(p.el('back').hidden,false);assert.equal(p.storage.size,1);
});
test('別アカウントからの再ログイン導線は記録を保持し、元の本人だけが明示確認を再開',async()=>{
 const p=await page({pending:true,hash:''});const saved=p.storage.get('monetail.ledgerInvitation');p.account(ledger);
 await p.click('check');assert.equal(p.el('invitation-login').hidden,false);assert.match(p.el('invitation-login').textContent,/元のアカウント/);
 assert.equal(p.el('back').hidden,true);await p.click('back');await p.click('accept');
 assert.equal(p.storage.get('monetail.ledgerInvitation'),saved);assert.ok(p.calls.every(c=>c.url==='/auth/me'));
 const html=fs.readFileSync(path.join(pages,'LedgerInvitation.html'),'utf8');assert.match(html,/id="invitation-login" href="\/auth\/login\?returnUrl=%2Fledger-invitation"/);
 const loggedOut=await page({storage:p.storage,hash:'',logged:false});assert.equal(loggedOut.calls.length,0);await loggedOut.click('check');
 assert.equal(loggedOut.el('invitation-login').hidden,false);assert.equal(p.storage.get('monetail.ledgerInvitation'),saved);
 const restored=await page({storage:p.storage,hash:''});assert.equal(restored.calls.length,0);await restored.click('check');
 assert.deepEqual(restored.calls.map(c=>c.url),['/auth/me','/ledgers/'+ledger]);assert.equal(p.storage.size,0);assert.match(restored.el('message').textContent,/Windows版/);
});
