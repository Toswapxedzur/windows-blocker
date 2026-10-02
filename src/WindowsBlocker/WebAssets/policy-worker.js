/* Trusted pure policy functions. This worker never loads user source. */
"use strict";
importScripts("platform-profiles.js", "group-actions.js", "group-scopes.js", "parental-pin.js");
self.addEventListener("message", async function (event) {
  const request = event.data || {};
  const allowed = { CBGroupActions: self.CBGroupActions, CBGroupScopes: self.CBGroupScopes, CBParentalPin: self.CBParentalPin };
  try {
    const api = allowed[request.module];
    if (!api || !Object.prototype.hasOwnProperty.call(api, request.method) || typeof api[request.method] !== "function" || !Array.isArray(request.args)) throw new Error("unknown-policy-operation");
    const result = await api[request.method](...request.args);
    self.postMessage({ kind: "rule-runtime-response", requestId: request.requestId, ok: true, result: { value: result === undefined ? null : result } });
  } catch (error) {
    self.postMessage({ kind: "rule-runtime-response", requestId: request.requestId, ok: false, error: String(error?.message || error) });
  }
});
