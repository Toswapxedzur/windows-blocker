# Windows development scripts

- `build-and-test.ps1` builds the native products and runs screen-free contract suites on mini1's Windows VM.
- `test-native-live.ps1` verifies graceful quit/retry with a harmless normal-user fixture.
- `test-dictionary-ui.ps1` verifies first-launch default/opt-out and persistent choice in an isolated normal-user Windows app, then closes its app normally.
- `test-windows-ui.ps1` verifies the actual normal-user editor/MCP host.
- `test-folder-broker.ps1` runs the production folder broker against owned Windows files as an Interactive/Limited user.
- `start-browser-fixture.ps1`, `browser-fixture-session.ps1`, `stop-browser-fixture.ps1` own a disposable signed-browser/production-worker session and ordinary app outage/reconnect; `load-browser-extension-fixture.ps1` uses Chrome's supported isolated Developer mode sideload UI, following its broker dialogs by window ownership. Start creates the model hardlink as the Limited user; stop restores registration and removes only its GUID browser profile.
- `test-external-links.ps1` runs actual visible WebView link-click contracts with an owned callback, so it does not launch an unrelated browser.
- `test-package-guards.ps1` verifies that packaging and installation reject corrupt payloads, path escapes, unrelated destinations and stale pre-dictionary workers before changing them.
- `verify-mini1.py` exports source to the isolated mini1 VM and runs the selected suites.
- `install-native-host.ps1` registers development/production authentication helpers in the current user's Chrome/Edge registry.
- `run-windows-vault.ps1` rebuilds and relaunches the development app on mini1.

- `test-rule-initialization.ps1` builds and runs the actual WebView2/native initialization persistence contracts in a disposable Limited-user interactive task.
