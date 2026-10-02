# Windows Vault tests

All suites run on mini1, with native .NET/WebView2 suites in its Windows VM.

- `runner-custom-rule-stress.js`, `runner-rule-worker.js`, `runner-editor-syntax.js` — canonical JS rule engine, disposable worker isolation and document-start native bridge/storage.
- `NativeContracts/` — source-only native policy, authentication, identity and editor contract fixtures.
- `NativeLiveContracts/`, `QuitFixture/` — actual normal-user graceful quit/retry and unsaved-work prompt.
- `WindowsUiContracts/` — actual WPF/WebView2 host and authenticated MCP editing/sandbox gates.
- `McpConnectorContracts/` — nondestructive client registration and authenticated bundled stdio framing.
- `WorkerClientContracts/`, `WorkerFixture/` — backend process replacement, crash/restart and request attribution.
