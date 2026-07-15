(function () {
  "use strict";

  var workers = new Map();

  function respond(requestId, ok, result, error) {
    window.chrome.webview.postMessage({
      kind: "rule-runtime-response",
      requestId: String(requestId || ""),
      ok: !!ok,
      result: result || {},
      error: error || ""
    });
  }

  function resetGroup(groupId) {
    var existing = workers.get(groupId);
    if (existing) existing.terminate();
    workers.delete(groupId);
  }

  function createGroupWorker(groupId) {
    resetGroup(groupId);
    var worker = new Worker("rule-worker.js");
    worker.addEventListener("message", function (event) {
      var data = event.data;
      if (!data || data.kind !== "rule-runtime-response") return;
      respond(data.requestId, data.ok, data.result, data.error);
    });
    worker.addEventListener("error", function () {
      resetGroup(groupId);
    });
    workers.set(groupId, worker);
    return worker;
  }

  window.chrome.webview.addEventListener("message", function (event) {
    var request = event.data;
    if (!request || typeof request !== "object") return;

    if (request.kind === "rule-runtime-reset") {
      resetGroup(String(request.groupId || ""));
      return;
    }
    if (request.kind !== "rule-runtime-request") return;

    var requestId = String(request.requestId || "");
    var operation = String(request.operation || "");
    var groupId = String(request.groupId || "");
    if (operation === "ping") {
      respond(requestId, true, { ready: true }, "");
      return;
    }
    if (!groupId) {
      respond(requestId, false, {}, "missing-group-id");
      return;
    }
    if (operation === "unload") {
      resetGroup(groupId);
      respond(requestId, true, {}, "");
      return;
    }

    var worker = operation === "load"
      ? createGroupWorker(groupId)
      : workers.get(groupId);
    if (!worker) {
      respond(requestId, false, {}, "group-not-loaded");
      return;
    }
    worker.postMessage(request);
  });
})();
