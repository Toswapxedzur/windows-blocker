# Windows UI contracts

- `WindowsUiContracts.csproj`, `Program.cs` run actual normal-user WPF/WebView2, MCP, floating quick-add, native panel and selected-folder checks on mini1.
- `CloudFolderFixture.cs` registers only an owned temporary Cloud Files root, converts a test file and unregisters that root on disposal. No cloud account or network provider is used. The result explicitly records whether Windows exposed a real cloud reparse tag; an elided tag does not count as verified placeholder coverage.
