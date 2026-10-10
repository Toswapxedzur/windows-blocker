# Windows Vault tests

The current repair run uses mini1 under the replacement AGENTS. Windows
process/WebView2 checks require its Windows VM; portable managed hub contracts
can also run on mini1 macOS and do not establish Windows runtime acceptance.

- `runner-custom-rule-stress.js`, `runner-rule-worker.js`, `runner-editor-syntax.js` — canonical JS rule engine, disposable worker isolation and document-start native bridge/storage.
- `OverlayContracts/` — actual WPF overlay containment, virtualization, timer formatting and searchable menus.
- `NativeContracts/` — source-only native policy, authentication, identity and editor contract fixtures, plus all20 bundled native-language preferences/messages and literal state preservation.
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
- `NativeHostContracts/` — actual signed browser/system intermediary verification and denial of nonbrowser proof callers.
- `browser-native-host.py` — genuine guest Edge/Chrome proof, program binding, frame bounds, process ancestry and authenticated extension tunnel through the canonical CDP driver.
- `browser-worker-parity.py` — actual YouTube collector/private pill/pending cover, production model, correction, ordinary app outage/reconnect and native Activity privacy through that driver.
- `FolderBrokerContracts/` — production broker with actual Windows existing/dangling junctions, UTF-8 BOM text/JSON, sharing violations and revocation.
- `NativeLiveContracts/`, `QuitFixture/` — actual normal-user graceful quit/retry and unsaved-work prompt.
- `RuleInitializationContracts/` — actual isolated WebView2 registration before events, repeated Run/restart state, state-size rejection, native deletion/write-failure/late-event cleanup, and future/malformed/write-failure preservation of saved bytes and the previous worker. `scripts/development/test-rule-initialization.ps1` runs it as an owned Limited interactive task with a disposable profile and cleanup.
- `WindowsUiContracts/` — actual WPF/WebView2 host, authenticated MCP editing/sandbox gates, mouse quick-add/focus, owning-group panels and selected-folder/cloud boundaries.
- `ExternalLinkContracts/` — real WebView click/target-blank handling and programmatic-navigation denial using the production URI handler and an owned browser callback.
- `McpConnectorContracts/` — nondestructive client registration and authenticated bundled stdio framing.
- `WorkerClientContracts/`, `WorkerFixture/` — backend process replacement, crash/restart and request attribution.
