# Windows Vault package

Build and verify on mini1's Windows VM. `package-windows-vault.ps1` requires the
bundled shared Classifier worker produced by `scripts/classifier-worker/` and
checks its dependency hashes before publishing a self-contained x64 .NET app
and native authentication/MCP helper. End users need no .NET SDK, Swift, Node or
compiler. This is a development package; no public release or signed installer
is claimed.

Extract the package and run `Install.ps1` normally. It installs into the current
user's application directory, registers the browser native host in HKCU and
adds a Start menu shortcut. If Microsoft WebView2 is missing, it downloads and
verifies Microsoft's signed Evergreen bootstrapper before installing the
runtime. It needs an internet connection only for that missing dependency.
Use `-Environment development` for the separate development profile. Close a
running instance before updating its installation. User data and downloaded
models stay outside the application directory.

Windows 10 22H2 and Windows 11 are the intended x64 targets. Runtime acceptance
on each OS is required before claiming release support; a cross-build alone is
insufficient. Windows ARM64 and GPU acceleration have no acceptance claim.

Configuration locations/formats follow the native Mac client catalog with
Windows paths. Default MCP registration runs only after its authenticated
endpoint is live, preserves other server entries/settings, and refuses invalid
JSON or conflicting managed TOML tables. Stdio clients use the bundled helper.
VS Code, Zed and Windsurf/Devin configuration references were checked on
2026-10-02:

- <https://code.visualstudio.com/docs/agents/reference/mcp-configuration>
- <https://zed.dev/docs/ai/mcp>
- <https://github.com/zed-industries/zed/blob/main/crates/paths/src/paths.rs>
- <https://docs.devin.ai/desktop/cascade/mcp>
- <https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution>
