# windowsBlocker — Windows Vault (native Windows app)

> 🤖 **AI protocol:** Read `../package-info.md` (group), the group `AGENTS.md`, and `../misc/project-memory/PROJECT-MEMORY.md` before working here. Update this file when the folder changes. Never delete without owner consent; keep secrets out of git.

- **What:** the native Windows member (public name **Windows Vault**): .NET 8 WPF app with a WebView2 editor (mirrored extension UI in `src/WindowsBlocker/WebAssets`), app inventory, enforcement, custom-rule runtime, and a legacy v2 loopback bridge hub (not yet migrated to hub protocol v4).
- **Own git repo:** `Toswapxedzur/windows-blocker`, branch `main`; releases `v0.0.1`, `v0.0.2` alpha. Version source of truth = `<Version>` in `src/WindowsBlocker/WindowsBlocker.csproj`.
- **Build/test:** needs Windows + .NET 8 SDK (`WindowsBlocker.sln`). Real-Windows testing: the QEMU Windows 11 VM on mini1 (`~/winvm`, recipe in `~/Desktop/memory/mini1-windows-test-vm.md`; `guest-build.ps1` builds + launches, `gui.sh` drives the desktop). On the Mac, a C#-only cross-compile catches most errors. JS contracts in `tests/` run here: `runner-custom-rule-stress.js` via macOS `jsc`, the other two via `node`.
- **Folders:** `src/WindowsBlocker/` (`Core`, `Enforcement`, `Rules`, `Bridge`, `SelfPreservation`, `WebUI`, `WebAssets` incl. 20-language `manual/`, `Assets/` official Windows icon), `tests/`, `i18n-docs/`.
