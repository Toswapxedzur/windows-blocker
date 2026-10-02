/* Shared native storage and request contracts. Run on mini1. */
const fs=require("node:fs"),vm=require("node:vm"),path=require("node:path"),assert=require("node:assert/strict");
const shims=["../../macosBlocker/Sources/MacBlockerWebUI/WebAssets/chrome-shim.js","../src/WindowsBlocker/WebAssets/chrome-shim.js"];
(async()=>{for(const file of shims){
 const messages=[],storage=new Map();
 const context={console,Promise,URL,Set,setTimeout,clearTimeout,navigator:{language:"en"},document:{baseURI:"https://appassets.windowsblocker/"},
 localStorage:{getItem:key=>storage.get(key)||null,setItem:(key,value)=>storage.set(key,String(value))},
 __CB_DESKTOP_PROGRAM_ID:"windowsapp",__cbNativeStoreSeed:JSON.stringify({blockedGroups:[{id:"a"}],usageTimersMs:{a:10,b:20}})};
 context.window=context;
 context.webkit={messageHandlers:{cbBridge:{postMessage:message=>{messages.push(message);if(message.requestId) context.__cbNativeReply(message.requestId,{ok:true,loadResult:{ok:true,handlers:1}})}}}};
 vm.createContext(context);vm.runInContext(fs.readFileSync(path.resolve(__dirname,file),"utf8"),context);
 assert.equal(context.chrome.runtime.id,"windows-vault");
 await context.chrome.storage.local.set({usageTimersMs:{a:11,b:20}});
 assert.deepEqual(JSON.parse(JSON.stringify(messages[0])),{kind:"persist-store",changes:{usageTimersMs:{a:11}}});
 context.__cbApplyNativeStore({blockedGroups:[{id:"a"}],usageTimersMs:{a:11,b:99}});
 assert.equal((await context.chrome.storage.local.get("usageTimersMs")).usageTimersMs.b,99);
 assert.equal(messages.length,1,"native snapshots must not echo writes");
 const response=await context.chrome.runtime.sendMessage({type:"run-custom-group",groupId:"a",source:"(on,v)=>{}"});
 assert.equal(response.loadResult.handlers,1);assert.equal(messages[1].kind,"run-custom-group");
 context.__cbApplyNativeRuleLog([{source:"v.log",groupId:"a",message:"own"},{source:"diagnostic",groupId:"a",message:"hidden"},{source:"v.log",groupId:"other",message:"gone"}]);
 assert.equal((await context.chrome.runtime.sendMessage({type:"get-log-feed",groupId:"a"})).entries.length,1);
 assert.equal((await context.chrome.runtime.sendMessage({type:"get-log-feed",groupId:"other"})).entries.length,0);
 console.log("PASS",file,"patch merge, native reply, and per-group v.log contracts");
}})().catch(error=>{console.error(error);process.exitCode=1});
