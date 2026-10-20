# Windows Vault tests

The October 11 run uses the owner-selected Mini2 alt1 ARM Windows VM and Mini2
alt1 Node runtime. Other runs follow the default mini1 rule. Managed source
contracts and WPF builds do not establish live WebView acceptance.

- `runner-native-rule-log.js` — actual Windows native shim error source/level, live messages, per-rule persistence/recreation, clear and deleted-group pruning. Execute with Mini2 alt1 Node.
- `runner-custom-rule-stress.js`, `runner-rule-worker.js`, `runner-editor-syntax.js` — canonical JS rule engine, disposable worker isolation and document-start native bridge/storage.
- `OverlayContracts/` — actual WPF overlay containment, virtualization, timer formatting and searchable menus.
- `NativeContracts/` — source-only native policy, authentication, identity and editor contract fixtures, plus all20 bundled native-language preferences/messages and literal state preservation.
  `BrowserIdentityContracts.cs` / `--browser-identity-only` checks optional identity-v1 HMAC binding alongside mandatory v4 authentication, strict paired metadata, local-major legacy retirement, and actual in-memory hub receive/disconnect/reconnect cleanup. It starts no listener or GUI.
  `HubStopContextContracts.cs` / `--hub-stop-only` forces an incomplete listener-stop task under a non-pumping UI-like context. The synchronous hub stop must complete without pumping that context; queued callbacks are drained only to clean up a failing regression.
  `WebStoreContracts.cs` verifies atomic state/usage commits while an IPC reader
  holds the previous complete snapshot, preserving unrelated rule state.
  `--hub-only` runs actual managed first-link union, multi-browser joining,
  explicit deletion, fixed/rolling offline-transfer deduplication and restart
  receipts against disposable storage without invoking Windows process APIs.
  It also checks unequal first-link usage in both contribution orders and both
  initiators, rolling history, postjoin deltas, interrupted joining/restart,
  native original storage contribution and nested third-browser joins.
  `HubWebsiteEntryContracts.cs` checks independent include/exclude and action
  branches, trusted origin aliases, contribution order/restart, collision
  suffixes, zero-timestamp replay, alias forwarding, native Apps preservation,
  explicit edit/deletion and malformed registry refusal.
  `NativeDefinitionIntentContracts.cs` / `--definition-intent-only` cover the validated file→hub boundary and precise raw intent.
  `NativeEditorTransactionContracts.cs` / `--editor-transaction-only` passed nine Mini2 guest controls for staged async admission, physical write/rollback refusal, explicit partial-storage holds, and old full-group producer refusal. Canonical admission and transport acceptance remain separate.
- `NativeEditorAdmissionContracts/`, `native-editor-policy-stdio.js` — console-only actual NativePolicyTools/RuleEngine pipeline with immutable canonical JS evaluated on Mini2 alt1. All thirteen checks passed at source113a58f; it does not launch a WPF Application or browser.
- `NativeHostContracts/` — actual signed browser/system intermediary verification and denial of nonbrowser proof callers. `--browser-identity-only` runs executable-hash, original creator-chain rechecks, and signed-file controls without launching the separate caller-refusal child fixture.
- `browser-native-host.py` — genuine guest Edge/Chrome proof, program binding, frame bounds, process ancestry and authenticated extension tunnel through the canonical CDP driver.
- `browser-worker-parity.py` — actual YouTube collector/private pill/pending cover, production model, correction, ordinary app outage/reconnect and native Activity privacy through that driver.
- `FolderBrokerContracts/` — production broker with actual Windows existing/dangling junctions, UTF-8 BOM text/JSON, sharing violations and revocation.
- `NativeLiveContracts/`, `QuitFixture/` — actual normal-user graceful quit/retry and unsaved-work prompt.
- `RuleInitializationContracts/` — actual isolated WebView2 registration before events, repeated Run/restart state, state-size rejection, native deletion/write-failure/late-event cleanup, and future/malformed/write-failure preservation of saved bytes and the previous worker. `scripts/development/test-rule-initialization.ps1` runs it as an owned Limited interactive task with a disposable profile and cleanup.
- `WindowsUiContracts/` — actual WPF/WebView2 host, authenticated MCP editing/sandbox gates, mouse quick-add/focus, owning-group panels and selected-folder/cloud boundaries.
- `ExternalLinkContracts/` — real WebView click/target-blank handling and programmatic-navigation denial using the production URI handler and an owned browser callback.
- `McpConnectorContracts/` — nondestructive client registration and authenticated bundled stdio framing.
- `WorkerClientContracts/`, `WorkerFixture/` — backend process replacement, crash/restart and request attribution.

Activity history while recording is OFF is verified against the generated Windows
assets using the canonical Mac `Tests/WebUI/activity-recording-history.js` fixture
and `classifier/Tests/WebUI/autosave.mjs`, with `UI_ASSET_ROOT` pointing at
`src/WindowsBlocker/WebAssets`. Run on the owner-selected Mini2 host.

- `NativeContracts/ManagedRuleTimeContracts.cs`: event-save ordering and asynchronous hub membership checks; `--managed-time-only` starts no listener and uses disposable storage.

- `runner-rule-time-policy.js`: actual policy-worker JavaScript routing and module/method whitelist tests in an isolated VM; native WebView2 acceptance remains separate.

- `sync-webui-preservation.py` — default and explicit browser-only generation preserve Windows Activity/native templates/guides and platform messages while copying canonical browser resources.
- `runner-native-popup-snooze.js` — the actual canonical popup snooze producer through the actual Windows shim; native keyed writes preserve concurrent other-group entries and never use the browser-only snooze IPC. Run both on the authorized Mini2 host.
