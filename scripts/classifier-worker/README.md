# Shared Classifier worker

Windows Vault bundles the production Swift Classifier service alongside the .NET
host at `ClassifierWorker/VaultClassifierWorker.exe`. Its scene assets and
translations are generated from Mac Vault. The worker hosts the same action,
MCP, snapshot, tagging/correction, research, model-download and inference code.
The eight canonical Activity sources are shared by both native platforms.

Build and test on Mini2 alt1's owned Windows VM. Prerequisites are official Swift 6.4.0,
Git, Visual Studio 2022 C++ x64 Build Tools and Windows SDK 22621. Swift's
installer supplies its runtime. The build script pins the llama.cpp revision
used by the current Mac package, builds portable CPU variants and bundles every
imported Swift/llama/VC runtime DLL; end users do not install a Swift toolchain.
This x64 build does not claim untested ARM64 or GPU support.

```powershell
.\scripts\classifier-worker\build-worker.ps1 `
  -ClassifierSource C:\vault-porting-classifier\source\classifier `
  -DependencyDirectory C:\vault-porting-classifier\dependencies `
  -OutputDirectory C:\vault-porting-host\src\WindowsBlocker\ClassifierWorker
```

`bundle-manifest.json` records versions and SHA-256 hashes of source and shipped
files. Pass `-ClassifierRevision <verified-commit>` when exporting without Git.
The development launcher and release packager refuse workers whose source
provenance predates the official-dictionary backend. Rebuild the worker from the
accepted shared Classifier source when delivering this feature; rebuilding WPF
alone does not update the worker. The same guard also checks the exact accepted
Activity store, admission, handler, ready-event and dispatch hashes in
`required-worker-sources.json`. File presence does not establish support.
When intentionally integrating changes to these shared files, update that table
from the reviewed immutable shared commit and verify every source hash in the
new bundle. Do not relabel an older bundle's revision or edit its manifest.
The bundle includes the pinned runtime and resolved dependency licenses in
`Notices/`. No model or user
state is copied into the app bundle. Model downloads remain explicit scene
actions, just as on Mac. All port tests use isolated support directories.

Run `test-worker.ps1 -BundleDirectory <bundle>` on Mini2's guest after building.
It verifies the manifest, copies the bundle to a fresh location, removes Swift
from the child's PATH, then checks the actual private pipe, UTF-8 actions,
taxonomy, scene callbacks, persisted activation/DPAPI and graceful shutdown.

`test-inference.py --bundle <bundle> --data-root <isolated-root> --evidence
<new-directory>` additionally starts the production service with app-local
runtimes and verifies actual 3B model inference, a browser tag broadcast,
cached tags and a user correction. Explicitly stage an existing
`models/Qwen2.5-3B-Instruct-Q4_K_M.gguf` in its fresh support root first; this
fixture does not download a model or enable provider research.

The .NET host starts the worker without elevation, owns its private stdin/stdout
pipe, supplies `VAULT_DATA_ROOT` and `VAULT_ENVIRONMENT`, validates WebView2
origins/navigation and authenticates browser hub peers. It sends bounded
JSON-lines `{id, operation, data}` and receives `{id, ok, value}` or
`{id, ok, error}`. Events include `ready`, `state`, `broadcast` and `activity`.
The worker does not listen on a network port or share another hub secret.
Its current `ready` event advertises `activitySettingsNotifications: true`.
The host scopes this capability to the live creator-owned worker session and
reads settings before opening the browser Hub. Worker replacement/exit normally
closes authenticated browser sockets so a fresh welcome and settings GET use
the new store epoch. An older worker without the ready flag stays unsupported.
The native-only `activity-settings-committed` event carries the exact successful
commit's `settings`, `epoch`, `revision` and `changedAtMs`; the host relays it as
`activity-settings-updated` only to authenticated browsers, including the writer.
Settings replies preserve the same metadata but do not trigger another push.
Browser record replies retain `stored` plus `acceptedIDs`, `rejectedIDs` and
`pendingIDs`; persisted duplicates can be accepted without adding to `stored`.
Each response is a complete JSON frame, limited to 16 MiB. Oversized responses
return an explicit error with the request ID rather than partial data. Background
event refusals report an `error` event. UI snapshots omit native-paged knowledge
collections; the explicitly requested MCP `all` section retains full public state.

Normal app Quit stops new producer/client admissions, writes already admitted
frames before closing the private stdin pipe, then waits for the exact child.
The shared worker's EOF shutdown owns cooperative classifier cancellation and
Activity/state flush. Native shutdown reports clean exit, failure, and actual
forced fallback separately; a timed-out or forced worker must not count as
successful persistence acceptance. `WorkerShutdownOrderContracts` is the same
EOF-order runner for the prior and current client; it deliberately stalls any
pre-EOF flush reply and checks that admitted observations survive EOF.

For isolated .NET source fixtures, set `DOTNET_GENERATE_ASPNET_CERTIFICATE=false`
before the first SDK invocation, alongside a private `DOTNET_CLI_HOME`, package
cache and temporary root. A private CLI home alone does not prevent the SDK's
first-run development-certificate setup in the user's certificate store.

Operations:

- `snapshot`: current Classifier scene state.
- `action`: `{action, data}` using the existing bounded scene dispatcher.
  `knowledgePage` returns `{list}` for `receiveList`; editing a knowledge entry
  returns `{knowledgeRow}` for `receiveKnowledgeRow`, alongside any snapshot.
- `mcp`: `{kind: "state"|"action"|"actions", section?, action?, data?}`.
- `hub`: `{sourcePeerID, requestID, operation, body}`, response `{body}` or `{error}`.
- `tagNames`: `{platform}`, returns names from the current taxonomy.
- `resource`: `{url}`, only the private `vaultclassifiersourceicon://` cache,
  response `{contentType, dataBase64}`. Windows snapshots map these URLs to the
  host's private `https://appassets.windowsblocker/worker-resource?url=...` route.
- `activity`: existing scene `{kind,...}` messages. Snapshot replies contain
  `snapshot`, `icons`, `facts`, `collection`, `tags`, `contentSnapshot`; history,
  group-save and known-items return the corresponding callback payload.
  `browser-record` uses the canonical browser category validator; native-only
  `native-sample` accrues monotonic foreground app time. Recording stays opt-in.
- `hostEvent`: `{kind:"flush"}` flushes native Activity and queued state writes.

On shutdown the host stops admitting producers, drains already admitted JSONL
writes, closes stdin, and waits for the exact child to exit. It does not request
a flush reply before EOF. An unresponsive child is terminated only as an
explicitly reported abnormal fallback. The executable always starts the full
production engine/research service. Unit tests inject their hermetic engines
directly through the shared service initializer. Process fixtures run the actual
production worker with isolated host support paths and no provider consent.

C# transport validation on Mini2 alt1 (2026-10-11, source `fe39502`): the
identical EOF-order fixture fails on `4af20cf` and passes on the candidate with
all 32 admitted observations; 11 worker-client controls, 236 native contracts,
35 MCP contracts and 29 packaging guards pass. WPF and the native host compile
without warnings/errors. These hermetic transport/build checks do not establish
normal Quit, recording-OFF or inference cancellation with a rebuilt shared Swift
worker; that installed acceptance remains separate.

Activity notification source validation on Mini2 alt1 (2026-10-13): the focused
Hub runner passes 32 contracts, the current-session worker-pipe runner passes
five checks using normal EOF exits, and the required-source table passes 17
valid/missing/hash-drift controls with three PowerShell parsers. NativeContracts,
WorkerFixture, WorkerClientContracts and the WPF host compile with zero warnings
or errors using the private .NET 8.0.425 SDK and one build job. The exact source
fixtures and their outputs are archived; no production app or shared Swift
worker was launched. Rebuilt-worker and authenticated live-browser acceptance
remain pending. These focused runs do not execute the legacy force/crash tests.
