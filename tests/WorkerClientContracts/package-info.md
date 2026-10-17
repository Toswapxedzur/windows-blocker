# Worker client contracts

- `WorkerClientContracts.csproj` links the product worker client and startup helper into the console contract suite.
- `Program.cs` checks framing, capabilities, FIFO requests and shutdown outcomes using the separate WorkerFixture executable.
- `StartupSettingsContracts.cs` checks delayed WebView overlap, settings-before-welcome ordering, early Quit, initialization failure cleanup and settings errors with real fixture process/EOF observations.
