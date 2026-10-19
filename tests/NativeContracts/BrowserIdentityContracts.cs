using System.Collections;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using WindowsBlocker.Bridge;

static class BrowserIdentityContracts
{
    static void Check(bool ok, string label) { if (!ok) throw new Exception(label); Console.WriteLine("PASS " + label); }
    static readonly byte[] Secret = Enumerable.Range(0, 32).Select(n => (byte)n).ToArray();
    static readonly string Challenge = new('c', 43);
    static JsonObject Hello(string challenge, byte[] secret, bool identity = true, string value = "")
    {
        value = value.Length == 0 ? new('a', 64) : value;
        var hello = new JsonObject { ["kind"] = "hello", ["v"] = 4, ["program"] = "chrome", ["challenge"] = challenge,
            ["proof"] = LocalHubAuthentication.Proof("chrome", challenge, secret) };
        if (identity)
        {
            hello["browserIdentityVersion"] = 1;
            hello["browserIdentity"] = new JsonObject { ["v"] = 1, ["kind"] = LocalHubAuthentication.BrowserIdentityKind, ["value"] = value,
                ["proof"] = LocalHubAuthentication.BrowserIdentityProof("chrome", challenge, LocalHubAuthentication.BrowserIdentityKind, value, secret) };
        }
        return hello;
    }
    public static async Task Run()
    {
        var valid = Hello(Challenge, Secret);
        Check(ConnectionHub.HelloRejectionReason(valid, Challenge, Secret, 0) == null && ConnectionHub.HelloRejectionReason(valid, Challenge, Secret, 2) == null,
            "Updated identity authenticates independently of the legacy retirement major");
        foreach (var major in new[] { 0, 1 }) Check(ConnectionHub.HelloRejectionReason(Hello(Challenge, Secret, false), Challenge, Secret, major) == null, "Legacy v4 remains supported at Windows app major " + major);
        foreach (var major in new[] { -1, 2, 3 }) Check(ConnectionHub.HelloRejectionReason(Hello(Challenge, Secret, false), Challenge, Secret, major) == "browser-identity-upgrade-required", "Legacy metadata expires using authoritative local major " + major);
        var legacy = Hello(Challenge, Secret, false); legacy["localAppMajor"] = 0;
        Check(ConnectionHub.HelloRejectionReason(legacy, Challenge, Secret, 2) == "browser-identity-upgrade-required", "A remote major claim cannot extend the legacy window");
        foreach (var field in new[] { "browserIdentityVersion", "browserIdentity" })
        {
            var partial = (JsonObject)valid.DeepClone(); partial.Remove(field);
            Check(ConnectionHub.HelloRejectionReason(partial, Challenge, Secret, 0) == "invalid-browser-identity", "Partial new attestation cannot downgrade after missing " + field);
        }
        foreach (var bad in new JsonNode?[] { null, JsonValue.Create(true), JsonValue.Create(1.5), JsonValue.Create(1.0), JsonValue.Create("1"), JsonValue.Create(2) })
        {
            foreach (var field in new[] { "capability", "descriptor" })
            {
                var malformed = (JsonObject)valid.DeepClone();
                if (field == "capability") malformed["browserIdentityVersion"] = bad?.DeepClone(); else malformed["browserIdentity"]!["v"] = bad?.DeepClone();
                Check(ConnectionHub.HelloRejectionReason(malformed, Challenge, Secret, 0) == "invalid-browser-identity", "New " + field + " requires strict integer one: " + (bad?.ToJsonString() ?? "null"));
            }
        }
        foreach (var (field, value) in new[] { ("kind", "mac-bundle-sha256"), ("value", new string('A', 64)), ("value", new string('a', 63)), ("value", new string('a', 64) + "\n"), ("proof", new string('p', 42)), ("proof", new string('p', 43) + "\n") })
        {
            var bad = (JsonObject)valid.DeepClone(); bad["browserIdentity"]![field] = value;
            Check(ConnectionHub.HelloRejectionReason(bad, Challenge, Secret, 0) == "invalid-browser-identity", "Malformed platform/hash/proof refuses without legacy fallback: " + field + "/" + value.Length);
        }
        var extra = (JsonObject)valid.DeepClone(); extra["browserIdentity"]!["path"] = @"C:\untrusted\chrome.exe";
        Check(ConnectionHub.HelloRejectionReason(extra, Challenge, Secret, 0) == "invalid-browser-identity", "Descriptor shape does not accept a browser-supplied path");
        var altered = (JsonObject)valid.DeepClone(); altered["browserIdentity"]!["value"] = new string('b', 64);
        Check(ConnectionHub.HelloRejectionReason(altered, Challenge, Secret, 0) == "authentication-failed", "Changing the exact executable identity invalidates its independent proof");
        var wrongProgram = (JsonObject)valid.DeepClone(); wrongProgram["program"] = "edge"; wrongProgram["proof"] = LocalHubAuthentication.Proof("edge", Challenge, Secret);
        Check(ConnectionHub.HelloRejectionReason(wrongProgram, Challenge, Secret, 0) == "authentication-failed", "Browser attestation remains bound to its own program after a valid replacement base proof");
        var wrongChallenge = Hello(new string('d', 43), Secret); wrongChallenge["browserIdentity"] = valid["browserIdentity"]!.DeepClone();
        Check(ConnectionHub.HelloRejectionReason(wrongChallenge, new string('d', 43), Secret, 0) == "authentication-failed", "An old browser attestation cannot cross a fresh challenge");
        var noBase = (JsonObject)valid.DeepClone(); noBase.Remove("proof");
        Check(ConnectionHub.HelloRejectionReason(noBase, Challenge, Secret, 0) == "authentication-failed", "New browser identity never replaces the mandatory v4 proof");
        Check(LocalHubAuthentication.BrowserIdentityProof("chrome", Challenge, LocalHubAuthentication.BrowserIdentityKind, new string('a', 64), Secret) ==
            "WVhUUpyJbjg1gO9fQ10R0DSv7_LNU4msYIDyS8THz8o", "Browser identity canonical HMAC has exact bytes with no trailing newline");
        await PeerLifecycle();
    }

    sealed class Socket : WebSocket
    {
        readonly Channel<byte[]> input = Channel.CreateUnbounded<byte[]>();
        readonly List<JsonObject> frames = [];
        public readonly TaskCompletionSource<JsonObject> ChallengeReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<JsonObject> WelcomeReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override WebSocketState State => WebSocketState.Open;
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override string? SubProtocol => null;
        public override void Abort() { }
        public override void Dispose() { input.Writer.TryComplete(); }
        public override Task CloseAsync(WebSocketCloseStatus status, string? reason, CancellationToken token) { input.Writer.TryComplete(); return Task.CompletedTask; }
        public override Task CloseOutputAsync(WebSocketCloseStatus status, string? reason, CancellationToken token) { input.Writer.TryComplete(); return Task.CompletedTask; }
        public void Feed(JsonObject message) => input.Writer.TryWrite(Encoding.UTF8.GetBytes(message.ToJsonString()));
        public void Finish() => input.Writer.TryComplete();
        public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken token)
        {
            var bytes = await input.Reader.ReadAsync(token); bytes.CopyTo(buffer.AsSpan()); return new(bytes.Length, WebSocketMessageType.Text, true);
        }
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType type, bool end, CancellationToken token)
        {
            var message = (JsonObject)JsonNode.Parse(buffer.AsSpan())!; lock (frames) frames.Add(message);
            if (message["kind"]?.GetValue<string>() == "challenge") ChallengeReady.TrySetResult(message);
            if (message["kind"]?.GetValue<string>() == "welcome") WelcomeReady.TrySetResult(message);
            return Task.CompletedTask;
        }
    }
    static async Task PeerLifecycle()
    {
        var hub = new ConnectionHub(); var socket = new Socket();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(ConnectionHub).GetNestedType("Peer", BindingFlags.NonPublic)!;
        object Peer(Socket s) => Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [s], null)!;
        var peer = Peer(socket); var dictionary = (IDictionary)typeof(ConnectionHub).GetField("_peers", flags)!.GetValue(hub)!;
        dictionary.Add(type.GetField("Id")!.GetValue(peer)!, peer);
        var receive = (Task)typeof(ConnectionHub).GetMethod("ReceiveAsync", flags)!.Invoke(hub, [peer])!;
        try
        {
            Check(type.GetField("BrowserIdentity")!.GetValue(peer) == null, "A new unauthenticated peer has no executable identity");
            var challenge = (await socket.ChallengeReady.Task.WaitAsync(TimeSpan.FromSeconds(5)))["challenge"]!.GetValue<string>();
            socket.Feed(Hello(challenge, LocalHubAuthentication.Secret()));
            var welcome = await socket.WelcomeReady.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Check(welcome["v"]!.GetValue<int>() == 4 && welcome["browserIdentityVersion"]!.GetValue<int>() == 1, "Actual authenticated welcome keeps v4 and advertises identity capability one");
            Check(type.GetField("BrowserIdentity")!.GetValue(peer) is LocalHubAuthentication.VerifiedBrowserIdentity { Value: var value } && value == new string('a', 64), "Actual receive path installs only the verified transient descriptor");
            Check(!hub.CurrentStatusJson().Contains(new string('a', 64), StringComparison.Ordinal), "Private executable hash is not broadcast in the global peer list");
        }
        finally { socket.Finish(); await receive.WaitAsync(TimeSpan.FromSeconds(5)); }
        Check(type.GetField("BrowserIdentity")!.GetValue(peer) == null && !(bool)type.GetField("Connected")!.GetValue(peer)! && dictionary.Count == 0, "Disconnect removes the authenticated executable identity and peer");
        var replacement = Peer(new Socket());
        Check(type.GetField("BrowserIdentity")!.GetValue(replacement) == null && !File.Exists(WindowsBlocker.WebUI.Storage.ClustersPath), "A reconnect begins without stale identity and authentication persists no cluster schema");
        ((Socket)type.GetField("Socket")!.GetValue(replacement)!).Dispose();
    }
}
