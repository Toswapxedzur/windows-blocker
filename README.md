# Windows Vault

Windows Vault is the native Windows member of the Vault product family. It is a .NET 8 WPF application with a WebView2 editor, native application inventory, enforcement engine, Custom-rule runtime, the authenticated local hub, Classifier, Activity, and MCP.

The code is the product contract. The maintained in-app manual is [src/WindowsBlocker/WebAssets/manual/en.md](src/WindowsBlocker/WebAssets/manual/en.md).

## Current capabilities

- Default groups for selected Windows applications and Custom groups for advanced policy rules.
- Immediate and allowance modes; schedules; freeze; snooze; and group import/export.
- Windows application inventory and normal quit requests with user-configured retries.
- A WebView2 editor hosted from `src/WindowsBlocker/WebAssets/`.
- Canonical `(on, v) => { ... }` Custom rules with isolated disposable workers, per-group state/logs, native panels and selected-folder access.
- Shared Swift Classifier and Activity services with the same scenes and behavior as Mac Vault.
- Native MCP tools with the same editing gates as the editor.
- Protocol-4 authenticated loopback hub with explicit group-ID links, versioned locks, shared budgets and snoozes.
- Native timer, toast, and panel overlay windows.

## Build

Generate shared assets from the sibling browser/Mac checkouts with `bash sync-webui.sh`, then use the checked-in solution and project on mini1’s Windows VM:

```powershell
dotnet build WindowsBlocker.sln
```

The application project targets `net8.0-windows` and uses WPF plus WebView2. Build and run it on Windows with the required .NET SDK, bundled Classifier worker, and WebView2 runtime available. All product tests run on mini1; Windows presentation uses its VM. Windows 10 and Windows 11 are the intended OS targets. Do not infer a verified OS or CPU architecture from a cross-build.

## Project map

| Area | Source directory |
| --- | --- |
| Group model and policy evaluation | `src/WindowsBlocker/Core/` |
| Native enforcement | `src/WindowsBlocker/Enforcement/` |
| App inventory and WebView bridge | `src/WindowsBlocker/WebUI/` |
| Custom-rule runtime | `src/WindowsBlocker/Rules/` |
| Bridge hub | `src/WindowsBlocker/Bridge/` |
| WPF windows and overlays | `src/WindowsBlocker/` |

## Documentation and translations

English documents remain canonical. UI labels use the complete JSON catalogs in `src/WindowsBlocker/WebAssets/translation/`; translated manuals live beside `manual/en.md`, and translated copies of the remaining maintained documents are under `i18n-docs/<locale>/`.
