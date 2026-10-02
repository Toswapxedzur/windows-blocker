# Shared Classifier worker

Windows Vault bundles the production Swift Classifier service alongside the .NET
host at `ClassifierWorker/VaultClassifierWorker.exe`. Its scene assets and
translations are generated from Mac Vault. The worker hosts the same action,
MCP, snapshot, tagging/correction, research, model-download and inference code.
The six canonical Activity sources are shared by both native platforms.

Build and test on mini1's Windows VM. Prerequisites are official Swift 6.4.0,
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
The bundle includes the pinned runtime and resolved dependency licenses in
`Notices/`. No model or user
state is copied into the app bundle. Model downloads remain explicit scene
actions, just as on Mac. All port tests use isolated support directories.

Run `test-worker.ps1 -BundleDirectory <bundle>` on mini1's guest after building.
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
Each response is a complete JSON frame, limited to 16 MiB. Oversized responses
return an explicit error with the request ID rather than partial data. Background
event refusals report an `error` event. UI snapshots omit native-paged knowledge
collections; the explicitly requested MCP `all` section retains full public state.

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

On shutdown the host requests `hostEvent/flush`, closes stdin and waits briefly
before terminating an unresponsive worker. The executable always starts the full
production engine/research service. Unit tests inject their hermetic engines
directly through the shared service initializer. Process fixtures run the actual
production worker with isolated host support paths and no provider consent.
