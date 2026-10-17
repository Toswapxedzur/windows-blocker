# Native bridge code

The C# files here implement the worker JSONL client, native browser hub and authentication, loopback/MCP services, platform identities and their lifecycle helpers. `NativeStartup.cs` overlaps the first read-only worker settings request with WebView initialization and preserves Quit/error cleanup before later services start.
