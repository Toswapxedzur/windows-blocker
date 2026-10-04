# Windows Vault native application

- `App.xaml`, `MainWindow.*`: app and WebView2 editor shell.
- `Core/`, `Enforcement/`, `Bridge/`, `Rules/`, `WebUI/`: native policy, enforcement, authenticated integration, custom-rule execution and shared-editor transport.
- `WebAssets/`: generated browser/Mac editor, Classifier, Activity and locale assets; regenerate with root `sync-webui.sh`.
- `NativeControls.xaml`: shared owned-window controls; light connection settings and filled controls on translucent rule panels.
- `NativePanelSelect.cs`, `NativePinField.cs`: bounded searchable option menus and drawn LTR PIN digits.
- `PanelOverlayWindow.*`: work-area-bounded, virtualized custom-rule cards.
- `TimerOverlayWindow.*`, `QuickAddWindow.cs`, `McpConnectionsWindow.cs`: native timer, quick-add and AI connection surfaces.
- `Assets/`: selected Windows icon assets; `WindowsBlocker.csproj` and `app.manifest`: packaging/build metadata.

- `NativeInputHints.cs`: noninteractive watermarks in the scoped TextBox template. Panel labels wrap; timer rows reserve a countdown column and realize only the current five-second page.
