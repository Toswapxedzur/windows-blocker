# Rule initialization contracts

- `Program.cs` runs the actual WebView2 worker and native `RuleEngine` against disposable storage: initialization state before events, repeated Run/restart, oversized/timeout rejection, and preserved saved bytes/previous worker under future, malformed, missing and failed-write storage. Native delete-group calls prove memory cleanup, prior-worker preservation after a failed write, and no resurrection by late events/ticks.
- `RuleInitializationContracts.csproj` references the real Windows app, not a runtime mock.
- Run through `scripts/development/test-rule-initialization.ps1` on mini1's Windows guest. The script uses an owned Limited interactive task, then closes the host and removes its GUID profile.
