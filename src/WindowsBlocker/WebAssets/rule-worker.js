(function () {
  "use strict";

  // Capture the only worker capabilities the trusted host wrapper needs.
  var reply = self.postMessage.bind(self);
  var listen = self.addEventListener.bind(self);

  importScripts("custom-rule-runtime.js");
  var runtime = self.MacBlockerRuntime;
  self.MacBlockerRuntime = undefined;

  // Rule code needs synchronous JavaScript only. Remove network, host-message,
  // import, and scheduling capabilities before any user source is evaluated.
  function disabled() { throw new Error("capability-unavailable"); }
  function lockCapability(name) {
    try {
      Object.defineProperty(self, name, {
        value: disabled, writable: false, configurable: false
      });
    } catch (_) {}
    // Prevent bypassing the own-property guard with
    // DedicatedWorkerGlobalScope.prototype.<capability>.call(...).
    var prototype = Object.getPrototypeOf(self);
    while (prototype) {
      if (Object.prototype.hasOwnProperty.call(prototype, name)) {
        try {
          Object.defineProperty(prototype, name, {
            value: disabled, writable: false, configurable: false
          });
        } catch (_) {}
      }
      prototype = Object.getPrototypeOf(prototype);
    }
  }
  ["fetch", "XMLHttpRequest", "WebSocket", "EventSource", "importScripts",
   "postMessage", "close", "setTimeout", "setInterval", "queueMicrotask",
   "Worker", "SharedWorker", "BroadcastChannel", "MessageChannel",
   "indexedDB", "caches", "Notification", "dispatchEvent"].forEach(lockCapability);
  try {
    Object.defineProperty(self, "onmessage", {
      get: function () { return null; },
      set: function () {},
      configurable: false
    });
  } catch (_) {}
  try {
    lockCapability("addEventListener");
    lockCapability("removeEventListener");
  } catch (_) {}

  listen("message", function (event) {
    var request = event.data || {};
    var requestId = String(request.requestId || "");
    var groupId = String(request.groupId || "");
    try {
      var result;
      if (request.operation === "load") {
        try {
          result = JSON.parse(runtime.load(groupId, String(request.source || "")));
        } catch (error) {
          runtime.unload(groupId);
          throw error;
        }
      } else if (request.operation === "dispatch") {
        result = JSON.parse(runtime.dispatch(request.event || {}));
      } else {
        throw new Error("unsupported-operation");
      }
      reply({ kind: "rule-runtime-response", requestId: requestId, ok: true, result: result });
    } catch (error) {
      reply({
        kind: "rule-runtime-response",
        requestId: requestId,
        ok: false,
        error: String(error && error.message ? error.message : error)
      });
    }
  });
})();
