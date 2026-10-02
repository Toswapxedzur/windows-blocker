#!/usr/bin/env python3
"""Exercise the production worker with an existing, explicitly staged GGUF.

Run on mini1's Windows guest. The fixture does not download a model, enable
grounded research, send provider requests or use the worker's hermetic stub.
Its data directory must be new except for models/ and remains as evidence.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import queue
import subprocess
import threading
import time


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--bundle", type=Path, required=True)
    parser.add_argument("--data-root", type=Path, required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    args = parser.parse_args()
    bundle = args.bundle.resolve()
    data = args.data_root.resolve()
    evidence = args.evidence.resolve()
    evidence.mkdir(parents=True, exist_ok=False)
    if (data / "state.json").exists():
        raise RuntimeError("Use a fresh isolated data root; this fixture creates its own group.")
    model_name = "Qwen2.5-3B-Instruct-Q4_K_M.gguf"
    model = data / "models" / model_name
    if not model.is_file():
        raise RuntimeError("Stage the existing 3B GGUF under the fixture data root first.")
    manifest = json.loads((bundle / "bundle-manifest.json").read_text(encoding="utf-8-sig"))
    for entry in manifest["files"]:
        path = (bundle / entry["path"]).resolve()
        if bundle not in path.parents:
            raise RuntimeError("Bundle manifest path escapes its directory.")
        if hashlib.sha256(path.read_bytes()).hexdigest() != entry["sha256"]:
            raise RuntimeError("Bundle hash mismatch: " + entry["path"])
    env = dict(os.environ)
    env.update(VAULT_ENVIRONMENT="development", ADAMANCIA_VAULT_ENVIRONMENT="development",
               VAULT_DATA_ROOT=str(data), PATH=os.pathsep.join([
                   str(Path(env["WINDIR"]) / "System32"), env["WINDIR"]]))
    for key in ("SDKROOT", "VAULT_LLAMA_PREFIX", "ADAMANCIA_VAULT_LLM_MODEL", "ADAMANCIA_VAULT_TAG_TEST"):
        env.pop(key, None)
    messages = queue.Queue()
    deferred = []
    transcript = []
    sequence = 0
    stderr = (evidence / "stderr.log").open("w", encoding="utf-8")
    process = subprocess.Popen([str(bundle / "VaultClassifierWorker.exe")], cwd=evidence, env=env,
                               stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=stderr,
                               text=True, encoding="utf-8", bufsize=1)

    def read_output():
        for line in process.stdout:
            transcript.append(line)
            messages.put(json.loads(line))
        messages.put({"event": "eof"})

    reader = threading.Thread(target=read_output, daemon=True)
    reader.start()

    def take(predicate, timeout=120):
        for index, message in enumerate(deferred):
            if predicate(message):
                return deferred.pop(index)
        deadline = time.monotonic() + timeout
        while True:
            message = messages.get(timeout=max(0.01, deadline - time.monotonic()))
            if message.get("event") in ("fatal", "eof"):
                raise RuntimeError("Production worker stopped: " + repr(message))
            if predicate(message):
                return message
            deferred.append(message)
            if time.monotonic() >= deadline:
                raise TimeoutError("Production worker response timed out.")

    def request(operation, payload):
        nonlocal sequence
        sequence += 1
        identifier = "inference-" + str(sequence)
        process.stdin.write(json.dumps({"id": identifier, "operation": operation, "data": payload},
                                      ensure_ascii=False, separators=(",", ":")) + "\n")
        process.stdin.flush()
        response = take(lambda item: item.get("id") == identifier)
        if not response["ok"]:
            raise RuntimeError(response.get("error", "Worker request failed."))
        return response["value"]

    def action(name, payload):
        result = request("action", {"action": name, "data": payload})
        if result.get("issue"):
            raise RuntimeError(result["issue"])
        return result["snapshot"]

    def hub(operation, payload):
        result = request("hub", {"sourcePeerID": "chrome", "requestID": "real-model-fixture",
                                 "operation": operation, "body": payload})
        if "error" in result:
            raise RuntimeError(result["error"])
        return result["body"]

    try:
        assert take(lambda item: item.get("event") == "ready")["protocol"] == 1
        initial = request("snapshot", {})
        assert not initial.get("issue"), initial.get("issue")
        assert not initial["settings"]["research"]["enabled"], "Fixture must keep provider research disabled."
        action("addKnowledgeTerm", {"subject": "Fixture knowledge", "meaning": "Initial meaning."})
        page = request("action", {"action": "knowledgePage", "data": {"requestID": "knowledge-page", "kind": "term",
                                   "platformID": "", "query": "Fixture knowledge", "offset": 0, "limit": 1}})["list"]
        assert page["total"] == 1 and page["requestID"] == "knowledge-page"
        edited = request("action", {"action": "editKnowledgeEntry", "data": {
                         "id": page["items"][0]["id"], "meaning": "Saved meaning 中文。"}})
        assert edited["knowledgeRow"]["meaning"] == "Saved meaning 中文。"
        assert edited["snapshot"]["assets"]["knowledge"]["paged"]
        assert not edited["snapshot"]["assets"]["knowledge"]["terms"]
        snapshot = action("createClassifierType", {"name": "Windows inference 中文", "platformIDs": ["youtube"]})
        group = next(item for item in snapshot["assets"]["classifierTypes"] if item["name"] == "Windows inference 中文")
        action("saveClassifierTypeLocalModel", {"typeID": group["id"], "speedQuality": "fast", "strictness": 3,
                                               "houseRules": "", "minimumTagsOverride": 1, "maximumTagsOverride": 1})
        for name, description in (("Gaming", "Video games, including Minecraft survival gameplay."),
                                  ("Politics", "Government, elections and political policy.")):
            action("addTag", {"treeID": group["treeID"], "name": name, "description": description,
                              "positionX": 0, "positionY": 0})
        taxonomy = hub("classifier-taxonomy", {"platformID": "youtube"})
        type_tags = next(item["tags"] for item in taxonomy["types"] if item["typeID"] == group["id"])
        assert {tag["name"] for tag in type_tags} == {"Gaming", "Politics"}
        video = {"platformID": "youtube", "entryID": "youtube:video:windows-inference-fixture",
                 "creatorID": "youtube:channel:windows-inference-fixture",
                 "title": "Minecraft survival gameplay: building a stone castle"}
        collected = hub("collect", {"entry": {"requestID": "windows-inference-fixture", "platform": "youtube",
                         "entryID": video["entryID"], "sourceID": video["creatorID"], "sourceAliases": [],
                         "surface": "feed", "evidence": {"title": video["title"], "suppliedTags": [],
                         "metadata": {"sourceName": "Windows fixture", "entryType": "video"}}}})
        assert collected["accepted"] and collected["inserted"]
        started = time.monotonic()
        queued = hub("video-tags", video)
        assert queued["pending"], "Production classification must enter the background engine."
        resolved = take(lambda item: item.get("event") == "broadcast" and item.get("operation") == "video-tags-updated"
                        and any(video["entryID"] == result["entryID"] for result in item["body"]["items"]), timeout=600)
        result = next(item for item in resolved["body"]["items"] if item["entryID"] == video["entryID"])
        assert not result["pending"] and not result["predicted"], "The result must come from the actual local model."
        assert [tag["name"] for tag in result["tags"]] == ["Gaming"], result
        cached = hub("video-tags", video)
        assert not cached["pending"] and not cached["predicted"] and cached["tags"] == result["tags"]
        snapshot = request("snapshot", {})
        installed = next(item for item in snapshot["settings"]["localModels"]["modelLibrary"] if item["ggufFileName"] == model_name)
        assert installed["engineStatus"] == "loaded", installed
        politics = next(tag for tag in type_tags if tag["name"] == "Politics")
        correction = hub("submit-correction", {"platformID": video["platformID"], "entryID": video["entryID"],
                                                "creatorID": video["creatorID"], "typeID": group["id"],
                                                "correctTagIDs": [politics["id"]]})
        assert [tag["name"] for tag in correction["tags"]] == ["Politics"]
        corrected = hub("video-tags", video)
        assert not corrected["pending"] and [tag["name"] for tag in corrected["tags"]] == ["Politics"]
        request("hostEvent", {"kind": "flush"})
        process.stdin.close()
        assert process.wait(timeout=30) == 0
        result = {"passed": True, "productionWorker": True, "model": model_name,
                  "classifierRevision": manifest["classifierRevision"], "inferenceSeconds": time.monotonic() - started,
                  "checks": ["app-local runtimes", "knowledge list callback", "UTF-8 row acknowledgement",
                             "bounded UI snapshot", "UTF-8 group", "independent model settings", "taxonomy",
                             "real model broadcast", "cached tags", "loaded engine", "user correction", "EOF flush"]}
        (evidence / "result.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
        print("PASS: production Windows worker loaded the real 3B model, tagged Gaming, cached and corrected the video.")
        print("Evidence: " + str(evidence))
    finally:
        if process.poll() is None:
            process.stdin.close()
            try:
                process.wait(timeout=30)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=60)
        reader.join(timeout=5)
        stderr.close()
        (evidence / "worker.jsonl").write_text("".join(transcript), encoding="utf-8")


if __name__ == "__main__":
    main()
