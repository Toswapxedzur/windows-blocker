using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using WindowsBlocker.Core;
using WindowsBlocker.WebUI;

namespace WindowsBlocker.Bridge;

public sealed partial class ConnectionHub
{
    // Uses the existing trusted policy worker, never a rule's user-code worker.
    public Func<string, object?[], Task<JsonObject?>>? RuleTimePolicy { get; set; }
    private readonly SemaphoreSlim _timeOperations = new(1);
    private readonly HashSet<(Guid Peer, string Request)> _timeRequests = new();

    private static JsonObject TimeObjects(JsonObject? seed)
    {
        if (seed == null) return new();
        if (Number(seed["version"]) != 1 || seed["objects"] is not JsonObject objects || objects.Count > 32 || Encoding.UTF8.GetByteCount(seed.ToJsonString()) > 65536)
            throw new InvalidDataException("invalid-custom-time");
        foreach (var (id, raw) in objects)
        {
            if (!Regex.IsMatch(id, "^[A-Za-z0-9_-]{1,80}$") || id is "__proto__" or "prototype" or "constructor" || raw is not JsonObject value) throw new InvalidDataException("invalid-custom-time");
            var kind = Text(value["kind"]);
            if (kind == "timer")
            {
                var direction = Text(value["direction"]);
                if (direction is not ("up" or "down") || Number(value["durationMs"]) < (direction == "up" ? 0 : 1)) throw new InvalidDataException("invalid-custom-time");
            }
            else if (kind != "rolling" || Number(value["windowMs"]) <= 0 || Number(value["limitMs"]) < 0) throw new InvalidDataException("invalid-custom-time");
        }
        return objects;
    }

    private static bool PrepareTimeLink(Cluster cluster, string program, string groupId, JsonObject? own, string otherProgram, string otherGroupId, JsonObject? other)
    {
        try
        {
            var objects = cluster.SharedRuleTime?["objects"] as JsonObject ?? new();
            var combined = (JsonObject)objects.DeepClone();
            var seeds = (JsonArray)cluster.TimeSeeds.DeepClone();
            foreach (var previous in seeds.OfType<JsonObject>()) foreach (var (id, value) in TimeObjects(previous["state"] as JsonObject)) combined[id] = value?.DeepClone();
            foreach (var (peer, id, seed) in new[] { (program, groupId, own), (otherProgram, otherGroupId, other) })
            {
                if (cluster.Members.ContainsKey(peer)) continue;
                var incoming = TimeObjects(seed);
                foreach (var (objectId, node) in incoming)
                {
                    var value = (JsonObject)node!;
                    if (combined[objectId] is JsonObject old)
                    {
                        var fields = Text(value["kind"]) == "timer" ? new[] { "kind", "direction", "durationMs" } : new[] { "kind", "windowMs", "limitMs" };
                        if (fields.Any(field => !JsonNode.DeepEquals(old[field], value[field]))) return false;
                    }
                    else combined[objectId] = value.DeepClone();
                }
                seeds.Add(new JsonObject { ["member"] = peer + "/" + id, ["state"] = seed?.DeepClone() ?? new JsonObject { ["version"] = 1, ["objects"] = new JsonObject() } });
            }
            if (combined.Count > 32) return false;
            cluster.TimeSeeds = seeds;
            return true;
        }
        catch { return false; }
    }

    private async Task HandleRuleTimeAsync(Peer peer, JsonObject frame)
    {
        var requestId = Text(frame["requestID"]);
        var groupId = Text(frame["groupId"]);
        if (requestId.Length is < 1 or > 128 || requestId.Any(c => c < 0x21 || c > 0x7e) || frame["commands"] is not JsonArray commands || commands.Count > 64) return;
        lock (_gate)
        {
            if (_timeRequests.Count >= 32 || !_timeRequests.Add((peer.Id, requestId))) return;
        }
        try
        {
            var body = await SyncRuleTimeAsync(peer.Program, groupId, Text(frame["clusterId"]), frame["seed"] as JsonObject, commands, frame["usage"] as JsonObject, peer.Id);
            await SendAsync(peer, new() { ["kind"] = "rule-time-response", ["requestID"] = requestId, ["groupId"] = groupId, ["body"] = body });
        }
        finally { lock (_gate) _timeRequests.Remove((peer.Id, requestId)); }
    }

    public async Task<JsonObject> SyncRuleTimeAsync(string program, string groupId, string? clusterId, JsonObject? seed, JsonArray commands, JsonObject? usage = null, Guid? originPeer = null)
    {
        await _timeOperations.WaitAsync();
        try
        {
            Cluster cluster;
            string membership;
            JsonObject request;
            JsonObject? previous;
            JsonObject Unavailable(string id, string error = "") => new() { ["linked"] = true, ["ready"] = false, ["clusterId"] = id, ["error"] = error };
            bool LivePeer() => !originPeer.HasValue || _peers.TryGetValue(originPeer.Value, out var peer) && peer.Connected && peer.Program == program;
            lock (_gate)
            {
                var current = Find(program, groupId);
                if (current == null) return !string.IsNullOrEmpty(Text(seed?["scope"])) ? Unavailable(Text(seed?["scope"])) : new() { ["linked"] = false };
                cluster = current;
                if (!LivePeer() || !_clusterStorageWritable || _editorStorageHeld || _editorStage != null || RuleTimePolicy == null || commands.Count > 64 || !string.IsNullOrEmpty(clusterId) && clusterId != cluster.Id) return Unavailable(cluster.Id);
                if (Encoding.UTF8.GetByteCount(commands.ToJsonString()) + Encoding.UTF8.GetByteCount(seed?.ToJsonString() ?? "{}") + Encoding.UTF8.GetByteCount(usage?.ToJsonString() ?? "{}") > 96000) return Unavailable(cluster.Id, "custom-time-request-too-large");
                var enabled = cluster.Scalars["enabled"]?.GetValue<bool>() ?? true;
                if (!enabled && commands.OfType<JsonObject>().Any(c => Text(c["op"]) is not ("ensure" or "assign"))) return Unavailable(cluster.Id);
                membership = string.Join("|", cluster.Members.OrderBy(m => m.Key).Select(m => m.Key + "/" + m.Value));
                var initiator = cluster.Members.ContainsKey(cluster.Initiator) ? cluster.Initiator : cluster.Members.Keys.Order().First();
                request = new() { ["member"] = program + "/" + groupId, ["members"] = new JsonArray(cluster.Members.Select(m => (JsonNode?)JsonValue.Create(m.Key + "/" + m.Value)).ToArray()),
                    ["initiator"] = initiator + "/" + cluster.Members[initiator], ["seeds"] = cluster.TimeSeeds.DeepClone(), ["seed"] = seed?.DeepClone() ?? new JsonObject { ["version"] = 1, ["objects"] = new JsonObject() },
                    ["commands"] = new JsonArray(commands.OfType<JsonObject>().Where(command => Text(command["scope"]) == cluster.Id).Select(command => (JsonNode?)command.DeepClone()).ToArray()),
                    ["usage"] = enabled ? usage?.DeepClone() : null, ["nowMs"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
                previous = cluster.SharedRuleTime == null ? null : (JsonObject)cluster.SharedRuleTime.DeepClone();
            }
            JsonObject? result;
            try { result = await RuleTimePolicy!("syncTime", new object?[] { previous, request }); }
            catch { return Unavailable(cluster.Id, "custom-time-runtime-unavailable"); }
            lock (_gate)
            {
                if (!LivePeer() || !_clusterStorageWritable || !ReferenceEquals(Find(program, groupId), cluster) || membership != string.Join("|", cluster.Members.OrderBy(m => m.Key).Select(m => m.Key + "/" + m.Value))) return Unavailable(cluster.Id, "custom-time-membership-changed");
                if (result?["ok"]?.GetValue<bool>() != true || result["state"] is not JsonObject state) return Unavailable(cluster.Id, Text(result?["error"]));
                if (!JsonNode.DeepEquals(state, cluster.SharedRuleTime))
                {
                    var before = cluster.SharedRuleTime;
                    cluster.SharedRuleTime = (JsonObject)state.DeepClone();
                    Persist();
                    try
                    {
                        var saved = RegistryPayload(JsonNode.Parse(File.ReadAllText(Storage.ClustersPath))!).AsArray().OfType<JsonObject>().FirstOrDefault(c => Text(c["Id"]) == cluster.Id);
                        if (!_clusterStorageWritable || !JsonNode.DeepEquals(saved?["SharedRuleTime"], state)) throw new IOException("custom-time-save-failed");
                    }
                    catch { cluster.SharedRuleTime = before; _clusterStorageWritable = false; return Unavailable(cluster.Id, "custom-time-save-failed"); }
                    Broadcast(new() { ["kind"] = "cluster-updated", ["cluster"] = Snapshot(cluster) });
                }
                return new() { ["linked"] = true, ["ready"] = result["ready"]?.DeepClone() ?? JsonValue.Create(false), ["clusterId"] = cluster.Id,
                    ["state"] = state.DeepClone(), ["acknowledgedSerial"] = result["acknowledgedSerial"]?.DeepClone() ?? JsonValue.Create(0) };
            }
        }
        finally { _timeOperations.Release(); }
    }
}
