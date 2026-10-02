# Windows Vault tests

All suites run on mini1, with native .NET/WebView2 suites in its Windows VM.

- `runner-custom-rule-stress.js`, `runner-rule-worker.js`, `runner-editor-syntax.js` — canonical JS rule engine, disposable worker isolation and document-start native bridge/storage.
- `NativeContracts/` — source-only native policy, authentication, identity and editor contract fixtures.
- `NativeHostContracts/` — actual signed browser/system intermediary verification and denial of nonbrowser proof callers.
- `browser-native-host.py` — genuine guest Edge/Chrome proof, program binding, frame bounds, process ancestry and authenticated extension tunnel through the canonical CDP driver.
- `FolderBrokerContracts/` — production broker with actual Windows existing/dangling junctions, UTF-8 BOM text/JSON, sharing violations and revocation.
- `NativeLiveContracts/`, `QuitFixture/` — actual normal-user graceful quit/retry and unsaved-work prompt.
- `WindowsUiContracts/` — actual WPF/WebView2 host, authenticated MCP editing/sandbox gates, mouse quick-add/focus, owning-group panels and selected-folder/cloud boundaries.
- `ExternalLinkContracts/` — real WebView click/target-blank handling and programmatic-navigation denial using the production URI handler and an owned browser callback.
- `McpConnectorContracts/` — nondestructive client registration and authenticated bundled stdio framing.
- `WorkerClientContracts/`, `WorkerFixture/` — backend process replacement, crash/restart and request attribution.
