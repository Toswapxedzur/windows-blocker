# Native custom rules

- `CustomRuleModels.cs` defines the canonical raw-event runtime, action and panel wire shapes.
- `CustomRuleRuntime.cs` hosts isolated WebView2 rule workers and bounded policy requests.
- `RuleEngine.cs` imports active source/state and dispatches current native events.
- `LocalFolderGrant.cs` stores an explicit folder grant and brokers bounded text/JSON/CSV operations.
- `LocalFolderPathPolicy.cs` rejects Windows name-surrogate redirects while allowing ordinary Cloud Files placeholders.
