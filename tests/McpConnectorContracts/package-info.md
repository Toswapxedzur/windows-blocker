# MCP client/proxy contracts

- `McpConnectorContracts.csproj` compiles the production registry and stdio proxy into an isolated fixture executable.
- `Program.cs` checks client settings preservation, managed TOML idempotence/refusals, Windows paths and authenticated JSONL proxy framing against a disposable loopback endpoint. Run on mini1's Windows VM only.
