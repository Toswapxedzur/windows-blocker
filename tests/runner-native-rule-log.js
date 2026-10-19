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
  // Render the real native-shim feed through the actual popup renderer.
  const popup = fs.readFileSync(path.join(__dirname, '../src/WindowsBlocker/WebAssets/popup.js'), 'utf8');
  const element = () => ({ children: [], textContent: '', setAttribute() {}, appendChild(child) { this.children.push(child); } });
  const list = element();
  const renderer = vm.createContext({ document: { createElement: element }, logFeedList: list,
    logFeedSeenIds: new Set(), state: { selectedGroupId: 'a' }, logFeedGroupId: 'a',
    LOG_FEED_MAX_RENDER: 200, formatLogFeedTime: () => '', updateLogFeedVisibleCount() {} });
  vm.runInContext(popup.slice(popup.indexOf('function renderLogFeedEntry('), popup.indexOf('function updateLogFeedVisibleCount(')), renderer);
  feed.forEach(entry => renderer.renderLogFeedEntry(entry));
  assert.equal(list.children.length, 3);
  assert.match(list.children[1].children[0].textContent, /ERROR/);
  assert.equal(list.children[1].children[1].textContent, 'handler failed');
  assert.match(list.children[2].children[0].textContent, /ERROR/);
  feed.forEach(entry => renderer.renderLogFeedEntry(entry)); assert.equal(list.children.length, 3);
  const html = fs.readFileSync(path.join(__dirname, '../src/WindowsBlocker/WebAssets/popup.html'), 'utf8');
  const english = JSON.parse(fs.readFileSync(path.join(__dirname, '../src/WindowsBlocker/WebAssets/translation/en.json'), 'utf8'));
  assert.match(english['logFeed.hint'], /execution or delivery errors/);
  assert(html.includes(english['logFeed.hint']) && html.includes(english['logFeed.empty']));
  const manual = fs.readFileSync(path.join(__dirname, '../src/WindowsBlocker/WebAssets/code-manual/en.md'), 'utf8');
  const catalog = JSON.parse(fs.readFileSync(path.join(__dirname, '../src/WindowsBlocker/WebAssets/mcp-tools.json'), 'utf8'));
  const ruleTool = catalog.find(tool => tool.name === 'run_custom_rule');
  assert(ruleTool.description.endsWith(manual), 'MCP callers receive the current Windows managed-time/error contract');
  assert(ruleTool.description.includes('v.timer') && ruleTool.description.includes('eventName !== "click"'));
  const browserRuleTool = catalog.find(tool => tool.name === 'extension_run_custom_rule');
  assert(browserRuleTool.description.includes('v.timer') && browserRuleTool.description.includes('v.rolling') && !browserRuleTool.description.includes('no timers,'), 'Browser MCP entrypoint exposes the current managed-time API');
  const restarted = rig(r.storage);
  assert.deepEqual(await restarted.feed('a'), feed, 'Errors survive ordinary editor recreation independently of config');
  await restarted.context.chrome.runtime.sendMessage({ type: 'clear-log-feed', groupId: 'a' });
  assert.equal((await restarted.feed('a')).length, 0);
  assert.equal((await restarted.feed('b')).length, 1);
  restarted.context.__cbApplyNativeStore({ blockedGroups: [{ id: 'a' }] });
  assert.equal((await restarted.feed('b')).length, 0, 'Deleted groups cannot retain another rule\'s errors');
  console.log('PASS actual Windows shim-to-popup ERROR rows, UI/MCP contract, live delivery, per-rule persistence/recreation/clear/pruning');
})().catch(error => { console.error(error); process.exitCode = 1; });
