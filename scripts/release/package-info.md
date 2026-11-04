# Windows package scripts

- `package-windows-vault.ps1` verifies the shared worker and publishes a self-contained x64 application/helper package.
- `install-windows-vault.ps1` installs for the current user, supplies a missing Microsoft-signed WebView2 dependency, registers the browser host and restores the previous app/registrations/shortcut if a post-swap step fails.
- `test-package-runtime.ps1` verifies normal-user installation, SDK-free app/worker startup, authenticated Classifier actions, crash recovery, ordinary quit, update and persistence on Mini2's Windows guest; restores its temporary browser registration and shortcut.
- `WindowsVaultSetup.cs` supplies the per-user progress/error/open-app UI for the embedded, hash-pinned installer payload.
- `build-setup.ps1` produces the unsigned one-click Setup.exe and checksum from a verified package ZIP.
- `README.md` explains packaging, installation, verification limits and client configuration sources.
