using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WindowsBlocker.Core;
using WindowsBlocker.WebUI;

namespace WindowsBlocker.Bridge;

public sealed partial class ConnectionHub
{
    private sealed record EditorState(Dictionary<string, Cluster> Clusters,
        Dictionary<string, JsonArray> Rosters, Dictionary<string, LocalDefinition> Seen,
        HashSet<string> Unlinked, JsonObject? Rejection, bool Writable);
    private sealed class EditorStage
    {
        public readonly List<(Peer[] Recipients, JsonObject Frame)> Frames = new();
    }
    private EditorStage? _editorStage;
    private bool _editorStorageHeld;
    internal sealed record EditorCapture(JsonObject Root, JsonArray Groups,
        JsonObject Hub, byte[] CanonicalBytes, byte[]? RegistryBytes, HashSet<string> LinkedIds);

    private EditorState EditorStateNow() => new(new(_clusters), new(_rosters),
        new(_seenDefinitions), new(_unlinkedLocal), _rejection, _clusterStorageWritable);
    private static EditorState CloneEditorState(EditorState state) => new(
        state.Clusters.ToDictionary(p => p.Key, p => JsonSerializer.Deserialize<Cluster>(JsonSerializer.Serialize(p.Value))!),
        state.Rosters.ToDictionary(p => p.Key, p => (JsonArray)p.Value.DeepClone()),
        state.Seen.ToDictionary(p => p.Key, p => new LocalDefinition((JsonObject)p.Value.Scalars.DeepClone(),
            (JsonArray)p.Value.Scopes.DeepClone(), (JsonObject)p.Value.Lock.DeepClone())),
        new(state.Unlinked), (JsonObject?)state.Rejection?.DeepClone(), state.Writable);
    private void InstallEditorState(EditorState state)
    {
        _clusters.Clear(); foreach (var p in state.Clusters) _clusters.Add(p.Key, p.Value);
        _rosters.Clear(); foreach (var p in state.Rosters) _rosters.Add(p.Key, p.Value);
        _seenDefinitions.Clear(); foreach (var p in state.Seen) _seenDefinitions.Add(p.Key, p.Value);
        _unlinkedLocal.Clear(); foreach (var id in state.Unlinked) _unlinkedLocal.Add(id);
        _rejection = state.Rejection; _clusterStorageWritable = state.Writable;
    }
    private JsonObject EditorFingerprint() => new()
    {
        ["clusters"] = JsonSerializer.SerializeToNode(_clusters),
        ["rosters"] = JsonSerializer.SerializeToNode(_rosters),
        ["seen"] = JsonSerializer.SerializeToNode(_seenDefinitions),
        ["unlinked"] = new JsonArray(_unlinkedLocal.Order(StringComparer.Ordinal).Select(id => (JsonNode)JsonValue.Create(id)!).ToArray()),
        ["rejection"] = _rejection?.DeepClone(), ["writable"] = _clusterStorageWritable,
        ["peers"] = PeerList()
    };
    private static bool SameBytes(byte[]? a, byte[]? b) => a == null ? b == null : b != null && a.AsSpan().SequenceEqual(b);
    private static JsonNode RegistryFromBytes(byte[] bytes)
    {
        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, true);
        try
        {
            var payload = RegistryPayload(JsonNode.Parse(reader.ReadToEnd()) ?? throw new InvalidDataException("Invalid cluster registry."));
            if (payload is not JsonArray entries || entries.Any(entry => entry is not JsonObject)) throw new InvalidDataException("Invalid cluster registry.");
            // Exercise the same typed registry representation before any write,
            // rather than overwriting malformed nested field types.
            _ = payload.Deserialize<List<Cluster>>() ?? throw new InvalidDataException("Invalid cluster registry.");
            return payload;
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or OverflowException)
        { throw new InvalidDataException("Invalid cluster registry; saved data is preserved.", ex); }
    }
    private static byte[]? ReadEditorRegistry()
    {
        if (!File.Exists(Storage.ClustersPath)) return null;
        var bytes = File.ReadAllBytes(Storage.ClustersPath); _ = RegistryFromBytes(bytes); return bytes;
    }
    private void RequireNoPartialStorage()
    {
        if (_editorStorageHeld) throw new InvalidOperationException("editor-storage-partial-failure; storage repair required");
    }
    private void RequireEditorStorage()
    {
        RequireNoPartialStorage();
        if (!_clusterStorageWritable) throw new InvalidDataException("unsupported-cluster-storage; saved data is preserved");
    }
    internal EditorCapture CaptureEditor(WebStore store)
    {
        AttachLocalStore(store);
        EditorCapture? capture = null;
        store.WithLockedDocument((root, _) => { lock (_gate)
        {
            RequireEditorStorage();
            var registry = ReadEditorRegistry(); var original = EditorStateNow();
            var fingerprint = EditorFingerprint(); var canonical = File.ReadAllBytes(store.FilePath);
            var effective = (JsonObject)root.DeepClone();
            InstallEditorState(CloneEditorState(original)); _editorStage = new();
            try
            {
                ReconcileLocalLocked(effective, () => { });
                capture = new((JsonObject)root.DeepClone(), (JsonArray)effective["blockedGroups"]!.DeepClone(),
                    fingerprint, canonical, registry, _clusters.Values.Where(c => c.Members.ContainsKey(LocalProgram)).Select(c => c.Members[LocalProgram]).ToHashSet());
            }
            finally { _editorStage = null; InstallEditorState(original); }
        }});
        return capture!;
    }
    internal JsonArray CommitEditor(WebStore store, EditorCapture capture, JsonArray acceptedGroups, IReadOnlySet<string>? budgetRestarts = null)
    {
        JsonArray? reply = null;
        store.WithLockedDocument((root, save) => { lock (_gate)
        {
            RequireEditorStorage();
            if (!JsonNode.DeepEquals(root, capture.Root) || !JsonNode.DeepEquals(EditorFingerprint(), capture.Hub) ||
                !SameBytes(File.ReadAllBytes(store.FilePath), capture.CanonicalBytes) || !SameBytes(ReadEditorRegistry(), capture.RegistryBytes))
                throw new InvalidOperationException("store-changed; retry edit");
            var original = EditorStateNow(); var stage = new EditorStage();
            InstallEditorState(CloneEditorState(original)); _editorStage = stage;
            try
            {
                ReconcileLocalLocked(root, () => { });
                var oldIds = ((JsonArray)root["blockedGroups"]!).OfType<JsonObject>().Select(g => Text(g["id"])).ToHashSet();
                root["blockedGroups"] = acceptedGroups.DeepClone();
                WebStore.ValidateCanonicalDocument(root);
                var newIds = acceptedGroups.OfType<JsonObject>().Select(g => Text(g["id"])).ToHashSet();
                foreach (var deleted in oldIds.Except(newIds))
                {
                    WebStore.RemoveGroupMaps(root, deleted);
                    if (Text(root["quickAddGroupId"]) == deleted) root["quickAddGroupId"] = "";
                }
                foreach (var id in budgetRestarts ?? new HashSet<string>())
                {
                    if (capture.LinkedIds.Contains(id)) throw new InvalidOperationException("linked-budget-owned-by-hub");
                    foreach (var key in new[] { "usageTimersMs", "usageResetAtMs", "usageBucketsMs", "groupSnoozes" })
                        if (root[key] is JsonObject map) map.Remove(id);
                }
                ReconcileLocalLocked(root, () => { });
                var staged = EditorStateNow();
                var registry = StorageSchema.Wrap(JsonSerializer.SerializeToNode(_clusters.Values)!, "hub.clusters").ToJsonString();
                try
                {
#if VAULT_CONTRACT_TESTS
                    EditorDiskPhaseForTests?.Invoke("canonical-save");
#endif
                    save();
#if VAULT_CONTRACT_TESTS
                    EditorDiskPhaseForTests?.Invoke("registry-save");
#endif
                    StorageSchema.AtomicWrite(Storage.ClustersPath, registry);
                }
                catch (Exception commitError)
                {
                    var rollbackErrors = new List<Exception>();
                    try
                    {
#if VAULT_CONTRACT_TESTS
                        EditorDiskPhaseForTests?.Invoke("canonical-rollback");
#endif
                        if (!SameBytes(File.ReadAllBytes(store.FilePath), capture.CanonicalBytes)) store.RestoreValidatedBytes(capture.CanonicalBytes);
                    }
                    catch (Exception ex) { rollbackErrors.Add(ex); }
                    try
                    {
#if VAULT_CONTRACT_TESTS
                        EditorDiskPhaseForTests?.Invoke("registry-rollback");
#endif
                        var current = ReadEditorRegistry();
                        if (!SameBytes(current, capture.RegistryBytes))
                        {
                            if (capture.RegistryBytes == null) File.Delete(Storage.ClustersPath);
                            else AtomicRegistryBytes(capture.RegistryBytes);
                        }
                    }
                    catch (Exception ex) { rollbackErrors.Add(ex); }
                    if (rollbackErrors.Count > 0)
                    {
                        _editorStorageHeld = true; store.HoldEditorWrites();
                        throw new AggregateException("editor-storage-partial-failure; storage repair required", new[] { commitError }.Concat(rollbackErrors));
                    }
                    throw new IOException("editor-storage-save-failed; original storage restored", commitError);
                }
                // No asynchronous work, acknowledgments or sends run before both
                // ordinary writes succeed. This is not crash-atomic across files.
                _editorStage = null; InstallEditorState(staged);
                foreach (var frame in stage.Frames) foreach (var peer in frame.Recipients) _ = SendAsync(peer, frame.Frame);
                reply = (JsonArray)root["blockedGroups"]!.DeepClone();
            }
            catch { _editorStage = null; InstallEditorState(original); throw; }
            finally { _editorStage = null; }
        }});
        return reply!;
    }
    private static void AtomicRegistryBytes(byte[] bytes)
    {
        _ = RegistryFromBytes(bytes);
        var temporary = Storage.ClustersPath + ".tmp"; File.WriteAllBytes(temporary, bytes);
        if (File.Exists(Storage.ClustersPath)) File.Replace(temporary, Storage.ClustersPath, null); else File.Move(temporary, Storage.ClustersPath);
    }
#if VAULT_CONTRACT_TESTS
    internal Action<string>? EditorDiskPhaseForTests;
#endif
}
