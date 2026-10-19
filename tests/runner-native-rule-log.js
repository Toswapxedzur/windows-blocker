'use strict';
const fs = require('node:fs'), vm = require('node:vm'), path = require('node:path'), assert = require('node:assert/strict');
const source = fs.readFileSync(path.join(__dirname, '../src/WindowsBlocker/WebAssets/chrome-shim.js'), 'utf8');
function rig(storage = new Map()) {
  const context = { console, Promise, URL, Set, setTimeout, clearTimeout, navigator: { language: 'en' }, document: { baseURI: 'https://appassets.windowsblocker/' },
    localStorage: { getItem: key => storage.get(key) || null, setItem: (key,value) => storage.set(key,String(value)), removeItem: key => storage.delete(key) },
    __CB_DESKTOP_PROGRAM_ID: 'windowsapp', __cbNativeStoreSeed: JSON.stringify({ blockedGroups: [{ id: 'a' }, { id: 'b' }] }) };
  context.window = context;
  vm.createContext(context); vm.runInContext(source, context);
  return { context, storage, feed: async groupId => JSON.parse(JSON.stringify((await context.chrome.runtime.sendMessage({ type: 'get-log-feed', groupId })).entries)) };
}
(async () => {
  const r = rig(), delivered = [];
  r.context.chrome.runtime.onMessage.addListener(message => { if (message.type === 'log-feed-entry') delivered.push(message.entry); });
  r.context.__cbApplyNativeRuleLog([
    { source: 'v.log', level: 'log', groupId: 'a', message: 'ordinary' },
    { source: 'page-error', level: 'error', groupId: 'a', message: 'handler failed' },
    { source: 'v.log', level: 'error', groupId: 'a', message: 'legacy explicit error' },
    { source: 'page-error', groupId: 'b', message: 'other rule error' },
    { source: 'diagnostic', groupId: 'a', message: 'unknown source' },
    { source: 'page-error', groupId: 'gone', message: 'deleted rule' }
  ]);
  let feed = await r.feed('a');
  assert.deepEqual(feed.map(e => [e.source,e.level,e.message]), [
    ['v.log','log','ordinary'], ['page-error','error','handler failed'], ['v.log','error','legacy explicit error']
  ]);
  assert.equal((await r.feed('b'))[0].level, 'error');
  assert.equal((await r.feed('gone')).length, 0);
  assert.equal(delivered.filter(e => e.groupId === 'a').length, 3);
  const restarted = rig(r.storage);
  assert.deepEqual(await restarted.feed('a'), feed, 'Errors survive ordinary editor recreation independently of config');
  await restarted.context.chrome.runtime.sendMessage({ type: 'clear-log-feed', groupId: 'a' });
  assert.equal((await restarted.feed('a')).length, 0);
  assert.equal((await restarted.feed('b')).length, 1);
  restarted.context.__cbApplyNativeStore({ blockedGroups: [{ id: 'a' }] });
  assert.equal((await restarted.feed('b')).length, 0, 'Deleted groups cannot retain another rule\'s errors');
  console.log('PASS actual Windows shim error source/level, live delivery, per-rule persistence/recreation/clear/pruning');
})().catch(error => { console.error(error); process.exitCode = 1; });
