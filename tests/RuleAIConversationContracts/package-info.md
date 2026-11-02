# Rule AI conversation contracts

- `RuleAIConversationContracts.csproj` links the production Windows AI service and transcript store into a portable .NET 8 executable for mini1.
- `Program.cs` exercises persistence, link collision, model switching, same-name ordering and different-name concurrency with a credential-free worker fixture.
