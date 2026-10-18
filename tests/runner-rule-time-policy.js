"use strict";
const fs=require("node:fs"),vm=require("node:vm"),path=require("node:path"),assert=require("node:assert/strict");
const root=path.join(__dirname,"../src/WindowsBlocker/WebAssets");let listener,reply;
const worker=vm.createContext({console,TextEncoder,TextDecoder,URL,Uint8Array,crypto:require("node:crypto").webcrypto,addEventListener(type,fn){assert.equal(type,"message");listener=fn;},postMessage(value){reply=JSON.parse(JSON.stringify(value));}});
worker.self=worker;worker.importScripts=(...names)=>{for(const file of names)vm.runInContext(fs.readFileSync(path.join(root,file),"utf8"),worker,{filename:file});};
vm.runInContext(fs.readFileSync(path.join(root,"policy-worker.js"),"utf8"),worker,{filename:"policy-worker.js"});
async function request(module,method,args){reply=null;await listener({data:{requestId:"policy-test",module,method,args}});assert(reply);return reply;}
(async()=>{
 const merged=await request("RuleCore","mergeTime",[null,[],"",1800000000000]);assert(merged.ok);assert(merged.result.value.ok);assert.equal(merged.result.value.state.version,1);
 console.log("PASS actual policy worker routes the canonical time merge");
 const seed=merged.result.value.state;
 const reduced=await request("RuleCore","reduceTime",[seed,{member:"windowsapp/w",commands:[],nowMs:1800000000000}]);assert(reduced.ok);assert(reduced.result.value.ok);
 console.log("PASS actual policy worker routes the canonical serialized reducer");
 for(const [module,method] of [["RuleCore","createEngine"],["RuleCore","compile"],["RuleCore","constructor"],["constructor","keys"],["__proto__","toString"],["missing","syncTime"]])assert.equal((await request(module,method,[])).ok,false,module+"/"+method);
 console.log("PASS policy worker refuses rule execution and inherited module/method escapes");
 const invalid=await request("RuleCore","syncTime",[seed,{member:"windowsapp/w",members:["windowsapp/w"],initiator:"windowsapp/w",commands:[],nowMs:1800000000000}]);assert(invalid.ok);assert.equal(invalid.result.value.ok,false);
 console.log("PASS policy refusal remains structured instead of a successful shared acknowledgment");
 console.log(JSON.stringify({pass:true,cases:4}));
})().catch(error=>{console.error(error);process.exitCode=1;});
