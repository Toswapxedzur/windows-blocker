/* Disposable Windows rule-worker checks using Node's worker_threads adapter.
 * This exercises the shipped worker script without requiring WebView2.
 */

const fs = require("fs");
const path = require("path");
const { Worker } = require("worker_threads");

const runtimePath = path.resolve(__dirname, "../src/WindowsBlocker/WebAssets/custom-rule-runtime.js");
const workerPath = path.resolve(__dirname, "../src/WindowsBlocker/WebAssets/rule-worker.js");

function createRuleWorker() {
  const bootstrap = `
    const { parentPort } = require("worker_threads");
    const fs = require("fs");
    const vm = require("vm");
    globalThis.self = globalThis;
    globalThis.postMessage = (value) => parentPort.postMessage(value);
    globalThis.addEventListener = (type, handler) => {
      if (type === "message") parentPort.on("message", (data) => handler({ data }));
    };
    globalThis.removeEventListener = () => {};
    globalThis.importScripts = () => vm.runInThisContext(
      fs.readFileSync(${JSON.stringify(runtimePath)}, "utf8"),
      { filename: ${JSON.stringify(runtimePath)} }
    );
    vm.runInThisContext(
      fs.readFileSync(${JSON.stringify(workerPath)}, "utf8"),
      { filename: ${JSON.stringify(workerPath)} }
    );
  `;
  return new Worker(bootstrap, { eval: true });
}

function request(worker, payload, timeoutMs = 1000) {
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      cleanup();
      reject(new Error("timeout"));
    }, timeoutMs);
    function onMessage(message) {
      if (message?.requestId !== payload.requestId) return;
      cleanup();
      resolve(message);
    }
    function onError(error) {
      cleanup();
      reject(error);
    }
    function cleanup() {
      clearTimeout(timer);
      worker.off("message", onMessage);
      worker.off("error", onError);
    }
    worker.on("message", onMessage);
    worker.on("error", onError);
    worker.postMessage(payload);
  });
}

async function main() {
  let passed = 0;
  const worker = createRuleWorker();
  const loaded = await request(worker, {
    requestId: "load-normal",
    operation: "load",
    groupId: "normal",
    source: `(event) => {
      let unavailable = 0;
      for (const probe of [
        () => fetch("https://example.com"),
        () => postMessage({ kind: "spoof" }),
        () => new Worker("other.js"),
        () => indexedDB.open("shared")
      ]) {
        try { probe(); } catch (_) { unavailable += 1; }
      }
      event.on("tickEvent", "capabilities", (ev) => {
        if (unavailable === 4) ev.block("safe.exe");
      });
    }`
  });
  if (!loaded.ok || loaded.result.handlers !== 1) throw new Error("normal rule did not load");
  passed += 1;
  console.log("PASS rule worker loads event.on rule");

  const dispatched = await request(worker, {
    requestId: "dispatch-normal",
    operation: "dispatch",
    groupId: "normal",
    event: {
      type: "tickEvent",
      groupID: "normal",
      now: new Date().toISOString(),
      data: { appId: "safe.exe", isBrowser: "false" }
    }
  });
  if (!dispatched.ok || dispatched.result.intents?.[0]?.target !== "safe.exe") {
    throw new Error("worker capabilities were not sealed or dispatch failed");
  }
  passed += 1;
  console.log("PASS rule worker seals network, messaging, child-worker, and shared-storage capabilities");
  await worker.terminate();

  const looping = createRuleWorker();
  const started = Date.now();
  let timedOut = false;
  try {
    await request(looping, {
      requestId: "load-loop",
      operation: "load",
      groupId: "loop",
      source: "(event) => { while (true) {} }"
    }, 150);
  } catch (error) {
    timedOut = error.message === "timeout";
  }
  await looping.terminate();
  if (!timedOut || Date.now() - started > 1000) throw new Error("stuck worker was not disposable");
  passed += 1;
  console.log("PASS stuck rule remains isolated and its worker can be terminated");

  console.log(`WINDOWS RULE WORKER TOTAL ${passed} PASS ${passed} FAIL 0`);
}

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
