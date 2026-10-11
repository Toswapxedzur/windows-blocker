"use strict";
// Executes only on the designated Mini2 Node runtime. Files are immutable
// canonical exports, not this Windows repository's possibly older mirrors.
const fs = require("node:fs"), path = require("node:path"), vm = require("node:vm"), crypto = require("node:crypto"), readline = require("node:readline");
const [sourceRoot, manifestPath] = process.argv.slice(2);
const manifest = JSON.parse(fs.readFileSync(manifestPath, "utf8"));
if (manifest.revision !== "db770c9e0ff9941f80786fbdb53edaa5638ff0a3") throw new Error("wrong canonical revision");
const files = ["platform-profiles.js", "parental-pin.js", "group-actions.js", "group-scopes.js"];
const ctx = vm.createContext({ console: { log() {}, warn() {}, error() {} }, crypto: crypto.webcrypto, TextEncoder, structuredClone });
for (const file of files) {
  const records = manifest.files.filter(record => record.path === file);
  if (records.length !== 1) throw new Error("missing or duplicate canonical file");
  const bytes = fs.readFileSync(path.join(sourceRoot, file));
  if (crypto.createHash("sha256").update(bytes).digest("hex") !== records[0].sha256) throw new Error("canonical hash mismatch: " + file);
  vm.runInContext(bytes.toString("utf8"), ctx, { filename: file, timeout: 5000 });
}
const allowed = { CBGroupScopes: new Set(["editorChange", "applyEditorRequest", "newGroup"]), CBGroupActions: new Set(["budgetRestarts"]) };
console.log(JSON.stringify({ kind: "ready", uid: process.getuid(), node: process.version, revision: manifest.revision, files: manifest.files }));
const input = readline.createInterface({ input: process.stdin, crlfDelay: Infinity });
input.on("line", line => {
  if (line.length > 1048576) { process.exitCode = 1; input.close(); return; }
  let request;
  try {
    request = JSON.parse(line);
    if (request.kind !== "policy" || !Number.isSafeInteger(request.id) || !allowed[request.module]?.has(request.method) || !Array.isArray(request.args)) throw new Error("unsupported fixture policy call");
    // vm's timeout bounds actual helper execution, not merely module loading.
    ctx.fixtureRequest = request;
    const value = vm.runInContext("globalThis[fixtureRequest.module][fixtureRequest.method](...fixtureRequest.args)", ctx, { timeout: 5000 });
    console.log(JSON.stringify({ id: request.id, value }));
  } catch (error) { console.log(JSON.stringify({ id: request?.id ?? null, error: error.message })); }
  finally { delete ctx.fixtureRequest; }
});
