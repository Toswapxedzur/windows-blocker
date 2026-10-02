# Native folder broker contracts

- `Program.cs` exercises the production broker against owned normal-user Windows files: selected root junctions, existing/dangling interior junctions, PowerShell-style UTF-8 BOM text/JSON, sharing errors and revocation.
- `FolderBrokerContracts.csproj` builds the fixture against the actual Windows native app.
