/* Run on Mini2: canonical popup snooze code through the actual Windows shim. */
"use strict";
const fs = require("node:fs"), vm = require("node:vm"), path = require("node:path"), assert = require("node:assert/strict");
const root = path.join(__dirname, "../src/WindowsBlocker/WebAssets");
const popup = fs.readFileSync(path.join(root, "popup.js"), "utf8");
const nativeDetector = popup.slice(popup.indexOf("function isNativeHost()"), popup.indexOf("// Stable identifier for this endpoint"));
const snooze = popup.slice(popup.indexOf("async function persistSnooze("), popup.indexOf("async function loadGroups()"));
assert(nativeDetector.startsWith("function isNativeHost()") && snooze.startsWith("async function persistSnooze("));
const messages = [], local = new Map(), other = { startedAtMs: 20, durationMs: 700 };
const context = {console, Promise, URL, Set, setTimeout, clearTimeout, navigator: {language: "en"},
  document: {baseURI: "https://appassets.windowsblocker/"}, __CB_DESKTOP_PROGRAM_ID: "windowsapp",
  __cbNativeStoreSeed: JSON.stringify({groupSnoozes: {b: other}}),
  localStorage: {getItem: key => local.get(key) || null, setItem: (key,value) => local.set(key,String(value)), removeItem: key => local.delete(key)},
  refuseWhileDesktopVaultAway: () => false, t: key => key, browserIdentityUpgradeRequired: () => false,
  browserIdentityErrorText: String, setStatus() {}, render() {}, sanitizeSnoozes: value => value};
context.window = context;
context.webkit = {messageHandlers: {cbBridge: {postMessage: value => messages.push(JSON.parse(JSON.stringify(value)))}}};
vm.createContext(context);
vm.runInContext(fs.readFileSync(path.join(root, "chrome-shim.js"), "utf8"), context, {filename: "actual-Windows-chrome-shim.js"});
vm.runInContext(nativeDetector + '\nconst IS_NATIVE_DESKTOP=isNativeHost(); const GROUP_SNOOZES_KEY="groupSnoozes"; const state={groups:[{id:"a"}],groupSnoozes:{}};\n' + snooze, context, {filename:"actual-canonical-popup-snooze.js"});
(async () => {
 assert.equal(vm.runInContext("IS_NATIVE_DESKTOP",context), true);
 assert.equal(context.chrome.runtime.id,"windows-vault");
 context.chrome.runtime.sendMessage = () => {throw new Error("Browser-only set-group-snooze IPC must not reach Windows");};
 const started = {startedAtMs: 100, durationMs: 60000};
 await context.persistSnooze("a", started);
 assert.deepEqual(messages, [{kind: "persist-store", changes: {groupSnoozes: {a: started}}}]);
 assert.deepEqual(JSON.parse(JSON.stringify((await context.chrome.storage.local.get("groupSnoozes")).groupSnoozes)), {a: started,b: other});
 const concurrent = {startedAtMs: 30,durationMs: 900};
 context.__cbApplyNativeStore({groupSnoozes:{a:started,b:concurrent}});
 assert.equal(messages.length,1,"native snapshots must not echo a write");
 const ended = {...started,endedAtMs:120};
 await context.persistSnooze("a",ended);
 assert.deepEqual(messages[1],{kind:"persist-store",changes:{groupSnoozes:{a:ended}}});
 assert.deepEqual(JSON.parse(JSON.stringify((await context.chrome.storage.local.get("groupSnoozes")).groupSnoozes)),{a:ended,b:concurrent});
 context.refuseWhileDesktopVaultAway=()=>true;
 await assert.rejects(context.persistSnooze("a",started),/link.enforceOnly/);
 assert.equal(messages.length,2,"refusal must not persist a snooze");
 console.log("PASS actual Windows shim selects native popup snooze storage, preserves concurrent other-group data, and refuses before persistence");
})().catch(error=>{console.error(error);process.exitCode=1;});
