/* Cross-runtime custom-rule stress checks for the Windows host resource.
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
    data: {}
  }, event || {})));
}

function siteIntents(result) {
  return result.intents.filter((intent) => intent.action === "blockSite");
}

function appIntents(result) {
  return result.intents.filter((intent) => intent.action === "blockApp");
}

const shortsRule = `
(event, helpers) => {
  event.registerAppChangedEvent("only-youtube-shorts", (ev, h) => {
    const youtube = h.getPlatformHelper().youtube();
    if (youtube.isShortUrl(ev.url)) ev.block("youtube.com");
  });
}
`;

// W1: Mobile host plus a query string must still match a real Shorts route.
MacBlockerRuntime.load("w-mobile-shorts", shortsRule);
check("W1 mobile YouTube Shorts blocks", siteIntents(dispatch("w-mobile-shorts", {
  url: "https://m.youtube.com/shorts/abc123?feature=share"
})).map((intent) => intent.pattern).join(",") === "youtube.com");

// W2: A foreign route that merely looks like Shorts must never match YouTube.
MacBlockerRuntime.load("w-foreign-shorts", shortsRule);
check("W2 foreign /shorts/ path stays allowed", siteIntents(dispatch("w-foreign-shorts", {
  url: "https://example.com/shorts/abc123"
})).length === 0);

// W3: Long-form YouTube watch pages remain outside a Shorts-only rule.
MacBlockerRuntime.load("w-watch-page", shortsRule);
check("W3 normal YouTube watch page stays allowed", siteIntents(dispatch("w-watch-page", {
  url: "https://www.youtube.com/watch?v=abc123"
})).length === 0);

// W4: The focused app identity is supplied in event.data.appId.
MacBlockerRuntime.load("w-steam", `
(event) => {
  event.registerAppChangedEvent("pause-steam", (ev) => {
    if (ev.data.appId === "steam.exe") ev.block();
  });
}
`);
check("W4 focused Steam app blocks", appIntents(dispatch("w-steam", {
  data: { appId: "steam.exe" }
})).map((intent) => intent.target).join(",") === "steam.exe");

// W5: A stale target object cannot impersonate the focused application.
MacBlockerRuntime.load("w-identity", `
(event) => {
  event.registerAppChangedEvent("pause-steam", (ev) => {
    if (ev.data.appId === "steam.exe") ev.block();
  });
}
`);
check("W5 stale target cannot block Notes", appIntents(dispatch("w-identity", {
  target: { id: "steam-target", normalizedValue: "steam.exe" },
  data: { appId: "notes.exe" }
})).length === 0);

// W6: A higher-priority rule can stop later handlers from adding side effects.
MacBlockerRuntime.load("w-priority", `
(event) => {
  event.registerAppChangedEvent("first", (ev) => {
    ev.block("first.example");
    ev.stopPropagation();
  }, { priority: 20 });
  event.registerAppChangedEvent("second", (ev) => {
    ev.block("second.example");
  });
}
`);
check("W6 stopPropagation prevents lower-priority block", siteIntents(dispatch("w-priority"))
  .map((intent) => intent.pattern).join(",") === "first.example");

// W7: An interval rule must avoid duplicate firing inside its own window.
MacBlockerRuntime.load("w-interval", `
(event) => {
  event.registerTickEvent("minute-rule", (ev) => {
    ev.block("minute.example");
  }, { intervalMs: 60000 });
}
`);
const firstTick = dispatch("w-interval", { type: "tickEvent", now: "2027-01-15T12:00:00.000Z" });
const earlyTick = dispatch("w-interval", { type: "tickEvent", now: "2027-01-15T12:00:30.000Z" });
const dueTick = dispatch("w-interval", { type: "tickEvent", now: "2027-01-15T12:01:01.000Z" });
check("W7 interval throttles then fires again", siteIntents(firstTick).length === 1 &&
  siteIntents(earlyTick).length === 0 && siteIntents(dueTick).length === 1);

// W8: Native local-file responses must expose the same normalized shortcut
// fields as extension rules: parsed JSON, typed entries, booleans and bytes.
MacBlockerRuntime.load("w-local-file-result", `
(event) => {
  event.registerLocalFileEvent("consume", (ev) => {
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
    ok: "true",
    eventName: "read",
    action: "readJson",
    path: "config/focus.json",
    valueJSON: "{\"enabled\":true}",
    entriesJSON: "[{\"name\":\"focus.json\",\"kind\":\"file\"}]",
    exists: "true",
    bytes: "17"
  }
});
check("W8 local-file result exposes parsed callback fields",
  appIntents(localFileResult).map((intent) => intent.target).join(",") === "local-file-ok.exe");

// W9: The native helper must only queue safe, supported local-file requests.
MacBlockerRuntime.load("w-local-file-helper", `
(event) => {
  event.registerTickEvent("request", (ev, h) => {
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
    ["write", "notes/focus.txt"],
    ["append", "notes/focus.txt"],
    ["readJson", "config/focus.json"],
    ["list", ""]
  ]));

print("WINDOWS CUSTOM-RULE STRESS TOTAL " + (passed + failed) + " PASS " + passed + " FAIL " + failed);
if (failed > 0) {
  print("__CB_TEST_RESULT__: FAIL");
  throw new Error("Windows custom-rule stress tests failed");
}
print("__CB_TEST_RESULT__: OK");
