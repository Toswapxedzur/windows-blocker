# windowsBlocker

`windowsBlocker` is the Windows port of `macosBlocker` / `customBlocker`. It
reuses the **verbatim** customBlocker editor UI (the same `popup.html`,
`popup.js`, `popup.css`, templates, translations, and manual) inside a
**WebView2** host, and ports the platform-agnostic policy engine to C#. Native
blocking is done by **closing windows** — the exact effect of clicking the
red "X" — backed by a re-close loop so a blocked app cannot be reopened.

> Built with .NET 8 / WPF + WebView2. The custom JavaScript rule engine from
> macosBlocker is **intentionally excluded** from this port (see Scope).

## What was ported

| Layer | Source (macosBlocker, Swift) | Here (C#) |
| --- | --- | --- |
| Block groups / targets | `BlockGroup.swift` | `Core/BlockGroup.cs` |
| Schedules / time windows | `Schedule.swift` | `Core/Schedule.cs` |
| Policy decisions | `PolicyDecision.swift` | `Core/PolicyModels.cs` |
| Policy evaluation | `PolicyEvaluator.swift` | `Core/PolicyEvaluator.cs` |
| Usage / snooze / freeze state | `UsageState.swift` | `Core/UsageState.cs` |
| Chrome-schema import | `ChromeExtensionImport.swift` | `Core/ChromeExtensionImporter.cs` |
| Capabilities | `PlatformCapabilities.swift` | `Core/PlatformCapabilities.cs` |
| Editor UI | `WebAssets/` (verbatim) | `WebAssets/` (verbatim copy) |
| chrome.* shim | `chrome-shim.js` (verbatim) | same, transport remapped to WebView2 |
| Native store | `BlockerWebStore.swift` | `WebUI/WebStore.cs` |
| Enforcement bridge | `MacEnforcementBridge.swift` | `Enforcement/EnforcementEngine.cs` |
| Running-app sweep | `MacProcessTerminator.swift` | `Enforcement/WindowCloser.cs` |
| Focus/launch events | `BrowserFocusObserver.swift` | `Enforcement/WinEventMonitor.cs` |
| App identity | macOS bundle ID | `Enforcement/ProcessIdentity.cs` (exe path + UWP AUMID descent) |

## Enforcement model (the red "X")

1. **`WM_CLOSE` only.** The single action is `PostMessage(hwnd, WM_CLOSE)` to
   every top-level, visible, non-tool window of a blocked app
   (`WindowCloser.cs`). This is cooperative and non-destructive — no kill, no
   suspend.
2. **Re-close loop.** A system-wide `SetWinEventHook` on `EVENT_OBJECT_SHOW` +
   `EVENT_SYSTEM_FOREGROUND` (`WinEventMonitor.cs`) closes a blocked window the
   instant it appears, so relaunches and tray-restores can't be used. A
   1-second `DispatcherTimer` sweep (`EnforcementEngine.cs`) is the backstop.
3. **Self-preservation.** `SelfPreservationGuard` warns **once** when the user
   tries to quit windowsBlocker (window X / Alt+F4 / Task Manager "End task",
   which posts `WM_CLOSE`) and cancels that first attempt; any later attempt is
   allowed, so the user is never truly trapped.

### Known limits (by design)

- An app that **vetoes `WM_CLOSE`** (an unsaved-changes prompt the user cancels)
  cannot be force-closed, because the port uses no suspend/terminate escalation.
- A **hard** `TerminateProcess` (Task Manager "End process") cannot be
  intercepted from user space; self-preservation only covers cooperative quits.
- **Elevated** apps cannot be closed by this non-elevated process (Windows UIPI).

## Scope

Excluded: the custom JavaScript rule engine (`CustomJavaScriptPolicyRuntime` /
JavaScriptCore). The editor's custom-rule UI still loads (it is part of the
verbatim web UI and its in-page syntax check runs), but rules are **not**
executed or enforced natively. Site/tab/DOM-level blocking remains the job of
the `customBlocker` browser extension; windowsBlocker only blocks whole apps.

## Build & run (Windows only)

Requires the .NET 8 SDK and the WebView2 Runtime (preinstalled on current
Windows 10/11).

```powershell
cd windowsBlocker
dotnet run --project src/WindowsBlocker -c Debug
```

> This repository was scaffolded on macOS, so it has not been compiled here;
> build it on Windows. Pin `Microsoft.Web.WebView2` to a concrete version once
> you have a build machine (currently floated at `1.0.*`).

## Storage

State lives under `%LOCALAPPDATA%\WindowsBlocker\`:

```text
web-store.json   the editor's chrome.storage snapshot (groups, settings, usage)
WebView2/        WebView2 user-data folder
LocalFiles/      managed local-files folder for the editor
```
