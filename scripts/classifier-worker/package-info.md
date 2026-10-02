# classifier-worker

- `build-worker.ps1` — mini1 Windows source build of the shared Swift worker,
  pinned llama.cpp CPU backends, transitive DLL bundle and provenance manifest.
- `test-platform.ps1` / `platform-smoke.cpp` — mini1 guest checks for DPAPI, ACLs, image conversion and fail-closed package reclamation.
- `test-worker.ps1` — relocated bundle, private pipe protocol, shared scene
  callbacks, UTF-8, persisted activation/DPAPI and graceful shutdown checks.
- `test-inference.py` — production worker with an explicitly staged existing
  GGUF, real background tag broadcast/cache and authoritative user correction.
- `runtime-notices/` — original pinned official Swift runtime dependency
  licenses and their source references, copied into the hashed worker bundle.
- `README.md` — build prerequisites, ownership boundaries and private pipe
  operations shared with `Bridge/ClassifierWorkerClient.cs`.
- `package-info.md` — this direct-content map.

Canonical service source is `macosBlocker/classifier/`; generated worker binaries,
dependencies and user models are not committed to this repository.
