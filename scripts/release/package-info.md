# Windows package scripts

- `package-windows-vault.ps1` verifies the shared worker and publishes a self-contained x64 application/helper package.
- `install-windows-vault.ps1` installs for the current user, supplies a missing Microsoft-signed WebView2 dependency and registers the browser host.
- `test-package-runtime.ps1` verifies normal-user installation, SDK-free app/worker startup, authenticated Classifier actions, crash recovery, ordinary quit, update and persistence on mini1's Windows guests; restores its temporary browser registration and shortcut.
- `README.md` explains packaging, installation, verification limits and client configuration sources.
