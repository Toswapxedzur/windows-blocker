# Windows development scripts

- `build-and-test.ps1` builds the native products and runs screen-free contract suites on mini1's Windows VM.
- `test-native-live.ps1` verifies graceful quit/retry with a harmless normal-user fixture.
- `test-windows-ui.ps1` verifies the actual normal-user editor/MCP host.
- `test-folder-broker.ps1` runs the production folder broker against owned Windows files as an Interactive/Limited user.
- `test-external-links.ps1` runs actual visible WebView link-click contracts with an owned callback, so it does not launch an unrelated browser.
- `test-package-guards.ps1` verifies that packaging and installation reject corrupt payloads, path escapes and unrelated destinations before changing them.
- `verify-mini1.py` exports source to the isolated mini1 VM and runs the selected suites.
- `install-native-host.ps1` registers development/production authentication helpers in the current user's Chrome/Edge registry.
- `run-windows-vault.ps1` rebuilds and relaunches the development app on mini1.
