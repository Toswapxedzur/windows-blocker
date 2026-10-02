# Build metadata

- `mcp-tools.mac.json` — public tool definitions exported from the canonical Swift MCP registry by `PortableMCPCatalogTests` on mini1. The current export passed the 257-test Mac suite at source checkpoint `c257052`; it contains 38 tools and no state or credentials.
- `package-info.md` — this map.

Refresh after MCP contract changes: run the Mac native suite on mini1 with `VAULT_MCP_CATALOG_EXPORT` set to an artifact path, copy that public JSON here, then run `sync-webui.sh`. The generator adapts native application identifiers and the platform-specific rule reference while preserving the tool names and validation schemas.
