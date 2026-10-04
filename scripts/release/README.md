# Windows Vault package

Build and verify on mini1's Windows VM. `package-windows-vault.ps1` requires the
bundled shared Classifier worker produced by `scripts/classifier-worker/` and
checks its dependency hashes before publishing a self-contained x64 .NET app
and native authentication/MCP helper. Exact .NET, WebView2 and worker dependency
notices are included in the hashed payload. End users need no .NET SDK, Swift, Node or
compiler. The current alpha build is distributed as an unsigned Setup.exe;
Windows can show an unknown-publisher or SmartScreen warning.

Open Setup.exe as your normal user. It installs for your account and then offers
to open Windows Vault. No extraction or terminal command is required.

For an internal ZIP build, extract it and run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install.ps1
```

This execution-policy option applies only to that process. The installer writes
to the current user's application directory, registers the browser native host in HKCU and
adds a Start menu shortcut. If Microsoft WebView2 is missing, it downloads and
verifies Microsoft's signed Evergreen bootstrapper before installing the
runtime. It needs an internet connection only for that missing dependency.
Use `-Environment development` for the separate development profile. Close the
app and connected browser/MCP clients before updating its installation. User data and downloaded
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

Build Setup.exe with `build-setup.ps1 -PackageZip <verified ZIP> -OutputExe <new EXE>`.
It embeds the exact ZIP plus its SHA-256, validates both archive paths and
installer payload hashes, and delegates to the same current-user installer.
Advanced unattended installation supports `/quiet`, `/environment:development`
and `/destination:<absolute path>`; it does not launch the app and returns a
nonzero exit code on failure. The GUI retains a diagnostic log on failure.
