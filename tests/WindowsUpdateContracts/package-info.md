# Windows updater contract tests

- `WindowsUpdateContracts.csproj` links the production metadata verifier into a small .NET 8 fixture.
- `Program.cs` checks a valid P-256 signed feed, rejection paths, exact installer staging, preference persistence, and corrupt download cleanup.
