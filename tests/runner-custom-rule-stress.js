/* Native custom-rule contract checks for the Windows host resource.
 * Run from windowsBlocker with:
 * /System/Library/Frameworks/JavaScriptCore.framework/Versions/A/Helpers/jsc tests/runner-custom-rule-stress.js
 */

load("src/WindowsBlocker/WebAssets/custom-rule-runtime.js");

let passed = 0;
let failed = 0;

function check(name, condition, details) {
  if (condition) {
    passed += 1;
    print("PASS " + name);
    return;
  }
  failed += 1;
  print("FAIL " + name + " " + JSON.stringify(details || {}));
}

function dispatch(groupId, event) {
  return JSON.parse(MacBlockerRuntime.dispatch(Object.assign({
    type: "appChangedEvent",
    groupID: groupId,
    now: "2027-01-15T12:00:00.000Z",
    url: "",
    hostname: "",
    data: { isBrowser: "false" }
  }, event || {})));
}

function appIntents(result) {
  return result.intents.filter((intent) => intent.action === "blockApp");
}

// W1: The raw event.on API used by the editor/templates is functional.
MacBlockerRuntime.load("w-event-on", `
(event) => {
  event.on("focusEvent", "block-focus", (ev) => {
    if (ev.data.appId === "steam.exe") ev.block("steam.exe");
  });
}
`);
check("W1 event.on registers native focus handler", appIntents(dispatch("w-event-on", {
  type: "focusEvent", data: { appId: "steam.exe", isBrowser: "false" }
})).map((intent) => intent.target).join(",") === "steam.exe");

// W2/W3: Dynamic app blocks belong to one group and unload clears the owner.
MacBlockerRuntime.load("w-owner-a", `
(event) => { event.on("tickEvent", "block", (ev, h) => h.getWindowHelper().block("a.exe")); }
`);
dispatch("w-owner-a", { type: "tickEvent", data: { appId: "a.exe", intervalMs: "1000" } });
MacBlockerRuntime.load("w-owner-b", `
(event) => {
  event.on("tickEvent", "check", (ev, h) => {
    if (h.getWindowHelper().isBlocked("a.exe")) ev.block("leaked.exe");
  });
}
`);
check("W2 dynamic app blocks are group-scoped",
  appIntents(dispatch("w-owner-b", { type: "tickEvent" })).length === 0);
check("W3 dynamic blocklist reports the owning group",
  JSON.stringify(MacBlockerRuntime.getDynamicBlocklist("w-owner-a")) === JSON.stringify(["a.exe"]));
MacBlockerRuntime.unload("w-owner-a");
check("W3 unload clears the owning dynamic blocklist",
  MacBlockerRuntime.getDynamicBlocklist("w-owner-a").length === 0);

// W4: The focused app identity is supplied in event.data.appId.
MacBlockerRuntime.load("w-steam", `
(event) => {
  event.on("appChangedEvent", "pause-steam", (ev) => {
    if (ev.data.appId === "steam.exe") ev.block();
  });
}
`);
check("W4 focused Steam app blocks", appIntents(dispatch("w-steam", {
  data: { appId: "steam.exe", isBrowser: "false" }
})).map((intent) => intent.target).join(",") === "steam.exe");

// W5: A stale target object cannot impersonate the focused application.
MacBlockerRuntime.load("w-identity", `
(event) => {
  event.on("appChangedEvent", "pause-steam", (ev) => {
    if (ev.data.appId === "steam.exe") ev.block();
  });
}
`);
check("W5 stale target cannot block Notes", appIntents(dispatch("w-identity", {
  target: { id: "steam-target", normalizedValue: "steam.exe" },
  data: { appId: "notes.exe", isBrowser: "false" }
})).length === 0);

// W6: A higher-priority rule can stop later handlers from adding side effects.
MacBlockerRuntime.load("w-priority", `
(event) => {
  event.on("appChangedEvent", "first", (ev) => {
    ev.block("first.exe");
    ev.stopPropagation();
  }, { priority: 20 });
  event.on("appChangedEvent", "second", (ev) => ev.block("second.exe"));
}
`);
check("W6 stopPropagation prevents lower-priority block", appIntents(dispatch("w-priority"))
  .map((intent) => intent.target).join(",") === "first.exe");

// W7: An interval rule must avoid duplicate firing inside its own window.
MacBlockerRuntime.load("w-interval", `
(event) => {
  event.on("tickEvent", "minute-rule", (ev) => ev.block("minute.exe"), { intervalMs: 60000 });
}
`);
const firstTick = dispatch("w-interval", { type: "tickEvent", now: "2027-01-15T12:00:00.000Z" });
const earlyTick = dispatch("w-interval", { type: "tickEvent", now: "2027-01-15T12:00:30.000Z" });
const dueTick = dispatch("w-interval", { type: "tickEvent", now: "2027-01-15T12:01:01.000Z" });
check("W7 interval throttles then fires again", appIntents(firstTick).length === 1 &&
  appIntents(earlyTick).length === 0 && appIntents(dueTick).length === 1);

// W8: Native local-file responses expose normalized shortcut fields.
MacBlockerRuntime.load("w-local-file-result", `
(event) => {
  event.on("localFileEvent", "consume", (ev) => {
    if (ev.ok && ev.eventName === "read" && ev.action === "readJson" &&
        ev.value && ev.value.enabled === true && ev.entries.length === 1 &&
        ev.entries[0].kind === "file" && ev.exists === true && ev.bytes === 17) {
      ev.block("local-file-ok.exe");
    }
  });
}
`);
const localFileResult = dispatch("w-local-file-result", {
  type: "localFileEvent",
  data: {
    ok: "true", eventName: "read", action: "readJson", path: "config/focus.json",
    valueJSON: "{\"enabled\":true}",
    entriesJSON: "[{\"name\":\"focus.json\",\"kind\":\"file\"}]",
    exists: "true", bytes: "17", isBrowser: "false"
  }
});
check("W8 local-file result exposes parsed callback fields",
  appIntents(localFileResult).map((intent) => intent.target).join(",") === "local-file-ok.exe");

// W9: The native helper only queues safe, supported local-file requests.
MacBlockerRuntime.load("w-local-file-helper", `
(event) => {
  event.on("tickEvent", "request", (ev, h) => {
    const files = h.getLocalFolderHelper();
    files.requestWrite("notes/focus.txt", "start");
    files.requestAppend("notes/focus.txt", "+more");
    files.requestReadJson("config/focus.json");
    files.requestList();
    files.requestRead("../private.txt");
    files.requestWrite("notes/focus.md", "nope");
  });
}
`);
const localFileRequests = dispatch("w-local-file-helper", { type: "tickEvent" }).intents
  .filter((intent) => intent.kind === "localFile");
check("W9 local-file helper rejects unsafe or unsupported requests",
  JSON.stringify(localFileRequests.map((intent) => [intent.action, intent.path])) === JSON.stringify([
    ["write", "notes/focus.txt"], ["append", "notes/focus.txt"],
    ["readJson", "config/focus.json"], ["list", ""]
  ]));

// W10: Browser feed mutation remains intentionally unavailable in native apps.
MacBlockerRuntime.load("w-browser-inert", `
(event) => {
  event.on("tickEvent", "browser-api", (ev, h) => h.platform("youtube").hide("shorts", () => true));
}
`);
const browserResult = dispatch("w-browser-inert", { type: "tickEvent" });
check("W10 browser feed helper is inert on Windows",
  browserResult.intents.length === 0 && browserResult.decisions.some((decision) => decision.action === "log"));

// W11: Every bundled desktop template generates a native rule that loads.
const desktopTemplates = [];
globalThis.CB_REGISTER_TEMPLATES = (templates) => desktopTemplates.push(...templates);
load("src/WindowsBlocker/WebAssets/templates/examples.js");
const templateLoads = desktopTemplates.map((template, index) => {
  const values = {};
  template.params.forEach((param) => { values[param.id] = param.defaultValue; });
  return JSON.parse(MacBlockerRuntime.load("w-template-" + index, template.buildCode(values))).handlers;
});
check("W11 native desktop templates all register handlers",
  desktopTemplates.length === 5 && templateLoads.every((count) => count > 0), templateLoads);

print("WINDOWS CUSTOM-RULE STRESS TOTAL " + (passed + failed) + " PASS " + passed + " FAIL " + failed);
if (failed > 0) {
  print("__CB_TEST_RESULT__: FAIL");
  throw new Error("Windows custom-rule stress tests failed");
}
print("__CB_TEST_RESULT__: OK");
