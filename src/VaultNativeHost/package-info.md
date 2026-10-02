# Windows Vault native helper

- `Program.cs` serves Chrome/Edge native authentication and dispatches the bundled MCP stdio mode.
- `NativeMcpProxy.cs` relays JSON-RPC to the authenticated current-environment loopback MCP listener. It needs no Node installation and keeps credentials out of process arguments.
- `VaultNativeHost.csproj` builds the helper using the shared local authentication and current-user storage sources.
