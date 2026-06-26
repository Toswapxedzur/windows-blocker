# windowsBlocker

`windowsBlocker` is the Windows port of `macosBlocker` / `customBlocker`. It
reuses the **verbatim** customBlocker editor UI (the same `popup.html`,
`popup.js`, `popup.css`, templates, translations, and manual) inside a
**WebView2** host, and ports the platform-agnostic policy engine to C#. Native
blocking is done by **closing windows** — the exact effect of clicking the
red "X" — backed by a re-close loop so a blocked app cannot be reopened.

> Built with .NET 8 / WPF + WebView2. This port has **full feature parity** with
> macosBlocker, including the custom JavaScript rule engine, the floating timer
> HUD, the toast/log overlay, and interactive custom-rule + system overlay panels
> (see Custom rule engine). The only macOS capability not reproduced natively is
> reading **browser tabs** — that stays the browser extension's job (see Scope).

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
| App picker inventory | installed `.app` scan | `WebUI/InstalledAppCatalog.cs` + `WebUI/AppInventory.cs` |
| Timer overlay HUD | `TimerOverlayPanel.swift` | `TimerOverlayWindow.xaml(.cs)` |
| Toast / log overlay | `ToastOverlayPanelController` | `ToastOverlayWindow.xaml(.cs)` |
| Interactive + system panels | `PanelOverlayPanelController` | `PanelOverlayWindow.xaml(.cs)` |
| Custom JS rule runtime | `custom-rule-runtime.js` (in JSCore) | `WebAssets/custom-rule-runtime.js` (in WebView2) |
| Custom-rule orchestration | `CustomJavaScriptPolicyRuntime.swift` + bridge | `Rules/RuleEngine.cs` + `Rules/CustomRuleRuntime.cs` |
| Web-app bridge hub | `ConnectionHub.swift` | `Bridge/ConnectionHub.cs` |

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

## App picker (lists installed apps, like macOS)

Where macOS lists every installed `.app`, the picker enumerates every launchable
app via the Shell **AppsFolder** (`shell:AppsFolder` — the Start menu's "All
apps"), so apps that **aren't running right now** are still selectable, including
**UWP/Store** apps (`WebUI/InstalledAppCatalog.cs`). Each entry carries its real
icon (extracted via `IShellItemImageFactory` → PNG data URL).

Identity is chosen so what you pick is exactly what gets blocked:

- **UWP/Store app** → its **AUMID** (`PackageFamily!AppId`). A running Store
  window is matched by AUMID via `GetApplicationUserModelId`, so it blocks even
  though its on-disk path under `WindowsApps` is never exposed to the picker.
- **Win32 desktop app** → its target **executable path** (matched by path/name).

`WebUI/AppInventory.cs` runs this on a dedicated STA thread (Shell COM needs it),
then folds in any currently-running, windowed, *unpackaged* app missing from the
AppsFolder (e.g. portable exes). The result is pushed into the editor via
`window.__cbApplyAppInventory(...)` after navigation completes.

## Timer overlay HUD (floats over other apps, like macOS)

`TimerOverlayWindow` is the Windows analog of macOS's `TimerOverlayPanel`: a
borderless, always-on-top, **click-through** window that shows the live
`Name: MM:SS` countdown for the timed group whose app is **currently focused**
(foreground). Time only accrues — and the HUD only appears — while that app is
the foreground window, matching macOS's frontmost-app gating. It
never steals focus and passes all input to the window beneath it
(`WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE`), and is hidden from
Alt-Tab (`WS_EX_TOOLWINDOW`). It is fed each second from the `EnforcementStatus`
the engine already produces, and auto-hides when nothing is counting down.

- **Accrual:** a timed group's budget ticks down only while its target app is the
  foreground window (`GetForegroundWindow` → `ProcessIdentity.ForWindow`), so
  background apps never burn time — identical to macOS `reconcileUsage`.
- **Placement:** top-left of the primary work area (16px inset), matching macOS.
- **Known limit:** topmost floats over normal/maximized/fullscreen-video windows,
  but a true *exclusive*-fullscreen app (some games) can still cover it — there is
  no Windows equivalent of macOS's screen-saver window level.

## Custom rule engine (full JavaScript parity)

The custom JavaScript rule engine is ported in full. Rather than embed a second
JS engine, the **verbatim** `custom-rule-runtime.js` from macosBlocker (the same
self-contained `MacBlockerRuntime` macOS runs in JavaScriptCore) is injected into
the editor's WebView2 page at document-creation time and driven over
`ExecuteScriptAsync`, so rules behave identically to macOS.

- **Orchestration** (`Rules/RuleEngine.cs`) is the Windows analog of the
  custom-rule half of `MacEnforcementBridge`: each second it reconciles loaded
  rules against the editor's groups, diffs running apps to synthesize lifecycle
  events (`openApp`/`closeApp`/`focus`/`unfocus`/`switchApp`/`appChanged`), fires
  `tickEvent`, and dispatches every event through the runtime
  (`Rules/CustomRuleRuntime.cs`). Decisions and intents are folded back into
  native enforcement: `shield`/`blockApp` join the same `WM_CLOSE` sweep,
  `close`/`openApp`/`unblockApp` act immediately, and rule **custom timers**
  appear in the same floating HUD as native timers.
- **Overlays.** Rule `log`/`overlay.show` output is shown as bottom-right
  **toast** cards (`ToastOverlayWindow`), and rule-created **interactive panels**
  (`getPanelHelper`) plus host-driven **system panels** (e.g. the parental-PIN
  prompt) are rendered as live, focus-preserving WPF cards
  (`PanelOverlayWindow` + `PanelOverlay`), one window per screen position. Panel
  input is throttled (leading edge + trailing flush) like macOS's panel handler.
- **Local files.** `getLocalFolderHelper` read/write/append/list/exists requests
  are serviced from `%LOCALAPPDATA%\WindowsBlocker\LocalFiles\` (`.txt`/`.csv`/
  `.json` only, path-escape guarded), feeding a `localFileEvent` back to the rule.

## Web-app bridge (acts as a server, like macOS)

windowsBlocker hosts the **same loopback WebSocket hub** the macOS app runs, so
the `customBlocker` browser extension links to it identically — the extension is
OS-agnostic and always dials `ws://127.0.0.1:8787`, so no extension change was
needed. `Bridge/ConnectionHub.cs` is a faithful port of `ConnectionHub.swift`:

- **Loopback only.** Binds `http://127.0.0.1:8787/` (a literal-loopback prefix
  needs no `netsh urlacl` / admin), so the hub is never exposed on the LAN.
- **Per-group clusters.** Same-named, unfrozen groups on two programs link into a
  cluster with one shared budget; scalars are last-writer-wins, block lists are
  unioned (apps from this app, sites from browsers), freeze takes the most
  restrictive member, and the usage timer is a delta accumulator.
- **Auto-start + persistence.** The hub auto-starts on launch when the editor's
  "Web-app bridge" server toggle was last enabled, and the cluster registry is
  persisted to `clusters.json` so links survive a restart — same as macOS.
- **Editor wiring.** The embedded editor drives the hub through the `cbBridge`
  channel (`connection-server-start/stop`, `groups-announce`, `group-connect`,
  `group-disconnect`, `group-sync`, `clusters-status`) and the hub pushes
  `__cbConnectionState` / `__cbClustersState` / `__cbGroupRejected` each second.

> The first connect may need a Windows Defender Firewall allow on the private
> network; loopback traffic is normally permitted without a prompt. Safari is
> not relevant on Windows (it uses native messaging on macOS, not this socket).

## Scope

The native app blocks **whole apps** (window-level), runs the full custom JS rule
engine, and hosts the web-app bridge. As on macOS, **site/tab/DOM-level** blocking
is the `customBlocker` browser extension's job: a rule's web-level intents
(`blockSite`, `unblockSite`, `closeTab`, `closeTabsByPattern`) are **ignored** by
the native app (not acted on, not logged) and left entirely to the extension,
which runs the same rule against its own web events, and the rule runtime's
`__nativeGetAllTabs` hook returns no tabs (the native app never reads browser
tabs). The native app touches only **app-level** intents. Everything else — app
shields, custom
timers, toast logs, interactive + system panels, local-file rule storage — runs
natively at parity with macosBlocker.

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
clusters.json    web-app bridge cluster registry (survives restart)
WebView2/        WebView2 user-data folder
LocalFiles/      managed local-files folder for custom-rule file I/O
```
