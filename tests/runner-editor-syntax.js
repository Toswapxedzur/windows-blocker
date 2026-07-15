/* Desktop editor syntax-check contract. Run from the repository root with:
 * node windowsBlocker/tests/runner-editor-syntax.js
 */

const fs = require("fs");
const path = require("path");
const vm = require("vm");

const shims = [
  path.resolve(__dirname, "../../macosBlocker/Sources/MacBlockerWebUI/WebAssets/chrome-shim.js"),
  path.resolve(__dirname, "../src/WindowsBlocker/WebAssets/chrome-shim.js")
];

async function checkShim(path) {
  const storage = new Map();
  const context = {
    console,
    Promise,
    URL,
    navigator: { language: "en" },
    document: { baseURI: "https://appassets.windowsblocker/" },
    localStorage: {
      getItem: (key) => storage.has(key) ? storage.get(key) : null,
      setItem: (key, value) => storage.set(key, String(value))
    }
  };
  context.window = context;
  vm.createContext(context);
  vm.runInContext(fs.readFileSync(path, "utf8"), context, { filename: path });

  const response = await context.chrome.runtime.sendMessage({
    type: "check-custom-group-syntax",
    source: `(event) => {
      event.on("tickEvent", "raw-on", () => {});
      event.registerFocusEvent("typed", () => {});
    }`
  });
  if (!response?.result?.ok || response.result.handlers !== 2) {
    throw new Error(`${path}: expected event.on + typed alias to count as 2 handlers; got ${JSON.stringify(response)}`);
  }
  const started = Date.now();
  const bounded = await context.chrome.runtime.sendMessage({
    type: "check-custom-group-syntax",
    source: `(event) => { while (true) {} event.on("tickEvent", "loop", () => {}); }`
  });
  if (!bounded?.result?.ok || bounded.result.handlers !== 1 || Date.now() - started > 250) {
    throw new Error(`${path}: syntax preview executed the registration body or lost event.on`);
  }
  console.log(`PASS ${path} counts event.on and typed registrations`);
  console.log(`PASS ${path} parses without executing registration code`);
}

(async () => {
  for (const path of shims) await checkShim(path);
  console.log("DESKTOP EDITOR SYNTAX TOTAL 4 PASS 4 FAIL 0");
})().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
