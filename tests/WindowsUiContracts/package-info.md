# Windows UI contracts

- `WindowsUiContracts.csproj`, `Program.cs` run actual normal-user WPF/WebView2, MCP, floating quick-add, native panel and selected-folder checks on mini1.
- Snapshot polling retries only the brief Windows sharing lock during atomic replacement; malformed JSON and other IO errors remain test failures.
- Folder checks drive the actual chooser, canonicalize an explicitly selected junction root, reject existing/dangling interior junctions, decode Windows BOM files, sanitize sharing failures and verify immediate revocation.
- `CloudFolderFixture.cs` registers only an owned temporary Cloud Files root, converts a test file and unregisters that root on disposal. No cloud account or network provider is used. The result explicitly records whether Windows exposed a real cloud reparse tag; an elided tag does not count as verified placeholder coverage.
