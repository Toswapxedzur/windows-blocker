# Windows Vault native helper

- `Program.cs` serves Chrome/Edge native authentication and dispatches the bundled MCP stdio mode.
- `NativeBrowserParent.cs` verifies the actual launch parent with Windows Authenticode, signed browser metadata and a bounded system cmd intermediary.
- `NativeHostProtocol.cs` fixes browser-native environment by registered executable alias, exact extension origin and a 64 KiB challenge frame cap.
- `NativeMcpProxy.cs` relays JSON-RPC to the authenticated current-environment loopback MCP listener. It needs no Node installation and keeps credentials out of process arguments.
- `VaultNativeHost.csproj` builds the helper using the shared local authentication and current-user storage sources.
