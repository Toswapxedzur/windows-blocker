# Windows Vault

Windows Vault is the native Windows member of the Vault product family. It is a .NET 8 WPF application with a WebView2 editor, native application inventory, enforcement engine, Custom-rule runtime, and a local web-app bridge hub.

The code is the product contract. The maintained in-app manual is [src/WindowsBlocker/WebAssets/manual/en.md](src/WindowsBlocker/WebAssets/manual/en.md).

## Current capabilities

- Default groups for selected Windows applications and Custom groups for advanced policy rules.
- Immediate, allowance, and countdown modes; schedules; freeze; snooze; and group import/export.
- Windows application inventory and window-based enforcement components.
- A WebView2 editor hosted from `src/WindowsBlocker/WebAssets/`.
- Controlled Custom-rule execution with syntax checking and a log feed.
- A loopback bridge hub for explicitly linked compatible groups.
- Native timer, toast, and panel overlay windows.

## Build

Use the checked-in solution and project:

```powershell
dotnet build WindowsBlocker.sln
```

The application project targets `net8.0-windows` and uses WPF plus WebView2. Build and run it on Windows with the required .NET SDK and WebView2 runtime available.

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
