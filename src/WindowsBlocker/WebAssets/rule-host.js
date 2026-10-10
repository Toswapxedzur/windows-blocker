(function () {
  "use strict";

  var workers = new Map();
  var candidates = new Map();
  var policyWorker = null;

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
    var candidate = candidates.get(groupId);
    if (candidate) candidate.terminate();
    candidates.delete(groupId);
  }

  function createGroupWorker(groupId, staged) {
    var previousCandidate = candidates.get(groupId);
    if (previousCandidate) previousCandidate.terminate();
    var worker = new Worker("rule-worker.js");
    candidates.set(groupId, worker);
    worker.addEventListener("message", function (event) {
      var data = event.data;
      if (!data || data.kind !== "rule-runtime-response") return;
      if (candidates.get(groupId) === worker) {
        if (staged && data.ok && data.result && data.result.ok) {
          worker.prepared = true;
          respond(data.requestId, data.ok, data.result, data.error);
          return;
        }
        candidates.delete(groupId);
        if (data.ok && data.result && data.result.ok) {
          var previous = workers.get(groupId);
          if (previous) previous.terminate();
          workers.set(groupId, worker);
        } else {
          worker.terminate();
        }
      }
      respond(data.requestId, data.ok, data.result, data.error);
    });
    worker.addEventListener("error", function () {
      if (candidates.get(groupId) === worker) candidates.delete(groupId);
      if (workers.get(groupId) === worker) workers.delete(groupId);
      worker.terminate();
    });
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
    if (operation === "policy") {
      if (!policyWorker) {
        policyWorker = new Worker("policy-worker.js");
        policyWorker.addEventListener("message", function (event) {
          var data = event.data;
          if (data && data.kind === "rule-runtime-response") respond(data.requestId, data.ok, data.result, data.error);
        });
        policyWorker.addEventListener("error", function () {
          policyWorker.terminate();
          policyWorker = null;
        });
      }
      policyWorker.postMessage(request);
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

    if (operation === "discard-load") {
      var rejected = candidates.get(groupId);
      if (rejected) rejected.terminate();
      candidates.delete(groupId);
      respond(requestId, true, { ok: true }, "");
      return;
    }
    if (operation === "commit-load") {
      var prepared = candidates.get(groupId);
      if (!prepared || !prepared.prepared) {
        respond(requestId, false, {}, "group-not-prepared");
        return;
      }
      candidates.delete(groupId);
      var previous = workers.get(groupId);
      if (previous) previous.terminate();
      workers.set(groupId, prepared);
      respond(requestId, true, { ok: true }, "");
      return;
    }
    var worker = operation === "load" || operation === "prepare-load"
      ? createGroupWorker(groupId, operation === "prepare-load")
      : workers.get(groupId);
    if (!worker) {
      respond(requestId, false, {}, "group-not-loaded");
      return;
    }
    worker.postMessage(operation === "prepare-load" ? { ...request, operation: "load" } : request);
  });
})();
